// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vertx.PgClient.Codec;

namespace Vertx.PgClient;

/// <summary>
/// A connection that supports true multiplexing - multiple concurrent callers
/// can submit queries that are pipelined on a single socket, with a background
/// response dispatcher routing responses to the correct caller.
/// </summary>
internal sealed class MultiplexedConnection : IAsyncDisposable
{
    private readonly PgSocketConnection _socket;
    private readonly ILogger _logger;
    private readonly Channel<PgCommand> _commandChannel;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _commandSenderTask;
    private readonly Task _responseDispatcherTask;
    private readonly Queue<PgCommand> _inflight = new();
    private readonly PreparedStatementCache? _preparedStatementCache;
    private bool _disposed;

    /// <summary>
    /// Gets the pipelining limit for this connection.
    /// </summary>
    public int PipeliningLimit => _socket.PipeliningLimit;

    /// <summary>
    /// Gets the current number of inflight commands.
    /// </summary>
    public int InflightCount
    {
        get
        {
            lock (_inflight)
            {
                return _inflight.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of available pipeline slots.
    /// </summary>
    public int AvailableSlots => Math.Max(0, PipeliningLimit - InflightCount);

    /// <summary>
    /// Gets whether the connection is connected.
    /// </summary>
    public bool IsConnected => _socket.IsConnected && !_disposed;

    private MultiplexedConnection(PgSocketConnection socket, ILogger? logger, PreparedStatementCache? cache)
    {
        _socket = socket;
        _logger = logger ?? NullLogger.Instance;
        _preparedStatementCache = cache;
        
        // Bounded channel to provide backpressure
        _commandChannel = Channel.CreateBounded<PgCommand>(new BoundedChannelOptions(PipeliningLimit * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        // Start background tasks
        _commandSenderTask = Task.Run(CommandSenderAsync);
        _responseDispatcherTask = Task.Run(ResponseDispatcherAsync);
    }

    /// <summary>
    /// Creates a new multiplexed connection.
    /// </summary>
    public static async ValueTask<MultiplexedConnection> CreateAsync(
        PgConnectOptions options, 
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var socket = new PgSocketConnection(options, logger);
        await socket.ConnectAsync(cancellationToken);
        
        // Create cache if enabled in options
        PreparedStatementCache? cache = null;
        if (options.PreparedStatementCacheMaxSize > 0)
        {
            cache = new PreparedStatementCache(options.PreparedStatementCacheMaxSize, options.PreparedStatementCacheSqlLimit);
        }
        
        return new MultiplexedConnection(socket, logger, cache);
    }

    /// <summary>
    /// Wraps an existing socket connection.
    /// </summary>
    public static MultiplexedConnection Wrap(PgSocketConnection socket, ILogger? logger = null)
    {
        return new MultiplexedConnection(socket, logger, null);
    }

    /// <summary>
    /// Schedules a simple query for execution and returns immediately.
    /// The returned task completes when the query result is available.
    /// </summary>
    public async Task<RowSet> QueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var command = new SimpleQueryCommand(sql);
        await ScheduleCommandAsync(command, cancellationToken);
        return await command.Task;
    }

    /// <summary>
    /// Schedules a prepared query for execution and returns immediately.
    /// Uses the extended query protocol with automatic statement caching.
    /// The returned task completes when the query result is available.
    /// </summary>
    public async Task<RowSet> PreparedQueryAsync(string sql, ITuple? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var command = new PreparedQueryCommand(sql, parameters, _preparedStatementCache);
        await ScheduleCommandAsync(command, cancellationToken);
        return await command.Task;
    }

    /// <summary>
    /// Schedules a command for execution by writing it to the command channel.
    /// The background command sender task will pick it up and send it to the server.
    /// </summary>
    private async ValueTask ScheduleCommandAsync(PgCommand command, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        await _commandChannel.Writer.WriteAsync(command, linkedCts.Token);
    }

    /// <summary>
    /// Background task that reads commands from the channel and sends them to the server.
    /// Batches multiple pending commands together for true pipelining.
    /// </summary>
    private async Task CommandSenderAsync()
    {
        var batch = new List<PgCommand>(PipeliningLimit);
        
        try
        {
            while (await _commandChannel.Reader.WaitToReadAsync(_disposeCts.Token))
            {
                batch.Clear();
                
                // Drain all available commands from the channel (up to pipelining limit)
                while (batch.Count < PipeliningLimit && _commandChannel.Reader.TryRead(out var command))
                {
                    batch.Add(command);
                }
                
                if (batch.Count == 0)
                {
                    continue;
                }

                try
                {
                    // Add all commands to inflight queue first
                    lock (_inflight)
                    {
                        foreach (var command in batch)
                        {
                            _inflight.Enqueue(command);
                        }
                    }

                    // Send all commands in a single batch (with lock to serialize writes)
                    await _sendLock.WaitAsync(_disposeCts.Token);
                    try
                    {
                        await _socket.SendCommandsAsync(batch, _disposeCts.Token);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch (Exception ex) when (!_disposeCts.Token.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error sending {Count} commands", batch.Count);
                    
                    // Complete all commands in the batch with error
                    lock (_inflight)
                    {
                        // Rebuild queue excluding the failed commands
                        int count = _inflight.Count;
                        for (int i = 0; i < count; i++)
                        {
                            var c = _inflight.Dequeue();
                            if (!batch.Contains(c))
                            {
                                _inflight.Enqueue(c);
                            }
                        }
                    }
                    
                    foreach (var command in batch)
                    {
                        command.Complete(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_disposeCts.Token.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command sender crashed");
        }
    }

    /// <summary>
    /// Background task that reads responses and dispatches them to the correct command.
    /// </summary>
    private async Task ResponseDispatcherAsync()
    {
        try
        {
            while (!_disposed && !_disposeCts.Token.IsCancellationRequested)
            {
                // Wait for a command to be in flight before trying to read responses
                PgCommand? current;
                lock (_inflight)
                {
                    if (!_inflight.TryPeek(out current))
                    {
                        current = null;
                    }
                }

                if (current is null)
                {
                    // No commands yet, wait a bit
                    await Task.Delay(10, _disposeCts.Token);
                    continue;
                }

                try
                {
                    var response = await _socket.ReceiveResponseAsync(_disposeCts.Token);

                    // Handle global responses that don't belong to any command
                    switch (response)
                    {
                        case NoticeResponse notice:
                            _socket.RaiseNoticeReceived(notice);
                            continue;

                        case NotificationResponse notif:
                            _socket.RaiseNotificationReceived(notif);
                            continue;

                        case ParameterStatusResponse param:
                            _socket.UpdateServerParameter(param.Name, param.Value);
                            continue;

                        case ReadyForQueryResponse ready:
                            _socket.TransactionStatus = ready.Status;
                            break;
                    }

                    // Dispatch to the current command
                    bool complete = current.HandleResponse(response);

                    // Handle two-phase prepared query commands
                    if (!complete && current is PreparedQueryCommand preparedCmd && preparedCmd.ParseDescribeComplete)
                    {
                        // Parse/describe phase complete, send bind/execute phase
                        await _sendLock.WaitAsync(_disposeCts.Token);
                        try
                        {
                            await _socket.SendPreparedBindExecuteAsync(preparedCmd, _disposeCts.Token);
                        }
                        finally
                        {
                            _sendLock.Release();
                        }
                    }

                    if (complete)
                    {
                        lock (_inflight)
                        {
                            _inflight.Dequeue();
                        }
                        current.Complete();
                    }
                }
                catch (OperationCanceledException) when (_disposeCts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in response dispatcher");
                    
                    // Complete the current command with error
                    if (current is not null)
                    {
                        lock (_inflight)
                        {
                            if (_inflight.TryPeek(out var peek) && peek == current)
                            {
                                _inflight.Dequeue();
                            }
                        }
                        current.Complete(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_disposeCts.Token.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Response dispatcher crashed");
        }
        finally
        {
            // Complete all remaining commands with error
            lock (_inflight)
            {
                while (_inflight.TryDequeue(out var cmd))
                {
                    cmd.Complete(new ObjectDisposedException(nameof(MultiplexedConnection)));
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        // Signal shutdown and complete the channel
        _commandChannel.Writer.Complete();
        _disposeCts.Cancel();

        // Wait for background tasks to finish
        try
        {
            await Task.WhenAll(_commandSenderTask, _responseDispatcherTask).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout
        }

        await _socket.DisposeAsync();
        _sendLock.Dispose();
        _disposeCts.Dispose();
    }
}

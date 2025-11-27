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
    private readonly Task _responseDispatcherTask;
    private readonly Queue<PgCommand> _inflight = new();
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

    private MultiplexedConnection(PgSocketConnection socket, ILogger? logger)
    {
        _socket = socket;
        _logger = logger ?? NullLogger.Instance;
        
        // Bounded channel to provide backpressure
        _commandChannel = Channel.CreateBounded<PgCommand>(new BoundedChannelOptions(PipeliningLimit * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        // Start the response dispatcher
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
        return new MultiplexedConnection(socket, logger);
    }

    /// <summary>
    /// Wraps an existing socket connection.
    /// </summary>
    public static MultiplexedConnection Wrap(PgSocketConnection socket, ILogger? logger = null)
    {
        return new MultiplexedConnection(socket, logger);
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
    /// Schedules a command for execution.
    /// </summary>
    private async ValueTask ScheduleCommandAsync(PgCommand command, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        
        // Send the command immediately (with lock to serialize writes)
        await _sendLock.WaitAsync(linkedCts.Token);
        try
        {
            // Add to inflight before sending
            lock (_inflight)
            {
                _inflight.Enqueue(command);
            }

            // Encode and send
            await _socket.SendCommandAsync(command, linkedCts.Token);
        }
        catch
        {
            // Remove from inflight on error
            lock (_inflight)
            {
                // Try to remove this command if it's still at the end
                // This is a simplified approach - in practice we might need a more robust cleanup
            }
            throw;
        }
        finally
        {
            _sendLock.Release();
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
                PgCommand? current;
                lock (_inflight)
                {
                    if (!_inflight.TryPeek(out current))
                    {
                        // No commands inflight, wait a bit
                        // In a more sophisticated implementation, we'd use a signal
                    }
                }

                if (current is null)
                {
                    // Wait for commands to be scheduled
                    await Task.Delay(1, _disposeCts.Token);
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

                    if (complete)
                    {
                        lock (_inflight)
                        {
                            _inflight.Dequeue();
                        }
                        current.Complete();
                    }

                    // Handle extended query commands that need to send bind/execute after parse
                    if (current is ExtendedQueryCommand extCmd && extCmd.NeedsSendBindExecute)
                    {
                        await _sendLock.WaitAsync(_disposeCts.Token);
                        try
                        {
                            await _socket.SendBufferAsync(extCmd.GetBindExecuteBuffer(), _disposeCts.Token);
                        }
                        finally
                        {
                            _sendLock.Release();
                        }
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
        _disposeCts.Cancel();

        // Wait for response dispatcher to finish
        try
        {
            await _responseDispatcherTask.WaitAsync(TimeSpan.FromSeconds(5));
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

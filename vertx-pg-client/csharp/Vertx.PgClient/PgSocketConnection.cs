// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vertx.PgClient.Codec;

namespace Vertx.PgClient;

/// <summary>
/// Handles the low-level socket connection and PostgreSQL protocol communication.
/// Supports pipelining - multiple commands can be sent before waiting for responses.
/// </summary>
internal sealed class PgSocketConnection : IAsyncDisposable
{
    private readonly PgConnectOptions _options;
    private readonly ILogger _logger;
    private readonly PgEncoder _encoder;
    private readonly PgDecoder _decoder;
    private readonly Dictionary<string, string> _serverParameters = new();
    private readonly byte[] _receiveBuffer;
    private int _receiveBufferOffset;
    private int _receiveBufferLength;

    // Pipelining state
    private readonly Queue<PgCommand> _pending = new();
    private readonly Queue<PgCommand> _inflight = new();
    private int _inflightCount;
#pragma warning disable CS0649 // Reserved for future flow control
    private bool _paused;
#pragma warning restore CS0649
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private Socket? _socket;
    private Stream? _stream;
    private NetworkStream? _networkStream;
    private SslStream? _sslStream;

    public int ProcessId { get; private set; }
    public int SecretKey { get; private set; }
    public char TransactionStatus { get; internal set; } = 'I';
    public bool IsConnected => _socket?.Connected == true;
    public int PipeliningLimit => _options.PipeliningLimit;
    
    public IReadOnlyDictionary<string, string> ServerParameters => _serverParameters;

    public event Action<PgNotification>? NotificationReceived;
    public event Action<NoticeResponse>? NoticeReceived;

    // Internal methods for MultiplexedConnection
    internal void RaiseNoticeReceived(NoticeResponse notice) => NoticeReceived?.Invoke(notice);
    internal void RaiseNotificationReceived(NotificationResponse notif) => 
        NotificationReceived?.Invoke(new PgNotification(notif.Channel, notif.ProcessId, notif.Payload));
    internal void UpdateServerParameter(string name, string value) => _serverParameters[name] = value;

    public PgSocketConnection(PgConnectOptions options, ILogger? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger.Instance;
        _encoder = new PgEncoder();
        _decoder = new PgDecoder();
        _receiveBuffer = ArrayPool<byte>.Shared.Rent(65536);
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        var host = _options.Host;
        var port = _options.Port;

        _logger.LogDebug("Connecting to {Host}:{Port}", host, port);

        await _socket.ConnectAsync(host, port, cancellationToken);
        _networkStream = new NetworkStream(_socket, ownsSocket: false);
        _stream = _networkStream;

        // Handle SSL
        if (_options.SslMode != SslMode.Disable)
        {
            await NegotiateSslAsync(cancellationToken);
        }

        // Send startup message
        await SendStartupMessageAsync(cancellationToken);

        // Handle authentication and wait for ReadyForQuery
        await HandleStartupResponseAsync(cancellationToken);

        _logger.LogDebug("Connected successfully. ProcessId={ProcessId}", ProcessId);
    }

    private async ValueTask NegotiateSslAsync(CancellationToken cancellationToken)
    {
        _encoder.Reset();
        _encoder.WriteSslRequest();
        await SendAsync(cancellationToken);

        var response = new byte[1];
        int bytesRead = await _stream!.ReadAsync(response, cancellationToken);
        
        if (bytesRead != 1)
            throw new PgException("Failed to receive SSL response", "08000", "");

        if (response[0] == 'S')
        {
            // Server supports SSL
            _logger.LogDebug("Server supports SSL, establishing secure connection");
            
            var sslOptions = _options.SslOptions ?? new PgSslOptions();
            
            _sslStream = new SslStream(
                _networkStream!,
                leaveInnerStreamOpen: true,
                userCertificateValidationCallback: CreateCertificateValidationCallback(sslOptions)
            );

            var sslClientOptions = new SslClientAuthenticationOptions
            {
                TargetHost = _options.Host ?? "localhost",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            await _sslStream.AuthenticateAsClientAsync(sslClientOptions, cancellationToken);
            _stream = _sslStream;

            _logger.LogDebug("SSL connection established. Protocol: {Protocol}", _sslStream.SslProtocol);
        }
        else if (response[0] == 'N')
        {
            // Server does not support SSL
            if (_options.SslMode == SslMode.Require || 
                _options.SslMode == SslMode.VerifyCa || 
                _options.SslMode == SslMode.VerifyFull)
            {
                throw new PgException("Server does not support SSL but sslmode requires it", "08000", "");
            }
            
            _logger.LogDebug("Server does not support SSL, continuing with unencrypted connection");
        }
        else
        {
            throw new PgException($"Unexpected SSL response: {(char)response[0]}", "08000", "");
        }
    }

    private RemoteCertificateValidationCallback CreateCertificateValidationCallback(PgSslOptions options)
    {
        return (sender, certificate, chain, errors) =>
        {
            if (_options.SslMode == SslMode.Prefer || _options.SslMode == SslMode.Require)
            {
                // Don't verify certificate
                return true;
            }

            if (_options.SslMode == SslMode.VerifyCa)
            {
                // Verify certificate chain but not hostname
                return errors == SslPolicyErrors.None || 
                       errors == SslPolicyErrors.RemoteCertificateNameMismatch;
            }

            // VerifyFull - verify everything
            return errors == SslPolicyErrors.None;
        };
    }

    private async ValueTask SendStartupMessageAsync(CancellationToken cancellationToken)
    {
        _encoder.Reset();
        
        var properties = new Dictionary<string, string>();
        
        if (_options.Properties is not null)
        {
            foreach (var prop in _options.Properties)
            {
                properties[prop.Key] = prop.Value;
            }
        }

        _encoder.WriteStartupMessage(
            _options.User,
            _options.Database,
            properties
        );

        await SendAsync(cancellationToken);
    }

    private async ValueTask HandleStartupResponseAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await ReceiveAsync(cancellationToken);

            switch (response)
            {
                case AuthenticationOkResponse:
                    _logger.LogDebug("Authentication successful");
                    break;

                case AuthenticationCleartextPasswordResponse:
                    await SendPasswordAsync(_options.Password, cancellationToken);
                    break;

                case AuthenticationMd5PasswordResponse md5:
                    await SendMd5PasswordAsync(md5.Salt, cancellationToken);
                    break;

                case AuthenticationSASLResponse sasl:
                    await HandleScramAuthenticationAsync(sasl.Mechanisms, cancellationToken);
                    break;

                case AuthenticationSASLContinueResponse saslContinue:
                    // This is handled within HandleScramAuthenticationAsync
                    _logger.LogWarning("Unexpected SASL Continue response outside of SCRAM flow");
                    break;

                case AuthenticationSASLFinalResponse saslFinal:
                    // This is handled within HandleScramAuthenticationAsync
                    _logger.LogWarning("Unexpected SASL Final response outside of SCRAM flow");
                    break;

                case BackendKeyDataResponse keyData:
                    ProcessId = keyData.ProcessId;
                    SecretKey = keyData.SecretKey;
                    break;

                case ParameterStatusResponse param:
                    _serverParameters[param.Name] = param.Value;
                    _logger.LogTrace("Server parameter: {Name}={Value}", param.Name, param.Value);
                    break;

                case ReadyForQueryResponse ready:
                    TransactionStatus = ready.Status;
                    return;

                case ErrorResponse error:
                    throw new PgException(error.Message, error.Code, error.Severity);

                case NoticeResponse notice:
                    NoticeReceived?.Invoke(notice);
                    break;

                default:
                    _logger.LogWarning("Unexpected response during startup: {Type}", response.GetType().Name);
                    break;
            }
        }
    }

    private async ValueTask SendPasswordAsync(string password, CancellationToken cancellationToken)
    {
        _encoder.Reset();
        _encoder.WritePasswordMessage(password);
        await SendAsync(cancellationToken);
    }

    private async ValueTask SendMd5PasswordAsync(byte[] salt, CancellationToken cancellationToken)
    {
        // MD5(MD5(password + username) + salt)
        var user = _options.User ?? Environment.UserName;
        var password = _options.Password ?? "";

        using var md5 = MD5.Create();
        
        // First hash: MD5(password + username)
        var firstInput = Encoding.UTF8.GetBytes(password + user);
        var firstHash = md5.ComputeHash(firstInput);
        var firstHex = Convert.ToHexString(firstHash).ToLowerInvariant();

        // Second hash: MD5(firstHex + salt)
        var secondInput = new byte[firstHex.Length + salt.Length];
        Encoding.ASCII.GetBytes(firstHex, secondInput);
        salt.CopyTo(secondInput.AsSpan(firstHex.Length));
        var secondHash = md5.ComputeHash(secondInput);
        var secondHex = Convert.ToHexString(secondHash).ToLowerInvariant();

        var hash = "md5" + secondHex;

        _encoder.Reset();
        _encoder.WriteMd5PasswordMessage(hash);
        await SendAsync(cancellationToken);
    }

    private async ValueTask HandleScramAuthenticationAsync(string[] mechanisms, CancellationToken cancellationToken)
    {
        var username = _options.User ?? Environment.UserName;
        var password = _options.Password ?? "";

        // Create SCRAM client (no channel binding data for now)
        var scram = new ScramClient(username, password);

        if (!scram.SelectMechanism(mechanisms))
        {
            throw new PgException(
                $"No supported SASL mechanism. Server offered: {string.Join(", ", mechanisms)}. " +
                "This client supports SCRAM-SHA-256.",
                "28000",
                "authentication_failed"
            );
        }

        _logger.LogDebug("Using SCRAM authentication with mechanism: {Mechanism}", scram.Mechanism);

        // Step 1: Send client-first-message
        var clientFirstMessage = scram.CreateClientFirstMessage();
        _encoder.Reset();
        _encoder.WriteSaslInitialResponse(scram.Mechanism, clientFirstMessage);
        await SendAsync(cancellationToken);

        // Step 2: Receive server-first-message
        var response = await ReceiveAsync(cancellationToken);
        if (response is not AuthenticationSASLContinueResponse saslContinue)
        {
            if (response is ErrorResponse error)
                throw new PgException(error.Message, error.Code, error.Severity);
            throw new PgException($"Expected SASL Continue, got {response.GetType().Name}", "28000", "authentication_failed");
        }

        var serverFirstMessage = Encoding.UTF8.GetString(saslContinue.Data);
        _logger.LogTrace("SCRAM server-first-message: {Message}", serverFirstMessage);

        // Step 3: Send client-final-message
        var clientFinalMessage = scram.ProcessServerFirstMessage(serverFirstMessage);
        _encoder.Reset();
        _encoder.WriteSaslResponse(clientFinalMessage);
        await SendAsync(cancellationToken);

        // Step 4: Receive server-final-message
        response = await ReceiveAsync(cancellationToken);
        if (response is not AuthenticationSASLFinalResponse saslFinal)
        {
            if (response is ErrorResponse error)
                throw new PgException(error.Message, error.Code, error.Severity);
            throw new PgException($"Expected SASL Final, got {response.GetType().Name}", "28000", "authentication_failed");
        }

        var serverFinalMessage = Encoding.UTF8.GetString(saslFinal.Data);
        _logger.LogTrace("SCRAM server-final-message: {Message}", serverFinalMessage);

        // Verify server signature
        scram.VerifyServerFinalMessage(serverFinalMessage);

        _logger.LogDebug("SCRAM authentication successful");
    }

    public async ValueTask<RowSet> QueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        _encoder.Reset();
        _encoder.WriteQuery(sql);
        await SendAsync(cancellationToken);

        return await ReceiveQueryResultAsync(cancellationToken);
    }

    public async ValueTask<RowSet> PreparedQueryAsync(string sql, ITuple? parameters, CancellationToken cancellationToken = default)
    {
        var statementName = _encoder.GenerateStatementName();
        
        _encoder.Reset();
        _encoder.WriteParse(sql, statementName);
        _encoder.WriteDescribe('S', statementName);
        _encoder.WriteSync();
        await SendAsync(cancellationToken);

        // Wait for ParseComplete and ParameterDescription/RowDescription
        PgColumnDesc[]? paramTypes = null;
        PgColumnDesc[]? rowDesc = null;

        while (true)
        {
            var response = await ReceiveAsync(cancellationToken);

            switch (response)
            {
                case ParseCompleteResponse:
                    break;

                case ParameterDescriptionResponse paramDesc:
                    paramTypes = paramDesc.TypeOids.Select(oid => 
                        new PgColumnDesc("", 0, 0, DataType.LookupByOid(oid), oid, 0, 0, DataFormat.Binary)
                    ).ToArray();
                    break;

                case RowDescriptionResponse rd:
                    rowDesc = rd.Columns;
                    break;

                case NoDataResponse:
                    break;

                case ReadyForQueryResponse ready:
                    TransactionStatus = ready.Status;
                    goto afterDescribe;

                case ErrorResponse error:
                    // Consume until ReadyForQuery
                    await ConsumeUntilReadyAsync(cancellationToken);
                    throw new PgException(error.Message, error.Code, error.Severity);

                case NoticeResponse notice:
                    NoticeReceived?.Invoke(notice);
                    break;

                case NotificationResponse notif:
                    NotificationReceived?.Invoke(new PgNotification(notif.Channel, notif.ProcessId, notif.Payload));
                    break;
            }
        }

        afterDescribe:

        // Update row description to binary format since we request binary results in Bind
        if (rowDesc is not null)
        {
            for (int i = 0; i < rowDesc.Length; i++)
            {
                rowDesc[i] = rowDesc[i].ToBinaryDataFormat();
            }
        }

        // Now bind and execute
        _encoder.Reset();
        _encoder.WriteBind(statementName, "", parameters, paramTypes);
        _encoder.WriteExecute();
        _encoder.WriteClose('S', statementName);
        _encoder.WriteSync();
        await SendAsync(cancellationToken);

        return await ReceiveExtendedQueryResultAsync(rowDesc, cancellationToken);
    }

    private async ValueTask<RowSet> ReceiveQueryResultAsync(CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        PgColumnDesc[]? columnDesc = null;
        int rowsAffected = 0;

        while (true)
        {
            var response = await ReceiveAsync(cancellationToken);

            switch (response)
            {
                case RowDescriptionResponse rd:
                    columnDesc = rd.Columns;
                    break;

                case DataRowResponse dataRow:
                    if (columnDesc is not null)
                    {
                        var row = DecodeRow(dataRow.Values, columnDesc);
                        rows.Add(row);
                    }
                    break;

                case CommandCompleteResponse cmd:
                    rowsAffected = ParseRowsAffected(cmd.Tag);
                    break;

                case EmptyQueryResponse:
                    break;

                case ReadyForQueryResponse ready:
                    TransactionStatus = ready.Status;
                    var columnNames = columnDesc?.Select(c => c.Name).ToArray() ?? Array.Empty<string>();
                    return new RowSet(rows, columnNames, rowsAffected);

                case ErrorResponse error:
                    await ConsumeUntilReadyAsync(cancellationToken);
                    throw new PgException(error.Message, error.Code, error.Severity);

                case NoticeResponse notice:
                    NoticeReceived?.Invoke(notice);
                    break;

                case NotificationResponse notif:
                    NotificationReceived?.Invoke(new PgNotification(notif.Channel, notif.ProcessId, notif.Payload));
                    break;
            }
        }
    }

    private async ValueTask<RowSet> ReceiveExtendedQueryResultAsync(PgColumnDesc[]? rowDesc, CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        int rowsAffected = 0;

        while (true)
        {
            var response = await ReceiveAsync(cancellationToken);

            switch (response)
            {
                case BindCompleteResponse:
                    break;

                case DataRowResponse dataRow:
                    if (rowDesc is not null)
                    {
                        var row = DecodeRow(dataRow.Values, rowDesc);
                        rows.Add(row);
                    }
                    break;

                case CommandCompleteResponse cmd:
                    rowsAffected = ParseRowsAffected(cmd.Tag);
                    break;

                case CloseCompleteResponse:
                    break;

                case PortalSuspendedResponse:
                    break;

                case ReadyForQueryResponse ready:
                    TransactionStatus = ready.Status;
                    var columnNames = rowDesc?.Select(c => c.Name).ToArray() ?? Array.Empty<string>();
                    return new RowSet(rows, columnNames, rowsAffected);

                case ErrorResponse error:
                    await ConsumeUntilReadyAsync(cancellationToken);
                    throw new PgException(error.Message, error.Code, error.Severity);

                case NoticeResponse notice:
                    NoticeReceived?.Invoke(notice);
                    break;

                case NotificationResponse notif:
                    NotificationReceived?.Invoke(new PgNotification(notif.Channel, notif.ProcessId, notif.Payload));
                    break;
            }
        }
    }

    private Row DecodeRow(byte[][] values, PgColumnDesc[] columnDesc)
    {
        var decodedValues = new PgValue[values.Length];
        
        for (int i = 0; i < values.Length; i++)
        {
            var column = columnDesc[i];
            if (values[i] is null)
            {
                decodedValues[i] = PgValue.CreateNull(column.DataType);
            }
            else
            {
                decodedValues[i] = column.DataFormat == DataFormat.Binary
                    ? PgValue.DecodeBinary(column.DataType, values[i])
                    : PgValue.DecodeText(column.DataType, values[i]);
            }
        }

        return new Row(decodedValues, columnDesc.Select(c => c.Name).ToArray());
    }

    private static int ParseRowsAffected(string tag)
    {
        // Tag format: "INSERT 0 5", "UPDATE 5", "DELETE 5", "SELECT 5"
        // Find the last space and parse the number after it
        ReadOnlySpan<char> span = tag.AsSpan();
        int lastSpace = span.LastIndexOf(' ');
        if (lastSpace >= 0 && int.TryParse(span[(lastSpace + 1)..], out int count))
        {
            return count;
        }
        return 0;
    }

    #region Pipelining and Multiplexing

    /// <summary>
    /// Sends a command to the server. Used by MultiplexedConnection.
    /// </summary>
    internal async ValueTask SendCommandAsync(PgCommand command, CancellationToken cancellationToken = default)
    {
        _encoder.Reset();
        command.Encode(_encoder);
        await SendAsync(cancellationToken);
    }

    /// <summary>
    /// Receives a single response from the server. Used by MultiplexedConnection.
    /// </summary>
    internal async ValueTask<Response> ReceiveResponseAsync(CancellationToken cancellationToken = default)
    {
        return await ReceiveAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a raw buffer to the server. Used by MultiplexedConnection for extended query bind/execute.
    /// </summary>
    internal async ValueTask SendBufferAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_stream is not null)
        {
            await _stream.WriteAsync(buffer, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Schedules a command for pipelined execution.
    /// Commands are sent immediately if under the pipelining limit,
    /// otherwise they are queued and sent when slots become available.
    /// </summary>
    public void Schedule(PgCommand command)
    {
        lock (_pending)
        {
            _pending.Enqueue(command);
        }
    }

    /// <summary>
    /// Processes pending commands, sending them if under the pipelining limit.
    /// This should be called periodically to flush the pending queue.
    /// </summary>
    public async ValueTask CheckPendingAsync(CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await CheckPendingCoreAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async ValueTask CheckPendingCoreAsync(CancellationToken cancellationToken)
    {
        int written = 0;
        
        while (!_paused && _inflightCount < _options.PipeliningLimit)
        {
            PgCommand? cmd;
            lock (_pending)
            {
                if (!_pending.TryDequeue(out cmd))
                    break;
            }

            _inflightCount++;
            lock (_inflight)
            {
                _inflight.Enqueue(cmd);
            }

            // Encode the command
            _encoder.Reset();
            cmd.Encode(_encoder);
            
            // Send it
            await SendAsync(cancellationToken);
            written++;
        }
    }

    /// <summary>
    /// Handles responses from the server and dispatches them to the appropriate command.
    /// </summary>
    public async ValueTask ProcessResponsesAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            PgCommand? current;
            lock (_inflight)
            {
                if (!_inflight.TryPeek(out current))
                {
                    // No commands in flight
                    break;
                }
            }

            var response = await ReceiveAsync(cancellationToken);

            // Handle global responses
            switch (response)
            {
                case ReadyForQueryResponse ready:
                    TransactionStatus = ready.Status;
                    break;
                    
                case NoticeResponse notice:
                    NoticeReceived?.Invoke(notice);
                    continue;
                    
                case NotificationResponse notif:
                    NotificationReceived?.Invoke(new PgNotification(notif.Channel, notif.ProcessId, notif.Payload));
                    continue;
                    
                case ParameterStatusResponse param:
                    _serverParameters[param.Name] = param.Value;
                    continue;
            }

            // Dispatch to the command
            bool complete = current.HandleResponse(response);
            
            if (complete)
            {
                lock (_inflight)
                {
                    _inflight.Dequeue();
                }
                _inflightCount--;
                current.Complete();
                
                // Check if we can send more commands
                await CheckPendingCoreAsync(cancellationToken);
            }

            // Handle extended query commands that need to send bind/execute after parse
            if (current is ExtendedQueryCommand extCmd && extCmd.NeedsSendBindExecute)
            {
                var buffer = extCmd.GetBindExecuteBuffer();
                if (_stream is not null)
                {
                    await _stream.WriteAsync(buffer, cancellationToken);
                    await _stream.FlushAsync(cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Schedules a command and waits for its result.
    /// This is a convenience method for executing a single command with pipelining support.
    /// </summary>
    public async ValueTask<RowSet> ScheduleAndWaitAsync(PgCommand command, CancellationToken cancellationToken = default)
    {
        Schedule(command);
        await CheckPendingAsync(cancellationToken);
        
        // Process responses until our command completes
        while (true)
        {
            PgCommand? current;
            lock (_inflight)
            {
                if (!_inflight.TryPeek(out current))
                    break;
            }

            var response = await ReceiveAsync(cancellationToken);

            // Handle global responses
            switch (response)
            {
                case ReadyForQueryResponse ready:
                    TransactionStatus = ready.Status;
                    break;
                    
                case NoticeResponse notice:
                    NoticeReceived?.Invoke(notice);
                    continue;
                    
                case NotificationResponse notif:
                    NotificationReceived?.Invoke(new PgNotification(notif.Channel, notif.ProcessId, notif.Payload));
                    continue;
                    
                case ParameterStatusResponse param:
                    _serverParameters[param.Name] = param.Value;
                    continue;
            }

            // Dispatch to the command
            bool complete = current.HandleResponse(response);
            
            if (complete)
            {
                lock (_inflight)
                {
                    _inflight.Dequeue();
                }
                _inflightCount--;
                current.Complete();
                
                // Check if we can send more commands
                await CheckPendingCoreAsync(cancellationToken);
                
                // If this was our command, we're done
                if (current == command)
                {
                    break;
                }
            }

            // Handle extended query commands that need to send bind/execute after parse
            if (current is ExtendedQueryCommand extCmd && extCmd.NeedsSendBindExecute)
            {
                var buffer = extCmd.GetBindExecuteBuffer();
                if (_stream is not null)
                {
                    await _stream.WriteAsync(buffer, cancellationToken);
                    await _stream.FlushAsync(cancellationToken);
                }
            }
        }

        // Get the result from the command
        if (command is SimpleQueryCommand simpleCmd)
        {
            return await simpleCmd.Task;
        }
        else if (command is ExtendedQueryCommand extendedCmd)
        {
            return await extendedCmd.Task;
        }
        
        throw new InvalidOperationException("Unknown command type");
    }

    /// <summary>
    /// Executes multiple queries in a pipelined fashion.
    /// All queries are sent before waiting for any responses.
    /// </summary>
    public async ValueTask<RowSet[]> PipelineQueryAsync(string[] queries, CancellationToken cancellationToken = default)
    {
        var commands = new SimpleQueryCommand[queries.Length];
        
        // Schedule all commands
        for (int i = 0; i < queries.Length; i++)
        {
            commands[i] = new SimpleQueryCommand(queries[i]);
            Schedule(commands[i]);
        }

        // Send pending commands
        await CheckPendingAsync(cancellationToken);

        // Wait for all results
        var results = new RowSet[queries.Length];
        for (int i = 0; i < queries.Length; i++)
        {
            // Process responses until this command completes
            while (!commands[i].Task.IsCompleted)
            {
                PgCommand? current;
                lock (_inflight)
                {
                    if (!_inflight.TryPeek(out current))
                        break;
                }

                var response = await ReceiveAsync(cancellationToken);

                // Handle global responses
                switch (response)
                {
                    case ReadyForQueryResponse ready:
                        TransactionStatus = ready.Status;
                        break;
                        
                    case NoticeResponse notice:
                        NoticeReceived?.Invoke(notice);
                        continue;
                        
                    case NotificationResponse notif:
                        NotificationReceived?.Invoke(new PgNotification(notif.Channel, notif.ProcessId, notif.Payload));
                        continue;
                        
                    case ParameterStatusResponse param:
                        _serverParameters[param.Name] = param.Value;
                        continue;
                }

                bool complete = current.HandleResponse(response);
                
                if (complete)
                {
                    lock (_inflight)
                    {
                        _inflight.Dequeue();
                    }
                    _inflightCount--;
                    current.Complete();
                    
                    await CheckPendingCoreAsync(cancellationToken);
                }
            }

            results[i] = await commands[i].Task;
        }

        return results;
    }

    #endregion

    private async ValueTask ConsumeUntilReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await ReceiveAsync(cancellationToken);
            if (response is ReadyForQueryResponse ready)
            {
                TransactionStatus = ready.Status;
                return;
            }
        }
    }

    private async ValueTask SendAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected");

        var data = _encoder.Buffer;
        await _stream.WriteAsync(data, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async ValueTask<Response> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected");

        while (true)
        {
            // Try to parse from existing buffer
            var availableData = new ReadOnlySpan<byte>(_receiveBuffer, _receiveBufferOffset, _receiveBufferLength);
            if (_decoder.TryParse(availableData, out var response, out int bytesConsumed))
            {
                _receiveBufferOffset += bytesConsumed;
                _receiveBufferLength -= bytesConsumed;
                return response!;
            }

            // Need more data - compact buffer if needed
            if (_receiveBufferOffset > 0)
            {
                if (_receiveBufferLength > 0)
                {
                    Array.Copy(_receiveBuffer, _receiveBufferOffset, _receiveBuffer, 0, _receiveBufferLength);
                }
                _receiveBufferOffset = 0;
            }

            // Read more data
            int bytesRead = await _stream.ReadAsync(
                _receiveBuffer.AsMemory(_receiveBufferLength),
                cancellationToken
            );

            if (bytesRead == 0)
                throw new PgException("Connection closed by server", "08003", "");

            _receiveBufferLength += bytesRead;
        }
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is not null)
        {
            try
            {
                _encoder.Reset();
                _encoder.WriteTerminate();
                await SendAsync(cancellationToken);
            }
            catch
            {
                // Ignore errors during close
            }
        }

        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_sslStream is not null)
        {
            await _sslStream.DisposeAsync();
            _sslStream = null;
        }

        if (_networkStream is not null)
        {
            await _networkStream.DisposeAsync();
            _networkStream = null;
        }

        _socket?.Dispose();
        _socket = null;
        _stream = null;

        ArrayPool<byte>.Shared.Return(_receiveBuffer);
    }
}

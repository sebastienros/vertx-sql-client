/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Apex.MsSqlClient.Internal;
using Apex.SqlClient;

namespace Apex.MsSqlClient.Tests;

[TestClass]
public sealed class MsSqlConnectionWireTests
{
  [TestMethod]
  public async Task ConnectsAndExecutesAgainstInMemoryProtocolServer()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = RunQueryServerAsync(listener);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      SqlRowSet rows = await connection.QueryAsync("SELECT 42 AS value");

      Assert.AreEqual(1, rows.Count);
      Assert.AreEqual(42, rows[0].GetInt32("value"));
      Assert.AreEqual(16, connection.DatabaseMetadata.MajorVersion);
      Assert.IsFalse(connection.IsSecure);
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task AttentionIsSentOnSameConnectionAndDrainedBeforeReuse()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    TaskCompletionSource queryReceived =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task server = RunCancellationServerAsync(listener, queryReceived);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      using CancellationTokenSource cancellation = new();
      Task<SqlRowSet> pending = connection.QueryAsync(
        "WAITFOR DELAY '00:01:00'",
        cancellation.Token).AsTask();
      await queryReceived.Task;
      await cancellation.CancelAsync();

      await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending);
      SqlRowSet rows = await connection.QueryAsync("SELECT 7 AS value");
      Assert.AreEqual(7, rows[0].GetInt32(0));
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task BorrowedReaderAndSafeStreamHonorRowLifetimes()
  {
    TcpListener readerListener = new(IPAddress.Loopback, 0);
    readerListener.Start();
    int readerPort = ((IPEndPoint)readerListener.LocalEndpoint).Port;
    Task readerServer = RunRowsServerAsync(readerListener, 1, 2);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(readerPort));
      ISqlRowReader reader = await connection.ExecuteReaderAsync("SELECT value");
      Assert.IsTrue(await reader.ReadAsync());
      Assert.AreEqual(1, reader.GetInt32(0));
      Assert.IsTrue(await reader.ReadAsync());
      Assert.AreEqual(2, reader.GetInt32(0));
      Assert.IsFalse(await reader.ReadAsync());
      await reader.DisposeAsync();
      Assert.ThrowsExactly<ObjectDisposedException>(() => reader.GetInt32(0));
      await readerServer;
    }
    finally
    {
      readerListener.Stop();
    }

    TcpListener streamListener = new(IPAddress.Loopback, 0);
    streamListener.Start();
    int streamPort = ((IPEndPoint)streamListener.LocalEndpoint).Port;
    Task streamServer = RunRowsServerAsync(streamListener, 3, 4);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(streamPort));
      List<SqlRow> safeRows = [];
      await foreach (SqlRow row in connection.StreamAsync("SELECT value", fetchSize: 1))
      {
        safeRows.Add(row);
      }

      Assert.AreEqual(3, safeRows[0].GetInt32(0));
      Assert.AreEqual(4, safeRows[1].GetInt32(0));
      await streamServer;
    }
    finally
    {
      streamListener.Stop();
    }
  }

  [TestMethod]
  public async Task BorrowedReaderCopiesReadOnlyMemoryValues()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = RunBinaryRowsServerAsync(
      listener,
      [1, 2, 3],
      [4, 5, 6]);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      await using ISqlRowReader reader =
        await connection.ExecuteReaderAsync("SELECT value");

      Assert.IsTrue(await reader.ReadAsync());
      ReadOnlyMemory<byte> first = reader.Get<ReadOnlyMemory<byte>>(0);
      Assert.IsTrue(await reader.ReadAsync());
      ReadOnlyMemory<byte> second = reader.Get<ReadOnlyMemory<byte>>(0);

      CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, first.ToArray());
      CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, second.ToArray());
      Assert.IsFalse(await reader.ReadAsync());
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task ConnectTimeoutIncludesStalledLoginResponse()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    TaskCompletionSource loginReceived =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task server = RunStalledLoginServerAsync(
      listener,
      loginReceived,
      release.Task);
    try
    {
      MsSqlConnectOptions options = TestOptions(port) with
      {
        ConnectTimeout = TimeSpan.FromMilliseconds(150),
      };
      long started = System.Diagnostics.Stopwatch.GetTimestamp();
      await Assert.ThrowsExactlyAsync<OperationCanceledException>(
        () => MsSqlClient.ConnectAsync(options).AsTask());
      Assert.IsTrue(loginReceived.Task.IsCompleted);
      Assert.IsTrue(
        System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(3));
    }
    finally
    {
      release.TrySetResult();
      await server.WaitAsync(TimeSpan.FromSeconds(5));
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task ReaderDeliversSplitRowBeforeEndOfMessage()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    TaskCompletionSource firstRowWritten =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseRemainder =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task server = RunSplitRowsServerAsync(
      listener,
      firstRowWritten,
      releaseRemainder.Task);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      await using ISqlRowReader reader =
        await connection.ExecuteReaderAsync("SELECT value");
      Task<bool> firstRead = reader.ReadAsync().AsTask();
      await firstRowWritten.Task;

      Task completed = await Task.WhenAny(firstRead, Task.Delay(TimeSpan.FromSeconds(2)));
      Assert.AreSame(firstRead, completed, "The first row waited for END_OF_MESSAGE.");
      Assert.IsTrue(await firstRead);
      Assert.AreEqual(1, reader.GetInt32(0));

      releaseRemainder.SetResult();
      Assert.IsTrue(await reader.ReadAsync());
      Assert.AreEqual(2, reader.GetInt32(0));
      Assert.IsFalse(await reader.ReadAsync());
      await server;
    }
    finally
    {
      releaseRemainder.TrySetResult();
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task StreamKeepsRowsWithTheirResultSetMetadata()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = RunMultiResultServerAsync(listener);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      List<SqlRow> rows = [];
      await foreach (SqlRow row in connection.StreamAsync("SELECT 1; SELECT N'x'"))
      {
        rows.Add(row);
      }

      Assert.HasCount(2, rows);
      Assert.AreEqual(0, rows[0].GetOrdinal("a"));
      Assert.AreEqual(1, rows[0].GetInt32(0));
      Assert.AreEqual(0, rows[1].GetOrdinal("b"));
      Assert.AreEqual("x", rows[1].GetString(0));
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task ReaderDecodesPlpValueSplitAcrossPackets()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = RunFragmentedJsonServerAsync(listener);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      await using ISqlRowReader reader =
        await connection.ExecuteReaderAsync("SELECT payload");

      Assert.IsTrue(await reader.ReadAsync());
      Assert.AreEqual("""{"split":true}""", reader.GetString(0));
      Assert.IsFalse(await reader.ReadAsync());
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task PreparedLifecycleUsesOnePrepexecThenExecuteAndUnprepare()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = RunPreparedLifecycleServerAsync(listener, fragmentedFirst: false);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      ISqlPreparedStatement unused = await connection.PrepareAsync("SELECT @P1");
      await unused.DisposeAsync();

      ISqlPreparedStatement statement = await connection.PrepareAsync("SELECT @P1");
      Task<SqlRowSet> first =
        statement.QueryAsync(SqlParameters.Create(41)).AsTask();
      Task<SqlRowSet> second =
        statement.QueryAsync(SqlParameters.Create(42)).AsTask();
      Task dispose = statement.DisposeAsync().AsTask();
      SqlRowSet[] results = await Task.WhenAll(first, second);

      Assert.AreEqual(41, results[0][0].GetInt32(0));
      Assert.AreEqual(42, results[1][0].GetInt32(0));
      await dispose;
      Assert.AreEqual(
        7,
        (await connection.QueryAsync("SELECT 7"))[0].GetInt32(0));
      await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
        () => statement.QueryAsync(SqlParameters.Create(43)).AsTask());
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task FirstPreparedReaderCapturesFragmentedReturnHandle()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = RunPreparedLifecycleServerAsync(listener, fragmentedFirst: true);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      ISqlPreparedStatement statement = await connection.PrepareAsync("SELECT @P1");
      await using (ISqlRowReader reader =
                   await statement.ExecuteReaderAsync(SqlParameters.Create(41)))
      {
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(41, reader.GetInt32(0));
        Assert.IsFalse(await reader.ReadAsync());
      }

      SqlRowSet second = await statement.QueryAsync(SqlParameters.Create(42));
      Assert.AreEqual(42, second[0].GetInt32(0));
      await statement.DisposeAsync();
      Assert.AreEqual(
        7,
        (await connection.QueryAsync("SELECT 7"))[0].GetInt32(0));
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  [TestMethod]
  public async Task ReaderBreaksConnectionWhenIncrementalParsingFails()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = RunUnsupportedTypeServerAsync(listener);
    try
    {
      await using MsSqlConnection connection = await MsSqlClient.ConnectAsync(
        TestOptions(port));
      ISqlRowReader reader =
        await connection.ExecuteReaderAsync("SELECT CAST(1 AS sql_variant)");

      await Assert.ThrowsExactlyAsync<NotSupportedException>(
        () => reader.ReadAsync().AsTask());
      Assert.IsFalse(connection.IsUsable);
      await Assert.ThrowsExactlyAsync<NotSupportedException>(
        () => reader.DisposeAsync().AsTask());
      await server;
    }
    finally
    {
      listener.Stop();
    }
  }

  private static MsSqlConnectOptions TestOptions(int port) =>
    new()
    {
      Host = "127.0.0.1",
      Port = port,
      Username = "sa",
      Password = "password",
      Database = "master",
      EncryptionMode = MsSqlEncryptionMode.Disable,
      ConnectTimeout = TimeSpan.FromSeconds(5),
    };

  private static async Task RunQueryServerAsync(TcpListener listener)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsMessage query = await new TdsPacketReader(stream).ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);
    using TdsPacketWriter writer = new(stream, 4096);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildIntResult(42),
      default);
  }

  private static async Task RunPreparedLifecycleServerAsync(
    TcpListener listener,
    bool fragmentedFirst)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsPacketReader reader = new(stream);

    TdsMessage first = await reader.ReadMessageAsync(default);
    AssertPreparedRequest(
      first,
      TdsProcedureId.PrepExec,
      expectedHandle: 0,
      expectedValue: 41);
    byte[] firstResponse = BuildPreparedIntResult(41, preparedHandle: 73);
    if (fragmentedFirst)
    {
      int position = 0;
      byte packetId = 1;
      while (position < firstResponse.Length)
      {
        int count = Math.Min(3, firstResponse.Length - position);
        bool final = position + count == firstResponse.Length;
        await WritePacketAsync(
          stream,
          firstResponse.AsMemory(position, count),
          final,
          packetId++);
        position += count;
      }
    }
    else
    {
      using TdsPacketWriter firstWriter = new(stream, 4096);
      await firstWriter.WriteMessageAsync(
        TdsMessageType.TabularResult,
        firstResponse,
        default);
    }

    TdsMessage second = await reader.ReadMessageAsync(default);
    AssertPreparedRequest(
      second,
      TdsProcedureId.Execute,
      expectedHandle: 73,
      expectedValue: 42);
    using TdsPacketWriter writer = new(stream, 4096);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildPreparedIntResult(42, preparedHandle: null),
      default);

    TdsMessage close = await reader.ReadMessageAsync(default);
    AssertPreparedRequest(
      close,
      TdsProcedureId.Unprepare,
      expectedHandle: 73,
      expectedValue: null);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildRpcDone(),
      default);

    TdsMessage reuse = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, reuse.Type);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildIntResult(7),
      default);
  }

  private static async Task RunCancellationServerAsync(
    TcpListener listener,
    TaskCompletionSource queryReceived)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsPacketReader reader = new(stream);
    TdsMessage query = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);
    queryReceived.SetResult();

    TdsMessage attention = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.Attention, attention.Type);
    using TdsPacketWriter writer = new(stream, 4096);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildAttentionAck(),
      default);

    TdsMessage second = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, second.Type);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildIntResult(7),
      default);
  }

  private static async Task RunRowsServerAsync(
    TcpListener listener,
    params int[] values)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsMessage query = await new TdsPacketReader(stream).ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);
    using TdsPacketWriter writer = new(stream, 4096);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildIntResult(values),
      default);
  }

  private static async Task RunBinaryRowsServerAsync(
    TcpListener listener,
    params byte[][] values)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsMessage query = await new TdsPacketReader(stream).ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);
    using TdsPacketWriter writer = new(stream, 4096);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildBinaryResult(values),
      default);
  }

  private static async Task RunStalledLoginServerAsync(
    TcpListener listener,
    TaskCompletionSource loginReceived,
    Task release)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    TdsPacketReader reader = new(stream);
    TdsMessage preLogin = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.PreLogin, preLogin.Type);
    using (TdsPacketWriter writer = new(stream, 4096))
    {
      await writer.WriteMessageAsync(
        TdsMessageType.TabularResult,
        TdsPreLogin.Encode(TdsEncryptionLevel.NotSupported),
        default);
    }

    TdsMessage login = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.Login7, login.Type);
    loginReceived.SetResult();
    await release;
  }

  private static async Task RunSplitRowsServerAsync(
    TcpListener listener,
    TaskCompletionSource firstRowWritten,
    Task releaseRemainder)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsMessage query = await new TdsPacketReader(stream).ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);

    byte[] response = BuildIntResult(1, 2);
    const int metadataSplit = 5;
    const int firstRowValueSplit = 24;
    const int firstRowEnd = 26;
    await WritePacketAsync(stream, response.AsMemory(0, metadataSplit), false, 1);
    await WritePacketAsync(
      stream,
      response.AsMemory(metadataSplit, firstRowValueSplit - metadataSplit),
      false,
      2);
    await WritePacketAsync(
      stream,
      response.AsMemory(firstRowValueSplit, firstRowEnd - firstRowValueSplit),
      false,
      3);
    firstRowWritten.SetResult();
    await releaseRemainder;
    await WritePacketAsync(stream, response.AsMemory(firstRowEnd), true, 4);
  }

  private static async Task RunMultiResultServerAsync(TcpListener listener)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsMessage query = await new TdsPacketReader(stream).ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);
    using TdsPacketWriter writer = new(stream, 4096);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildMultiResult(),
      default);
  }

  private static async Task RunFragmentedJsonServerAsync(TcpListener listener)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsMessage query = await new TdsPacketReader(stream).ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);

    byte[] response = BuildJsonResult("""{"split":true}""");
    int position = 0;
    byte packetId = 1;
    while (position < response.Length)
    {
      int count = Math.Min(3, response.Length - position);
      bool final = position + count == response.Length;
      await WritePacketAsync(
        stream,
        response.AsMemory(position, count),
        final,
        packetId++);
      position += count;
    }
  }

  private static async Task RunUnsupportedTypeServerAsync(TcpListener listener)
  {
    using TcpClient client = await listener.AcceptTcpClientAsync();
    await using NetworkStream stream = client.GetStream();
    await LoginAsync(stream);
    TdsMessage query = await new TdsPacketReader(stream).ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.SqlBatch, query.Type);

    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.ColumnMetadata);
    response.WriteUInt16LittleEndian(1);
    response.WriteUInt32LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteByte(0x62);
    response.WriteBVarChar("unsupported");
    WriteDone(response, 0);
    using TdsPacketWriter writer = new(stream, 4096);
    await writer.WriteMessageAsync(
      TdsMessageType.TabularResult,
      response.WrittenMemory,
      default);
  }

  private static async Task LoginAsync(NetworkStream stream)
  {
    TdsPacketReader reader = new(stream);
    TdsMessage preLogin = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.PreLogin, preLogin.Type);
    using (TdsPacketWriter writer = new(stream, 4096))
    {
      await writer.WriteMessageAsync(
        TdsMessageType.TabularResult,
        TdsPreLogin.Encode(TdsEncryptionLevel.NotSupported),
        default);
    }

    TdsMessage login = await reader.ReadMessageAsync(default);
    Assert.AreEqual(TdsMessageType.Login7, login.Type);
    using TdsPacketWriter loginWriter = new(stream, 4096);
    await loginWriter.WriteMessageAsync(
      TdsMessageType.TabularResult,
      BuildLoginAck(),
      default);
  }

  private static byte[] BuildLoginAck()
  {
    ArrayBufferWriter<byte> body = new();
    body.WriteByte(1);
    body.Write("\x04\x00\x00\x74"u8);
    body.WriteBVarChar("SQL Server");
    body.WriteByte(16);
    body.WriteByte(0);
    body.WriteUInt16BigEndian(1000);

    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.LoginAck);
    response.WriteUInt16LittleEndian(checked((ushort)body.WrittenCount));
    response.Write(body.WrittenSpan);
    WriteDone(response, 0);
    return response.WrittenMemory.ToArray();
  }

  private static byte[] BuildIntResult(params int[] values)
  {
    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.ColumnMetadata);
    response.WriteUInt16LittleEndian(1);
    response.WriteUInt32LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteByte(TdsDataType.Int4);
    response.WriteBVarChar("value");
    foreach (int value in values)
    {
      response.WriteByte(TdsTokenType.Row);
      response.WriteInt32LittleEndian(value);
    }
    WriteDone(response, 0);
    return response.WrittenMemory.ToArray();
  }

  private static byte[] BuildBinaryResult(params byte[][] values)
  {
    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.ColumnMetadata);
    response.WriteUInt16LittleEndian(1);
    response.WriteUInt32LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteByte(TdsDataType.BigVarBinary);
    response.WriteUInt16LittleEndian(8000);
    response.WriteBVarChar("value");
    foreach (byte[] value in values)
    {
      response.WriteByte(TdsTokenType.Row);
      response.WriteUInt16LittleEndian(checked((ushort)value.Length));
      response.Write(value);
    }

    WriteDone(response, 0);
    return response.WrittenMemory.ToArray();
  }

  private static byte[] BuildPreparedIntResult(
    int value,
    int? preparedHandle)
  {
    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.ColumnMetadata);
    response.WriteUInt16LittleEndian(1);
    response.WriteUInt32LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteByte(TdsDataType.Int4);
    response.WriteBVarChar("value");
    response.WriteByte(TdsTokenType.Row);
    response.WriteInt32LittleEndian(value);
    WriteDone(response, (ushort)TdsDoneStatus.More);
    response.WriteByte(TdsTokenType.ReturnStatus);
    response.WriteInt32LittleEndian(0);
    if (preparedHandle is int handle)
    {
      response.WriteByte(TdsTokenType.ReturnValue);
      response.WriteUInt16LittleEndian(2);
      response.WriteBVarChar("@ignored");
      response.WriteByte(1);
      response.WriteUInt32LittleEndian(0);
      response.WriteUInt16LittleEndian(0);
      response.WriteByte(TdsDataType.IntN);
      response.WriteByte(sizeof(int));
      response.WriteByte(sizeof(int));
      response.WriteInt32LittleEndian(999);

      response.WriteByte(TdsTokenType.ReturnValue);
      response.WriteUInt16LittleEndian(1);
      response.WriteBVarChar(string.Empty);
      response.WriteByte(1);
      response.WriteUInt32LittleEndian(0);
      response.WriteUInt16LittleEndian(0);
      response.WriteByte(TdsDataType.IntN);
      response.WriteByte(sizeof(int));
      response.WriteByte(sizeof(int));
      response.WriteInt32LittleEndian(handle);
    }

    response.WriteByte(TdsTokenType.DoneProc);
    response.WriteUInt16LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteInt64LittleEndian(0);
    return response.WrittenMemory.ToArray();
  }

  private static byte[] BuildRpcDone()
  {
    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.ReturnStatus);
    response.WriteInt32LittleEndian(0);
    response.WriteByte(TdsTokenType.DoneProc);
    response.WriteUInt16LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteInt64LittleEndian(0);
    return response.WrittenMemory.ToArray();
  }

  private static void AssertPreparedRequest(
    TdsMessage message,
    ushort procedureId,
    int expectedHandle,
    int? expectedValue)
  {
    Assert.AreEqual(TdsMessageType.Rpc, message.Type);
    TdsPayloadReader reader = new(message.Payload.Span);
    reader.Skip(22);
    Assert.AreEqual(ushort.MaxValue, reader.ReadUInt16LittleEndian());
    Assert.AreEqual(procedureId, reader.ReadUInt16LittleEndian());
    Assert.AreEqual(0, reader.ReadUInt16LittleEndian());
    Assert.AreEqual(expectedHandle, ReadIntParameter(ref reader, output: procedureId != TdsProcedureId.Unprepare));

    if (procedureId == TdsProcedureId.PrepExec)
    {
      Assert.AreEqual("@P1 int", ReadNVarCharParameter(ref reader));
      Assert.AreEqual("SELECT @P1", ReadNVarCharParameter(ref reader));
    }

    if (expectedValue is int value)
    {
      Assert.AreEqual(value, ReadIntParameter(ref reader, output: false));
    }

    Assert.AreEqual(0, reader.Remaining);
  }

  private static int ReadIntParameter(
    ref TdsPayloadReader reader,
    bool output)
  {
    _ = reader.ReadBVarChar();
    Assert.AreEqual(output ? 1 : 0, reader.ReadByte());
    Assert.AreEqual(TdsDataType.IntN, reader.ReadByte());
    Assert.AreEqual(sizeof(int), reader.ReadByte());
    Assert.AreEqual(sizeof(int), reader.ReadByte());
    return reader.ReadInt32LittleEndian();
  }

  private static string ReadNVarCharParameter(ref TdsPayloadReader reader)
  {
    _ = reader.ReadBVarChar();
    Assert.AreEqual(0, reader.ReadByte());
    Assert.AreEqual(TdsDataType.NVarChar, reader.ReadByte());
    Assert.AreEqual(8000, reader.ReadUInt16LittleEndian());
    reader.Skip(5);
    int byteLength = reader.ReadUInt16LittleEndian();
    return System.Text.Encoding.Unicode.GetString(reader.ReadSpan(byteLength));
  }

  private static byte[] BuildAttentionAck()
  {
    ArrayBufferWriter<byte> response = new();
    WriteDone(response, (ushort)TdsDoneStatus.Attention);
    return response.WrittenMemory.ToArray();
  }

  private static byte[] BuildMultiResult()
  {
    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.ColumnMetadata);
    response.WriteUInt16LittleEndian(1);
    response.WriteUInt32LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteByte(TdsDataType.Int4);
    response.WriteBVarChar("a");
    response.WriteByte(TdsTokenType.Row);
    response.WriteInt32LittleEndian(1);
    WriteDone(response, (ushort)TdsDoneStatus.More);

    response.WriteByte(TdsTokenType.ColumnMetadata);
    response.WriteUInt16LittleEndian(1);
    response.WriteUInt32LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteByte(TdsDataType.NVarChar);
    response.WriteUInt16LittleEndian(20);
    response.WriteUInt32LittleEndian(0x0409);
    response.WriteByte(0);
    response.WriteBVarChar("b");
    response.WriteByte(TdsTokenType.Row);
    response.WriteUInt16LittleEndian(2);
    response.WriteUtf16("x");
    WriteDone(response, 0);
    return response.WrittenMemory.ToArray();
  }

  private static byte[] BuildJsonResult(string json)
  {
    byte[] value = System.Text.Encoding.UTF8.GetBytes(json);
    ArrayBufferWriter<byte> response = new();
    response.WriteByte(TdsTokenType.ColumnMetadata);
    response.WriteUInt16LittleEndian(1);
    response.WriteUInt32LittleEndian(0);
    response.WriteUInt16LittleEndian(0);
    response.WriteByte(TdsDataType.Json);
    response.WriteBVarChar("payload");
    response.WriteByte(TdsTokenType.Row);
    response.WriteUInt64LittleEndian(checked((ulong)value.Length));
    response.WriteUInt32LittleEndian(checked((uint)value.Length));
    response.Write(value);
    response.WriteUInt32LittleEndian(0);
    WriteDone(response, 0);
    return response.WrittenMemory.ToArray();
  }

  private static async ValueTask WritePacketAsync(
    Stream stream,
    ReadOnlyMemory<byte> payload,
    bool endOfMessage,
    byte packetId)
  {
    byte[] header = new byte[8];
    header[0] = TdsMessageType.TabularResult;
    header[1] = endOfMessage ? (byte)1 : (byte)0;
    BinaryPrimitives.WriteUInt16BigEndian(
      header.AsSpan(2),
      checked((ushort)(payload.Length + header.Length)));
    header[6] = packetId;
    await stream.WriteAsync(header);
    await stream.WriteAsync(payload);
    await stream.FlushAsync();
  }

  private static void WriteDone(ArrayBufferWriter<byte> response, ushort status)
  {
    response.WriteByte(TdsTokenType.Done);
    response.WriteUInt16LittleEndian(status);
    response.WriteUInt16LittleEndian(0);
    response.WriteInt64LittleEndian(0);
  }
}

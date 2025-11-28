// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Xunit;

namespace Vertx.PgClient.Tests;

/// <summary>
/// Tests for PostgreSQL LISTEN/NOTIFY functionality.
/// Notifications are received during query execution, so we trigger a 
/// lightweight query after sending notifications to receive them.
/// </summary>
public class NotificationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public NotificationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 30000)]
    public async Task ListenNotify_ReceivesNotification()
    {
        var channelName = $"test_channel_{Guid.NewGuid():N}";
        var receivedNotifications = new List<PgNotification>();

        await using var listener = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        listener.NotificationReceived += notification =>
        {
            receivedNotifications.Add(notification);
        };

        // Start listening
        await listener.QueryAsync($"LISTEN {channelName}");

        // Send notification from another connection
        await using var notifier = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await notifier.QueryAsync($"NOTIFY {channelName}, 'hello world'");

        // Trigger a read on the listener to receive pending notifications
        await listener.QueryAsync("SELECT 1");

        Assert.Single(receivedNotifications);
        Assert.Equal(channelName, receivedNotifications[0].Channel);
        Assert.Equal("hello world", receivedNotifications[0].Payload);
    }

    [Fact(Timeout = 30000)]
    public async Task ListenNotify_ReceivesMultipleNotifications()
    {
        var channelName = $"test_channel_{Guid.NewGuid():N}";
        var receivedNotifications = new List<PgNotification>();

        await using var listener = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        listener.NotificationReceived += notification =>
        {
            receivedNotifications.Add(notification);
        };

        await listener.QueryAsync($"LISTEN {channelName}");

        // Send multiple notifications
        await using var notifier = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await notifier.QueryAsync($"NOTIFY {channelName}, 'message1'");
        await notifier.QueryAsync($"NOTIFY {channelName}, 'message2'");
        await notifier.QueryAsync($"NOTIFY {channelName}, 'message3'");

        // Trigger a read on the listener to receive pending notifications
        await listener.QueryAsync("SELECT 1");

        Assert.Equal(3, receivedNotifications.Count);
        Assert.Equal("message1", receivedNotifications[0].Payload);
        Assert.Equal("message2", receivedNotifications[1].Payload);
        Assert.Equal("message3", receivedNotifications[2].Payload);
    }

    [Fact(Timeout = 30000)]
    public async Task ListenNotify_EmptyPayload()
    {
        var channelName = $"test_channel_{Guid.NewGuid():N}";
        var receivedNotifications = new List<PgNotification>();

        await using var listener = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        listener.NotificationReceived += notification =>
        {
            receivedNotifications.Add(notification);
        };

        await listener.QueryAsync($"LISTEN {channelName}");

        // Send notification without payload
        await using var notifier = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await notifier.QueryAsync($"NOTIFY {channelName}");

        // Trigger a read on the listener
        await listener.QueryAsync("SELECT 1");

        Assert.Single(receivedNotifications);
        Assert.Equal(channelName, receivedNotifications[0].Channel);
        Assert.Equal("", receivedNotifications[0].Payload);
    }

    [Fact(Timeout = 30000)]
    public async Task ListenNotify_MultipleChannels()
    {
        var channel1 = $"test_channel1_{Guid.NewGuid():N}";
        var channel2 = $"test_channel2_{Guid.NewGuid():N}";
        var receivedNotifications = new List<PgNotification>();

        await using var listener = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        listener.NotificationReceived += notification =>
        {
            receivedNotifications.Add(notification);
        };

        // Listen on multiple channels
        await listener.QueryAsync($"LISTEN {channel1}");
        await listener.QueryAsync($"LISTEN {channel2}");

        // Send to both channels
        await using var notifier = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await notifier.QueryAsync($"NOTIFY {channel1}, 'from channel 1'");
        await notifier.QueryAsync($"NOTIFY {channel2}, 'from channel 2'");

        // Trigger a read on the listener
        await listener.QueryAsync("SELECT 1");

        Assert.Equal(2, receivedNotifications.Count);
        Assert.Contains(receivedNotifications, n => n.Channel == channel1 && n.Payload == "from channel 1");
        Assert.Contains(receivedNotifications, n => n.Channel == channel2 && n.Payload == "from channel 2");
    }

    [Fact(Timeout = 30000)]
    public async Task Unlisten_StopsReceivingNotifications()
    {
        var channelName = $"test_channel_{Guid.NewGuid():N}";
        var receivedNotifications = new List<PgNotification>();

        await using var listener = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        listener.NotificationReceived += notification =>
        {
            receivedNotifications.Add(notification);
        };

        await listener.QueryAsync($"LISTEN {channelName}");

        // Send first notification
        await using var notifier = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await notifier.QueryAsync($"NOTIFY {channelName}, 'before unlisten'");
        
        // Trigger a read to receive the notification
        await listener.QueryAsync("SELECT 1");

        // Unlisten
        await listener.QueryAsync($"UNLISTEN {channelName}");

        // Send another notification
        await notifier.QueryAsync($"NOTIFY {channelName}, 'after unlisten'");
        
        // Trigger another read
        await listener.QueryAsync("SELECT 1");

        // Should only have received the first notification
        Assert.Single(receivedNotifications);
        Assert.Equal("before unlisten", receivedNotifications[0].Payload);
    }

    [Fact(Timeout = 30000)]
    public async Task ListenNotify_SpecialCharactersInPayload()
    {
        var channelName = $"test_channel_{Guid.NewGuid():N}";
        var receivedNotifications = new List<PgNotification>();

        await using var listener = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        listener.NotificationReceived += notification =>
        {
            receivedNotifications.Add(notification);
        };

        await listener.QueryAsync($"LISTEN {channelName}");

        // Send notification with special characters (using prepared query for safety)
        var payload = "Hello 'World' with \"quotes\" and \\ backslash and unicode: 你好";
        await using var notifier = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        await notifier.PreparedQueryAsync($"SELECT pg_notify($1, $2)", Tuple.Create(channelName, payload));

        // Trigger a read on the listener
        await listener.QueryAsync("SELECT 1");

        Assert.Single(receivedNotifications);
        Assert.Equal(payload, receivedNotifications[0].Payload);
    }

    [Fact(Timeout = 30000)]
    public async Task Notification_IncludesProcessId()
    {
        var channelName = $"test_channel_{Guid.NewGuid():N}";
        var receivedNotifications = new List<PgNotification>();

        await using var listener = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        
        listener.NotificationReceived += notification =>
        {
            receivedNotifications.Add(notification);
        };

        await listener.QueryAsync($"LISTEN {channelName}");

        await using var notifier = await PgConnection.ConnectAsync(_fixture.CreateConnectOptions());
        var notifierPid = notifier.ProcessId;
        await notifier.QueryAsync($"NOTIFY {channelName}, 'test'");

        // Trigger a read on the listener
        await listener.QueryAsync("SELECT 1");

        Assert.Single(receivedNotifications);
        Assert.Equal(notifierPid, receivedNotifications[0].ProcessId);
    }
}

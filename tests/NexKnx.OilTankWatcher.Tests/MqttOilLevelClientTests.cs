using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Packets;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Mqtt;

namespace NexKnx.OilTankWatcher.Tests;

/// <summary>
/// Hand-geschriebenes Test-Double für MQTTnets <see cref="IMqttClient"/> -
/// selbst ein von der Bibliothek bereitgestelltes Interface und daher ohne
/// eigene Testnaht fakebar (siehe interner Konstruktor von
/// <see cref="MqttOilLevelClient"/>).
/// </summary>
public sealed class FakeMqttClient : IMqttClient
{
    public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
    public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;

    // Von IMqttClient vorgeschrieben, aber von diesem Fake nie ausgeloest -
    // CS0067 ("Ereignis wird nie verwendet") ist hier unvermeidbar und erwartet.
#pragma warning disable CS0067
    public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;
    public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;
    public event Func<MQTTnet.Diagnostics.PacketInspection.InspectMqttPacketEventArgs, Task>? InspectPacketAsync;
#pragma warning restore CS0067

    public bool IsConnected { get; private set; }
    public MqttClientOptions? Options { get; private set; }

    public int ConnectCalls { get; private set; }
    public int SubscribeCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public int DisconnectCalls { get; private set; }

    /// <summary>
    /// Wirft bei den ersten N Aufrufen von <see cref="ConnectAsync"/> die
    /// konfigurierte Exception, bevor ein Erfolg simuliert wird.
    /// </summary>
    public int FailuresBeforeSuccess { get; set; }

    public Exception ConnectException { get; set; } = new InvalidOperationException("Simulierter Verbindungsfehler.");

    public Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken = default)
    {
        Options = options;
        ConnectCalls++;

        if (ConnectCalls <= FailuresBeforeSuccess)
        {
            throw ConnectException;
        }

        IsConnected = true;
        return Task.FromResult(new MqttClientConnectResult());
    }

    public Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken = default)
    {
        DisconnectCalls++;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task PingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage applicationMessage, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MqttClientPublishResult(null, MQTTnet.MqttClientPublishReasonCode.Success, null, []));

    public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken = default)
    {
        SubscribeCalls++;
        return Task.FromResult(new MqttClientSubscribeResult(0, [], null, []));
    }

    public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MqttClientUnsubscribeResult(0, [], null, []));

    public void RaiseMessageReceived(string topic, string payload)
    {
        var message = new MqttApplicationMessage
        {
            Topic = topic,
            PayloadSegment = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)),
        };
        var args = new MqttApplicationMessageReceivedEventArgs(
            "fake-client",
            message,
            new MqttPublishPacket(),
            (_, _) => Task.CompletedTask);

        ApplicationMessageReceivedAsync?.Invoke(args);
    }

    public void RaiseDisconnected() =>
        DisconnectedAsync?.Invoke(new MqttClientDisconnectedEventArgs(
            false,
            null,
            MqttClientDisconnectReason.NormalDisconnection,
            null,
            null,
            null));

    public void Dispose() => DisposeCalls++;
}

public class MqttOilLevelClientTests
{
    private static MqttOptions CreateOptions() => new()
    {
        Host = "test-host",
        Port = 1883,
        Topic = "oiltank/test",
        ClientId = "test-client",
        // Minimal gehalten, damit Backoff-Wartezeiten im Test nicht spürbar sind.
        ReconnectDelaySeconds = 0,
        MaxReconnectDelaySeconds = 0,
    };

    [Fact]
    public async Task StartAsync_ConnectsAndSubscribes_OnFirstTry()
    {
        var fakeClient = new FakeMqttClient();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);

        await client.StartAsync(CancellationToken.None);

        Assert.Equal(1, fakeClient.ConnectCalls);
        Assert.Equal(1, fakeClient.SubscribeCalls);
    }

    [Fact]
    public async Task StartAsync_RetriesWithBackoff_UntilConnectSucceeds()
    {
        var fakeClient = new FakeMqttClient { FailuresBeforeSuccess = 3 };
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);

        await client.StartAsync(CancellationToken.None);

        Assert.Equal(4, fakeClient.ConnectCalls);
        Assert.Equal(1, fakeClient.SubscribeCalls);
    }

    [Fact]
    public async Task StartAsync_StopsRetrying_WhenAlreadyCancelled()
    {
        // Hinweis: Ein Abbruch, der genau während des "await Task.Delay(...)"
        // innerhalb der catch-Klausel von ConnectWithRetryAsync eintrifft,
        // wird von der vorhandenen Implementierung NICHT abgefangen (der
        // umgebende "catch (OperationCanceledException)" deckt nur den
        // try-Block ab, nicht den Delay-Aufruf innerhalb des ersten catch-
        // Blocks) - ein vorbestehendes, hier nicht behobenes Verhalten.
        // Dieser Test prüft daher den unkritischen Fall: der Token ist schon
        // vor dem ersten Verbindungsversuch abgebrochen.
        var fakeClient = new FakeMqttClient { FailuresBeforeSuccess = int.MaxValue };
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await client.StartAsync(cts.Token);

        Assert.Equal(0, fakeClient.ConnectCalls);
        Assert.Equal(0, fakeClient.SubscribeCalls);
    }

    [Fact]
    public async Task MessageReceived_ValidPayload_RaisesValueReceived()
    {
        var fakeClient = new FakeMqttClient();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);
        double? received = null;
        client.ValueReceived += value => received = value;

        await client.StartAsync(CancellationToken.None);
        fakeClient.RaiseMessageReceived("oiltank/test", "73.4");

        Assert.Equal(73.4, received);
    }

    [Fact]
    public async Task MessageReceived_UnparsablePayload_DoesNotRaiseValueReceived()
    {
        var fakeClient = new FakeMqttClient();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);
        var receivedCount = 0;
        client.ValueReceived += _ => receivedCount++;

        await client.StartAsync(CancellationToken.None);
        fakeClient.RaiseMessageReceived("oiltank/test", "not-a-number");

        Assert.Equal(0, receivedCount);
    }

    [Fact]
    public async Task Disconnected_WhileNotCancelled_TriggersReconnect()
    {
        var fakeClient = new FakeMqttClient();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);

        await client.StartAsync(CancellationToken.None);
        Assert.Equal(1, fakeClient.ConnectCalls);

        fakeClient.RaiseDisconnected();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (fakeClient.ConnectCalls < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, fakeClient.ConnectCalls);
    }

    [Fact]
    public async Task DisposeAsync_WhileConnected_DisconnectsAndDisposesUnderlyingClient()
    {
        var fakeClient = new FakeMqttClient();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);
        await client.StartAsync(CancellationToken.None);

        await client.DisposeAsync();

        Assert.Equal(1, fakeClient.DisconnectCalls);
        Assert.Equal(1, fakeClient.DisposeCalls);
    }

    [Fact]
    public async Task DisposeAsync_WhileNotConnected_DoesNotCallDisconnect()
    {
        var fakeClient = new FakeMqttClient { FailuresBeforeSuccess = int.MaxValue };
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, fakeClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await client.StartAsync(cts.Token);

        await client.DisposeAsync();

        Assert.Equal(0, fakeClient.DisconnectCalls);
        Assert.Equal(1, fakeClient.DisposeCalls);
    }
}

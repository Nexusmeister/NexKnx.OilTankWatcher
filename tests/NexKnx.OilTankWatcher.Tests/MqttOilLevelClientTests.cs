using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Packets;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Mqtt;

namespace NexKnx.OilTankWatcher.Tests;

/// <summary>
/// NSubstitute-Test-Double für MQTTnets <see cref="IMqttClient"/> - selbst ein
/// von der Bibliothek bereitgestelltes Interface und daher ohne eigene
/// Testnaht fakebar (siehe interner Konstruktor von
/// <see cref="MqttOilLevelClient"/>).
/// </summary>
file sealed class MqttClientHarness
{
    private int _connectCalls;
    private int _subscribeCalls;
    private int _disconnectCalls;
    private bool _isConnected;

    public IMqttClient Client { get; }

    public int ConnectCalls => _connectCalls;
    public int SubscribeCalls => _subscribeCalls;
    public int DisconnectCalls => _disconnectCalls;

    /// <summary>
    /// Wirft bei den ersten N Aufrufen von <c>ConnectAsync</c> die
    /// konfigurierte Exception, bevor ein Erfolg simuliert wird.
    /// </summary>
    public int FailuresBeforeSuccess { get; set; }

    public Exception ConnectException { get; set; } = new InvalidOperationException("Simulierter Verbindungsfehler.");

    public MqttClientHarness()
    {
        Client = Substitute.For<IMqttClient>();

        Client.IsConnected.Returns(_ => _isConnected);

        Client.ConnectAsync(Arg.Any<MqttClientOptions>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _connectCalls++;

            if (_connectCalls <= FailuresBeforeSuccess)
            {
                throw ConnectException;
            }

            _isConnected = true;
            return Task.FromResult(new MqttClientConnectResult());
        });

        Client.DisconnectAsync(Arg.Any<MqttClientDisconnectOptions>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _disconnectCalls++;
            _isConnected = false;
            return Task.CompletedTask;
        });

        Client.SubscribeAsync(Arg.Any<MqttClientSubscribeOptions>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _subscribeCalls++;
            return Task.FromResult(new MqttClientSubscribeResult(0, [], null, []));
        });
    }

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

        Client.ApplicationMessageReceivedAsync += Raise.Event<Func<MqttApplicationMessageReceivedEventArgs, Task>>(args);
    }

    public void RaiseDisconnected() =>
        Client.DisconnectedAsync += Raise.Event<Func<MqttClientDisconnectedEventArgs, Task>>(
            new MqttClientDisconnectedEventArgs(
                false,
                null,
                MqttClientDisconnectReason.NormalDisconnection,
                null,
                null,
                null));
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
        var harness = new MqttClientHarness();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);

        await client.StartAsync(CancellationToken.None);

        harness.ConnectCalls.ShouldBe(1);
        harness.SubscribeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task StartAsync_RetriesWithBackoff_UntilConnectSucceeds()
    {
        var harness = new MqttClientHarness { FailuresBeforeSuccess = 3 };
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);

        await client.StartAsync(CancellationToken.None);

        harness.ConnectCalls.ShouldBe(4);
        harness.SubscribeCalls.ShouldBe(1);
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
        var harness = new MqttClientHarness { FailuresBeforeSuccess = int.MaxValue };
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await client.StartAsync(cts.Token);

        harness.ConnectCalls.ShouldBe(0);
        harness.SubscribeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task MessageReceived_ValidPayload_RaisesValueReceived()
    {
        var harness = new MqttClientHarness();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);
        double? received = null;
        client.ValueReceived += value => received = value;

        await client.StartAsync(CancellationToken.None);
        harness.RaiseMessageReceived("oiltank/test", "73.4");

        received.ShouldBe(73.4);
    }

    [Fact]
    public async Task MessageReceived_UnparsablePayload_DoesNotRaiseValueReceived()
    {
        var harness = new MqttClientHarness();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);
        var receivedCount = 0;
        client.ValueReceived += _ => receivedCount++;

        await client.StartAsync(CancellationToken.None);
        harness.RaiseMessageReceived("oiltank/test", "not-a-number");

        receivedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Disconnected_WhileNotCancelled_TriggersReconnect()
    {
        var harness = new MqttClientHarness();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);

        await client.StartAsync(CancellationToken.None);
        harness.ConnectCalls.ShouldBe(1);

        harness.RaiseDisconnected();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (harness.ConnectCalls < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        harness.ConnectCalls.ShouldBe(2);
    }

    [Fact]
    public async Task DisposeAsync_WhileConnected_DisconnectsAndDisposesUnderlyingClient()
    {
        var harness = new MqttClientHarness();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);
        await client.StartAsync(CancellationToken.None);

        await client.DisposeAsync();

        harness.DisconnectCalls.ShouldBe(1);
        harness.Client.Received(1).Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WhileNotConnected_DoesNotCallDisconnect()
    {
        var harness = new MqttClientHarness { FailuresBeforeSuccess = int.MaxValue };
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await client.StartAsync(cts.Token);

        await client.DisposeAsync();

        harness.DisconnectCalls.ShouldBe(0);
        harness.Client.Received(1).Dispose();
    }

    [Fact]
    public void Constructor_WithRealMqttClientFactory_DoesNotThrow()
    {
        // Deckt den öffentlichen, von der DI verwendeten Konstruktor ab, der
        // einen echten MQTTnet-Client per MqttClientFactory erzeugt - dabei
        // wird noch keine Netzwerkverbindung aufgebaut (erst StartAsync tut das).
        Should.NotThrow(() => new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance));
    }

    [Fact]
    public async Task StartAsync_WithCredentials_ConnectsSuccessfully()
    {
        var options = CreateOptions();
        options.Username = "user";
        options.Password = "pass";
        var harness = new MqttClientHarness();
        var client = new MqttOilLevelClient(options, NullLogger<MqttOilLevelClient>.Instance, harness.Client);

        await client.StartAsync(CancellationToken.None);

        harness.ConnectCalls.ShouldBe(1);
        harness.SubscribeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task StartAsync_CancelledDuringConnectAttempt_StopsRetryLoop()
    {
        var harness = new MqttClientHarness();
        using var cts = new CancellationTokenSource();

        // Simuliert eine Abbruchanforderung, die genau während eines laufenden
        // ConnectAsync-Aufrufs eintrifft: der Token ist zum Zeitpunkt des Catch
        // bereits abgebrochen, wodurch nicht die allgemeine Fehlerbehandlung
        // (Backoff/Retry) greift, sondern der eigene OperationCanceledException-
        // Catch-Block.
        Func<NSubstitute.Core.CallInfo, Task<MqttClientConnectResult>> cancelDuringConnect = _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };
        harness.Client.ConnectAsync(Arg.Any<MqttClientOptions>(), Arg.Any<CancellationToken>())
            .Returns(cancelDuringConnect);
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);

        await client.StartAsync(cts.Token);

        harness.SubscribeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Disconnected_AfterCancellation_DoesNotTriggerReconnect()
    {
        var harness = new MqttClientHarness();
        var client = new MqttOilLevelClient(CreateOptions(), NullLogger<MqttOilLevelClient>.Instance, harness.Client);
        using var cts = new CancellationTokenSource();

        await client.StartAsync(cts.Token);
        harness.ConnectCalls.ShouldBe(1);
        cts.Cancel();

        harness.RaiseDisconnected();
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        harness.ConnectCalls.ShouldBe(1);
    }
}

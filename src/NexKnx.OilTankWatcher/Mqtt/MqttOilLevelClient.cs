using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using NexKnx.OilTankWatcher.Configuration;

namespace NexKnx.OilTankWatcher.Mqtt;

/// <summary>
/// MQTT-Client für den Füllstand-Rohwert des ESP32-CAM / AI-on-the-edge-device.
///
/// API-Stand gegen die tatsächlich installierte MQTTnet-Version 5.2.0
/// verifiziert (per Reflection): Kernpaket "MQTTnet" ohne ManagedClient
/// (dieser wurde ab MQTTnet 5 aus dem Kernpaket entfernt) - Auto-Reconnect
/// wird daher hier über DisconnectedAsync + eigene Backoff-Schleife
/// realisiert. "MqttClientFactory" (nicht mehr "MqttFactory"),
/// "MqttApplicationMessageExtensions.ConvertPayloadToString()" und die
/// Convenience-Erweiterung "SubscribeAsync(topic, qos, ct)" existieren in
/// dieser Version exakt so.
/// </summary>
public sealed class MqttOilLevelClient : IAsyncDisposable
{
    private readonly MqttOptions _options;
    private readonly ILogger<MqttOilLevelClient> _logger;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _clientOptions;
    private CancellationToken _lifetimeToken;

    public event Action<double>? ValueReceived;

    public MqttOilLevelClient(MqttOptions options, ILogger<MqttOilLevelClient> logger)
    {
        _options = options;
        _logger = logger;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        var optionsBuilder = factory.CreateClientOptionsBuilder()
            .WithTcpServer(options.Host, options.Port)
            .WithClientId(options.ClientId)
            .WithCleanSession();

        if (!string.IsNullOrEmpty(options.Username))
        {
            optionsBuilder = optionsBuilder.WithCredentials(options.Username, options.Password);
        }

        _clientOptions = optionsBuilder.Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetimeToken = cancellationToken;
        await ConnectWithRetryAsync(cancellationToken);
    }

    private async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(_options.ReconnectDelaySeconds);
        var maxDelay = TimeSpan.FromSeconds(_options.MaxReconnectDelaySeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _client.ConnectAsync(_clientOptions, cancellationToken);
                await _client.SubscribeAsync(
                    _options.Topic,
                    MqttQualityOfServiceLevel.AtLeastOnce,
                    cancellationToken);

                _logger.LogInformation(
                    "MQTT verbunden mit {Host}:{Port}, Topic '{Topic}'.",
                    _options.Host,
                    _options.Port,
                    _options.Topic);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "MQTT-Verbindung zu {Host}:{Port} fehlgeschlagen, erneuter Versuch in {Delay}.",
                    _options.Host,
                    _options.Port,
                    delay);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_lifetimeToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "MQTT-Verbindung getrennt ({Reason}). Automatischer Reconnect wird gestartet.",
            args.Reason);

        // Nicht hier awaiten: die Reconnect-Schleife läuft unabhängig im
        // Hintergrund weiter, der Disconnect-Handler selbst muss zügig zurückkehren.
        _ = ConnectWithRetryAsync(_lifetimeToken);
        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var payload = args.ApplicationMessage.ConvertPayloadToString();

        if (TryParsePercent(payload, out var percent))
        {
            ValueReceived?.Invoke(percent);
        }
        else
        {
            _logger.LogWarning("MQTT-Payload konnte nicht als Prozentwert interpretiert werden: '{Payload}'", payload);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Das AI-on-the-edge-device veröffentlicht den Hauptwert standardmäßig
    /// als reine Zahl im Klartext. Zusätzlich wird defensiv ein einfaches
    /// JSON-Objekt mit einer "value"-Eigenschaft unterstützt, falls eine
    /// abweichende Konfiguration verwendet wird.
    /// </summary>
    internal static bool TryParsePercent(string payload, out double percent)
    {
        payload = payload.Trim();

        if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("value", out var valueProp) &&
                valueProp.TryGetDouble(out percent))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            // Kein JSON - wurde oben bereits als Klartext-Zahl versucht.
        }

        percent = 0;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _client.Dispose();
    }
}

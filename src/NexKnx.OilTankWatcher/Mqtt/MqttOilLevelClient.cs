using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Logging;

namespace NexKnx.OilTankWatcher.Mqtt;

/// <summary>
/// MQTT client for the raw fill-level value from the ESP32-CAM /
/// AI-on-the-edge-device.
///
/// API shape verified against the actually installed MQTTnet version 5.2.0
/// (by reflection): the core package "MQTTnet" has no ManagedClient
/// (removed from the core package as of MQTTnet 5) - auto-reconnect is
/// therefore implemented here via DisconnectedAsync + a custom backoff
/// loop. "MqttClientFactory" (no longer "MqttFactory"),
/// "MqttApplicationMessageExtensions.ConvertPayloadToString()" and the
/// convenience extension "SubscribeAsync(topic, qos, ct)" exist exactly
/// like this in this version.
/// </summary>
public sealed class MqttOilLevelClient : IMqttOilLevelClient
{
    private readonly MqttOptions _options;
    private readonly ILogger<MqttOilLevelClient> _logger;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _clientOptions;
    private CancellationToken _lifetimeToken;

    public event Action<double>? ValueReceived;

    public MqttOilLevelClient(MqttOptions options, ILogger<MqttOilLevelClient> logger)
        : this(options, logger, new MqttClientFactory().CreateMqttClient())
    {
    }

    /// <summary>
    /// For tests: allows injecting a fake <see cref="IMqttClient"/> without
    /// establishing a real network connection. <see cref="IMqttClient"/> is
    /// itself an interface provided by MQTTnet and therefore fakeable
    /// without its own test seam.
    /// </summary>
    internal MqttOilLevelClient(MqttOptions options, ILogger<MqttOilLevelClient> logger, IMqttClient client)
    {
        _options = options;
        _logger = logger;
        _client = client;

        var optionsBuilder = new MqttClientOptionsBuilder()
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

                _logger.LogMqttConnected(_options.Host, _options.Port, _options.Topic);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "MQTT connection to {Host}:{Port} failed, retrying in {Delay}.",
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
            "MQTT connection lost ({Reason}). Starting automatic reconnect.",
            args.Reason);

        // Don't await here: the reconnect loop keeps running independently
        // in the background, the disconnect handler itself must return promptly.
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
            _logger.LogWarning("MQTT payload could not be interpreted as a percent value: '{Payload}'", payload);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The AI-on-the-edge-device publishes the main value by default as a
    /// plain number in plain text. In addition, a simple JSON object with a
    /// "value" property is defensively supported, in case a different
    /// configuration is used.
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
            // Not JSON - already attempted as a plain-text number above.
        }

        percent = 0;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            // Dispose must not depend on the (possibly already expired)
            // lifetime token of the connection - hence deliberately
            // CancellationToken.None.
            await _client.DisconnectAsync(cancellationToken: CancellationToken.None);
        }

        _client.Dispose();
    }
}

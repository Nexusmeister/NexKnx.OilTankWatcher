namespace NexKnx.OilTankWatcher.Mqtt;

/// <summary>
/// Abstraction over the MQTT client for the raw fill-level value, so that
/// <see cref="NexKnx.OilTankWatcher.Worker"/> can be tested independently of
/// the concrete MQTTnet implementation.
/// </summary>
public interface IMqttOilLevelClient : IAsyncDisposable
{
    /// <summary>
    /// Raised as soon as a new raw fill-level value (in percent) has been received.
    /// </summary>
    event Action<double>? ValueReceived;

    /// <summary>
    /// Establishes the MQTT connection and subscribes to the configured topic.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);
}

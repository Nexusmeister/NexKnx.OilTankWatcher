namespace NexKnx.OilTankWatcher.Mqtt;

/// <summary>
/// Abstraktion über den MQTT-Client für den Füllstand-Rohwert, damit der
/// <see cref="NexKnx.OilTankWatcher.Worker"/> unabhängig von der konkreten MQTTnet-Implementierung
/// getestet werden kann.
/// </summary>
public interface IMqttOilLevelClient : IAsyncDisposable
{
    /// <summary>
    /// Wird ausgelöst, sobald ein neuer Füllstand-Rohwert (in Prozent) empfangen wurde.
    /// </summary>
    event Action<double>? ValueReceived;

    /// <summary>
    /// Baut die MQTT-Verbindung auf und abonniert das konfigurierte Topic.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);
}

using Knx.Falcon;
using NexKnx.OilTankWatcher.Configuration;

namespace NexKnx.OilTankWatcher.Logging;

/// <summary>
/// Zentrale Sammelstelle fuer alle per [LoggerMessage]-Source-Generator
/// erzeugten Log-Methoden (CA1873: vermeidet die Argumentauswertung, wenn
/// der jeweilige Log-Level deaktiviert ist). Als Extension-Methoden auf
/// ILogger statt pro Klasse verteilter partial-Methoden, damit alle
/// Log-Message-Templates an einer Stelle zu finden sind.
/// </summary>
internal static partial class LoggerExtensions
{
    [LoggerMessage(Level = LogLevel.Information, Message = "NexKnx.OilTankWatcher Version {Version} startet.")]
    public static partial void LogStartup(this ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} persistierte Historienwerte geladen.")]
    public static partial void LogHistoryLoaded(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "KNX aktualisiert: {Percent:0.0}% ({Liters:0} l), Warnung={Warning}, Störung={Fault}.")]
    public static partial void LogKnxUpdated(this ILogger logger, double percent, double liters, bool warning, bool fault);

    [LoggerMessage(Level = LogLevel.Information, Message = "KNX-Verbindungsstatus: {State}")]
    public static partial void LogKnxConnectionStateChanged(this ILogger logger, BusConnectionState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "KNX-Bus verbunden ({ConnectionType}), Status: {State}.")]
    public static partial void LogKnxConnected(this ILogger logger, KnxConnectionType connectionType, BusConnectionState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT verbunden mit {Host}:{Port}, Topic '{Topic}'.")]
    public static partial void LogMqttConnected(this ILogger logger, string host, int port, string topic);
}

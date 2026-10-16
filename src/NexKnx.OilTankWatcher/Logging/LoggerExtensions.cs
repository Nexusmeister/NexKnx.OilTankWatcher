using Knx.Falcon;
using NexKnx.OilTankWatcher.Configuration;

namespace NexKnx.OilTankWatcher.Logging;

/// <summary>
/// Central collection point for all log methods generated via the
/// [LoggerMessage] source generator (CA1873: avoids argument evaluation
/// when the respective log level is disabled). As extension methods on
/// ILogger instead of partial methods spread across each class, so all
/// log message templates can be found in one place.
/// </summary>
internal static partial class LoggerExtensions
{
    [LoggerMessage(Level = LogLevel.Information, Message = "NexKnx.OilTankWatcher version {Version} starting.")]
    public static partial void LogStartup(this ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} persisted history values loaded.")]
    public static partial void LogHistoryLoaded(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "KNX updated: {Percent:0.0}% ({Liters:0} l), warning={Warning}, fault={Fault}.")]
    public static partial void LogKnxUpdated(this ILogger logger, double percent, double liters, bool warning, bool fault);

    [LoggerMessage(Level = LogLevel.Information, Message = "KNX connection state: {State}")]
    public static partial void LogKnxConnectionStateChanged(this ILogger logger, BusConnectionState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "KNX bus connected ({ConnectionType}), state: {State}.")]
    public static partial void LogKnxConnected(this ILogger logger, KnxConnectionType connectionType, BusConnectionState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT connected to {Host}:{Port}, topic '{Topic}'.")]
    public static partial void LogMqttConnected(this ILogger logger, string host, int port, string topic);
}

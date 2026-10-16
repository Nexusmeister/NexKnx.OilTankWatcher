using System.Net;
using Knx.Falcon;
using Knx.Falcon.ApplicationData.DatapointTypes;
using Knx.Falcon.Configuration;
using Knx.Falcon.Sdk;
using Microsoft.Extensions.Logging;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Logging;

namespace NexKnx.OilTankWatcher.Knx;

/// <summary>
/// KNX integration via the Falcon SDK (Knx.Falcon.Sdk 6.x).
///
/// API shape verified against the actually installed package version (by
/// reflection), not taken from memory:
///   - Knx.Falcon.Sdk.KnxBus(ConnectorParameters) + ConnectAsync/WriteGroupValueAsync
///   - Knx.Falcon.Configuration.IpTunnelingConnectorParameters /
///     IpRoutingConnectorParameters, including ConnectorParameters.AutoReconnect
///   - Knx.Falcon.ApplicationData.DatapointTypes: DptFactory.Default.Get(5,1)
///     returns the DPT_Scaling implementation for DPT 5.001 (percent 0..100%,
///     round-trip tested: 42% -> byte 107); DptFactory.Default.Get(12,1200)
///     returns "DPT_VolumeLiquid_Litre" (liters, 4-byte integer, round-trip
///     tested: 6688 L -> exactly 6688 L back, without the rounding
///     inaccuracy that a 2-byte floating-point DPT 9.xxx would have at
///     values of this magnitude); Dpt9 for the (fractional) remaining
///     runtime in days and Dpt1 for the warning and fault bit (DPT 1.005
///     "Alarm").
///     Note: all DPT-1.x and DPT-12.x subtypes have the same 1-bit or
///     4-byte integer encoding on the wire - the subtype number is pure ETS
///     project metadata (display/interpretation of the group address) and
///     is not transmitted along with it.
/// </summary>
public sealed class FalconKnxGateway : IKnxGateway
{
    private readonly KnxOptions _options;
    private readonly ILogger<FalconKnxGateway> _logger;
    private readonly KnxBus _bus;

    private readonly DptBase _percentDpt = DptFactory.Default.Get(5, 1); // DPT 5.001 "Scaling"
    private readonly DptBase _volumeDpt = DptFactory.Default.Get(12, 1200); // DPT 12.1200 "VolumeLiquid_Litre"
    private readonly Dpt9 _runtimeDpt = new(); // DPT 9.xxx (fractional remaining runtime in days)
    private readonly Dpt1 _alarmDpt = new(); // DPT 1.005 "Alarm" (warning and fault bit)

    private readonly GroupAddress _percentGa;
    private readonly GroupAddress _litersGa;
    private readonly GroupAddress _warningGa;
    private readonly GroupAddress _faultGa;
    private readonly GroupAddress _runtimeGa;

    public FalconKnxGateway(KnxOptions options, ILogger<FalconKnxGateway> logger)
    {
        _options = options;
        _logger = logger;

        _percentGa = GroupAddress.Parse(options.GroupAddresses.PercentLevel);
        _litersGa = GroupAddress.Parse(options.GroupAddresses.LitersLevel);
        _warningGa = GroupAddress.Parse(options.GroupAddresses.WarningBit);
        _faultGa = GroupAddress.Parse(options.GroupAddresses.FaultBit);
        _runtimeGa = GroupAddress.Parse(options.GroupAddresses.RuntimeDays);

        var connectorParameters = BuildConnectorParameters(options);
        connectorParameters.AutoReconnect = true;

        _bus = new KnxBus(connectorParameters);
        _bus.ConnectionStateChanged += (_, _) => _logger.LogKnxConnectionStateChanged(_bus.ConnectionState);
    }

    private static ConnectorParameters BuildConnectorParameters(KnxOptions options) => options.ConnectionType switch
    {
        KnxConnectionType.Tunneling => new IpTunnelingConnectorParameters(
            options.Tunneling.Host,
            options.Tunneling.Port,
            useNat: options.Tunneling.UseNat),

        KnxConnectionType.Routing => new IpRoutingConnectorParameters(
            IPAddress.Parse(options.Routing.MulticastAddress)),

        _ => throw new NotSupportedException($"Unknown KNX connection type: {options.ConnectionType}")
    };

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _bus.ConnectAsync(cancellationToken);
        _logger.LogKnxConnected(_options.ConnectionType, _bus.ConnectionState);
    }

    public Task WritePercentAsync(double percent, CancellationToken cancellationToken) =>
        WriteAsync(_percentGa, _percentDpt.ToGroupValue((float)percent), cancellationToken);

    public Task WriteLitersAsync(double liters, CancellationToken cancellationToken) =>
        WriteAsync(_litersGa, _volumeDpt.ToGroupValue((uint)Math.Round(liters)), cancellationToken);

    public Task WriteWarningAsync(bool warning, CancellationToken cancellationToken) =>
        WriteAsync(_warningGa, _alarmDpt.ToGroupValue(warning), cancellationToken);

    public Task WriteFaultAsync(bool fault, CancellationToken cancellationToken) =>
        WriteAsync(_faultGa, _alarmDpt.ToGroupValue(fault), cancellationToken);

    public Task WriteRuntimeDaysAsync(double days, CancellationToken cancellationToken) =>
        WriteAsync(_runtimeGa, _runtimeDpt.ToGroupValue((float)days), cancellationToken);

    private async Task WriteAsync(GroupAddress address, GroupValue value, CancellationToken cancellationToken)
    {
        try
        {
            await _bus.WriteGroupValueAsync(address, value, MessagePriority.Low, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KNX write to group address {Address} failed.", address);
        }
    }

    public async ValueTask DisposeAsync() => await _bus.DisposeAsync();
}

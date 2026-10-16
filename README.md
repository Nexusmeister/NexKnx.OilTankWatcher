# NexKnx.OilTankWatcher

.NET 10 Worker Service for the Raspberry Pi 5 (arm64, Docker) that receives
the fill level of an oil tank (nominal volume configurable, default 9100 l)
over MQTT from an ESP32-CAM running [AI-on-the-edge-device](https://github.com/jomjol/AI-on-the-edge-device),
validates it for plausibility, smooths it, and writes it to the KNX bus.

## Architecture

```
MQTT (raw value %) --> Plausibility check --> Median smoothing (5 values)
                                                      |
                     +--------------------------------+
                     v
            Hysteresis (warning bit) / Fault detection (timeout)
                     |
                     v
         Send gate (on change OR every X minutes)
                     |
                     v
                  KNX bus (Falcon SDK)
```

| Component | File | Purpose |
|---|---|---|
| `PlausibilityFilter` | `src/.../Processing/PlausibilityFilter.cs` | Rejects values outside 0–100 % and jumps beyond a configurable maximum |
| `MedianSmoother` | `src/.../Processing/MedianSmoother.cs` | Median of the last 5 valid values |
| `HysteresisGate` | `src/.../Processing/HysteresisGate.cs` | Warning bit with hysteresis (active ≤ 25 %, recovery > 28 %) |
| `FaultDetector` | `src/.../Processing/FaultDetector.cs` | Fault bit after a configurable time without a valid value |
| `RuntimeEstimator` | `src/.../Processing/RuntimeEstimator.cs` | Linear regression over the 30-day history → remaining days |
| `PublishGate` | `src/.../Processing/PublishGate.cs` | Send decision: on change or cyclically |
| `MqttOilLevelClient` | `src/.../Mqtt/MqttOilLevelClient.cs` | MQTT receive (MQTTnet 5) with auto-reconnect |
| `FalconKnxGateway` | `src/.../Knx/FalconKnxGateway.cs` | KNX write access (Knx.Falcon.Sdk 6) |

## Local development with .NET Aspire

For local development there is, in addition to `docker-compose.yml`
(intended for the Pi/production use), an Aspire AppHost that orchestrates
Mosquitto and the worker together with a dashboard (logs, traces, metrics):

```bash
dotnet run --project aspire/NexKnx.OilTankWatcher.AppHost
```

Starts a Mosquitto container (port 1883, `ContainerLifetime.Persistent` –
survives AppHost restarts) and the worker as a local process;
`Mqtt__Host`/`Mqtt__Port` are automatically wired to the container. All other
settings (KNX, tank, …) still come from `appsettings.json`. The console
output shows the dashboard URL (`https://localhost:17069/login?t=...`).

- `aspire/NexKnx.OilTankWatcher.AppHost` – orchestration (`AppHost.cs`)
- `aspire/NexKnx.OilTankWatcher.ServiceDefaults` – OpenTelemetry/health
  checks, wired into the worker via `builder.AddServiceDefaults()`

## KNX datapoints

| Value | DPT | Default GA |
|---|---|---|
| Fill level % | 5.001 (Scaling) | `10/3/0` |
| Fill level liters | 12.1200 (VolumeLiquid_Litre, 4-byte uint) | `10/3/1` |
| Warning bit (≤ 25 %, hysteresis up to 28 %) | 1.005 (Alarm) | `10/3/2` |
| Fault bit (> 12 h no valid value) | 1.005 (Alarm) | `10/3/2` |
| Remaining runtime (days) | 9.xxx (2-byte float) | `10/3/1` |

**Caution, shared group addresses:** the warning and fault bits both sit on
`10/3/2`, liters and remaining runtime both on `10/3/1`. Both values per GA
are written independently of the send gate as soon as *either one* changes –
the other value is overwritten on the same GA in the process. Two different
DPTs (12.1200 liters / 9.xxx days, or two different alarm bits) on one GA is
unusual and can lead to misinterpretation in a visualization that expects a
fixed DPT per GA – though it's unproblematic to observe as a raw value in
the ETS group monitor.

The API usage of `Knx.Falcon.Sdk` (version 6.4.8671) was not written from
memory, but verified by reflection against the actually installed NuGet
package version, including round-trip tests of the DPT encoding
(`DptFactory.Default.Get(5,1)` → DPT 5.001, `Dpt9`, `Dpt1`).

## Configuration

Configuration happens via `appsettings.json` plus environment variables
(standard .NET configuration, `__` as the separator for nested values, see
`docker-compose.yml` for a complete example).

The `OTEL_EXPORTER_OTLP_*` variables in `docker-compose.yml` are
deployment-specific (hostnames/ports of the respective Grafana stack) and
are therefore not hardcoded in the repo, but read from a local,
non-committed `.env` file in the project directory (Docker Compose reads
`.env` automatically). See `.env.example` for the required variables. If the
`.env` file or an individual variable is missing, the respective OTLP export
is simply disabled.

Important settings:

- `Mqtt:Host` / `Mqtt:Port` / `Mqtt:Topic` – broker and topic (default: `oiltank/main/value`)
- `Tank:NominalVolumeLiters` – nominal volume for the liter conversion
- `Tank:MaxStepPercent` – maximum plausible change per reading (default 5 percentage points)
- `Tank:MedianWindowSize` – window size of the median smoothing (default 5)
- `Knx:ConnectionType` – `Tunneling` or `Routing`
- `Knx:WarningThresholdPercent` / `Knx:WarningRecoveryPercent` – hysteresis thresholds
- `Knx:FaultAfterHours` – fault timeout (default 12 h)
- `Knx:SendIntervalMinutes` / `Knx:ChangeThresholdPercent` – cyclic vs. change-driven sending
- `RuntimeEstimation:Enabled` – remaining runtime estimation on/off

## Commissioning

1. **First observe in the ETS group monitor**, before connecting real
   actuators/visualizations to the production GAs (`10/3/0`–`10/3/2`) –
   especially because of the shared group addresses (see the note above
   under KNX datapoints).

2. Start the Mosquitto broker and the service with Docker Compose:

   ```bash
   docker compose up -d --build
   ```

3. Simulate a test value, e.g. with the `mosquitto_pub` client in the
   Mosquitto container:

   ```bash
   docker compose exec mosquitto mosquitto_pub -t oiltank/main/value -m "67.3"
   ```

4. Check the logs:

   ```bash
   docker compose logs -f oiltank-watcher
   ```

   On successful processing a line like this appears:
   `KNX updated: 67.3% (6125 l), warning=False, fault=False.`

5. Use the ETS group monitor (bus monitor) to check whether the values
   arrive on the test GA (DPT 5.001 for %, DPT 12.1200 for liters, DPT 9.xxx
   for the remaining runtime, DPT 1.005 for the warning and fault bits).

6. Only afterwards enter the real group addresses in `appsettings.json` or
   the environment variables and restart the service.

### KNX connection type

- **IP tunneling** (default): `Knx__ConnectionType=Tunneling`, set
  `Knx__Tunneling__Host` to the IP of the KNX/IP interface.
- **IP routing**: `Knx__ConnectionType=Routing`, multicast address via
  `Knx__Routing__MulticastAddress` (default `224.0.23.12`). Usually requires
  `network_mode: host` in Docker (see the comment in `docker-compose.yml`),
  since bridge networks don't forward multicast by default.

## Tests

```bash
dotnet test
```

The unit tests cover the plausibility check, smoothing, hysteresis, fault
detection, send gate, and remaining runtime estimation (47 tests, pure logic
without MQTT/KNX dependencies).

## Persistent history (SQLite)

The fill-level history for the remaining runtime estimation is stored in a
local SQLite database (`Microsoft.Data.Sqlite`) and thus survives a restart
of the service. Path configurable via `RuntimeEstimation:DatabasePath`
(default `history.db` in the working directory). In Docker operation the
compose setup points to `/data/history.db` with its own volume
(`history-data`), so the file also survives container restarts.

On startup the service loads the stored history (`IHistoryRepository`,
`src/.../Persistence/SqliteHistoryRepository.cs`); on every new daily value
it is appended, and entries older than `RuntimeEstimation:HistoryDays` are
discarded. Errors while loading/writing are only logged, not fatal – the
service keeps running with an empty or stale history on a DB problem.

## Limits / deliberate simplifications

- MQTT payload parsing accepts both plain text (the AI-on-the-edge-device
  default) and a simple JSON object `{"value": 67.3}` as a fallback.
- The Dockerfile default `DOTNET_gcServer=0` (workstation instead of server
  GC) is the more sensible choice for a Pi with few cores anyway; in
  addition, a crash pattern in the .NET thread pool was observed during
  cross-build testing for arm64 under QEMU emulation (amd64 CI/dev machine)
  that occurred more often with server GC. The SQLite integration itself ran
  without errors under QEMU/arm64 after this fix; an unrelated, sporadic
  crash deep inside generic .NET async internals (with no own code in the
  stack trace) could not be fully ruled out under QEMU. On real Pi 5
  hardware (no emulator) this shouldn't occur – still worth a real check on
  the Pi before going to production.

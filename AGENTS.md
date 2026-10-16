# AGENTS.md

Guidance for AI coding agents (Codex, Cursor, Copilot, Gemini, etc.) working
in this repository. Claude Code reads `CLAUDE.md`, which points back here.

## What this is

A .NET 10 Worker Service, deployed to a Raspberry Pi 5 (arm64) via Docker,
that reads an oil tank fill level from MQTT (published by an ESP32-CAM
running [AI-on-the-edge-device](https://github.com/jomjol/AI-on-the-edge-device)),
filters/smooths/evaluates it, and writes the result to a KNX bus via the
Falcon SDK. See `README.md` for the full domain write-up (German) including
KNX datapoints and commissioning steps.

## Commands

```bash
dotnet restore
dotnet build --configuration Release
dotnet test                                    # 47 unit tests, no MQTT/KNX dependencies
dotnet run --project aspire/NexKnx.OilTankWatcher.AppHost   # local dev: Mosquitto + worker + dashboard
```

`docker compose up -d --build` runs the production-shaped stack (Mosquitto +
worker) defined in `docker-compose.yml`.

## Build policy

`Directory.Build.props` sets `TreatWarningsAsErrors=true` for every project.
Do not introduce warnings, and do not suppress them instead of fixing the
underlying issue.

## Architecture

```
MQTT (raw %) --> PlausibilityFilter --> MedianSmoother (5 values)
                                              |
                      +-----------------------+
                      v
          HysteresisGate (warning bit) / FaultDetector (timeout)
                      |
                      v
              PublishGate (on change OR every N minutes)
                      |
                      v
                 KNX bus (Falcon SDK)
```

| Component | File | Purpose |
|---|---|---|
| `PlausibilityFilter` | `src/NexKnx.OilTankWatcher/Processing/PlausibilityFilter.cs` | Rejects values outside 0–100% and jumps beyond the configured max step |
| `MedianSmoother` | `src/NexKnx.OilTankWatcher/Processing/MedianSmoother.cs` | Median of the last 5 valid values |
| `HysteresisGate` | `src/NexKnx.OilTankWatcher/Processing/HysteresisGate.cs` | Warning bit with hysteresis |
| `FaultDetector` | `src/NexKnx.OilTankWatcher/Processing/FaultDetector.cs` | Fault bit after a configurable time with no valid value |
| `RuntimeEstimator` | `src/NexKnx.OilTankWatcher/Processing/RuntimeEstimator.cs` | Linear regression over 30-day history -> remaining days |
| `PublishGate` | `src/NexKnx.OilTankWatcher/Processing/PublishGate.cs` | Send decision: on change or cyclically |
| `MqttOilLevelClient` | `src/NexKnx.OilTankWatcher/Mqtt/MqttOilLevelClient.cs` | MQTT receive (MQTTnet 5) with auto-reconnect |
| `FalconKnxGateway` | `src/NexKnx.OilTankWatcher/Knx/FalconKnxGateway.cs` | KNX write access (Knx.Falcon.Sdk 6) |
| `SqliteHistoryRepository` | `src/NexKnx.OilTankWatcher/Persistence/SqliteHistoryRepository.cs` | Persists fill-level history for the runtime estimate across restarts |

Solution layout:
- `src/NexKnx.OilTankWatcher` — the worker itself
- `aspire/NexKnx.OilTankWatcher.AppHost` / `aspire/NexKnx.OilTankWatcher.ServiceDefaults` — local Aspire orchestration + OpenTelemetry/health-check defaults
- `tests/NexKnx.OilTankWatcher.Tests` — unit tests (xUnit + Shouldly + NSubstitute)

## Gotchas worth knowing before touching code

- **Shared KNX group addresses**: the warning and fault bits share GA
  `10/3/2`; liters and remaining-days share `10/3/1`. Both values on a shared
  GA are written independently whenever *either* changes, overwriting the
  other on the wire. This is intentional (see README "Achtung, geteilte
  Gruppenadressen") — don't "fix" it by giving them separate GAs without
  checking with the user first, since that changes the KNX project wiring.
- **`Knx.Falcon.Sdk` usage was verified by reflection** against the actual
  installed NuGet package version, not written from memory — if you change
  DPT handling in `FalconKnxGateway.cs`, re-verify against the installed SDK
  version rather than assuming API shape from general KNX knowledge.
- **The percent gauge reports no measurement** (not a fake 0%) until the
  first real MQTT reading arrives after a restart — preserve this when
  touching the OpenTelemetry metrics wiring in `Worker.cs`.
- **OTEL endpoints are deployment-specific** and read from a local,
  uncommitted `.env` file (see `.env.example`). Never hardcode or commit real
  endpoint hostnames.
- Workstation GC (`DOTNET_gcServer=0`) is the Dockerfile default — deliberate
  for the Pi's low core count; don't switch to server GC.

## Tests

- Framework: xUnit, assertions via Shouldly, mocking via NSubstitute.
- `tests/NexKnx.OilTankWatcher.Tests/coverlet.runsettings` defines coverage
  settings used both in CI and locally.
- Pure-logic components (`Processing/*`) are unit-tested without MQTT/KNX
  dependencies; `FalconKnxGateway` and `Program.cs`/AppHost/ServiceDefaults
  are excluded from coverage requirements (see SonarCloud exclusions in
  `.github/workflows/ci.yml`) since they wrap external SDKs/bootstrapping.

## CI/CD

`.github/workflows/ci.yml` runs on every PR and push to `main`:
build+test, a Trivy dependency scan, SonarCloud analysis, and a multi-arch
(amd64/arm64) Docker build. The image is only pushed to
`ghcr.io/nexusmeister/nexknx.oiltankwatcher` on push to `main` (i.e. after a
PR merges), tagged with the GitVersion semver, `sha-<short-sha>`, and
`latest`. Deploying the new image to the production Pi is a separate, manual
step outside this repo's CI.

## Conventions

- **Everything is English-only** (code, comments, commit messages, PR
  descriptions, docs, CI step names): the repo was originally written in
  German and was fully converted to English (see issue #14) — don't
  reintroduce German text in new work.
- Keep configuration changes backward compatible with
  `docker-compose.yml`/`appsettings.json` conventions (`__` as the nesting
  separator for env vars).

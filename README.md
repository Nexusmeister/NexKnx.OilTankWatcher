# NexKnx.OilTankWatcher

.NET 10 Worker Service für den Raspberry Pi 5 (arm64, Docker), der den
Füllstand eines Öltanks (Nennvolumen konfigurierbar, Standard 9100 l) per
MQTT von einem ESP32-CAM mit [AI-on-the-edge-device](https://github.com/jomjol/AI-on-the-edge-device)
empfängt, plausibilisiert, glättet und auf den KNX-Bus schreibt.

## Architektur

```
MQTT (Rohwert %) --> Plausibilitätsprüfung --> Median-Glättung (5 Werte)
                                                      |
                     +--------------------------------+
                     v
            Hysterese (Warnbit) / Störungserkennung (Timeout)
                     |
                     v
         Sende-Gate (bei Änderung ODER alle X Minuten)
                     |
                     v
                  KNX-Bus (Falcon SDK)
```

| Baustein | Datei | Zweck |
|---|---|---|
| `PlausibilityFilter` | `src/.../Processing/PlausibilityFilter.cs` | Verwirft Werte außerhalb 0–100 % und Sprünge > konfigurierbarem Maximum |
| `MedianSmoother` | `src/.../Processing/MedianSmoother.cs` | Median der letzten 5 gültigen Werte |
| `HysteresisGate` | `src/.../Processing/HysteresisGate.cs` | Warnbit mit Hysterese (aktiv ≤ 25 %, Rücknahme > 28 %) |
| `FaultDetector` | `src/.../Processing/FaultDetector.cs` | Störungsbit nach konfigurierbarer Zeit ohne gültigen Wert |
| `RuntimeEstimator` | `src/.../Processing/RuntimeEstimator.cs` | Lineare Regression über die 30-Tage-Historie → Resttage |
| `PublishGate` | `src/.../Processing/PublishGate.cs` | Sendeentscheidung: bei Änderung oder zyklisch |
| `MqttOilLevelClient` | `src/.../Mqtt/MqttOilLevelClient.cs` | MQTT-Empfang (MQTTnet 5) mit Auto-Reconnect |
| `FalconKnxGateway` | `src/.../Knx/FalconKnxGateway.cs` | KNX-Schreibzugriff (Knx.Falcon.Sdk 6) |

## Lokale Entwicklung mit .NET Aspire

Für die lokale Entwicklung gibt es zusätzlich zum `docker-compose.yml`
(gedacht für den Pi/Produktivbetrieb) ein Aspire AppHost, das Mosquitto
und den Worker gemeinsam mit einem Dashboard (Logs, Traces, Metrics)
orchestriert:

```bash
dotnet run --project aspire/NexKnx.OilTankWatcher.AppHost
```

Startet einen Mosquitto-Container (Port 1883, `ContainerLifetime.Persistent`
– bleibt zwischen AppHost-Neustarts erhalten) und den Worker als lokalen
Prozess; `Mqtt__Host`/`Mqtt__Port` werden automatisch auf den Container
verdrahtet. Alle übrigen Einstellungen (KNX, Tank, …) kommen weiterhin aus
`appsettings.json`. Die Konsolenausgabe zeigt die Dashboard-URL
(`https://localhost:17069/login?t=...`).

- `aspire/NexKnx.OilTankWatcher.AppHost` – Orchestrierung (`AppHost.cs`)
- `aspire/NexKnx.OilTankWatcher.ServiceDefaults` – OpenTelemetry/Health-Checks,
  vom Worker über `builder.AddServiceDefaults()` eingebunden

## KNX-Datenpunkte

| Wert | DPT | Standard-GA |
|---|---|---|
| Füllstand % | 5.001 (Scaling) | `10/3/0` |
| Füllstand Liter | 12.1200 (VolumeLiquid_Litre, 4-Byte-Uint) | `10/3/1` |
| Warnbit (≤ 25 %, Hysterese bis 28 %) | 1.005 (Alarm) | `10/3/2` |
| Störungsbit (> 12 h kein gültiger Wert) | 1.005 (Alarm) | `10/3/2` |
| Restreichweite (Tage) | 9.xxx (2-Byte-Float) | `10/3/1` |

**Achtung, geteilte Gruppenadressen:** Warn- und Störungsbit liegen beide
auf `10/3/2`, Liter und Restreichweite beide auf `10/3/1`. Beide Werte
pro GA werden unabhängig vom Sende-Gate geschrieben, sobald sich *einer*
von beiden ändert - der jeweils andere Wert wird dabei auf derselben GA
überschrieben. Zwei unterschiedliche DPTs (12.1200 Liter / 9.xxx Tage
bzw. zwei verschiedene Alarm-Bits) auf einer GA ist unüblich und kann in
einer Visualisierung, die eine feste DPT pro GA erwartet, zu
Fehlinterpretationen führen - im ETS-Gruppenmonitor als Rohwert aber
unproblematisch beobachtbar.

Die API-Nutzung von `Knx.Falcon.Sdk` (Version 6.4.8671) wurde nicht aus dem
Gedächtnis geschrieben, sondern per Reflection gegen die tatsächlich
installierte NuGet-Paketversion verifiziert, u. a. Rundtrip-Tests der
DPT-Kodierung (`DptFactory.Default.Get(5,1)` → DPT 5.001, `Dpt9`, `Dpt1`).

## Konfiguration

Konfiguration erfolgt über `appsettings.json` plus Umgebungsvariablen
(Standard-.NET-Konfiguration, `__` als Trennzeichen für verschachtelte
Werte, siehe `docker-compose.yml` für ein vollständiges Beispiel).

Wichtige Einstellungen:

- `Mqtt:Host` / `Mqtt:Port` / `Mqtt:Topic` – Broker und Topic (Standard: `oiltank/main/value`)
- `Tank:NominalVolumeLiters` – Nennvolumen für die Liter-Umrechnung
- `Tank:MaxStepPercent` – maximale plausible Änderung pro Messung (Standard 5 Prozentpunkte)
- `Tank:MedianWindowSize` – Fenstergröße der Median-Glättung (Standard 5)
- `Knx:ConnectionType` – `Tunneling` oder `Routing`
- `Knx:WarningThresholdPercent` / `Knx:WarningRecoveryPercent` – Hysterese-Schwellen
- `Knx:FaultAfterHours` – Störungs-Timeout (Standard 12 h)
- `Knx:SendIntervalMinutes` / `Knx:ChangeThresholdPercent` – zyklisches vs. änderungsgetriebenes Senden
- `RuntimeEstimation:Enabled` – Restreichweiten-Schätzung ein/aus

## Inbetriebnahme

1. **Zuerst im ETS-Gruppenmonitor beobachten**, bevor echte
   Aktoren/Visualisierungen an die produktiven GAs (`10/3/0`–`10/3/2`)
   angebunden werden - insbesondere wegen der geteilten Gruppenadressen
   (siehe Hinweis oben bei den KNX-Datenpunkten).

2. Mosquitto-Broker und Dienst mit Docker Compose starten:

   ```bash
   docker compose up -d --build
   ```

3. Testwert simulieren, z. B. mit dem `mosquitto_pub`-Client im
   Mosquitto-Container:

   ```bash
   docker compose exec mosquitto mosquitto_pub -t oiltank/main/value -m "67.3"
   ```

4. Logs prüfen:

   ```bash
   docker compose logs -f oiltank-watcher
   ```

   Bei erfolgreicher Verarbeitung erscheint eine Zeile wie:
   `KNX aktualisiert: 67.3% (6125 l), Warnung=False, Störung=False.`

5. Mit der ETS-Gruppenüberwachung (Bus-Monitor) prüfen, ob die Werte auf
   der Test-GA ankommen (DPT 5.001 für %, DPT 12.1200 für Liter, DPT 9.xxx
   für die Restreichweite, DPT 1.005 für Warn- und Störungsbit).

6. Erst danach die echten Gruppenadressen in `appsettings.json` bzw. den
   Umgebungsvariablen eintragen und den Dienst neu starten.

### KNX-Verbindungstyp

- **IP-Tunneling** (Standard): `Knx__ConnectionType=Tunneling`,
  `Knx__Tunneling__Host` auf die IP der KNX/IP-Schnittstelle setzen.
- **IP-Routing**: `Knx__ConnectionType=Routing`, Multicast-Adresse per
  `Knx__Routing__MulticastAddress` (Standard `224.0.23.12`). Benötigt in
  Docker in der Regel `network_mode: host` (siehe Kommentar in
  `docker-compose.yml`), da Bridge-Netzwerke Multicast nicht
  durchleiten.

## Tests

```bash
dotnet test
```

Die Unit-Tests decken Plausibilitätsprüfung, Glättung, Hysterese,
Störungserkennung, Sende-Gate und Restreichweitenschätzung ab (47 Tests,
reine Logik ohne MQTT-/KNX-Abhängigkeiten).

## Persistente Historie (SQLite)

Die Füllstandshistorie für die Restreichweitenschätzung wird in einer
lokalen SQLite-Datenbank gespeichert (`Microsoft.Data.Sqlite`) und
überlebt damit einen Neustart des Dienstes. Pfad über
`RuntimeEstimation:DatabasePath` konfigurierbar (Standard `history.db`
im Arbeitsverzeichnis). Im Docker-Betrieb zeigt das Compose-Setup auf
`/data/history.db` mit einem eigenen Volume (`history-data`), damit die
Datei auch Container-Neustarts übersteht.

Beim Start lädt der Dienst die gespeicherte Historie (`IHistoryRepository`,
`src/.../Persistence/SqliteHistoryRepository.cs`); bei jedem neuen
Tageswert wird er angehängt und älter als `RuntimeEstimation:HistoryDays`
werdende Einträge werden verworfen. Fehler beim Laden/Schreiben werden nur
geloggt, nicht fatal – der Dienst läuft bei einem DB-Problem mit leerer
bzw. nicht aktualisierter Historie weiter.

## Grenzen / bewusste Vereinfachungen

- Das MQTT-Payload-Parsing akzeptiert sowohl reinen Klartext (Standard
  des AI-on-the-edge-device) als auch ein einfaches JSON-Objekt
  `{"value": 67.3}` als Fallback.
- Der Dockerfile-Default `DOTNET_gcServer=0` (Workstation- statt
  Server-GC) ist für einen Pi mit wenigen Kernen ohnehin die sinnvollere
  Wahl; zusätzlich wurde beim Cross-Build-Testen für arm64 unter
  QEMU-Emulation (amd64-CI/Dev-Rechner) ein Absturzmuster im .NET-
  Thread-Pool beobachtet, das mit Server-GC häufiger auftrat. Die
  SQLite-Anbindung selbst lief unter QEMU/arm64 nach diesem Fix fehlerfrei;
  ein davon unabhängiger, sporadischer Absturz tief in generischen
  .NET-Async-Interna (ohne eigenen Code im Stacktrace) ließ sich unter
  QEMU nicht vollständig ausschließen. Auf echter Pi-5-Hardware (kein
  Emulator) sollte das nicht auftreten - vor dem produktiven Einsatz
  trotzdem einmal real auf dem Pi gegenprüfen.

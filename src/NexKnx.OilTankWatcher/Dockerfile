# Multi-stage build für Raspberry Pi 5 (arm64).
# Build-Stage läuft i. d. R. unter amd64 (Dev-PC/CI) und erzeugt per
# --arch/-a arm64 ein linux-arm64-Artefakt; die Runtime-Stage ist das
# native arm64-Image, das auf dem Pi ausgeführt wird.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY ["src/NexKnx.OilTankWatcher/NexKnx.OilTankWatcher.csproj", "src/NexKnx.OilTankWatcher/"]
COPY ["aspire/NexKnx.OilTankWatcher.ServiceDefaults/NexKnx.OilTankWatcher.ServiceDefaults.csproj", "aspire/NexKnx.OilTankWatcher.ServiceDefaults/"]
RUN dotnet restore "src/NexKnx.OilTankWatcher/NexKnx.OilTankWatcher.csproj" -a $TARGETARCH

COPY src/NexKnx.OilTankWatcher/ src/NexKnx.OilTankWatcher/
COPY aspire/NexKnx.OilTankWatcher.ServiceDefaults/ aspire/NexKnx.OilTankWatcher.ServiceDefaults/
WORKDIR /src/src/NexKnx.OilTankWatcher
RUN dotnet publish NexKnx.OilTankWatcher.csproj -c Release -a $TARGETARCH -o /app --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Läuft ohne Root-Rechte.
# Hinweis: Das .NET 10-Basisimage basiert auf Ubuntu (zuvor Debian bei .NET 8)
# und bringt kein "adduser"-Skript mehr mit, daher direkt "useradd".
# /data nimmt die persistente SQLite-Historiendatenbank auf (siehe
# docker-compose.yml, Volume "history-data"). /app selbst muss ebenfalls
# oiltank gehören, sonst kann der Default-Pfad "history.db" (ohne gemountetes
# Volume, z. B. im lokalen Testlauf) nicht angelegt werden - SQLite Error 14
# "unable to open database file". COPY --chown chownt nur die kopierten
# Dateien, nicht das von WORKDIR bereits angelegte /app-Verzeichnis selbst -
# daher zusätzlich ein expliziter chown auf das Verzeichnis.
RUN useradd --create-home --shell /bin/bash oiltank && \
    mkdir -p /data && \
    chown oiltank:oiltank /data /app

COPY --from=build --chown=oiltank:oiltank /app .

# Workstation-GC statt Server-GC: auf einem Pi mit wenigen Kernen sinnvoller
# (weniger GC-Threads/Overhead) und vermeidet zusätzlich ein unter QEMU-
# Emulation (Cross-Build-Tests für arm64 auf amd64-CI) beobachtetes
# Absturzmuster im .NET-Thread-Pool bei aktivierter Server-GC.
ENV DOTNET_gcServer=0

USER oiltank

ENTRYPOINT ["dotnet", "NexKnx.OilTankWatcher.dll"]

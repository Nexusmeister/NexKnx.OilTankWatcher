# Multi-stage build for Raspberry Pi 5 (arm64).
# The build stage usually runs under amd64 (dev PC/CI) and produces a
# linux-arm64 artifact via --arch/-a arm64; the runtime stage is the
# native arm64 image that runs on the Pi.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# SemVer version determined from Git history via GitVersion, passed in by
# the CI workflow via --build-arg (e.g. "0.1.3"). The .git directory itself
# is not part of the Docker build context (.dockerignore), so the version
# cannot be determined from Git here, and is instead passed through
# ready-made. Local builds without this argument fall back to a
# recognizable dev placeholder.
ARG VERSION=0.0.0-dev
WORKDIR /src

COPY ["src/NexKnx.OilTankWatcher/NexKnx.OilTankWatcher.csproj", "src/NexKnx.OilTankWatcher/"]
COPY ["aspire/NexKnx.OilTankWatcher.ServiceDefaults/NexKnx.OilTankWatcher.ServiceDefaults.csproj", "aspire/NexKnx.OilTankWatcher.ServiceDefaults/"]
RUN dotnet restore "src/NexKnx.OilTankWatcher/NexKnx.OilTankWatcher.csproj" -a $TARGETARCH

COPY src/NexKnx.OilTankWatcher/ src/NexKnx.OilTankWatcher/
COPY aspire/NexKnx.OilTankWatcher.ServiceDefaults/ aspire/NexKnx.OilTankWatcher.ServiceDefaults/
WORKDIR /src/src/NexKnx.OilTankWatcher
RUN dotnet publish NexKnx.OilTankWatcher.csproj -c Release -a $TARGETARCH -o /app --self-contained false \
    -p:Version=$VERSION -p:InformationalVersion=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Runs without root privileges.
# Note: the .NET 10 base image is based on Ubuntu (previously Debian for
# .NET 8) and no longer ships an "adduser" script, so "useradd" directly.
# /data holds the persistent SQLite history database (see docker-compose.yml,
# volume "history-data"). /app itself must also belong to oiltank, otherwise
# the default path "history.db" (without a mounted volume, e.g. in a local
# test run) can't be created - SQLite error 14 "unable to open database
# file". COPY --chown only chowns the copied files, not the /app directory
# itself already created by WORKDIR - hence an additional explicit chown on
# the directory.
RUN useradd --create-home --shell /bin/bash oiltank && \
    mkdir -p /data && \
    chown oiltank:oiltank /data /app

COPY --from=build --chown=oiltank:oiltank /app .

# Workstation GC instead of server GC: more sensible on a Pi with few cores
# (less GC thread/overhead) and additionally avoids a crash pattern in the
# .NET thread pool observed under QEMU emulation (cross-build tests for
# arm64 on amd64 CI) when server GC was enabled.
ENV DOTNET_gcServer=0

USER oiltank

ENTRYPOINT ["dotnet", "NexKnx.OilTankWatcher.dll"]

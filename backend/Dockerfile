# syntax=docker/dockerfile:1
# Images of the backend hosts (ADR 0013): one build, three targets — api, worker, auxctl.
#   docker build -f backend/Dockerfile --target api .      (context: the repository root, for .editorconfig)
# Used by deploy/compose.local.yml; production images and their hardening come with H-03.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# The analyzer settings of the repository (.editorconfig at the root) apply to the build, as in CI.
COPY .editorconfig ./
COPY backend/ backend/
WORKDIR /src/backend
# Locked restore (packages.lock.json) and NuGet audit, as in CI.
RUN dotnet restore Auxilia.slnx --locked-mode
RUN dotnet publish src/Auxilia.Api -c Release --no-restore -o /out/api \
 && dotnet publish src/Auxilia.Worker -c Release --no-restore -o /out/worker \
 && dotnet publish src/Auxilia.MigrationRunner -c Release --no-restore -o /out/auxctl

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Document files and daily logs live in /data (volumes shared by api, worker and auxctl); /shared receives the secrets
# the local bootstrap generates. Named volumes start with this ownership, so every container runs as the app user.
RUN mkdir -p /data/storage /data/logs /data/logs-buffer /shared && chown -R $APP_UID /data /shared
ENV AuxiliaLogging__LocalPath=/data/logs \
    AuxiliaLogging__BufferPath=/data/logs-buffer \
    Storage__Local__RootPath=/data/storage \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
USER $APP_UID

FROM runtime AS api
COPY --from=build /out/api .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Auxilia.Api.dll"]

FROM runtime AS worker
COPY --from=build /out/worker .
ENTRYPOINT ["dotnet", "Auxilia.Worker.dll"]

FROM runtime AS auxctl
COPY --from=build /out/auxctl .
ENTRYPOINT ["dotnet", "Auxilia.MigrationRunner.dll"]

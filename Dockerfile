# Multi-stage build: the SDK image compiles, the runtime image (no SDK, smaller) runs.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, in its own layer: it only re-runs when project files or package versions change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Demiurge.Engine/Demiurge.Engine.csproj src/Demiurge.Engine/
COPY src/Demiurge.Agents/Demiurge.Agents.csproj src/Demiurge.Agents/
COPY src/Demiurge.Harness/Demiurge.Harness.csproj src/Demiurge.Harness/
COPY src/Demiurge.Host/Demiurge.Host.csproj src/Demiurge.Host/
RUN dotnet restore src/Demiurge.Host/Demiurge.Host.csproj
COPY src/ src/
RUN dotnet publish src/Demiurge.Host/Demiurge.Host.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# The world lives on a volume; the non-root "app" user (built into the image) must own it.
RUN mkdir -p /data && chown app:app /data
USER app
ENV DEMIURGE__DatabasePath=/data/demiurge.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "Demiurge.Host.dll"]

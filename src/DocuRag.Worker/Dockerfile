# syntax=docker/dockerfile:1
# Build multi-stage. Contexte de build = racine du repo (Directory.Build.props / Directory.Packages.props).
#   docker build -f src/DocuRag.Worker/Dockerfile .
# En déploiement, azd le fait construire par ACR Tasks (remoteBuild).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/DocuRag.Core/DocuRag.Core.csproj src/DocuRag.Core/
COPY src/DocuRag.Worker/DocuRag.Worker.csproj src/DocuRag.Worker/
RUN dotnet restore src/DocuRag.Worker/DocuRag.Worker.csproj
COPY src/DocuRag.Core/ src/DocuRag.Core/
COPY src/DocuRag.Worker/ src/DocuRag.Worker/
RUN dotnet publish src/DocuRag.Worker/DocuRag.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Utilisateur non-root fourni par les images .NET ("app", uid 1654).
USER $APP_UID
# Pas de port HTTP : le worker écrit un heartbeat toutes les 15 s, on vérifie qu'il a moins de 60 s.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD [ -f /tmp/docurag-heartbeat ] && [ $(( $(date +%s) - $(stat -c %Y /tmp/docurag-heartbeat) )) -lt 60 ] || exit 1
ENTRYPOINT ["dotnet", "DocuRag.Worker.dll"]

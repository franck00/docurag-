# Dockerfile du worker (DocuRag.Worker) — multi-stage, image runtime, utilisateur non-root.
# Construit via ACR Tasks (az acr build), pas de build local (J8).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (couches de cache : d'abord les csproj + props).
COPY Directory.Build.props ./
COPY src/DocuRag.Core/DocuRag.Core.csproj src/DocuRag.Core/
COPY src/DocuRag.Infrastructure/DocuRag.Infrastructure.csproj src/DocuRag.Infrastructure/
COPY src/DocuRag.Worker/DocuRag.Worker.csproj src/DocuRag.Worker/
RUN dotnet restore src/DocuRag.Worker/DocuRag.Worker.csproj

# Build + publish.
COPY src/DocuRag.Core/ src/DocuRag.Core/
COPY src/DocuRag.Infrastructure/ src/DocuRag.Infrastructure/
COPY src/DocuRag.Worker/ src/DocuRag.Worker/
RUN dotnet publish src/DocuRag.Worker/DocuRag.Worker.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# procps fournit pgrep (health check du process) — installé avant de passer non-root.
RUN apt-get update \
    && apt-get install -y --no-install-recommends procps \
    && rm -rf /var/lib/apt/lists/*

# Utilisateur non-root fourni par l'image .NET (UID 1654).
USER $APP_UID

# Le worker n'expose pas d'HTTP : le health check vérifie que le process tourne.
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD pgrep -f DocuRag.Worker > /dev/null || exit 1

ENTRYPOINT ["dotnet", "DocuRag.Worker.dll"]

# Dockerfile de l'API (DocuRag.Api) — multi-stage, image ASP.NET, utilisateur non-root.
# Contexte de build = racine du repo (voir azure.yaml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/DocuRag.Core/DocuRag.Core.csproj src/DocuRag.Core/
COPY src/DocuRag.Infrastructure/DocuRag.Infrastructure.csproj src/DocuRag.Infrastructure/
COPY src/DocuRag.Api/DocuRag.Api.csproj src/DocuRag.Api/
RUN dotnet restore src/DocuRag.Api/DocuRag.Api.csproj

COPY src/DocuRag.Core/ src/DocuRag.Core/
COPY src/DocuRag.Infrastructure/ src/DocuRag.Infrastructure/
COPY src/DocuRag.Api/ src/DocuRag.Api/
RUN dotnet publish src/DocuRag.Api/DocuRag.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# curl pour le health check HTTP — installé avant de passer non-root.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "DocuRag.Api.dll"]

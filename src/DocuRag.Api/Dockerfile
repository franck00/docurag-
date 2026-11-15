# syntax=docker/dockerfile:1
# Build multi-stage. Contexte de build = racine du repo (Directory.Build.props / Directory.Packages.props).
#   docker build -f src/DocuRag.Api/Dockerfile .
# En déploiement, azd le fait construire par ACR Tasks (remoteBuild).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/DocuRag.Core/DocuRag.Core.csproj src/DocuRag.Core/
COPY src/DocuRag.Api/DocuRag.Api.csproj src/DocuRag.Api/
RUN dotnet restore src/DocuRag.Api/DocuRag.Api.csproj
COPY src/DocuRag.Core/ src/DocuRag.Core/
COPY src/DocuRag.Api/ src/DocuRag.Api/
RUN dotnet publish src/DocuRag.Api/DocuRag.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocuRag.Api.dll"]

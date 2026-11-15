#!/bin/sh
# Après `azd provision`, exporte les endpoints Azure pour lancer worker / API / Function en local
# (authentification par `az login`, rôles attribués au développeur par le Bicep).
# Usage :  . ./scripts/local-settings.sh   puis   dotnet run --project src/DocuRag.Api
eval "$(azd env get-values)"
export DocuRag__Cosmos__Endpoint="$DOCURAG_COSMOS_ENDPOINT"
export DocuRag__ServiceBus__FullyQualifiedNamespace="$DOCURAG_SERVICEBUS_NAMESPACE"
export DocuRag__OpenAI__Endpoint="$DOCURAG_OPENAI_ENDPOINT"
export DocuRag__Storage__BlobServiceUri="$DOCURAG_BLOB_SERVICE_URI"
export AppConfig__Endpoint="$DOCURAG_APPCONFIG_ENDPOINT"
echo "Variables DocuRag__* exportées pour l'environnement $AZURE_ENV_NAME"

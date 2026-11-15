#!/bin/sh
# Crée (ou met à jour) l'abonnement Event Grid : BlobCreated sur documents/*.pdf → Function OnBlobCreated.
# Appelé automatiquement par azd après `azd deploy` (hook postdeploy). Idempotent.
set -eu

eval "$(azd env get-values)"

echo "Abonnement Event Grid ${EVENTGRID_SYSTEM_TOPIC_NAME} → ${INGESTION_FUNCTION_APP_NAME}/OnBlobCreated"
az eventgrid system-topic event-subscription create \
  --name blob-created-pdf \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --system-topic-name "$EVENTGRID_SYSTEM_TOPIC_NAME" \
  --endpoint-type azurefunction \
  --endpoint "${INGESTION_FUNCTION_APP_ID}/functions/OnBlobCreated" \
  --included-event-types Microsoft.Storage.BlobCreated \
  --subject-begins-with "/blobServices/default/containers/${AZURE_STORAGE_CONTAINER}/" \
  --subject-ends-with ".pdf" \
  --max-delivery-attempts 10 \
  --event-ttl 1440 \
  --output none

echo "OK"

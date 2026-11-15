# Crée (ou met à jour) l'abonnement Event Grid : BlobCreated sur documents/*.pdf → Function OnBlobCreated.
# Appelé automatiquement par azd après `azd deploy` (hook postdeploy). Idempotent.
$ErrorActionPreference = 'Stop'

azd env get-values | ForEach-Object {
    if ($_ -match '^([^=]+)="?(.*?)"?$') { Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2] }
}

Write-Host "Abonnement Event Grid $env:EVENTGRID_SYSTEM_TOPIC_NAME -> $env:INGESTION_FUNCTION_APP_NAME/OnBlobCreated"
az eventgrid system-topic event-subscription create `
  --name blob-created-pdf `
  --resource-group $env:AZURE_RESOURCE_GROUP `
  --system-topic-name $env:EVENTGRID_SYSTEM_TOPIC_NAME `
  --endpoint-type azurefunction `
  --endpoint "$env:INGESTION_FUNCTION_APP_ID/functions/OnBlobCreated" `
  --included-event-types Microsoft.Storage.BlobCreated `
  --subject-begins-with "/blobServices/default/containers/$env:AZURE_STORAGE_CONTAINER/" `
  --subject-ends-with ".pdf" `
  --max-delivery-attempts 10 `
  --event-ttl 1440 `
  --output none
if ($LASTEXITCODE -ne 0) { throw "Échec de la création de l'abonnement Event Grid" }
Write-Host "OK"

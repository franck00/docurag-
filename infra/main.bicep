targetScope = 'subscription'

@minLength(1)
@maxLength(32)
@description('Nom de l\'environnement azd (préfixe des ressources).')
param environmentName string

@minLength(1)
@description('Région Azure. francecentral ou swedencentral : gpt-4o-mini, embeddings et Cosmos vector search y sont disponibles.')
param location string

@description('Object id du développeur qui lance azd (rôles pour faire tourner les services en local). Fourni par azd.')
param principalId string = ''

@allowed(['User', 'ServicePrincipal'])
param principalType string = 'User'

@description('Image du worker (renseignée par azd après le premier déploiement).')
param workerImageName string = ''

@description('Image de l\'API (renseignée par azd après le premier déploiement).')
param apiImageName string = ''

@description('E-mail de l\'alerte budget. Vide = pas de budget.')
param budgetContactEmail string = ''

@description('Budget mensuel en devise de facturation de l\'abonnement.')
param budgetAmount int = 20

param chatModelName string = 'gpt-4o-mini'
param chatModelVersion string = '2024-07-18'
param embeddingModelName string = 'text-embedding-3-small'
param embeddingModelVersion string = '1'

var tags = { 'azd-env-name': environmentName, project: 'docurag' }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    principalId: principalId
    principalType: principalType
    workerImageName: workerImageName
    apiImageName: apiImageName
    budgetContactEmail: budgetContactEmail
    budgetAmount: budgetAmount
    chatModelName: chatModelName
    chatModelVersion: chatModelVersion
    embeddingModelName: embeddingModelName
    embeddingModelVersion: embeddingModelVersion
  }
}

// Sorties lues par azd (déploiement), par les hooks et pour lancer les services en local.
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.containerRegistryEndpoint
output AZURE_CONTAINER_REGISTRY_NAME string = resources.outputs.containerRegistryName
output AZURE_STORAGE_ACCOUNT_NAME string = resources.outputs.storageAccountName
output AZURE_STORAGE_CONTAINER string = resources.outputs.documentsContainer
output EVENTGRID_SYSTEM_TOPIC_NAME string = resources.outputs.eventGridSystemTopicName
output INGESTION_FUNCTION_APP_NAME string = resources.outputs.functionAppName
output INGESTION_FUNCTION_APP_ID string = resources.outputs.functionAppId
output SERVICE_API_URI string = resources.outputs.apiUri
output DOCURAG_COSMOS_ENDPOINT string = resources.outputs.cosmosEndpoint
output DOCURAG_SERVICEBUS_NAMESPACE string = resources.outputs.serviceBusNamespace
output DOCURAG_SERVICEBUS_NAMESPACE_NAME string = resources.outputs.serviceBusNamespaceName
output DOCURAG_OPENAI_ENDPOINT string = resources.outputs.openAIEndpoint
output DOCURAG_BLOB_SERVICE_URI string = resources.outputs.blobServiceUri
output DOCURAG_APPCONFIG_ENDPOINT string = resources.outputs.appConfigEndpoint
output APPLICATIONINSIGHTS_NAME string = resources.outputs.appInsightsName

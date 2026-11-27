// Point d'entrée azd (portée souscription) : crée le resource group puis délègue
// toutes les ressources à resources.bicep. Déploiement : `azd up`.
targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Nom de l\'environnement azd (sert à nommer le resource group et les ressources).')
param environmentName string

@minLength(1)
@description('Région Azure, ex. francecentral, westeurope.')
param location string

@description('Principal (objectId) à qui accorder les rôles data-plane pour tester en local. Vide = ignoré.')
param principalId string = ''

@description('Active le cache sémantique de /ask (J13) sur l\'API. `azd env set SEMANTIC_CACHE_ENABLED true` puis `azd provision`.')
param semanticCacheEnabled bool = false

var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    location: location
    tags: tags
    environmentName: environmentName
    principalId: principalId
    semanticCacheEnabled: semanticCacheEnabled
  }
}

output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output SERVICE_API_ENDPOINT string = resources.outputs.SERVICE_API_ENDPOINT
output AZURE_OPENAI_ENDPOINT string = resources.outputs.AZURE_OPENAI_ENDPOINT
output AZURE_COSMOS_ENDPOINT string = resources.outputs.AZURE_COSMOS_ENDPOINT
output AZURE_SERVICEBUS_NAMESPACE string = resources.outputs.AZURE_SERVICEBUS_NAMESPACE
output AZURE_STORAGE_BLOB_ENDPOINT string = resources.outputs.AZURE_STORAGE_BLOB_ENDPOINT

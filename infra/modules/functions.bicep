@description('Region de deploiement.')
param location string

@description('Tags a appliquer aux ressources.')
param tags object

@description('Identifiant unique utilise pour nommer les ressources.')
param resourceToken string

@description('Nom du compte de stockage existant, reutilise comme storage du runtime Functions.')
param storageAccountName string

@description('Chaine de connexion Cosmos DB (compte, pas base/conteneur).')
@secure()
param cosmosConnectionString string

@description('Chaine de connexion Service Bus (namespace).')
@secure()
param serviceBusConnectionString string

@description('Chaine de connexion Application Insights.')
@secure()
param appInsightsConnectionString string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

// Connection string reconstruite a partir de la cle du storage existant :
// evite de creer un 2e compte de stockage rien que pour le runtime Functions.
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

// Plan Consumption (Y1) : facture a l'execution, scale a zero, generereux quota gratuit.
resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${resourceToken}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-ingestion-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': 'ingestion' })
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'CosmosConnection', value: cosmosConnectionString }
        { name: 'ServiceBusConnection', value: serviceBusConnectionString }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output functionAppId string = functionApp.id

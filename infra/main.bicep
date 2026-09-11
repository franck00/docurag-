targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Nom de l\'environnement azd (ex: dev). Sert a deriver le nom du resource group et un identifiant unique de ressources.')
param environmentName string

@minLength(1)
@description('Region Azure de deploiement de toutes les ressources.')
param location string

@description('Email qui recoit l\'alerte de depassement de budget mensuel.')
param budgetAlertEmail string

@description('Seuil mensuel (dans la devise de facturation de l\'abonnement) au-dela duquel l\'alerte de budget se declenche.')
param budgetAmount int = 20

// Identifiant court et stable, derive de l'abonnement + l'environnement + la region,
// utilise pour generer des noms de ressources uniques (ex: compte de stockage).
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))

// Tag attendu par azd pour retrouver les ressources d'un environnement (azd down s'appuie dessus).
var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
  }
}

module serviceBus 'modules/service-bus.bicep' = {
  name: 'service-bus'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
  }
}

module functions 'modules/functions.bicep' = {
  name: 'functions'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    storageAccountName: storage.outputs.storageAccountName
    cosmosConnectionString: cosmos.outputs.connectionString
    serviceBusConnectionString: serviceBus.outputs.connectionString
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
  }
}

module eventGrid 'modules/event-grid.bicep' = {
  name: 'event-grid'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    storageAccountName: storage.outputs.storageAccountName
    functionAppId: functions.outputs.functionAppId
    functionName: 'BlobUploaded'
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  scope: rg
  params: {
    resourceGroupName: rg.name
    alertEmail: budgetAlertEmail
    amount: budgetAmount
  }
}

output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_STORAGE_ACCOUNT_NAME string = storage.outputs.storageAccountName
output AZURE_STORAGE_BLOB_ENDPOINT string = storage.outputs.blobEndpoint
output AZURE_STORAGE_DOCUMENTS_CONTAINER string = storage.outputs.documentsContainerName
output AZURE_COSMOS_ACCOUNT_NAME string = cosmos.outputs.accountName
output AZURE_COSMOS_ENDPOINT string = cosmos.outputs.endpoint
output AZURE_SERVICE_BUS_NAMESPACE string = serviceBus.outputs.namespaceName
output AZURE_SERVICE_BUS_ENDPOINT string = serviceBus.outputs.endpoint
output AZURE_SERVICE_BUS_QUEUE string = serviceBus.outputs.queueName
output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = monitoring.outputs.logAnalyticsWorkspaceName
output APPLICATIONINSIGHTS_CONNECTION_STRING string = monitoring.outputs.appInsightsConnectionString
output AZURE_FUNCTION_APP_NAME string = functions.outputs.functionAppName

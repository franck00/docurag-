@description('Region de deploiement.')
param location string

@description('Tags a appliquer aux ressources.')
param tags object

@description('Identifiant unique utilise pour nommer les ressources.')
@minLength(13)
@maxLength(13)
param resourceToken string

// Nom de compte de stockage : 3-24 caracteres, alphanumerique minuscule uniquement.
var storageAccountName = take('st${resourceToken}', 24)
var containerName = 'documents'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
}

output storageAccountName string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output documentsContainerName string = documentsContainer.name

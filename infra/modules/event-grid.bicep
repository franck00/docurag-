@description('Region de deploiement.')
param location string

@description('Tags a appliquer aux ressources.')
param tags object

@description('Identifiant unique utilise pour nommer les ressources.')
param resourceToken string

@description('Nom du compte de stockage source des evenements.')
param storageAccountName string

@description('Id de la Function App cible de l\'abonnement aux evenements.')
param functionAppId string

@description('Nom de la fonction Event Grid trigger a l\'interieur de la Function App.')
param functionName string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource systemTopic 'Microsoft.EventGrid/systemTopics@2024-06-01-preview' = {
  name: 'evgt-${resourceToken}'
  location: location
  tags: tags
  properties: {
    source: storageAccount.id
    topicType: 'Microsoft.Storage.StorageAccounts'
  }
}

// Filtre : uniquement les creations de blob dont le nom se termine par .pdf.
resource blobCreatedSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2024-06-01-preview' = {
  parent: systemTopic
  name: 'blob-created-pdf'
  properties: {
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: '${functionAppId}/functions/${functionName}'
        maxEventsPerBatch: 1
      }
    }
    filter: {
      includedEventTypes: [
        'Microsoft.Storage.BlobCreated'
      ]
      subjectEndsWith: '.pdf'
    }
  }
}

output systemTopicName string = systemTopic.name

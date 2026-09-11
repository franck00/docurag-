@description('Region de deploiement.')
param location string

@description('Tags a appliquer aux ressources.')
param tags object

@description('Identifiant unique utilise pour nommer les ressources.')
param resourceToken string

// SKU Basic : suffisant pour une simple queue point-a-point (pas de topics/sessions
// necessaires ici). A remonter en Standard le jour ou des topics deviennent utiles.
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'sb-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource docProcessingQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBusNamespace
  name: 'doc-processing'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT1M'
  }
}

output namespaceName string = serviceBusNamespace.name
output endpoint string = serviceBusNamespace.properties.serviceBusEndpoint
output queueName string = docProcessingQueue.name

@description('Chaine de connexion du namespace (regle RootManageSharedAccessKey). A utiliser uniquement jusqu\'au J11, ou elle sera remplacee par une managed identity.')
@secure()
output connectionString string = listKeys(resourceId('Microsoft.ServiceBus/namespaces/AuthorizationRules', serviceBusNamespace.name, 'RootManageSharedAccessKey'), '2024-01-01').primaryConnectionString

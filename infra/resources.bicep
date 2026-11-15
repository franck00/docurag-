// Toutes les ressources DocuRAG, dans le resource group créé par main.bicep.
// Principes :
//  - aucune clé : disableLocalAuth / allowSharedKeyAccess=false partout où c'est possible,
//    chaque service a sa managed identity (user-assigned) et seulement les rôles dont il a besoin ;
//  - coût minimal : Cosmos serverless, Service Bus Basic, Container Apps à 0 replica, Functions Flex Consumption.

param environmentName string
param location string
param tags object
param principalId string
param principalType string
param workerImageName string
param apiImageName string
param budgetContactEmail string
param budgetAmount int
param chatModelName string
param chatModelVersion string
param embeddingModelName string
param embeddingModelVersion string

@description('Date de début du budget (premier jour du mois courant).')
param budgetStartDate string = utcNow('yyyy-MM-01')

var token = toLower(uniqueString(subscription().id, resourceGroup().id, environmentName))
var prefix = 'docurag'
var documentsContainerName = 'documents'
var queueName = 'doc-processing'
var maxDeliveryCount = 5
var cosmosDatabaseName = 'docurag'
var embeddingDimensions = 1536
var placeholderImage = 'mcr.microsoft.com/k8se/quickstart:latest'
var deploymentContainerName = 'app-package-ingestion'

// ---------------------------------------------------------------------------------------------
// Rôles intégrés (ids stables)
// ---------------------------------------------------------------------------------------------
var roles = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  storageBlobDataReader: '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
  serviceBusDataOwner: '090c5cfd-751d-490a-894a-3ce6f1109419'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2552ae39'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  cognitiveServicesOpenAIUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  appConfigDataReader: '516239f1-63e1-4d78-a4de-a74fb236a071'
}
// Rôle data-plane Cosmos DB "Built-in Data Contributor" (RBAC Cosmos, distinct de l'IAM Azure).
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

// ---------------------------------------------------------------------------------------------
// Identités
// ---------------------------------------------------------------------------------------------
resource ingestionIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-ingestion-${token}'
  location: location
  tags: tags
}

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-worker-${token}'
  location: location
  tags: tags
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-api-${token}'
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------------------------
// Observabilité
// ---------------------------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${prefix}-${token}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${prefix}-${token}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------------------------
// Key Vault : la chaîne de connexion Application Insights y est stockée et référencée
// par la Function (@Microsoft.KeyVault(...)) et par les Container Apps (secret keyVaultUrl).
// ---------------------------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${take(token, 18)}'
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource appInsightsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'appinsights-connection-string'
  properties: {
    value: appInsights.properties.ConnectionString
    contentType: 'text/plain'
  }
}

// ---------------------------------------------------------------------------------------------
// Storage : conteneur "documents" (PDF déposés) + conteneur de déploiement de la Function.
// ---------------------------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${prefix}${take(token, 13)}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: documentsContainerName
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
}

// System topic Event Grid sur le storage. L'abonnement (BlobCreated, *.pdf → Function) est créé par le hook
// azd "postdeploy" : Event Grid valide le endpoint à la création, la Function doit donc déjà être déployée.
resource eventGridTopic 'Microsoft.EventGrid/systemTopics@2025-02-15' = {
  name: 'evgt-${prefix}-${token}'
  location: location
  tags: tags
  properties: {
    source: storage.id
    topicType: 'Microsoft.Storage.StorageAccounts'
  }
}

// ---------------------------------------------------------------------------------------------
// Service Bus : tier Basic (queues + dead-letter suffisent, pas besoin de topics).
// ---------------------------------------------------------------------------------------------
resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'sb-${prefix}-${token}'
  location: location
  tags: tags
  sku: { name: 'Basic', tier: 'Basic' }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: queueName
  properties: {
    maxDeliveryCount: maxDeliveryCount
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
  }
}

// ---------------------------------------------------------------------------------------------
// Cosmos DB for NoSQL : serverless + vector search (DiskANN).
// ---------------------------------------------------------------------------------------------
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: 'cosmos-${prefix}-${token}'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    disableLocalAuth: true
    locations: [
      { locationName: location, failoverPriority: 0, isZoneRedundant: false }
    ]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    capabilities: [
      { name: 'EnableServerless' }
      { name: 'EnableNoSQLVectorSearch' }
    ]
  }
}

resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: cosmosDatabaseName
  properties: {
    resource: { id: cosmosDatabaseName }
  }
}

var vectorEmbeddingPolicy = {
  vectorEmbeddings: [
    {
      path: '/embedding'
      dataType: 'float32'
      dimensions: embeddingDimensions
      distanceFunction: 'cosine'
    }
  ]
}

var vectorIndexingPolicy = {
  indexingMode: 'consistent'
  automatic: true
  includedPaths: [ { path: '/*' } ]
  // Le vecteur est exclu de l'index "classique" : il a son propre index DiskANN.
  excludedPaths: [ { path: '/embedding/*' }, { path: '/"_etag"/?' } ]
  vectorIndexes: [ { path: '/embedding', type: 'diskANN' } ]
}

resource documentsCosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDb
  name: 'documents'
  properties: {
    resource: {
      id: 'documents'
      partitionKey: { paths: [ '/id' ], kind: 'Hash', version: 2 }
    }
  }
}

resource chunksCosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDb
  name: 'chunks'
  properties: {
    resource: {
      id: 'chunks'
      partitionKey: { paths: [ '/documentId' ], kind: 'Hash', version: 2 }
      indexingPolicy: vectorIndexingPolicy
      vectorEmbeddingPolicy: vectorEmbeddingPolicy
    }
  }
}

// Cache sémantique : même politique vectorielle, entrées expirées automatiquement après 24 h (TTL).
resource cacheCosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDb
  name: 'cache'
  properties: {
    resource: {
      id: 'cache'
      partitionKey: { paths: [ '/id' ], kind: 'Hash', version: 2 }
      defaultTtl: 86400
      indexingPolicy: vectorIndexingPolicy
      vectorEmbeddingPolicy: vectorEmbeddingPolicy
    }
  }
}

resource statsCosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDb
  name: 'stats'
  properties: {
    resource: {
      id: 'stats'
      partitionKey: { paths: [ '/id' ], kind: 'Hash', version: 2 }
    }
  }
}

// Baux du change feed processor.
resource leasesCosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDb
  name: 'leases'
  properties: {
    resource: {
      id: 'leases'
      partitionKey: { paths: [ '/id' ], kind: 'Hash', version: 2 }
    }
  }
}

// ---------------------------------------------------------------------------------------------
// Azure OpenAI
// ---------------------------------------------------------------------------------------------
resource openAI 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'oai-${prefix}-${token}'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: 'oai-${prefix}-${token}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAI
  name: embeddingModelName
  sku: { name: 'GlobalStandard', capacity: 50 }
  properties: {
    model: { format: 'OpenAI', name: embeddingModelName, version: embeddingModelVersion }
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAI
  name: chatModelName
  sku: { name: 'GlobalStandard', capacity: 30 }
  properties: {
    model: { format: 'OpenAI', name: chatModelName, version: chatModelVersion }
  }
  // Les déploiements d'un même compte doivent être créés l'un après l'autre.
  dependsOn: [ embeddingDeployment ]
}

// ---------------------------------------------------------------------------------------------
// App Configuration : configuration non sensible + feature flag du cache.
// ---------------------------------------------------------------------------------------------
resource appConfig 'Microsoft.AppConfiguration/configurationStores@2024-05-01' = {
  name: 'appcs-${prefix}-${token}'
  location: location
  tags: tags
  sku: { name: 'free' }
}

var appConfigValues = {
  'DocuRag:Rag:TopK': '5'
  'DocuRag:Rag:MaxOutputTokens': '800'
  'DocuRag:Rag:Temperature': '0.1'
  'DocuRag:Cache:SimilarityThreshold': '0.95'
  'DocuRag:Chunking:TargetTokens': '800'
  'DocuRag:Chunking:OverlapTokens': '100'
  'DocuRag:OpenAI:EmbeddingBatchSize': '64'
}

resource appConfigKeyValues 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = [for item in items(appConfigValues): {
  parent: appConfig
  name: item.key
  properties: { value: item.value }
}]

resource semanticCacheFlag 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfig
  name: '.appconfig.featureflag~2FSemanticCache'
  properties: {
    contentType: 'application/vnd.microsoft.appconfig.ff+json;charset=utf-8'
    value: string({
      id: 'SemanticCache'
      description: 'Cache sémantique des réponses (/ask) dans Cosmos DB'
      enabled: true
      conditions: { client_filters: [] }
    })
  }
}

// ---------------------------------------------------------------------------------------------
// Container Registry + Container Apps (worker, API)
// ---------------------------------------------------------------------------------------------
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${prefix}${take(token, 13)}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-${prefix}-${token}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

var commonEnv = [
  { name: 'DocuRag__Cosmos__Endpoint', value: cosmos.properties.documentEndpoint }
  { name: 'DocuRag__ServiceBus__FullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
  { name: 'DocuRag__ServiceBus__QueueName', value: queueName }
  { name: 'DocuRag__ServiceBus__MaxDeliveryCount', value: string(maxDeliveryCount) }
  { name: 'DocuRag__OpenAI__Endpoint', value: openAI.properties.endpoint }
  { name: 'DocuRag__OpenAI__EmbeddingDeployment', value: embeddingDeployment.name }
  { name: 'DocuRag__OpenAI__ChatDeployment', value: chatDeployment.name }
  { name: 'DocuRag__Storage__BlobServiceUri', value: storage.properties.primaryEndpoints.blob }
  { name: 'AppConfig__Endpoint', value: appConfig.properties.endpoint }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
]

resource workerApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-${prefix}-worker-${take(token, 8)}'
  location: location
  tags: union(tags, { 'azd-service-name': 'worker' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workerIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [ { server: registry.properties.loginServer, identity: workerIdentity.id } ]
      secrets: [
        {
          name: 'appinsights-connection-string'
          keyVaultUrl: appInsightsSecret.properties.secretUri
          identity: workerIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: empty(workerImageName) ? placeholderImage : workerImageName
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, [
            { name: 'AZURE_CLIENT_ID', value: workerIdentity.properties.clientId }
          ])
        }
      ]
      // KEDA : 0 replica quand la queue est vide, 1 replica pour 5 messages, 10 au maximum.
      // Le scaler s'authentifie avec la managed identity du worker (pas de connection string).
      scale: {
        minReplicas: 0
        maxReplicas: 10
        rules: [
          {
            name: 'servicebus-queue-length'
            custom: {
              type: 'azure-servicebus'
              identity: workerIdentity.id
              metadata: {
                namespace: serviceBus.name
                queueName: queueName
                messageCount: '5'
              }
            }
          }
        ]
      }
    }
  }
  dependsOn: [ workerStorageRole, workerServiceBusRole, workerOpenAIRole, workerAcrRole, workerAppConfigRole, workerKeyVaultRole, workerCosmosRole ]
}

resource apiApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-${prefix}-api-${take(token, 8)}'
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [ { server: registry.properties.loginServer, identity: apiIdentity.id } ]
      secrets: [
        {
          name: 'appinsights-connection-string'
          keyVaultUrl: appInsightsSecret.properties.secretUri
          identity: apiIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: empty(apiImageName) ? placeholderImage : apiImageName
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, [
            { name: 'AZURE_CLIENT_ID', value: apiIdentity.properties.clientId }
            { name: 'OTEL_SERVICE_NAME', value: 'docurag-api' }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          { name: 'http', http: { metadata: { concurrentRequests: '20' } } }
        ]
      }
    }
  }
  dependsOn: [ apiServiceBusRole, apiOpenAIRole, apiAcrRole, apiAppConfigRole, apiKeyVaultRole, apiCosmosRole ]
}

// ---------------------------------------------------------------------------------------------
// Azure Functions (Flex Consumption) : ingestion Event Grid → Cosmos → Service Bus
// ---------------------------------------------------------------------------------------------
resource functionPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${prefix}-${token}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${prefix}-${token}'
  location: location
  tags: union(tags, { 'azd-service-name': 'ingestion' })
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${ingestionIdentity.id}': {} }
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    keyVaultReferenceIdentity: ingestionIdentity.id
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        // Stockage du runtime Functions par identité, sans clé de compte.
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: ingestionIdentity.properties.clientId }
        { name: 'AZURE_CLIENT_ID', value: ingestionIdentity.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: '@Microsoft.KeyVault(SecretUri=${appInsightsSecret.properties.secretUri})' }
        { name: 'OTEL_SERVICE_NAME', value: 'docurag-ingestion' }
        { name: 'DocuRag__Cosmos__Endpoint', value: cosmos.properties.documentEndpoint }
        { name: 'DocuRag__ServiceBus__FullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
        { name: 'DocuRag__ServiceBus__QueueName', value: queueName }
        { name: 'DocuRag__Storage__Container', value: documentsContainerName }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: ingestionIdentity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
    }
  }
  dependsOn: [ ingestionStorageRole, ingestionServiceBusRole, ingestionKeyVaultRole, ingestionCosmosRole, deploymentContainer ]
}

// ---------------------------------------------------------------------------------------------
// RBAC : chaque identité reçoit le minimum nécessaire, au plus près de la ressource.
// ---------------------------------------------------------------------------------------------

// Ingestion : écrit le document "Pending" (Cosmos) et publie sur la queue.
resource ingestionStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(ingestionIdentity.id, storage.id, roles.storageBlobDataOwner)
  scope: storage
  properties: {
    principalId: ingestionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataOwner)
  }
}

resource ingestionServiceBusRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(ingestionIdentity.id, queue.id, roles.serviceBusDataSender)
  scope: queue
  properties: {
    principalId: ingestionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataSender)
  }
}

// Worker : lit les PDF, consomme la queue (Data Owner : requis par le scaler KEDA pour lire la longueur de la queue),
// appelle Azure OpenAI, tire son image de l'ACR.
resource workerStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workerIdentity.id, storage.id, roles.storageBlobDataReader)
  scope: storage
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataReader)
  }
}

resource workerServiceBusRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workerIdentity.id, serviceBus.id, roles.serviceBusDataOwner)
  scope: serviceBus
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataOwner)
  }
}

resource workerOpenAIRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workerIdentity.id, openAI.id, roles.cognitiveServicesOpenAIUser)
  scope: openAI
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesOpenAIUser)
  }
}

resource workerAcrRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workerIdentity.id, registry.id, roles.acrPull)
  scope: registry
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
  }
}

resource workerAppConfigRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workerIdentity.id, appConfig.id, roles.appConfigDataReader)
  scope: appConfig
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.appConfigDataReader)
  }
}

// API : lit Cosmos, appelle Azure OpenAI, consulte la dead-letter queue (peek = Receiver).
resource apiServiceBusRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiIdentity.id, queue.id, roles.serviceBusDataReceiver)
  scope: queue
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataReceiver)
  }
}

resource apiOpenAIRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiIdentity.id, openAI.id, roles.cognitiveServicesOpenAIUser)
  scope: openAI
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesOpenAIUser)
  }
}

resource apiAcrRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiIdentity.id, registry.id, roles.acrPull)
  scope: registry
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
  }
}

resource apiAppConfigRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiIdentity.id, appConfig.id, roles.appConfigDataReader)
  scope: appConfig
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.appConfigDataReader)
  }
}

// Key Vault Secrets User pour les trois identités (lecture de la chaîne de connexion App Insights).
resource ingestionKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(ingestionIdentity.id, keyVault.id, roles.keyVaultSecretsUser)
  scope: keyVault
  properties: {
    principalId: ingestionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
  }
}

resource workerKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workerIdentity.id, keyVault.id, roles.keyVaultSecretsUser)
  scope: keyVault
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
  }
}

resource apiKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiIdentity.id, keyVault.id, roles.keyVaultSecretsUser)
  scope: keyVault
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
  }
}

// Cosmos DB : RBAC data-plane (les clés sont désactivées), pour les trois services.
resource ingestionCosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, ingestionIdentity.id, cosmosDataContributorRoleId)
  properties: {
    principalId: ingestionIdentity.properties.principalId
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    scope: cosmos.id
  }
}

resource workerCosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, workerIdentity.id, cosmosDataContributorRoleId)
  properties: {
    principalId: workerIdentity.properties.principalId
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    scope: cosmos.id
  }
}

resource apiCosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, apiIdentity.id, cosmosDataContributorRoleId)
  properties: {
    principalId: apiIdentity.properties.principalId
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    scope: cosmos.id
  }
}

// ---------------------------------------------------------------------------------------------
// Rôles du développeur (az login) pour lancer worker / API / Function en local contre Azure.
// ---------------------------------------------------------------------------------------------
resource developerStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(principalId, storage.id, roles.storageBlobDataContributor)
  scope: storage
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
  }
}

resource developerServiceBusRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(principalId, serviceBus.id, roles.serviceBusDataOwner)
  scope: serviceBus
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataOwner)
  }
}

resource developerOpenAIRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(principalId, openAI.id, roles.cognitiveServicesOpenAIUser)
  scope: openAI
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesOpenAIUser)
  }
}

resource developerAppConfigRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(principalId, appConfig.id, roles.appConfigDataReader)
  scope: appConfig
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.appConfigDataReader)
  }
}

resource developerCosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = if (!empty(principalId)) {
  parent: cosmos
  name: guid(cosmos.id, principalId, cosmosDataContributorRoleId)
  properties: {
    principalId: principalId
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    scope: cosmos.id
  }
}

// ---------------------------------------------------------------------------------------------
// Budget : alerte e-mail à 80 % et 100 % du montant mensuel.
// ---------------------------------------------------------------------------------------------
resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(budgetContactEmail)) {
  name: 'budget-${environmentName}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [ budgetContactEmail ]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [ budgetContactEmail ]
      }
    }
  }
}

output containerRegistryEndpoint string = registry.properties.loginServer
output containerRegistryName string = registry.name
output storageAccountName string = storage.name
output documentsContainer string = documentsContainerName
output blobServiceUri string = storage.properties.primaryEndpoints.blob
output eventGridSystemTopicName string = eventGridTopic.name
output functionAppName string = functionApp.name
output functionAppId string = functionApp.id
output apiUri string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output serviceBusNamespace string = '${serviceBus.name}.servicebus.windows.net'
output serviceBusNamespaceName string = serviceBus.name
output openAIEndpoint string = openAI.properties.endpoint
output appConfigEndpoint string = appConfig.properties.endpoint
output appInsightsName string = appInsights.name
output workerAppName string = workerApp.name

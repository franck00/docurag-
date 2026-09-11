@description('Region de deploiement.')
param location string

@description('Tags a appliquer aux ressources.')
param tags object

@description('Identifiant unique utilise pour nommer les ressources.')
param resourceToken string

// Compte Cosmos DB for NoSQL en mode serverless : pas de RU provisionnees a l'avance,
// facturation a l'usage reel. Adapte a un projet portfolio a faible trafic.
// La base de donnees, le conteneur et la politique d'indexation vectorielle
// (DiskANN, dimension 1536) seront ajoutes au J5, une fois qu'on connait la forme
// exacte des chunks a indexer.
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: 'cosmos-${resourceToken}'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
  }
}

output accountName string = cosmosAccount.name
output endpoint string = cosmosAccount.properties.documentEndpoint

@description('Chaine de connexion complete du compte (cle primaire). A utiliser uniquement jusqu\'au J11, ou elle sera remplacee par une managed identity.')
@secure()
output connectionString string = cosmosAccount.listConnectionStrings().connectionStrings[0].connectionString

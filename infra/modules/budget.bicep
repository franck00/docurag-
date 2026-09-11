@description('Nom du resource group surveille (utilise pour nommer le budget).')
param resourceGroupName string

@description('Email qui recoit les alertes.')
param alertEmail string

@description('Seuil mensuel de depense.')
param amount int

@description('Premier jour du mois courant (calcule automatiquement, ne pas surcharger).')
param startDate string = utcNow('yyyy-MM-01')

// Alerte de budget mensuelle, scopee sur ce resource group : ne bloque rien,
// previent juste par email a 80% et 100% du seuil defini. Recommande des le J2
// par le plan pour eviter les mauvaises surprises de facturation.
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'budget-${resourceGroupName}'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      actualGreaterThan80Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: [
          alertEmail
        ]
      }
      actualGreaterThan100Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: [
          alertEmail
        ]
      }
    }
  }
}

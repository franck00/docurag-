# Requêtes KQL (Application Insights)

À lancer dans *Application Insights → Logs*. Les trois services s'appellent `docurag-ingestion`, `docurag-worker` et `docurag-api` (`cloud_RoleName`).

## 1. Latence P95 par opération

```kusto
union requests, dependencies
| where timestamp > ago(24h)
| summarize p50 = percentile(duration, 50), p95 = percentile(duration, 95), appels = count()
    by cloud_RoleName, operation = name
| order by p95 desc
```

## 2. Taux d'échec par service et par heure

```kusto
requests
| where timestamp > ago(24h)
| summarize total = count(), echecs = countif(success == false) by cloud_RoleName, bin(timestamp, 1h)
| extend taux_echec = round(100.0 * echecs / total, 2)
| order by timestamp desc
```

## 3. Documents partis en dead-letter (et pourquoi)

```kusto
// Compteur custom émis par le worker
customMetrics
| where timestamp > ago(7d) and name == "docurag.documents.processed"
| extend status = tostring(customDimensions["status"])
| summarize documents = sum(valueSum) by status, bin(timestamp, 1d)
| order by timestamp desc
```

```kusto
// Motifs détaillés, depuis les logs du worker
traces
| where timestamp > ago(7d) and cloud_RoleName == "docurag-worker"
| where message has "dead-letter" or message has "Échec du traitement"
| project timestamp, severityLevel, message, documentId = tostring(customDimensions["DocumentId"])
| order by timestamp desc
```

Le motif exact (`InvalidPdf`, `MaxDeliveryCountExceeded`…) est aussi exposé par l'API : `GET /admin/dead-letters`.

## 4. Consommation de tokens et coût estimé par jour

```kusto
// Prix indicatifs en USD pour 1M tokens — à mettre à jour selon la grille tarifaire en vigueur.
let prix = datatable(model: string, kind: string, usd_par_million: real) [
    "text-embedding-3-small", "embedding", 0.02,
    "gpt-4o-mini", "prompt", 0.15,
    "gpt-4o-mini", "completion", 0.60
];
customMetrics
| where timestamp > ago(30d) and name == "docurag.tokens"
| extend model = tostring(customDimensions["model"]), kind = tostring(customDimensions["kind"])
| summarize tokens = sum(valueSum) by jour = bin(timestamp, 1d), model, kind
| join kind=leftouter prix on model, kind
| extend cout_usd = round(tokens / 1e6 * usd_par_million, 4)
| project jour, model, kind, tokens, cout_usd
| order by jour desc
```

## 5. Trace de bout en bout d'un document

```kusto
// Remplacer par l'id du document (GET /documents)
let documentId = "<id>";
let operations = union requests, dependencies
    | where timestamp > ago(7d)
    | where tostring(customDimensions["docurag.document_id"]) == documentId
    | distinct operation_Id;
union requests, dependencies, traces, exceptions
| where operation_Id in (operations)
| project timestamp, cloud_RoleName, itemType, name, duration, success, message, operation_Id
| order by timestamp asc
```

Grâce à la propagation de `traceparent` dans le message Service Bus, la Function (`ingestion.blob-created`)
et le worker (`worker.process-document`) partagent le même `operation_Id` : la requête ci-dessus montre
l'événement Event Grid, l'écriture Cosmos `Pending`, l'envoi sur la queue, le téléchargement du blob,
les appels d'embedding et les batchs Cosmos dans une seule chronologie.

## Bonus — efficacité du cache

```kusto
customMetrics
| where timestamp > ago(7d) and name == "docurag.cache.lookups"
| extend result = tostring(customDimensions["result"])
| summarize lookups = sum(valueSum) by result
| extend part = round(100.0 * lookups / toscalar(customMetrics
    | where timestamp > ago(7d) and name == "docurag.cache.lookups" | summarize sum(valueSum)), 1)
```

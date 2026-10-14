# Requêtes KQL (Application Insights)

Cinq requêtes pour suivre le pipeline en production. À coller dans
Application Insights → Logs.

## 1. Latence P95 par opération

```kql
dependencies
| where timestamp > ago(24h)
| summarize p95 = percentile(duration, 95), count() by name
| order by p95 desc
```

## 2. Taux d'échec par opération

```kql
requests
| where timestamp > ago(24h)
| summarize total = count(), echecs = countif(success == false) by name
| extend tauxEchec = round(100.0 * echecs / total, 2)
| order by tauxEchec desc
```

## 3. Documents en dead-letter

```kql
traces
| where timestamp > ago(7d)
| where message has "dead-letter" or message has "DeadLetter"
| project timestamp, message, severityLevel
| order by timestamp desc
```

## 4. Coût en tokens par jour (Azure OpenAI)

```kql
// Suppose une métrique custom "openai.tokens" émise par le worker/l'API.
customMetrics
| where timestamp > ago(30d)
| where name == "openai.tokens"
| summarize tokens = sum(value) by bin(timestamp, 1d)
| order by timestamp asc
```

## 5. Trace de bout en bout d'un document

```kql
// Remplacer <operationId> par l'operation_Id vu dans une trace d'upload.
union requests, dependencies, traces
| where operation_Id == "<operationId>"
| project timestamp, itemType, name, message, duration, success
| order by timestamp asc
```

# DocuRAG

Assistant Q&R sur des textes réglementaires publics (douanes CEMAC, OHADA, DGI), construit comme un pipeline d'ingestion event-driven avec recherche vectorielle sur Azure.

Projet portfolio pour la certification **AI-200 (Azure AI Cloud Developer Associate)** — voir [DocuRAG-PLAN-14-JOURS.md](DocuRAG-PLAN-14-JOURS.md) pour le déroulé jour par jour.

## Le problème

Les textes réglementaires publics (codes des douanes, actes uniformes OHADA, notes de la DGI) sont denses, peu structurés et difficiles à interroger. L'objectif est de pouvoir poser une question en langage naturel et obtenir une réponse sourcée, avec citation du document et du numéro de page.

## Architecture visée

```
 Upload PDF → Blob Storage → Event Grid → Azure Function (Ingestion)
                                                 │
                                                 ▼
                                        Service Bus (doc-processing)
                                                 │
                                                 ▼
                                   Container App (Worker) : extraction
                                   PdfPig → chunking → embeddings
                                   Azure OpenAI → écriture Cosmos DB
                                                 │
                                                 ▼
                              Cosmos DB for NoSQL (vector search, DiskANN)
                                                 │
                                                 ▼
                                   API (POST /ask) → recherche vectorielle
                                   → gpt-4o-mini → réponse + sources
```

Le schéma détaillé arrivera dans `docs/` (J7).

## Stack

| Choix | Valeur |
|---|---|
| Langage | C# / .NET 10 |
| Base vectorielle | Azure Cosmos DB for NoSQL (vector search, index DiskANN) |
| Modèles | Azure OpenAI — `text-embedding-3-small` + `gpt-4o-mini` |
| Messaging | Event Grid (system topic sur le storage) + Service Bus (queue) |
| Compute | Azure Functions (ingestion) + Azure Container Apps (worker) |
| Infra | Bicep + Azure Developer CLI (`azd`) |
| Extraction PDF | PdfPig |
| Observabilité | OpenTelemetry → Application Insights |

## Structure du repo

```
docurag/
├─ src/
│  ├─ DocuRag.Core/          # modèles, chunking, contrats
│  ├─ DocuRag.Ingestion/     # Azure Function (Event Grid trigger)
│  ├─ DocuRag.Worker/        # worker conteneurisé (Service Bus)
│  └─ DocuRag.Api/           # API minimale /ask
├─ infra/                    # Bicep
├─ docs/                     # architecture, captures, GIF de démo
├─ corpus/                   # 10-15 PDF publics
├─ JOURNAL.md
└─ README.md
```

## Statut

🚧 En construction — J1/14. Voir [JOURNAL.md](JOURNAL.md) pour le suivi jour par jour.

## Lancer en local

```bash
dotnet build
```

Les instructions de déploiement (`azd up`) et de lancement complet du pipeline seront ajoutées au fil de la semaine 1.

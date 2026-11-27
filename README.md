# DocuRAG

Assistant Q&R sur des textes réglementaires publics (douanes CEMAC, OHADA, DGI), construit comme un pipeline d'ingestion event-driven avec recherche vectorielle sur Azure.

Projet portfolio pour la certification **AI-200 (Azure AI Cloud Developer Associate)** — voir [DocuRAG-PLAN-14-JOURS.md](DocuRAG-PLAN-14-JOURS.md) pour le déroulé jour par jour.

## Le problème

Les textes réglementaires publics (codes des douanes, actes uniformes OHADA, notes de la DGI) sont denses, peu structurés et difficiles à interroger. L'objectif : poser une question en langage naturel et obtenir une réponse **sourcée**, avec citation du document et du numéro de page.

## Architecture

```
 Upload PDF → Blob Storage → Event Grid → Azure Function (Ingestion)
                                                 │  statut Pending (Cosmos)
                                                 ▼
                                        Service Bus (doc-processing)
                                                 │  KEDA scale 0..N
                                                 ▼
                                   Container App (Worker) : extraction
                                   PdfPig → chunking → embeddings (batch)
                                   Azure OpenAI → écriture Cosmos DB
                                                 │  statut Indexed
                                                 ▼
                              Cosmos DB for NoSQL (vector search, DiskANN)
                                                 │
                                                 ▼
                                   API (POST /ask) → recherche vectorielle
                                   → gpt-4o-mini → réponse + sources
```

Schéma détaillé et trace distribuée : [docs/architecture.md](docs/architecture.md).

## Stack

| Choix | Valeur |
|---|---|
| Langage | C# / .NET 10 |
| Base vectorielle | Azure Cosmos DB for NoSQL (vector search, index DiskANN) |
| Modèles | Azure OpenAI — `text-embedding-3-small` + `gpt-4o-mini` |
| Messaging | Event Grid (system topic sur le storage) + Service Bus (queue) |
| Compute | Azure Functions (ingestion) + Azure Container Apps (worker + API) |
| Infra | Bicep + Azure Developer CLI (`azd`) |
| Extraction PDF | PdfPig |
| Observabilité | OpenTelemetry → Application Insights |
| Sécurité | Managed identity + RBAC partout (aucune connection string) |

## Structure du dépôt

```
docurag/
├─ src/
│  ├─ DocuRag.Core/            # modèles, chunking, contrats (aucune dépendance Azure)
│  ├─ DocuRag.Infrastructure/  # implémentations Azure partagées (Cosmos, OpenAI, Service Bus…)
│  ├─ DocuRag.Ingestion/       # Azure Function (trigger Event Grid)
│  ├─ DocuRag.Worker/          # worker conteneurisé (Service Bus + change feed)
│  └─ DocuRag.Api/             # API minimale /ask, /documents, /dead-letters
├─ tests/DocuRag.Core.Tests/   # tests xUnit (chunking, identité)
├─ infra/                      # Bicep (main.bicep + resources.bicep)
├─ scripts/                    # upload du corpus, test de charge
├─ docs/                       # architecture (Mermaid), requêtes KQL
└─ corpus/                     # PDF publics (non versionnés)
```

> Note : le plan d'origine prévoyait 4 projets. Un 5ᵉ, `DocuRag.Infrastructure`, a été
> ajouté pour partager les implémentations Azure entre l'API, le worker et l'ingestion
> sans les dupliquer, et garder `Core` purement domaine.

## Lancer en local

Build et tests (nécessite le SDK .NET 10) :

```bash
dotnet build DocuRag.slnx
dotnet test DocuRag.slnx
```

Le pipeline complet s'exécute **contre des ressources Azure réelles** (Cosmos, Service Bus,
Azure OpenAI, Storage). Pour le faire tourner, déploie d'abord l'infra puis renseigne les
endpoints dans `appsettings.Development.json` (ou via variables d'environnement). L'auth
locale passe par `az login` (DefaultAzureCredential).

## Déployer sur Azure

Prérequis : [Azure Developer CLI](https://aka.ms/azd), une souscription Azure, `az login`.

```bash
azd up            # provisionne l'infra + build/push images + déploie
./scripts/upload-corpus.sh   # pousse le corpus → déclenche l'ingestion
azd down          # détruit tout proprement
```

Interroger l'API une fois déployée :

```bash
curl -X POST "$(azd env get-value SERVICE_API_ENDPOINT)/ask" \
  -H "Content-Type: application/json" \
  -d '{"question":"Quels sont les droits de douane applicables ?","topK":5}'
# Contraintes : question ≤ 2000 caractères, topK 1–20 (0 ou absent = 5),
# documentType ≤ 64 caractères ; sinon 400 avec la liste des erreurs.
# documentType est insensible à la casse (« OHADA » filtre comme « ohada »).
# Cache sémantique (J13, opt-in : SemanticCache__Enabled=true) : une question à
# similarité cosinus ≥ 0,95 d'une question déjà posée (même documentType et topK)
# est servie sans Cosmos ni LLM. Cache en mémoire par réplica, TTL 30 min.
# Activation sur Azure : azd env set SEMANTIC_CACHE_ENABLED true && azd provision

# Messages en dead-letter et leur motif (lecture seule, max 1–100, défaut 20)
curl "$(azd env get-value SERVICE_API_ENDPOINT)/dead-letters?max=20"
```

> État : l'infrastructure Bicep **compile** (`bicep build`, 0 avertissement) mais n'a pas
> été déployée dans l'environnement de développement (pas d'accès Azure). Un premier
> `azd provision` peut nécessiter de petits ajustements (quotas de modèles OpenAI,
> disponibilité régionale du vector search Cosmos).

## Correspondance avec les domaines AI-200

| Domaine AI-200 | Où dans le projet |
|---|---|
| Développer des solutions conteneurisées (20–25 %) | Dockerfiles multi-stage, Container Apps, KEDA ([infra](infra/resources.bicep)) |
| Solutions IA via services de données Azure (25–30 %) | Cosmos vector search, embeddings, RAG ([RagService](src/DocuRag.Infrastructure/Rag/RagService.cs)) |
| Se connecter aux services Azure (20–25 %) | Event Grid, Service Bus, change feed, résilience/DLQ |
| Sécuriser, monitorer, dépanner (20–25 %) | Managed identity + RBAC, OpenTelemetry, [KQL](docs/kql.md) |

## Statut

✅ Code complet (build + tests verts, `TreatWarningsAsErrors`), infra Bicep validée,
CI/CD GitHub Actions. Voir [JOURNAL.md](JOURNAL.md) pour le détail.

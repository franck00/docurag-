# DocuRAG

Assistant Q&R sur des textes réglementaires publics (douanes CEMAC, OHADA, DGI), construit comme un **pipeline d'ingestion event-driven avec recherche vectorielle sur Azure**.
On dépose un PDF, il est découpé, vectorisé et indexé tout seul ; on pose une question en français, on obtient une réponse **sourcée avec le nom du fichier et le numéro de page**.

Projet portfolio pour la certification **AI-200 (Azure AI Cloud Developer Associate)** — déroulé dans [DocuRAG-PLAN-14-JOURS.md](DocuRAG-PLAN-14-JOURS.md), suivi dans [JOURNAL.md](JOURNAL.md).

> 📽️ `docs/demo.gif` — à ajouter (30 s : upload d'un PDF → `curl /ask` → réponse citée → même question servie par le cache).

## Le problème

Les textes réglementaires publics (codes des douanes, actes uniformes OHADA, notes de la DGI) sont denses, peu structurés et difficiles à interroger. L'objectif : poser une question en langage naturel et obtenir une réponse vérifiable, avec citation du document et de la page.

## Architecture

```
 PDF ─▶ Blob Storage ─▶ Event Grid ─▶ Azure Function (Flex) ──▶ Cosmos « Pending »
                                            │ traceparent
                                            ▼
                                Service Bus (doc-processing) ──▶ DLQ (PDF corrompu, 5 échecs)
                                            │  KEDA 0 → 10
                                            ▼
                       Worker (Container Apps) : PdfPig → chunking 800/100 → embeddings par lots
                                            │
                                            ▼
             Cosmos DB NoSQL (DiskANN, cosinus 1536) ── change feed ──▶ stats + invalidation du cache
                                            ▲
         Client ─▶ API (Container Apps) POST /ask ─▶ cache sémantique ─▶ VectorDistance top-5 ─▶ gpt-4o-mini
```

Schémas Mermaid, flux de `/ask`, justification des choix et matrice RBAC : **[docs/architecture.md](docs/architecture.md)**.

## Ce que le projet démontre

- **Ingestion event-driven** : system topic Event Grid filtré (`BlobCreated`, `*.pdf`), Function isolated .NET 10, Service Bus.
- **Traitement résilient** : complétion explicite des messages, **dead-letter** immédiat pour un PDF corrompu (motif lisible), abandon + `maxDeliveryCount` pour les erreurs transitoires, **traitement idempotent** (ids déterministes, upsert, nettoyage des extraits orphelins), **retry exponentiel** respectant `Retry-After` sur les 429 d'Azure OpenAI.
- **RAG** : chunking par paragraphes (~800 tokens, chevauchement 100) qui conserve les pages, embeddings par lots, **index vectoriel DiskANN** dans Cosmos DB, filtre par type de document, prompt à extraits numérotés, citations.
- **Conteneurs** : Dockerfiles multi-stage non-root avec health check, build par **ACR Tasks**, **Container Apps** avec scale **KEDA** sur la longueur de la queue (0 → 10 → 0) authentifié par managed identity.
- **Sécurité** : 3 **managed identities**, RBAC minimal, aucune clé (`disableLocalAuth`, `allowSharedKeyAccess=false`), **Key Vault** (références depuis la Function et les Container Apps), **App Configuration** + **feature flag** du cache.
- **Observabilité** : OpenTelemetry → Application Insights, **trace unique à travers Service Bus** (`traceparent`), métriques custom (documents, extraits, latence d'embedding, tokens, cache), [5 requêtes KQL](docs/kql.md).
- **Cosmos avancé** : **change feed processor** (stats d'index + invalidation du cache), **cache sémantique** à deux niveaux avec TTL, batch transactionnel, concurrence optimiste par ETag.
- **Livraison** : Bicep + `azd`, GitHub Actions avec **OIDC** (aucun secret de service principal).

## Correspondance avec les domaines AI-200

| Domaine AI-200 | Poids | Où le voir |
|---|---|---|
| Développer des solutions conteneurisées | 20–25 % | `src/*/Dockerfile`, Container Apps + KEDA et ACR dans [`infra/resources.bicep`](infra/resources.bicep), `azure.yaml` (`remoteBuild`), [`scripts/load-test.sh`](scripts/load-test.sh) |
| Solutions IA via les services de données Azure | 25–30 % | [`ChunkRepository`](src/DocuRag.Core/Cosmos/Repositories.cs) (`VectorDistance`), politique vectorielle Cosmos (Bicep), [`SemanticCache`](src/DocuRag.Core/Cosmos/SemanticCache.cs), [`ChangeFeedService`](src/DocuRag.Worker/ChangeFeedService.cs), [`RagService`](src/DocuRag.Api/RagService.cs) |
| Se connecter aux services Azure et les consommer | 20–25 % | [`OnBlobCreated`](src/DocuRag.Ingestion/OnBlobCreated.cs) (Event Grid), [`QueueWorker`](src/DocuRag.Worker/QueueWorker.cs) (Service Bus, DLQ), [`RetryPolicy`](src/DocuRag.Core/AI/RetryPolicy.cs) |
| Sécuriser, monitorer, dépanner | 20–25 % | identités et RBAC (Bicep), Key Vault / App Configuration, [`TraceContextPropagation`](src/DocuRag.Core/Messaging/TraceContextPropagation.cs), [`DocuRagTelemetry`](src/DocuRag.Core/Telemetry/DocuRagTelemetry.cs), [docs/kql.md](docs/kql.md) |

## Stack

| Choix | Valeur |
|---|---|
| Langage | C# / .NET 10 |
| Base vectorielle | Azure Cosmos DB for NoSQL (serverless, vector search, index DiskANN) |
| Modèles | Azure OpenAI — `text-embedding-3-small` + `gpt-4o-mini` |
| Messaging | Event Grid (system topic) + Service Bus (queue, Basic) |
| Compute | Azure Functions Flex Consumption (ingestion) + Azure Container Apps (worker, API) |
| Infra | Bicep + Azure Developer CLI (`azd`) |
| Extraction PDF | PdfPig |
| Observabilité | OpenTelemetry → Application Insights |

## Déployer (`azd up`)

Prérequis : [.NET 10 SDK](https://dotnet.microsoft.com/download), [Azure Developer CLI](https://aka.ms/azd), [Azure CLI](https://aka.ms/azcli) (pour les hooks et scripts). Pas besoin de Docker : les images sont construites dans ACR.

```bash
az login
azd auth login
azd env new docurag-dev
azd env set AZURE_LOCATION francecentral          # ou swedencentral
azd env set BUDGET_CONTACT_EMAIL toi@exemple.com  # optionnel : alerte budget à 80 % (20 / mois par défaut)
azd up
```

`azd up` provisionne toute l'infra, déploie les trois services, puis le hook `postdeploy` crée l'abonnement Event Grid (`BlobCreated *.pdf` → Function).

Indexer le corpus puis interroger :

```bash
./scripts/upload-corpus.sh                         # corpus/<type>/*.pdf → documents/<type>/*.pdf
API=$(azd env get-value SERVICE_API_URI)
curl -s $API/documents | jq                        # statut : Pending → Processing → Indexed
curl -s -X POST $API/ask -H 'Content-Type: application/json' \
  -d '{"question":"Quelles sont les mentions obligatoires des statuts d'\''une SARL ?","docType":"ohada"}' | jq
curl -s $API/stats | jq                            # statistiques tenues par le change feed
curl -s $API/admin/dead-letters | jq               # messages en DLQ et leur motif
```

Tout détruire : `azd down --purge`. Le Bicep recrée tout en une commande.

### Endpoints de l'API

| Méthode | Route | Rôle |
|---|---|---|
| `POST` | `/ask` | `{ "question": "...", "docType": "ohada"?, "topK": 5? }` → réponse, sources (fichier, pages, score, extrait), statut du cache (`exact` / `semantic` / `miss` / `disabled`), tokens |
| `GET` | `/documents` | Documents connus et statut d'indexation |
| `GET` | `/stats` | Statistiques d'index (change feed processor) |
| `GET` | `/admin/dead-letters` | Contenu de la dead-letter queue avec le motif |
| `GET` | `/openapi/v1.json` | Spécification OpenAPI |

## Lancer en local

Les services tournent en local contre les ressources Azure provisionnées, avec ton `az login` (le Bicep donne à ton compte les rôles nécessaires).

```bash
dotnet build
dotnet test

azd provision                       # si pas déjà fait
. ./scripts/local-settings.sh       # exporte DocuRag__* depuis l'environnement azd
dotnet run --project src/DocuRag.Worker
dotnet run --project src/DocuRag.Api     # http://localhost:5064, voir src/DocuRag.Api/DocuRag.Api.http
```

La Function se lance avec `func start` dans `src/DocuRag.Ingestion` (copier `local.settings.sample.json` en `local.settings.json`).

## CI/CD

[`.github/workflows/ci-cd.yml`](.github/workflows/ci-cd.yml) : build, tests, vérification « aucune clé dans le code », lint Bicep à chaque push ; sur `main`, `azd provision` + `azd deploy` (images construites par ACR Tasks, Container Apps mises à jour).

L'authentification Azure passe par **OIDC** : une fois, en local, `azd pipeline config` crée l'identité fédérée et les variables du repo (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_ENV_NAME`, `AZURE_LOCATION`). Tant qu'elles n'existent pas, le job de déploiement est ignoré.

## Coûts

Cosmos serverless, Service Bus Basic, Container Apps à 0 replica au repos, Functions Flex Consumption, App Configuration Free : au repos, le coût se limite au stockage, au registre ACR Basic et à Log Analytics. Les tokens Azure OpenAI sont la seule dépense proportionnelle à l'usage (requête KQL n°4 pour la suivre). `azd down --purge` dès que tu ne travailles pas sur le projet.

## Structure

```
docurag/
├─ src/
│  ├─ DocuRag.Core/        # modèles, chunking, PdfPig, Azure OpenAI + retry, Cosmos (vecteurs, cache, stats), traces
│  ├─ DocuRag.Ingestion/   # Azure Function : Event Grid → Cosmos Pending → Service Bus
│  ├─ DocuRag.Worker/      # Service Bus → extraction → embeddings → Cosmos ; change feed processor
│  └─ DocuRag.Api/         # POST /ask, GET /documents, /stats, /admin/dead-letters
├─ tests/DocuRag.Core.Tests/
├─ infra/                  # Bicep (main.bicep + resources.bicep)
├─ scripts/                # abonnement Event Grid, upload du corpus, test de charge KEDA
├─ docs/                   # architecture, KQL, captures, GIF de démo
├─ corpus/                 # PDF publics (non versionnés, sources dans corpus/README.md)
├─ azure.yaml
└─ JOURNAL.md
```

## Licence

MIT

# Journal

Suivi jour par jour du plan [DocuRAG-PLAN-14-JOURS.md](DocuRAG-PLAN-14-JOURS.md).

## J1 — Fondations — 2026-09-07

- Repo git initialisé, `.gitignore` dotnet + entrées Azure (`.azure/`, `local.settings.json`).
- Solution `DocuRag.slnx` avec 4 projets : `DocuRag.Core` (classlib), `DocuRag.Ingestion` (Azure Function isolated, net10.0), `DocuRag.Worker` (BackgroundService), `DocuRag.Api` (ASP.NET Core Web API). Références de projet vers `DocuRag.Core` posées pour les trois autres.
- `Directory.Build.props` : `net10.0`, nullable, `TreatWarningsAsErrors`.
- Vulnérabilité NU1903 sur `Microsoft.OpenApi` 2.0.0 (transitif via `Microsoft.AspNetCore.OpenApi` 10.0.9) : épinglé en direct sur `2.12.2` (dernière version 2.x, compatible avec les générateurs de source d'AspNetCore.OpenApi — la 3.x casse la compilation, `IOpenApiMediaType.Example` devient read-only).
- README initial (problème, archi visée, stack, structure).
- `dotnet build` passe, 0 warning, 0 erreur.

**Reste à faire pour clore J1** : rassembler les 10-15 PDF publics dans `corpus/`.

**Blocage rencontré** : `dotnet add reference` est resté bloqué indéfiniment (process suspendu, aucune sortie, CPU quasi nul) — contourné en éditant les `.csproj` à la main pour ajouter les `<ProjectReference>`.

## J2 → J14 — Implémentation complète — 2026-10-14

Session de rattrapage : le dépôt git n'existait finalement pas (jamais `git init`), il a
été (re)créé proprement et l'historique reconstruit en commits par jour du plan. SDK .NET 10
installé localement (l'environnement n'avait que 8.0) pour builder et tester.

- **J4 — Core** : modèles (`DocumentRecord`, `DocumentChunk` à id déterministe,
  `DocumentStatus`, `DocumentIdentity`), contrats (`IEmbeddingGenerator`,
  `IChatCompletionService`, `IVectorStore`, `IDocumentQueue`, `IPdfTextExtractor`,
  `IDocumentDownloader`), chunking par paragraphes (~800 tokens, chevauchement ~100,
  page conservée). 15 tests xUnit verts — un vrai bug de chevauchement trouvé et corrigé.
- **J5 — Infrastructure** (projet ajouté) : `CosmosVectorStore` (VectorDistance),
  embeddings OpenAI en batch, chat, `ServiceBusDocumentQueue`, PdfPig, `DocumentProcessor`
  idempotent, `RagService`. DI `DefaultAzureCredential` partout, Cosmos en System.Text.Json.
- **J3 — Ingestion** : Function Event Grid → Cosmos (Pending) → Service Bus.
- **J4 — Worker** : `ServiceBusProcessor` (Complete/Abandon/DeadLetter), poison → DLQ,
  retry via `maxDeliveryCount`, restauration du `traceparent` (trace distribuée).
- **J6 — API** : `POST /ask` (réponse sourcée), `GET /documents`, `GET /health`.
- **J2/J11 — Infra** : `main.bicep` + `resources.bicep` (Storage, Event Grid, Service Bus,
  Cosmos vector/DiskANN, OpenAI ×2, Key Vault, App Config, ACR, Container Apps + KEDA,
  Function Flex). Managed identity + RBAC, `disableLocalAuth`. `bicep build` : 0 avertissement.
- **J8 — Conteneurisation** : Dockerfiles multi-stage non-root + health checks.
- **J12 — Observabilité** : OpenTelemetry → Azure Monitor sur les 3 services ; `docs/kql.md`.
- **J13 — Change feed** : `StatsChangeFeedService` (stats d'indexation incrémentales).
- **J14 — CI/CD** : `ci.yml` (build/test) + `deploy.yml` (azd, OIDC). `docs/architecture.md`.

**Reste à faire (nécessite un accès Azure)** :
- Lancer `azd up` et itérer si besoin (quotas de modèles OpenAI, région du vector search).
- Rassembler 10–15 PDF publics dans `corpus/` et les pousser (`scripts/upload-corpus.sh`).
- J9 : observer le scale KEDA avec `scripts/load-test.sh` et capturer l'écran.
- GIF de démo de 30 s dans `docs/` (dépend de la stack déployée).
- Tag `v1.0` une fois le déploiement validé.

**Choix notables** : 5ᵉ projet `Infrastructure` pour mutualiser les implémentations Azure ;
OpenTelemetry remonté en 1.15.x (les 1.10 avaient des vulnérabilités bloquées par
warnings-as-errors) ; check Newtonsoft de Cosmos désactivé (sérialisation System.Text.Json).

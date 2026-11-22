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

## Amélioration quotidienne — 2026-11-20

**Endpoint `GET /dead-letters` (J10 — gestion de la DLQ).** Le plan demandait « un endpoint
ou une commande qui liste les messages en DLQ avec la raison » ; seul le dead-lettering
existait côté worker.

- `IDeadLetterInspector` (Core) + `ServiceBusDeadLetterInspector` (Infrastructure) : lecture
  de la sous-queue `$DeadLetterQueue` en *peek* (rien n'est consommé ni verrouillé), borne
  à 100 messages par appel. Le corps illisible (cas `DeserializationError`) est toléré : le
  motif reste restitué.
- API : `GET /dead-letters?max=N` (défaut 20, 400 hors 1–100). DI : `AddDeadLetterInspection`,
  enregistrement du `ServiceBusClient` mutualisé avec `AddServiceBusQueue`.
- Bicep : variables `ServiceBus__*` ajoutées à l'API (le rôle Data Receiver de l'identité
  partagée couvre déjà le peek). `bicep build` OK.
- 2 tests unitaires sur la conversion message → entrée (corps lisible / illisible).
- `DocuRag.Api.http` : remplace l'exemple `weatherforecast` du template.

**Non vérifié** : pas de SDK .NET ni NuGet dans l'environnement — ni build ni tests lancés.
À vérifier : `dotnet build` / `dotnet test`, en particulier la signature de
`ServiceBusModelFactory.ServiceBusReceivedMessage` (paramètres nommés) en 7.18.2, puis un
appel réel à `/dead-letters` une fois la stack déployée.

## Amélioration quotidienne — 2026-11-21

**Métriques custom du worker (J12).** Le plan demandait « documents traités, chunks générés,
latence d'embedding » ; le meter `DocuRag.Worker` était bien abonné par OpenTelemetry mais
aucun instrument n'était créé, donc rien n'arrivait dans `customMetrics`.

- `DocumentProcessingMetrics` (Infrastructure/Processing) : compteurs
  `docurag.documents.processed` (tags `outcome` = `indexed`/`failed`, `document.type`),
  `docurag.chunks.generated`, histogramme `docurag.embedding.duration` (ms, un point par lot
  de 16, retries du client inclus).
- `DocumentProcessor` alimente ces instruments (succès, échec, chaque appel d'embedding).
- 2 tests unitaires via `MeterListener` (cas indexé / cas en échec), isolés par un
  `document.type` propre à chaque test puisque le meter est statique.
- `docs/kql.md` : requête n°6 sur ces métriques.

**Non vérifié** : pas de SDK .NET ni NuGet dans l'environnement — ni build ni tests lancés.
À vérifier : `dotnet build` (warnings-as-errors) / `dotnet test`, puis la présence des
métriques dans Application Insights (`customMetrics`) une fois la stack déployée — le nom
exact des `customDimensions` côté exporteur Azure Monitor reste à confirmer.

## Amélioration quotidienne — 2026-11-22

**Validation des entrées de `POST /ask` (robustesse / coûts).** L'API ne vérifiait que la
présence de la question : une question de plusieurs dizaines de milliers de caractères ou un
`topK` à 10 000 partaient tels quels vers l'embedding, Cosmos et `gpt-4o-mini` (tokens
facturés, prompt démesuré).

- `AskRequestValidator` (Core/Models) : question requise et ≤ 2000 caractères, `topK` entre
  1 et 20 (0 reste accepté = défaut 5 appliqué par `RagService`), `documentType` ≤ 64
  caractères. Pas de restriction de jeu de caractères sur le type : il suit le nom du dossier
  blob (accents possibles) et le filtre Cosmos est déjà paramétré.
- API : `/ask` renvoie 400 avec `error` (première erreur, compatible avec l'existant) et
  `errors` (liste complète), avant tout appel payant.
- 9 tests unitaires (xUnit, dont théories) dans `DocuRag.Core.Tests`.
- README : contraintes documentées sous l'exemple `curl`.

**Non vérifié** : pas de SDK .NET ni NuGet dans l'environnement — ni build ni tests lancés.
À vérifier : `dotnet build` (warnings-as-errors, analyzers xUnit) / `dotnet test`. Point
ouvert non traité : le filtre `documentType` est sensible à la casse alors que les types
sont stockés en minuscules — une normalisation côté API pourrait être utile.

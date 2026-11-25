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

## Amélioration quotidienne — 2026-11-23

**Filtre `documentType` de `/ask` insensible à la casse (robustesse).** Point ouvert la veille :
les types sont stockés en minuscules (`DocumentIdentity.ToDocumentType`) mais le filtre Cosmos
`c.documentType = @type` compare à l'identique — `"documentType":"OHADA"` renvoyait donc
« aucun extrait » sans erreur, ce qui ressemble à un trou du corpus plutôt qu'à une faute de
saisie.

- `DocumentIdentity.NormalizeDocumentTypeFilter` (Core) : `Trim` + `ToLowerInvariant`, blanc →
  `null` (pas de filtre), placé à côté de `ToDocumentType` pour garder une seule convention.
- `RagService` normalise le filtre avant `SearchAsync` ; aucune modification de la requête
  Cosmos (pas de `LOWER()` côté serveur, qui empêcherait d'utiliser l'index).
- Tests : 2 cas dans `DocumentIdentityTests` (dont l'aller-retour avec `ToDocumentType`) et une
  théorie dans `RagServiceTests` (le faux store retient désormais le filtre reçu).
- README : comportement documenté sous l'exemple `curl`.

**Non vérifié** : pas de SDK .NET ni NuGet dans l'environnement — ni build ni tests lancés.
À vérifier : `dotnet build` (warnings-as-errors, analyzers xUnit sur les `InlineData(null)`) /
`dotnet test`. Les documents déjà indexés ne sont pas concernés (types déjà en minuscules).

## Amélioration quotidienne — 2026-11-24

**Cache sémantique de `/ask` (J13, première étape).** Seul le change feed de J13 était fait ;
le cache sémantique (« seuil 0,95, gérer l'expiration et l'invalidation ») manquait. Plutôt
que d'ajouter Redis d'un coup (ressource Bicep, package, RBAC — invérifiable ici), le cache
passe par un contrat pour pouvoir brancher Redis ensuite sans toucher à `RagService`.

- `ISemanticCache` (Core/Contracts) : `TryGetAsync` / `SetAsync` sur le vecteur de la question,
  avec `documentType` (normalisé) et `topK` dans la clé.
- `InMemorySemanticCache` (Infrastructure/Rag) : recherche cosinus linéaire, seuil 0,95, TTL
  30 min (invalidation par expiration), 500 entrées max (éviction des plus anciennes),
  `TimeProvider` injectable. Sans dépendance NuGet nouvelle.
- `RagService` : consulte le cache après l'embedding (l'embedding reste payé, mais Cosmos et
  `gpt-4o-mini` sont évités), tag `ask.cache` = `hit`/`miss` sur l'activité. Les réponses
  « aucun extrait » ne sont pas mises en cache (le corpus peut grandir).
- Feature flag `SemanticCache:Enabled` (défaut `false`, section ajoutée à `appsettings.json`),
  pilotable par variable d'environnement ou App Configuration comme prévu en J11.
- 9 tests unitaires, 11 cas (`SemanticCacheTests`) : hit, seuil, périmètre, TTL, éviction, flag,
  cosinus, et deux tests `RagService` (2ᵉ question identique sans appel LLM, pas de cache sans
  extrait). README : comportement documenté sous l'exemple `curl`.

**Non vérifié** : pas de SDK .NET ni NuGet dans l'environnement — ni build ni tests lancés.
À vérifier : `dotnet build` (warnings-as-errors ; type `Lock` de .NET 9+) / `dotnet test`, la
résolution DI des paramètres optionnels (`ISemanticCache? cache = null`, `TimeProvider? time`),
puis le temps de réponse de la 2ᵉ question une fois déployé. Reste à faire : variable
`SemanticCache__Enabled` dans le Bicep de l'API, puis Redis (cache partagé entre réplicas et
invalidation à la ré-indexation).

## Amélioration quotidienne — 2026-11-25

**Feature flag du cache sémantique câblé dans le Bicep (J11/J13).** Reste à faire de la veille :
le cache existait côté code (`SemanticCache:Enabled`, défaut `false`) mais rien dans l'infra ne
permettait de l'activer sur l'API déployée sans modifier le portail à la main (et perdre le
réglage au prochain `azd provision`).

- `infra/main.bicep` / `resources.bicep` : paramètre `semanticCacheEnabled` (bool, défaut
  `false`) transmis au module et converti en variable `SemanticCache__Enabled` (`true`/`false`)
  sur le Container App de l'API uniquement (le worker n'utilise pas le cache).
- `infra/main.parameters.json` : alimenté par la variable azd `SEMANTIC_CACHE_ENABLED`
  (défaut `false`) → activation par `azd env set SEMANTIC_CACHE_ENABLED true && azd provision`.
- README : commande d'activation ajoutée sous l'exemple `curl`.
- `bicep build infra/main.bicep` OK (CLI 0.49.2, aucun avertissement).

**Non vérifié** : pas de déploiement depuis cet environnement. À vérifier : que `azd provision`
convertit bien la valeur substituée `"false"`/`"true"` en booléen pour le paramètre (pattern
courant des templates azd), puis la présence de `SemanticCache__Enabled` dans la révision de
l'API. Toujours pas de SDK .NET ici : build/tests des jours précédents restent à lancer.
Reste à faire : Redis pour un cache partagé entre réplicas (l'API peut monter à 5 réplicas,
chacun a aujourd'hui son propre cache).

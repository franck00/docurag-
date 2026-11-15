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

## J2 → J14 — Rattrapage du code — 2026-11-15

Tout le code et l'infra du plan écrits d'un coup (branche `feat/complete-docurag`). **Rien n'est encore validé sur Azure** : les « Fini quand » de chaque jour restent à cocher en déployant.

Fait :
- **J2** `infra/main.bicep` + `infra/resources.bicep`, `azure.yaml`, budget optionnel (`BUDGET_CONTACT_EMAIL`). Lint Bicep OK.
- **J3** Function `OnBlobCreated` (Event Grid → Cosmos `Pending` → Service Bus). Abonnement Event Grid créé par le hook `postdeploy` (il faut que la Function existe avant).
- **J4** `QueueWorker` (`ServiceBusProcessor`, complétion explicite), `PdfTextExtractor` (lignes/paragraphes reconstruits depuis la position des mots), `ParagraphChunker` (800/100, pages conservées).
- **J5** Azure OpenAI dans le Bicep, embeddings par lots de 64, politique vectorielle Cosmos (DiskANN, cosinus, 1536), batch transactionnel par document.
- **J6** API : `POST /ask` (top-5 + filtre `docType`, citations fichier/pages), `GET /documents`.
- **J7** `docs/architecture.md` (Mermaid), README complet.
- **J8** Dockerfiles multi-stage non-root + HEALTHCHECK (heartbeat fichier), ACR, Container Apps, `remoteBuild` azd.
- **J9** règle KEDA `azure-servicebus` (0→10, 5 messages/replica, managed identity), `scripts/load-test.sh`.
- **J10** idempotence (ids déterministes + nettoyage des orphelins), `maxDeliveryCount` 5, DLQ immédiate sur `InvalidPdf`, `GET /admin/dead-letters`, `RetryPolicy` (backoff + `Retry-After`).
- **J11** 3 identités user-assigned, RBAC minimal, `disableLocalAuth` partout, Key Vault + App Configuration + flag `SemanticCache`.
- **J12** OpenTelemetry sur les 3 services, `traceparent` dans le message Service Bus, métriques custom, `docs/kql.md`.
- **J13** change feed processor (stats + invalidation du cache), cache sémantique exact + vectoriel.
- **J14** workflow GitHub Actions (build/test/lint + `azd` en OIDC sur `main`).
- Tests unitaires : chunking, extraction PDF (PDF générés avec le writer PdfPig, PDF corrompus), retry, propagation de trace, ids.
- Central Package Management (`Directory.Packages.props`).

Écarts assumés par rapport au plan :
- **Cache sémantique dans Cosmos au lieu de Redis** : Azure Managed Redis avec recherche vectorielle n'a pas de tier gratuit ; Cosmos fait déjà le vectoriel, TTL natif, et le change feed sert à l'invalidation.
- **Functions en Flex Consumption** (le plan Consumption Linux classique est en fin de vie et ne supporte pas l'identité pour le déploiement).
- **Comptage de tokens approximatif** (~4 caractères/token) pour le chunking — suffisant pour dimensionner, pas de dépendance tokenizer.

**Blocage** : le code n'a pas pu être compilé dans la session (pas de SDK .NET ni d'accès NuGet, et GitHub Actions refuse de démarrer : compte verrouillé pour un problème de facturation). Première chose à faire : `dotnet build` + `dotnet test` en local et corriger ce qui casse.

Reste à faire (nécessite Azure ou toi) :
- [ ] `dotnet build` / `dotnet test` verts en local
- [ ] Corpus : 10-15 PDF publics dans `corpus/<type>/` + sources dans `corpus/README.md`
- [ ] `azd up` depuis zéro, `azd down --purge`, re-`azd up`
- [ ] Upload → message en queue → extraits dans le Data Explorer → `curl /ask` sourcé
- [ ] PDF corrompu → DLQ avec motif `InvalidPdf`
- [ ] Capture du scale 0 → N → 0 (`scripts/load-test.sh`) dans `docs/`
- [ ] Trace de bout en bout dans Application Insights (requête KQL n°5) — capture dans `docs/`
- [ ] 2ᵉ appel identique à `/ask` servi par le cache en quelques dizaines de ms
- [ ] `azd pipeline config` (OIDC) + régler la facturation GitHub, push sur `main` qui redéploie
- [ ] GIF de démo 30 s dans `docs/demo.gif`, tag `v1.0`

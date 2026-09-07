# DocuRAG — plan 14 jours

Projet portfolio pour la certification **AI-200 (Azure AI Cloud Developer Associate)**.
Assistant Q&R sur des textes réglementaires publics (douanes CEMAC, OHADA, DGI), construit comme un pipeline d'ingestion event-driven avec recherche vectorielle.

---

## Règles du jeu

- **1h30 à 2h par jour.** Pas plus. Un jour = un objectif, un commit, une chose qui marche.
- **Chaque jour se termine par un commit poussé.** Même si c'est moche. Le repo doit raconter 14 jours de travail.
- **Si tu sautes un jour, tu décales — tu ne doubles pas.** Doubler casse le rythme et tu abandonnes au J9.
- **Critère de fin de journée** : la ligne « Fini quand » est atteinte. Si tu n'y es pas au bout de 2h, tu notes le blocage dans `JOURNAL.md` et tu passes au lendemain quand même.
- **20 min de Microsoft Learn** en plus du code, sur le module lié à la journée. C'est ce qui transforme le projet en révision.

## Décisions techniques figées (ne pas les rediscuter en cours de route)

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

---

# Semaine 1 — faire circuler la donnée

## J1 — Fondations

- Créer le repo GitHub `docurag`, public, licence MIT, `.gitignore` dotnet.
- Créer la solution et les 4 projets ci-dessus, `Directory.Build.props` avec `net10.0`, nullable et warnings-as-errors activés.
- Rassembler le corpus : 10 à 15 PDF publics (code des douanes CEMAC, actes uniformes OHADA, notes DGI). Vérifie qu'ils sont bien publics.
- Écrire le README initial : le problème, l'architecture visée, la stack.

**Fini quand** : `dotnet build` passe, le corpus est dans `corpus/`, le README décrit le projet.

## J2 — Le socle Azure

- `azd init`, écrire `infra/main.bicep` : resource group, Storage Account (+ conteneur `documents`), Cosmos DB for NoSQL, Service Bus namespace + queue `doc-processing`, Log Analytics + Application Insights.
- Paramétrer les SKU au minimum : Cosmos en serverless, Service Bus en Basic pour l'instant.
- `azd up`. Vérifier dans le portail que tout est créé.

**Fini quand** : `azd up` déploie tout depuis zéro, et `azd down` détruit tout proprement. Teste les deux.
**Learn** : « Deploy infrastructure with Bicep ».

## J3 — Ingestion : du blob à la file

- Ajouter le system topic Event Grid sur le storage, filtré sur `Microsoft.Storage.BlobCreated` et sur l'extension `.pdf`.
- Écrire la Function `DocuRag.Ingestion` en isolated worker, trigger Event Grid : elle valide l'événement, crée un enregistrement de document dans Cosmos avec le statut `Pending`, puis publie un message sur la queue Service Bus.
- Déployer la Function et uploader un PDF pour de vrai.

**Fini quand** : tu déposes un PDF dans le conteneur et un message apparaît dans la queue Service Bus.
**Learn** : « Azure Event Grid » + « Azure Functions triggers and bindings ».

## J4 — Worker : consommation et extraction

- Écrire `DocuRag.Worker` en `BackgroundService` : processeur Service Bus (`ServiceBusProcessor`), avec `CompleteMessageAsync` / `AbandonMessageAsync` explicites.
- Télécharger le blob, extraire le texte avec PdfPig.
- Implémenter le chunking dans `DocuRag.Core` : découpage par paragraphes, cible ~800 tokens, chevauchement de 100. Garder le numéro de page dans chaque chunk (c'est ce qui rendra les citations crédibles).
- Tourner en local contre le Service Bus réel.

**Fini quand** : un upload produit N chunks affichés dans les logs du worker local.

## J5 — Embeddings et stockage vectoriel

- Déployer Azure OpenAI dans le Bicep, avec les deux modèles.
- Générer les embeddings des chunks par batch (le SDK accepte plusieurs inputs par appel — ne fais pas un appel par chunk).
- Configurer la **politique d'indexation vectorielle** du conteneur Cosmos : `vectorEmbeddingPolicy` sur le chemin `/embedding`, dimension 1536, distance cosinus, index `diskANN`.
- Écrire les chunks avec leur vecteur, passer le document à `Indexed`.

**Fini quand** : les chunks vectorisés sont visibles dans le Data Explorer Cosmos.
**Learn** : « Vector search in Azure Cosmos DB for NoSQL ». C'est le module le plus rentable des 14 jours.

## J6 — Recherche et API

- Requête `VectorDistance()` dans Cosmos, top-K = 5, avec filtre de métadonnées (par type de document).
- `DocuRag.Api` : endpoint `POST /ask` → embedding de la question → recherche vectorielle → construction du prompt avec les extraits → appel `gpt-4o-mini` → réponse **avec les sources et les numéros de page**.
- Endpoint `GET /documents` pour lister ce qui est indexé.

**Fini quand** : `curl` sur `/ask` renvoie une réponse sourcée sur ton corpus. C'est le cœur du projet — à partir d'ici tu as quelque chose à montrer.

## J7 — Rattrapage et consolidation

Journée tampon, et elle n'est pas optionnelle : tu auras du retard.

- Rattraper ce qui traîne.
- Écrire le schéma d'architecture dans `docs/` (Mermaid ou export d'un outil).
- Compléter le README : comment lancer en local, comment déployer.
- Nettoyer les branches, taguer `v0.1`.

**Fini quand** : quelqu'un d'autre pourrait cloner et faire tourner le projet en suivant le README.

---

# Semaine 2 — le rendre production-grade

C'est cette semaine qui fait la différence pour l'examen. Le RAG, tout le monde sait le faire. Le reste, non.

## J8 — Conteneurisation

- Dockerfile multi-stage pour le worker (image `mcr.microsoft.com/dotnet/runtime`, utilisateur non-root, health check).
- Pousser sur Azure Container Registry via **ACR Tasks** (`az acr build`) — pas de build local, c'est explicitement au programme.
- Ajouter Container Apps Environment + la Container App dans le Bicep, déployer le worker.

**Fini quand** : le worker tourne dans Container Apps et consomme la queue.
**Learn** : « Build and store container images with ACR » + « Azure Container Apps ».

## J9 — Scaling KEDA

- Ajouter une règle de scale KEDA sur la longueur de la queue Service Bus : min 0 replica, max 10, un replica pour 5 messages.
- Écrire un script qui balance 100 PDF d'un coup.
- Observer le scale-out puis le retour à zéro. Capturer l'écran — ça va dans le README.

**Fini quand** : tu as la preuve visuelle du scale de 0 à N et retour.
**Learn** : « Set scaling rules in Azure Container Apps ».

## J10 — Résilience

- Rendre le traitement **idempotent** : un message rejoué ne doit pas dupliquer les chunks (clé déterministe par document + index de chunk).
- Configurer `maxDeliveryCount` et gérer la **dead-letter queue** : un endpoint ou une commande qui liste les messages en DLQ avec la raison.
- Ajouter le retry avec backoff exponentiel sur les appels Azure OpenAI (throttling 429).
- Tester en injectant volontairement un PDF corrompu.

**Fini quand** : le PDF corrompu finit en DLQ avec un motif lisible, sans faire tomber le worker.

## J11 — Sécurité

- Créer Key Vault et App Configuration dans le Bicep.
- Activer les **managed identities** sur la Function, le worker et l'API.
- Attribuer les rôles RBAC : Storage Blob Data Reader, Cosmos DB Data Contributor, Service Bus Data Receiver, Key Vault Secrets User.
- **Supprimer toutes les connection strings du code et de la configuration.** `DefaultAzureCredential` partout.
- Mettre la config non sensible dans App Configuration, avec un flag de fonctionnalité (par exemple activer/désactiver le cache).

**Fini quand** : `grep -ri "AccountKey\|SharedAccessKey" src/` ne renvoie rien.
**Learn** : « Manage secrets with Key Vault » + « App Configuration ».

## J12 — Observabilité

- Instrumenter les trois services avec **OpenTelemetry**, exporteur Azure Monitor.
- Propager le contexte de trace **à travers le Service Bus** (injecter `traceparent` dans les propriétés du message, le restaurer côté worker). C'est le point technique intéressant de la journée — sans ça la trace se coupe.
- Ajouter des métriques custom : documents traités, chunks générés, latence d'embedding.
- Écrire 5 requêtes **KQL** dans `docs/kql.md` : latence P95 par opération, taux d'échec, documents en DLQ, coût en tokens par jour, trace de bout en bout d'un document.

**Fini quand** : tu vois une trace unique qui va de l'upload jusqu'à l'écriture Cosmos, en passant par la queue.
**Learn** : « Monitor with Application Insights » + « KQL basics ».

## J13 — Cache et change feed

- **Change feed processor** sur le conteneur Cosmos : à chaque nouveau document indexé, mettre à jour un compteur de statistiques. C'est explicitement au programme et personne ne le fait dans les projets portfolio.
- **Cache sémantique** avec Redis : avant d'appeler le LLM, chercher une question similaire déjà posée (recherche vectorielle sur Redis, seuil de similarité 0.95). Gérer l'expiration et l'invalidation quand un document change.

Si tu manques de temps : fais le change feed, saute Redis. Le change feed est plus discriminant.

**Fini quand** : la deuxième fois que tu poses la même question, la réponse arrive en quelques dizaines de millisecondes.

## J14 — Livraison

- Workflow GitHub Actions : build, tests, `az acr build`, déploiement Container App, avec OIDC (pas de secret de service principal dans le repo).
- README final : problème, architecture, capture du scaling, tableau de correspondance avec les domaines AI-200, instructions `azd up`.
- GIF de démo de 30 secondes dans `docs/` — c'est ce que les gens regardent en premier, et ça reste valable même après avoir détruit la stack.
- Tag `v1.0`. Post LinkedIn si tu veux capitaliser.

**Fini quand** : un push sur `main` redéploie tout seul.

---

## Correspondance avec les domaines de l'examen

| Domaine AI-200 | Poids | Jours |
|---|---|---|
| Développer des solutions conteneurisées | 20–25% | J8, J9 |
| Solutions IA via les services de données Azure | 25–30% | J5, J6, J13 |
| Se connecter aux services Azure et les consommer | 20–25% | J3, J4, J10 |
| Sécuriser, monitorer, dépanner | 20–25% | J11, J12 |

## Maîtrise des coûts

- `azd down` dès que tu ne travailles pas plusieurs jours de suite. Le Bicep te permet de tout recréer en une commande — c'est justement l'intérêt.
- Cosmos en serverless, Container Apps avec `minReplicas: 0`, Service Bus en Basic tant que tu n'as pas besoin des topics.
- Mets une **alerte de budget** dès le J2, à un seuil que tu choisis. Ça prend 3 minutes et ça évite les mauvaises surprises.
- Le GIF de démo est ta police d'assurance : tu peux détruire la stack sans perdre la preuve.

## Le piège à éviter

Ne pas passer trois jours sur l'UI. Le projet se juge sur le backend — c'est exactement ce que l'examen teste. Une page Next.js avec un champ de recherche et une liste de résultats suffit largement, et elle peut attendre après le J14.

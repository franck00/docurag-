# Corpus

Les PDF réglementaires publics ne sont **pas** versionnés dans le dépôt (voir
`.gitignore`). Dépose ici tes fichiers en respectant la convention de nommage, puis
pousse-les vers le Storage avec le script d'upload.

## Convention de nommage

```
corpus/
├─ douanes-cemac/   code-douanes-cemac.pdf, ...
├─ ohada/           acte-uniforme-societes.pdf, ...
└─ dgi/             note-circulaire-2024.pdf, ...
```

Le **premier dossier** devient le `documentType` (filtre de recherche), et le pipeline
dérive un `documentId` déterministe du chemin du blob (`{type}/{fichier}.pdf`).

## Sources publiques suggérées

- **Douanes CEMAC** — code des douanes de la CEMAC (textes communautaires publics).
- **OHADA** — actes uniformes (droit des sociétés, sûretés, etc.), publiés par l'OHADA.
- **DGI** — notes et circulaires des directions générales des impôts nationales.

> Vérifie que chaque document est bien **public et librement redistribuable** avant de
> l'ajouter. Vise 10 à 15 PDF pour un corpus de démonstration représentatif.

## Pousser le corpus vers Azure

Une fois l'infra déployée (`azd up`) et connecté (`az login`) :

```bash
./scripts/upload-corpus.sh
```

Le script lit le endpoint blob depuis les sorties azd et envoie tout `corpus/**/*.pdf`
dans le conteneur `documents`, ce qui déclenche l'ingestion via Event Grid.

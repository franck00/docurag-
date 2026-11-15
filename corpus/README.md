# Corpus

Déposer ici 10 à 15 PDF **publics**, rangés par type de document (le nom du dossier devient le `docType`,
utilisable comme filtre dans `POST /ask`) :

```
corpus/
├─ douane/   # code des douanes CEMAC, règlements CEMAC
├─ ohada/    # actes uniformes OHADA (droit commercial général, sociétés, sûretés…)
└─ dgi/      # notes et circulaires de la DGI
```

Puis : `./scripts/upload-corpus.sh` (après `azd up`).

Règles :
- uniquement des textes publiés officiellement et librement accessibles (journal officiel, sites OHADA / CEMAC / DGI) ;
- des PDF **texte** : un PDF scanné (image) part en dead-letter avec le motif « Aucun texte extractible » — l'OCR
  n'est pas dans le périmètre (ce serait Document Intelligence, une autre brique) ;
- les PDF ne sont pas versionnés dans git (voir `.gitignore`) : noter ici la source de chacun.

| Fichier | Source (URL officielle) | Date de récupération |
|---|---|---|
| | | |

#!/bin/sh
# Envoie les PDF de corpus/ dans le conteneur "documents", en conservant les sous-dossiers
# (le premier dossier = type de document : corpus/ohada/x.pdf → documents/ohada/x.pdf → docType "ohada").
# Authentification Entra ID (--auth-mode login) : aucune clé de compte.
set -eu
eval "$(azd env get-values)"

az storage blob upload-batch \
  --account-name "$AZURE_STORAGE_ACCOUNT_NAME" \
  --destination "$AZURE_STORAGE_CONTAINER" \
  --source "${1:-corpus}" \
  --pattern "*.pdf" \
  --auth-mode login \
  --overwrite \
  --output table

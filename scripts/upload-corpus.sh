#!/usr/bin/env bash
# Pousse corpus/**/*.pdf vers le conteneur blob "documents".
# Prérequis : infra déployée (azd up) + az login. Auth par identité (pas de clé).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONTAINER="documents"

# Endpoint blob depuis les sorties azd, sinon 1er argument.
BLOB_ENDPOINT="${1:-$(azd env get-value AZURE_STORAGE_BLOB_ENDPOINT 2>/dev/null || true)}"
if [[ -z "${BLOB_ENDPOINT}" ]]; then
  echo "Endpoint blob introuvable. Usage : $0 [https://<compte>.blob.core.windows.net/]" >&2
  exit 1
fi
ACCOUNT_URL="${BLOB_ENDPOINT%/}"

shopt -s globstar nullglob
count=0
for pdf in "${ROOT}"/corpus/**/*.pdf; do
  # Chemin relatif à corpus/ → {type}/{fichier}.pdf (= nom du blob).
  rel="${pdf#"${ROOT}/corpus/"}"
  echo "→ ${rel}"
  az storage blob upload \
    --blob-url "${ACCOUNT_URL}/${CONTAINER}/${rel}" \
    --file "${pdf}" \
    --auth-mode login \
    --overwrite true \
    --only-show-errors
  count=$((count + 1))
done

echo "Terminé : ${count} PDF envoyés dans ${CONTAINER}."

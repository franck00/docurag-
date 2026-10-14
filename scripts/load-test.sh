#!/usr/bin/env bash
# Test de charge (J9) : envoie N copies d'un PDF pour observer le scale KEDA
# du worker (0 → N → 0). Prérequis : infra déployée + az login.
set -euo pipefail

PDF="${1:?Usage: $0 <chemin.pdf> [N=100]}"
N="${2:-100}"
CONTAINER="documents"

BLOB_ENDPOINT="$(azd env get-value AZURE_STORAGE_BLOB_ENDPOINT 2>/dev/null || true)"
if [[ -z "${BLOB_ENDPOINT}" ]]; then
  echo "Endpoint blob introuvable (azd env). Déploie l'infra d'abord." >&2
  exit 1
fi
ACCOUNT_URL="${BLOB_ENDPOINT%/}"

echo "Envoi de ${N} copies de ${PDF}…"
for i in $(seq 1 "${N}"); do
  az storage blob upload \
    --blob-url "${ACCOUNT_URL}/${CONTAINER}/loadtest/doc-${i}.pdf" \
    --file "${PDF}" \
    --auth-mode login \
    --overwrite true \
    --only-show-errors &
  # Limite le parallélisme des uploads.
  if (( i % 10 == 0 )); then wait; fi
done
wait
echo "Terminé. Observe le scale : az containerapp replica list -n <worker> -g <rg>"

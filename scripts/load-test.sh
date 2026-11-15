#!/bin/sh
# J9 — test de charge pour observer le scale KEDA 0 → N → 0.
# Envoie N copies (100 par défaut) d'un PDF dans documents/loadtest/ en parallèle.
# Observer ensuite :  az containerapp replica list -g $AZURE_RESOURCE_GROUP -n <worker> -o table
# ou le graphe "Replica count" de la Container App dans le portail.
set -eu
eval "$(azd env get-values)"

PDF="${1:?usage: scripts/load-test.sh <fichier.pdf> [nombre]}"
COUNT="${2:-100}"
RUN="$(date +%Y%m%d-%H%M%S)"

echo "Envoi de $COUNT copies de $PDF vers documents/loadtest/$RUN/ ..."
i=1
while [ "$i" -le "$COUNT" ]; do
  az storage blob upload \
    --account-name "$AZURE_STORAGE_ACCOUNT_NAME" \
    --container-name "$AZURE_STORAGE_CONTAINER" \
    --name "loadtest/$RUN/copie-$i.pdf" \
    --file "$PDF" \
    --auth-mode login \
    --overwrite \
    --only-show-errors --output none &
  # 10 uploads en parallèle maximum
  if [ $((i % 10)) -eq 0 ]; then wait; fi
  i=$((i + 1))
done
wait
echo "Terminé. Suivi des replicas du worker :"
WORKER="$(az containerapp list -g "$AZURE_RESOURCE_GROUP" --query "[?tags.\"azd-service-name\"=='worker'].name | [0]" -o tsv)"
echo "  watch -n 5 az containerapp replica list -g $AZURE_RESOURCE_GROUP -n $WORKER -o table"

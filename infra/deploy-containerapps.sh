#!/usr/bin/env bash
# Publica o agente no Azure Container Apps rodando 24/7 (1 réplica fixa, sem escalar para zero).
# Ajuste as variáveis antes de rodar. Requer az CLI com a extensão containerapp.
set -euo pipefail

RG="rg-vigia"
LOCAL="eastus2"
ACR="acrvigia$RANDOM"
AMBIENTE="cae-vigia"
APP="vigia-agente"
FOUNDRY_ENDPOINT="https://SEU-RECURSO.openai.azure.com/"
FOUNDRY_RESOURCE_ID="/subscriptions/SUA-ASSINATURA/resourceGroups/SEU-RG/providers/Microsoft.CognitiveServices/accounts/SEU-RECURSO"
URL_DA_APLICACAO="https://sua-api.azurewebsites.net"

az group create -n "$RG" -l "$LOCAL"
az acr create -n "$ACR" -g "$RG" --sku Basic
az acr build -r "$ACR" -t vigia-agente:1.0 .

az containerapp env create -n "$AMBIENTE" -g "$RG" -l "$LOCAL"

# min = max = 1: o agente nunca dorme e nunca roda duplicado
az containerapp create -n "$APP" -g "$RG" \
  --environment "$AMBIENTE" \
  --image "$ACR.azurecr.io/vigia-agente:1.0" \
  --registry-server "$ACR.azurecr.io" --registry-identity system \
  --system-assigned \
  --min-replicas 1 --max-replicas 1 \
  --ingress internal --target-port 8080 \
  --env-vars "Foundry__Endpoint=$FOUNDRY_ENDPOINT" "Vigia__UrlDaAplicacao=$URL_DA_APLICACAO"

# A identidade do container chama o Foundry sem chave
PRINCIPAL_ID=$(az containerapp show -n "$APP" -g "$RG" --query identity.principalId -o tsv)
az role assignment create --assignee "$PRINCIPAL_ID" --role "Cognitive Services OpenAI User" --scope "$FOUNDRY_RESOURCE_ID"

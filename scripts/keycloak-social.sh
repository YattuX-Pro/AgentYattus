#!/usr/bin/env bash
# Déclare Google et GitHub comme fournisseurs d'identité du realm agent-yattus (Keycloak local de docker-compose).
#
# Les identifiants viennent de l'environnement, jamais du dépôt :
#   GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET   (console Google Cloud › Identifiants › ID client OAuth « Application Web »)
#   GITHUB_CLIENT_ID / GITHUB_CLIENT_SECRET   (GitHub › Settings › Developer settings › OAuth Apps)
# URL de redirection à déclarer chez chacun :
#   http://localhost:8380/realms/agent-yattus/broker/google/endpoint
#   http://localhost:8380/realms/agent-yattus/broker/github/endpoint
#
# Un fournisseur sans identifiants est ignoré. Le script peut être relancé : il met à jour l'existant.
# À relancer après chaque recréation du conteneur Keycloak (le realm est réimporté sans eux).
set -euo pipefail

container="${KEYCLOAK_CONTAINER:-agentyattus-keycloak-1}"
realm="agent-yattus"
kcadm() { docker exec -i "$container" /opt/keycloak/bin/kcadm.sh "$@"; }

# Compte d'administration de développement (docker-compose.yml : KEYCLOAK_ADMIN_PASSWORD, « admin » par défaut).
kcadm config credentials --server http://localhost:8380 --realm master --user admin \
  --password "${KEYCLOAK_ADMIN_PASSWORD:-admin}" >/dev/null 2>&1

configure() {
  local alias="$1" name="$2" order="$3" id="$4" secret="$5"
  if [[ -z "$id" || -z "$secret" ]]; then
    echo "$name : identifiants absents, ignoré."
    return
  fi
  # Le secret passe par l'entrée standard (fichier JSON), pas par la ligne de commande.
  local json
  json=$(printf '{"alias":"%s","providerId":"%s","displayName":"%s","enabled":true,"trustEmail":true,"config":{"clientId":"%s","clientSecret":"%s","syncMode":"IMPORT","guiOrder":"%s"}}' \
    "$alias" "$alias" "$name" "$id" "$secret" "$order")
  if kcadm get "identity-provider/instances/$alias" -r "$realm" >/dev/null 2>&1; then
    printf '%s' "$json" | kcadm update "identity-provider/instances/$alias" -r "$realm" -f - >/dev/null 2>&1
    echo "$name : mis à jour."
  else
    printf '%s' "$json" | kcadm create identity-provider/instances -r "$realm" -f - >/dev/null 2>&1
    echo "$name : ajouté."
  fi
}

configure google Google 1 "${GOOGLE_CLIENT_ID:-}" "${GOOGLE_CLIENT_SECRET:-}"
configure github GitHub 2 "${GITHUB_CLIENT_ID:-}" "${GITHUB_CLIENT_SECRET:-}"

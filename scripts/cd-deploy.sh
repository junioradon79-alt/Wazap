#!/usr/bin/env bash
# =============================================================
# CD WAZAP → SmarterASP.NET (FTP) — utilisé par .github/workflows/deploy.yml
#
# Déploiement "complet sûr" : app_offline pendant l'upload puis réactivation.
# Le web.config DISTANT est PRESERVÉ (il porte les env vars de prod :
# ConnectionStrings, tokens WhatChimp…). Pour modifier web.config, le faire
# à la main sur le serveur (voir DEPLOYMENT.md).
#
# Secrets attendus (repo) :
#   SMARTERASP_FTP_HOST / _USER / _PASSWORD / _REMOTE_DIR (défaut /wazap2)
#   SMARTERASP_APP_URL  (défaut https://junioradon79gm-001-site1.jtempurl.com/health)
#
# Usage :
#   bash scripts/cd-deploy.sh [dossier-de-publication]
# =============================================================
set -euo pipefail

HOST="${SMARTERASP_FTP_HOST:?SMARTERASP_FTP_HOST manquant}"
FTP_USER="${SMARTERASP_FTP_USER:?SMARTERASP_FTP_USER manquant}"
FTP_PASS="${SMARTERASP_FTP_PASSWORD:?SMARTERASP_FTP_PASSWORD manquant}"
REMOTE_DIR="${SMARTERASP_FTP_REMOTE_DIR:-/wazap2}"
PUBLISH_DIR="${1:-artifacts/publish-win64}"
HEALTH_URL="${SMARTERASP_APP_URL:-https://junioradon79gm-001-site1.jtempurl.com/health}"

OFFLINE="app_offline.htm"

echo "==> 1/4 Mise en maintenance ($OFFLINE)…"
printf '<html><body>Deploiement en cours…</body></html>' > "$OFFLINE"
curl -fsS --user "$FTP_USER:$FTP_PASS" --ftp-create-dirs -T "$OFFLINE" "$HOST$REMOTE_DIR/$OFFLINE"

echo "==> 2/4 Upload de $(find "$PUBLISH_DIR" -type f ! -name 'web.config' ! -name app_offline.htm ! -name appsettings.Development.json | wc -l) fichiers…"
cd "$PUBLISH_DIR"
while IFS= read -r -d '' f; do
  rel="${f#./}"
  echo "  -> $rel"
  curl -fsS --user "$FTP_USER:$FTP_PASS" --ftp-create-dirs -T "$rel" "$HOST$REMOTE_DIR/$rel"
done < <(find . -type f ! -name 'web.config' ! -name app_offline.htm ! -name appsettings.Development.json -print0)
cd - >/dev/null

echo "==> 3/4 Réactivation du site…"
curl -fsS --user "$FTP_USER:$FTP_PASS" -Q "-DELE $REMOTE_DIR/$OFFLINE" "$HOST/" >/dev/null
rm -f "$OFFLINE"

echo "==> 4/4 Health check : $HEALTH_URL"
for i in 1 2 3 4 5 6; do
  if curl -fsS --max-time 20 "$HEALTH_URL" | grep -q "Healthy\|OK"; then
    echo "✅ Déploiement terminé avec succès."
    exit 0
  fi
  echo "  (attente… $i/6)"
  sleep 10
done
echo "❌ Health check toujours en échec après 60 s." >&2
exit 1

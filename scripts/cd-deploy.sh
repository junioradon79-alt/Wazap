#!/usr/bin/env bash
# =============================================================
# CD WAZAP → SmarterASP.NET (FTP) — UPLOAD DIFFÉRENTIEL
# Utilisé par .github/workflows/deploy.yml
#
# Principe : un manifest SHA-256 (.deploy-manifest.sha256) est conservé sur le
# serveur. À chaque déploiement, on ne transfère que :
#   • les fichiers nouveaux ou dont le hash a changé ;
#   • les suppressions de fichiers qui n'existent plus localement (nettoyage).
# Le manifest distant est réécrit en FIN de déploiement (= point de commit :
# un run interrompu laisse l'ancien manifest → le run suivant re-transfère).
#
# Le web.config DISTANT est PRESERVÉ (env vars de prod). app_offline.htm n'est
# utilisé que lorsque le changement touche des binaires (.dll/.exe/.pdb) ou
# lors d'un déploiement complet.
#
# Secrets attendus (repo) :
#   SMARTERASP_FTP_HOST / _USER / _PASSWORD / _REMOTE_DIR (défaut /wazap2)
#   SMARTERASP_APP_URL  (défaut https://junioradon79gm-001-site1.jtempurl.com/health)
#
# Usage :
#   bash scripts/cd-deploy.sh [--full|--seed-manifest] [dossier-de-publication]
#   --seed-manifest : amorce/rabat le manifest distant SANS transférer de fichiers
#                    (utile après un déploiement complet manuel : évite un full).
#   --full          : force un déploiement complet (ignorer le manifest).
# Tests locaux sans réseau : CD_DRY_RUN=1 CD_REMOTE_MANIFEST=<fichier> bash scripts/cd-deploy.sh <dir>
# =============================================================
set -euo pipefail

HOST="${SMARTERASP_FTP_HOST:?SMARTERASP_FTP_HOST manquant}"
FTP_USER="${SMARTERASP_FTP_USER:?SMARTERASP_FTP_USER manquant}"
FTP_PASS="${SMARTERASP_FTP_PASSWORD:?SMARTERASP_FTP_PASSWORD manquant}"
REMOTE_DIR="${SMARTERASP_FTP_REMOTE_DIR:-/wazap2}"
HEALTH_URL="${SMARTERASP_APP_URL:-https://junioradon79gm-001-site1.jtempurl.com/health}"
DRY_RUN="${CD_DRY_RUN:-0}"
REMOTE_MAN_SRC="${CD_REMOTE_MANIFEST:-}"

MANIFEST=".deploy-manifest.sha256"
OFFLINE="app_offline.htm"

MODE="deploy"
if [ "${1:-}" = "--seed-manifest" ]; then MODE="seed"; shift; fi
if [ "${1:-}" = "--full" ]; then MODE="full"; shift; fi
if [ "${FORCE_FULL:-0}" = "1" ] || [ "${FORCE_FULL:-0}" = "true" ]; then MODE="full"; fi
PUBLISH_DIR="${1:-artifacts/publish-win64}"

TMPD="$(mktemp -d)"
trap 'rm -rf "$TMPD"' EXIT
LOCAL_MAN="$TMPD/local.manifest"
REMOTE_MAN="$TMPD/remote.manifest"
CHANGED="$TMPD/changed"
REMOVED="$TMPD/removed"

# --- manifest local (mêmes exclusions qu'à l'upload) ---------------------
build_manifest() { # $1=dir  $2=sortie
    ( cd "$1" && find . -type f \
        ! -name web.config \
        ! -name app_offline.htm \
        ! -name appsettings.Development.json \
        ! -name "$MANIFEST" \
        -print0 | LC_ALL=C sort -z | xargs -0 sha256sum \
        | sed -E 's/^([0-9a-f]{64}) +[*]?\.\//\1  /' | LC_ALL=C sort ) > "$2"
}

ftp_download() { # $1=chemin distant  $2=fichier local  -> 0 si OK
    curl -fsS --user "$FTP_USER:$FTP_PASS" "$HOST$REMOTE_DIR/$1" -o "$2" 2>/dev/null
}

ftp_upload() { # $1=chemin distant ; fichier local lu sur stdin
    curl -fsS --user "$FTP_USER:$FTP_PASS" --ftp-create-dirs -T - "$HOST$REMOTE_DIR/$1"
}

echo "==> Publication locale : $PUBLISH_DIR"
build_manifest "$PUBLISH_DIR" "$LOCAL_MAN"
total=$(wc -l < "$LOCAL_MAN")
echo "    $total fichiers indexés (hors web.config / app_offline / appsettings.Development.json / manifest)"

# Se placer dans le dossier de publication : les chemins du manifest sont relatifs à lui.
cd "$PUBLISH_DIR"

# --- amorçage du manifest (aucun transfert de fichiers) ------------------
if [ "$MODE" = "seed" ]; then
    if [ "$DRY_RUN" = "1" ]; then
        echo "==> [dry-run] seed : manifest prêt ($total lignes), aucune action réseau."
        exit 0
    fi
    echo "==> Seed du manifest distant ($MANIFEST) — aucun fichier transféré…"
    cat "$LOCAL_MAN" | ftp_upload "$MANIFEST"
    echo "✅ Manifest distant amorcé."
    exit 0
fi

# --- récupération du manifest distant --------------------------------------
FULL=0
if [ "$MODE" = "full" ]; then
    FULL=1
elif [ "$DRY_RUN" = "1" ]; then
    if [ -n "$REMOTE_MAN_SRC" ] && [ -f "$REMOTE_MAN_SRC" ]; then cp "$REMOTE_MAN_SRC" "$REMOTE_MAN"; else FULL=1; fi
else
    if ! ftp_download "$MANIFEST" "$REMOTE_MAN"; then FULL=1; fi
fi

# Conversion « hash  path » → liste « path hash » triée par chemin.
# ⚠️ La clé de comparaison est le CHEMIN (jamais la ligne complète) : un fichier modifié
# ne doit jamais être à la fois uploadé (nouveau hash) ET supprimé (ancien hash) — sinon
# on efface le fichier qu'on vient d'envoyer (bug réel du 06/09 qui a supprimé les Wazap.*).
to_pathhash() {
    local line h p
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        h="${line%%  *}"; p="${line#*  }"
        [ -n "$p" ] && printf '%s %s\n' "$p" "$h"
    done
}
local_ph="$TMPD/local.ph"; remote_ph="$TMPD/remote.ph"
to_pathhash < "$LOCAL_MAN"  | LC_ALL=C sort > "$local_ph"
to_pathhash < "$REMOTE_MAN" | LC_ALL=C sort > "$remote_ph"

if [ "$FULL" = "1" ]; then
    echo "==> Mode COMPLET (manifest absent ou --full) : $total fichiers à transférer."
    awk '{print $1}' "$local_ph" > "$CHANGED"
    : > "$REMOVED"
else
    # À uploader : chemins locaux absents du manifest distant, OU dont le hash diffère.
    LC_ALL=C join -t' ' -a1 -e MISSING -o '1.1,1.2,2.2' "$local_ph" "$remote_ph" \
        | awk '$2 != $3 {print $1}' | LC_ALL=C sort > "$CHANGED"
    # À supprimer : chemins du manifest distant totalement absents du dossier local.
    LC_ALL=C join -t' ' -v2 "$local_ph" "$remote_ph" | awk '{print $1}' > "$REMOVED"
    echo "==> Différentiel : $(wc -l < "$CHANGED") à transférer · $(wc -l < "$REMOVED") à supprimer · $total au total."
fi

n_chg=$(wc -l < "$CHANGED")
if [ "$DRY_RUN" = "1" ]; then
    echo "==> [dry-run] Plan d'action :"
    sed 's/^/    [upload] /' "$CHANGED"
    sed 's/^/    [delete] /' "$REMOVED"
    [ "$n_chg" -eq 0 ] && echo "    (aucun fichier à transférer)"
    exit 0
fi

# --- maintenance ? ----------------------------------------------------------
NEED_OFFLINE=0
if [ "$FULL" = "1" ] || grep -qE '\.(dll|exe|pdb)$' "$CHANGED"; then NEED_OFFLINE=1; fi
if [ "$NEED_OFFLINE" = "1" ]; then
    echo "==> Binaires impactés → mise en maintenance ($OFFLINE)…"
    printf '<html><body>Deploiement en cours…</body></html>' > "$TMPD/$OFFLINE"
    curl -fsS --user "$FTP_USER:$FTP_PASS" --ftp-create-dirs -T "$TMPD/$OFFLINE" "$HOST$REMOTE_DIR/$OFFLINE"
fi

# --- transfert des fichiers modifiés ---------------------------------------
echo "==> Upload de $n_chg fichiers…"
if [ "$n_chg" -gt 0 ]; then
    while IFS= read -r path; do
        [ -n "$path" ] || continue
        echo "  -> $path"
        curl -fsS --user "$FTP_USER:$FTP_PASS" --ftp-create-dirs -T "$path" "$HOST$REMOTE_DIR/$path"
    done < "$CHANGED"
fi

# --- nettoyage des fichiers périmés ------------------------------------------
n_rm=$(wc -l < "$REMOVED")
if [ "$n_rm" -gt 0 ]; then
    echo "==> Suppression de $n_rm fichiers périmés…"
    while IFS= read -r path; do
        [ -n "$path" ] || continue
        echo "  - $path"
        curl -fsS --user "$FTP_USER:$FTP_PASS" -Q "-DELE $REMOTE_DIR/$path" "$HOST/" >/dev/null 2>&1 || echo "    (absent ? ignoré)"
    done < "$REMOVED"
fi


# --- commit : réécriture du manifest distant --------------------------------
echo "==> Mise à jour du manifest distant ($MANIFEST)…"
cat "$LOCAL_MAN" | ftp_upload "$MANIFEST"

# --- réactivation ------------------------------------------------------------
if [ "$NEED_OFFLINE" = "1" ]; then
    echo "==> Réactivation du site…"
    curl -fsS --user "$FTP_USER:$FTP_PASS" -Q "-DELE $REMOTE_DIR/$OFFLINE" "$HOST/" >/dev/null
    rm -f "$TMPD/$OFFLINE"
fi

# --- health check -------------------------------------------------------------
echo "==> Health check : $HEALTH_URL"
for i in 1 2 3 4 5 6; do
    if curl -fsS --max-time 20 "$HEALTH_URL" | grep -q "Healthy\|OK"; then
        echo "✅ Déploiement terminé avec succès ($n_chg fichiers transférés)."
        exit 0
    fi
    echo "  (attente… $i/6)"
    sleep 10
done
echo "❌ Health check toujours en échec après 60 s." >&2
exit 1


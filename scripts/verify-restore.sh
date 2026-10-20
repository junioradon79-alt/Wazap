#!/usr/bin/env bash
set -euo pipefail
# Uniquement le conteneur PostgreSQL de test fourni par la CI. Aucun accès production.
container="${1:?Identifiant du conteneur PostgreSQL de test requis}"
source_db=wazap_restore_probe
target_db=wazap_restore_copy
cleanup() {
  docker exec "$container" dropdb -U postgres --if-exists "$target_db"
  docker exec "$container" dropdb -U postgres --if-exists "$source_db"
}
trap cleanup EXIT
docker exec "$container" createdb -U postgres "$source_db"
docker exec "$container" createdb -U postgres "$target_db"
docker exec "$container" psql -v ON_ERROR_STOP=1 -U postgres -d "$source_db" -c \
  "CREATE TABLE restore_probe (id integer PRIMARY KEY, amount numeric(18,2), reference text UNIQUE); INSERT INTO restore_probe VALUES (1,24818,'sandbox-vendor'),(2,1500,'sandbox-rider');"
mkdir -p artifacts/restore-check
docker exec "$container" pg_dump -U postgres -Fc "$source_db" > artifacts/restore-check/probe.dump
test -s artifacts/restore-check/probe.dump
docker exec -i "$container" pg_restore -U postgres -d "$target_db" --exit-on-error < artifacts/restore-check/probe.dump
actual=$(docker exec "$container" psql -U postgres -d "$target_db" -Atc "SELECT count(*)::text || '|' || sum(amount)::text FROM restore_probe;")
test "$actual" = '2|26318.00'
printf 'Restauration PostgreSQL vérifiée : 2 lignes, total 26318 FCFA.\n'

#!/usr/bin/env bash
# De demo resetten (of voor het eerst vullen) zonder iets te wissen: een NIEUWE database agf_demo_<datum>, gevuld met verzonnen
# demo-gegevens (scripts/demo-seed.sh). Daarna wijst deploy/.env daarheen en worden de containers herstart. Er is geen DROP en
# geen TRUNCATE: de vorige demo-database blijft staan, en het script noemt alleen welke oude databases je zelf kunt opruimen.
#
#   scripts/demo-reset.sh [<nieuwe-database>] [--ai-aan]
#
#   <nieuwe-database>  standaard agf_demo_<jjjjmmdd>; moet met agf_demo beginnen en mag nog niet bestaan.
#   --ai-aan           zet AI-lezen aan in de nieuwe demo (alleen als de klant weet dat geüploade documenten naar een externe
#                      AI-dienst gaan, en met een aparte demo-sleutel in deploy/.env).
#
# Draait op de server vanuit de hoofdmap van de repo, met Docker Compose (docs/demo-online.md). Dezelfde regels als
# scripts/new-clean-db.sh (alleen een nieuwe database, de API migreert hem), maar via de containers: op de server staat geen .NET.
# Lokaal proberen: AGF_DEMO_ENV_FILE=deploy/.env.local-test AGF_DEMO_COMPOSE_EXTRA=deploy/docker-compose.local.yml scripts/demo-reset.sh
set -euo pipefail

usage() {
    echo "Gebruik: $0 [<nieuwe-database>] [--ai-aan]" >&2
    exit 2
}

root="$(cd "$(dirname "$0")/.." && pwd)"
env_file="${AGF_DEMO_ENV_FILE:-$root/deploy/.env}"
compose_files=(-f "$root/deploy/docker-compose.demo.yml")
if [[ -n "${AGF_DEMO_COMPOSE_EXTRA:-}" ]]; then
    compose_files+=(-f "$root/$AGF_DEMO_COMPOSE_EXTRA")
fi

name=""
seed_args=()
for arg in "$@"; do
    case "$arg" in
        --ai-aan) seed_args+=(--ai-aan) ;;
        -*) usage ;;
        *) [[ -z "$name" ]] || usage; name="$arg" ;;
    esac
done
name="${name:-agf_demo_$(date +%Y%m%d)}"

# Alleen agf_demo...: zo kan een reset nooit een andere database als doel krijgen (en de API weigert in de demo toch elke andere).
if [[ ! "$name" =~ ^agf_demo[a-z0-9_]{0,55}$ ]]; then
    echo "Weigering: '$name' begint niet met agf_demo, of bevat andere tekens dan kleine letters, cijfers en _ (maximaal 63 tekens)." >&2
    exit 2
fi
if [[ ! -f "$env_file" ]]; then
    echo "Weigering: $env_file bestaat niet. Kopieer deploy/.env.demo.example naar deploy/.env en vul het in." >&2
    exit 2
fi

env_value() { grep -E "^$1=" "$env_file" | tail -n 1 | cut -d= -f2- || true; }
pg_user="$(env_value POSTGRES_USER)"
if [[ -z "$pg_user" ]]; then
    echo "Weigering: POSTGRES_USER ontbreekt in $env_file." >&2
    exit 2
fi
current="$(env_value AGF_DEMO_DATABASE)"

compose() { docker compose "${compose_files[@]}" --env-file "$env_file" "$@"; }
sql() { compose exec -T db psql -U "$pg_user" -d postgres -v ON_ERROR_STOP=1 -Atc "$1"; }

echo "Databaseserver starten (als die nog niet draait)…"
compose up -d db
for _ in $(seq 1 60); do
    compose exec -T db pg_isready -U "$pg_user" -d postgres >/dev/null 2>&1 && break
    sleep 2
done
if ! compose exec -T db pg_isready -U "$pg_user" -d postgres >/dev/null 2>&1; then
    echo "Gestopt: de databaseserver antwoordt niet. Er is niets veranderd." >&2
    exit 1
fi

if [[ "$(sql "select count(*) from pg_database where datname = '$name'")" != 0 ]]; then
    echo "Weigering: database $name bestaat al. Dit script maakt alleen een nieuwe database en raakt een bestaande nooit aan; kies een andere naam, bijvoorbeeld ${name}_2." >&2
    exit 2
fi

# Eerst de images van de API en de website opnieuw bouwen op de huidige code (na een git pull): `up -d` bouwt bestaande images
# niet opnieuw, dus anders blijft de oude versie draaien of mist de API de nieuwe migraties. Mislukt de build, dan is er nog niets veranderd.
echo "Images van API en website bouwen (huidige code)…"
if ! compose build api web; then
    echo "Gestopt: de build is mislukt. Er is niets veranderd (geen nieuwe database, deploy/.env ongewijzigd)." >&2
    exit 1
fi

# template0: nooit iets meenemen dat iemand in template1 heeft gezet. De API migreert hem bij het opstarten.
compose exec -T db createdb -U "$pg_user" -T template0 "$name"
echo "Nieuwe lege database $name gemaakt."

# deploy/.env omzetten; de vorige versie blijft als deploy/.env.vorige (ook niet in de repo).
cp -p "$env_file" "$env_file.vorige"
if grep -qE '^AGF_DEMO_DATABASE=' "$env_file"; then
    sed -i.tmp -E "s/^AGF_DEMO_DATABASE=.*/AGF_DEMO_DATABASE=$name/" "$env_file"
    rm -f "$env_file.tmp"
else
    echo "AGF_DEMO_DATABASE=$name" >>"$env_file"
fi

echo "Containers herstarten op $name; de API migreert de nieuwe database…"
compose up -d
for _ in $(seq 1 120); do
    compose exec -T api curl -fsS http://localhost:8080/health >/dev/null 2>&1 && break
    sleep 3
done
if ! compose exec -T api curl -fsS http://localhost:8080/health >/dev/null 2>&1; then
    echo "Gestopt: de API start niet op $name. Kijk in de logs: docker compose ${compose_files[*]} --env-file $env_file logs api" >&2
    echo "Terug naar de vorige demo: zet AGF_DEMO_DATABASE=$current in $env_file (of kopieer $env_file.vorige terug) en start opnieuw." >&2
    exit 1
fi

expected="$(find "$root/AgfAdmin/Migrations" -name '*.cs' ! -name '*.Designer.cs' ! -name '*ModelSnapshot.cs' | wc -l | tr -d ' ')"
applied="$(compose exec -T db psql -U "$pg_user" -d "$name" -Atc 'select count(*) from "__EFMigrationsHistory"')"
if [[ "$applied" != "$expected" ]]; then
    echo "Gestopt: $name heeft $applied van de $expected migraties. De database blijft staan; kijk in de logs van de API." >&2
    exit 1
fi

echo "Demo-gegevens invullen…"
if ! compose exec -T api /app/scripts/demo-seed.sh http://localhost:8080/api ${seed_args[@]+"${seed_args[@]}"}; then
    echo "Gestopt: het vullen lukte niet (de melding staat hierboven). De demo draait nu op een half gevulde $name." >&2
    echo "Terug naar de vorige demo: zet AGF_DEMO_DATABASE=$current in $env_file en doe 'up -d'; of reset opnieuw met een nieuwe naam (${name}_2)." >&2
    exit 1
fi

echo
if [[ -n "$current" && "$(sql "select count(*) from pg_database where datname = '$current'")" == 1 ]]; then
    echo "Klaar: de demo draait op $name. De vorige database ($current) blijft staan."
else
    echo "Klaar: de demo draait op $name."
fi
keep_days="$(env_value AGF_DEMO_KEEP_DAYS)"
keep_days="${keep_days:-14}"
cutoff="$(date -d "-$keep_days days" +%Y%m%d 2>/dev/null || date -v-"$keep_days"d +%Y%m%d)"
old=()
while IFS= read -r database; do
    [[ -z "$database" || "$database" == "$name" ]] && continue
    day="$(sed -nE 's/^agf_demo_([0-9]{8}).*/\1/p' <<<"$database")"
    if [[ -n "$day" && "$day" < "$cutoff" ]]; then old+=("$database"); fi
done < <(sql "select datname from pg_database where datname like 'agf\_demo%' order by datname")
if [[ ${#old[@]} -gt 0 ]]; then
    echo "Ouder dan $keep_days dagen (je kunt ze zelf opruimen, zie docs/demo-online.md › Oude demo-databases opruimen):"
    printf '  %s\n' "${old[@]}"
fi

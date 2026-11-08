#!/usr/bin/env bash
# Schone start: een NIEUWE, lege database met alleen wat de app nodig heeft om te werken. Er wordt nooit iets verwijderd,
# geleegd of overschreven: het script weigert een database die al bestaat, en de oude databases blijven staan als back-up.
#
#   scripts/new-clean-db.sh <nieuwe-database> [--settings-from <bestaande-database>]
#
# Wat er in komt:
#   - het schema via de gewone migraties (de API migreert bij het opstarten; demo-data staat daarbij expliciet uit);
#   - wat de migraties zelf vullen: de landenlijst, de standaard betaalcondities (14 en 30 dagen) en de standaard kostenartikelen;
#   - met --settings-from ook de instellingen uit die database (alleen lezen daar): bedrijfsinstellingen met de
#     partijnummering (ook de tweede reeks voor verkoop-partijen, als de bron die al heeft), eigen betaalcondities en snelkoppelingen
#     van landen. Regels met een testkenmerk (SMOKE-, DEMO-TOUR-,
#     E2E-) worden overgeslagen.
# Wat er niet in komt: relaties, artikelen, orders, ontvangsten, pallets, keuringen, reserveringen, facturen, partijen en hun historie.
#
# Er draait tijdelijk een API op poort 5296 (CLEAN_DB_PORT); daarna wordt die weer gestopt. Zie docs/schone-start.md.
set -euo pipefail

usage() {
    echo "Gebruik: $0 <nieuwe-database> [--settings-from <bestaande-database>]" >&2
    exit 2
}

database="${1:-}"
[[ -n "$database" ]] || usage
shift
source_db=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --settings-from) source_db="${2:-}"; [[ -n "$source_db" ]] || usage; shift 2 ;;
        *) usage ;;
    esac
done

if [[ ! "$database" =~ ^[a-z][a-z0-9_]{0,62}$ ]]; then
    echo "Weigering: '$database' is geen geldige naam (kleine letters, cijfers en _, beginnend met een letter)." >&2
    exit 2
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
port="${CLEAN_DB_PORT:-5296}"
api="http://localhost:$port/api"
# Eén PostgreSQL-server voor alle stappen: psql en createdb krijgen dezelfde host, poort en gebruiker als de API.
pg_host="${PGHOST:-localhost}"
pg_port="${PGPORT:-5432}"
user="${PGUSER:-$(whoami)}"
export PGHOST="$pg_host" PGPORT="$pg_port" PGUSER="$user"
connection="Host=$pg_host;Port=$pg_port;Database=$database;Username=$user${PGPASSWORD:+;Password=$PGPASSWORD}"

exists() { [[ "$(psql -d postgres -Atc "select count(*) from pg_database where datname = '$1'")" == 1 ]]; }

if exists "$database"; then
    echo "Weigering: database $database bestaat al. Dit script maakt alleen een nieuwe database en raakt een bestaande nooit aan." >&2
    exit 2
fi
if [[ -n "$source_db" ]] && ! exists "$source_db"; then
    echo "Weigering: database $source_db (voor --settings-from) bestaat niet." >&2
    exit 2
fi
if [[ "$source_db" == "$database" ]]; then
    echo "Weigering: bron en doel zijn dezelfde database." >&2
    exit 2
fi

# De tijdelijke API moet echt deze nieuwe database migreren: draait er al iets op de poort, dan zou de controle dat antwoord zien.
if curl -s -o /dev/null --max-time 2 "http://localhost:$port/"; [[ $? -ne 7 ]]; then
    echo "Weigering: poort $port is al in gebruik. Stop wat daar draait of kies een andere poort met CLEAN_DB_PORT." >&2
    exit 2
fi

# template0: nooit iets meenemen dat iemand in template1 heeft gezet.
createdb -T template0 "$database"
tables="$(psql -d "$database" -Atc "select count(*) from information_schema.tables where table_schema = 'public'")"
if [[ "$tables" != 0 ]]; then
    echo "Gestopt: de nieuwe database $database is niet leeg ($tables tabellen). Er is niets gemigreerd; kijk wat er mis is." >&2
    exit 1
fi

log="$(mktemp -t new-clean-db-api)"
ConnectionStrings__DefaultConnection="$connection" \
    ASPNETCORE_ENVIRONMENT=Development Development__SeedDemoData=false \
    ASPNETCORE_URLS="http://localhost:$port" \
    dotnet run --project "$root/AgfAdmin" --no-launch-profile >"$log" 2>&1 &
api_pid=$!
trap 'kill $api_pid 2>/dev/null || true; pkill -P $api_pid 2>/dev/null || true' EXIT

for _ in $(seq 1 150); do
    curl -sf -o /dev/null "$api/countries" && break
    kill -0 "$api_pid" 2>/dev/null || break
    sleep 2
done
curl -sf -o /dev/null "$api/countries" || { echo "De API start niet op $database; zie $log" >&2; exit 1; }
kill "$api_pid" 2>/dev/null || true
pkill -P "$api_pid" 2>/dev/null || true
wait "$api_pid" 2>/dev/null || true
trap - EXIT

# Gemigreerd tot en met de laatste migratie van deze code?
expected="$(find "$root/AgfAdmin/Migrations" -name '*.cs' ! -name '*.Designer.cs' ! -name '*ModelSnapshot.cs' | wc -l | tr -d ' ')"
applied="$(psql -d "$database" -Atc 'select count(*) from "__EFMigrationsHistory"' 2>/dev/null || echo 0)"
if [[ "$applied" != "$expected" ]]; then
    echo "Gestopt: $database heeft $applied van de $expected migraties; zie $log. De database blijft staan." >&2
    exit 1
fi

# Instellingen overnemen: alleen lezen uit de bron (COPY naar een tijdelijk bestand), schrijven alleen in de nieuwe database.
if [[ -n "$source_db" ]]; then
    work="$(mktemp -d -t new-clean-db)"
    # De export bevat bedrijfsgegevens (IBAN, contact): ook bij een fout opruimen.
    trap 'rm -rf "$work"' EXIT
    test_marker="'(SMOKE-|DEMO-TOUR-|E2E-)'"
    psql -d "$source_db" -Atc "copy (select \"CompanyName\", \"Address\", \"Email\", \"Phone\", \"KvkNumber\", \"VatNumber\", \"Iban\", \"LogoPath\",
        \"PurchaseInvoiceOptionalFields\", \"PurchaseInvoiceDefaultVatPercentage\", \"PurchaseInvoiceDefaultPaymentTermsDays\",
        \"PurchaseInvoiceDefaultCurrency\", \"GlobalGapPortalUrl\", \"PartijNumberMode\", \"PartijNumberFormat\", \"PartijSequenceReset\"
        from \"CompanySettings\" order by \"Id\" limit 1) to stdout with csv" >"$work/settings.csv"
    psql -d "$source_db" -Atc "copy (select \"Code\", \"CodeKey\", \"DescriptionNl\", \"DescriptionEn\", \"Days\", \"Basis\", \"DiscountPercent\",
        \"DiscountDays\", \"IsActive\", \"SortOrder\" from \"PaymentConditions\"
        where \"DescriptionNl\" !~* $test_marker and \"DescriptionEn\" !~* $test_marker and \"Code\" !~* $test_marker
        order by \"Id\") to stdout with csv" >"$work/payment.csv"
    psql -d "$source_db" -Atc "copy (select \"Alias\", \"AliasKey\", \"CountryCode\" from \"CountryAliases\"
        where \"Alias\" !~* $test_marker order by \"Id\") to stdout with csv" >"$work/aliases.csv"
    psql -d "$database" -v ON_ERROR_STOP=1 -q <<SQL
begin;
\copy "CompanySettings" ("CompanyName", "Address", "Email", "Phone", "KvkNumber", "VatNumber", "Iban", "LogoPath", "PurchaseInvoiceOptionalFields", "PurchaseInvoiceDefaultVatPercentage", "PurchaseInvoiceDefaultPaymentTermsDays", "PurchaseInvoiceDefaultCurrency", "GlobalGapPortalUrl", "PartijNumberMode", "PartijNumberFormat", "PartijSequenceReset") from '$work/settings.csv' with csv
create temp table import_payment ("Code" text, "CodeKey" text, "DescriptionNl" text, "DescriptionEn" text, "Days" int, "Basis" text,
    "DiscountPercent" numeric, "DiscountDays" int, "IsActive" boolean, "SortOrder" int) on commit drop;
\copy import_payment from '$work/payment.csv' with csv
-- Zelfde code als een standaardconditie uit de migratie (14D, 30D): de waarden van het bedrijf gelden.
update "PaymentConditions" p set "Code" = i."Code", "DescriptionNl" = i."DescriptionNl", "DescriptionEn" = i."DescriptionEn", "Days" = i."Days",
    "Basis" = i."Basis", "DiscountPercent" = i."DiscountPercent", "DiscountDays" = i."DiscountDays", "IsActive" = i."IsActive", "SortOrder" = i."SortOrder", "UpdatedAt" = now()
    from import_payment i where p."CodeKey" = i."CodeKey";
insert into "PaymentConditions" ("Code", "CodeKey", "DescriptionNl", "DescriptionEn", "Days", "Basis", "DiscountPercent", "DiscountDays", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt", "Version")
    select "Code", "CodeKey", "DescriptionNl", "DescriptionEn", "Days", "Basis", "DiscountPercent", "DiscountDays", "IsActive", "SortOrder", now(), now(), 0
    from import_payment i where not exists (select 1 from "PaymentConditions" p where p."CodeKey" = i."CodeKey");
create temp table import_alias ("Alias" varchar(40), "AliasKey" varchar(40), "CountryCode" char(2)) on commit drop;
\copy import_alias from '$work/aliases.csv' with csv
insert into "CountryAliases" ("Alias", "AliasKey", "CountryCode", "ActorReference", "CreatedAt")
    select "Alias", "AliasKey", "CountryCode", 'schone start', now()
    from import_alias i where not exists (select 1 from "CountryAliases" a where a."AliasKey" = i."AliasKey");
commit;
SQL
    # Nummering van P2 (formaat en cijfers voor verkoop-partijen, cijfers voor inkoop-partijen): alleen als de bron die kolommen al heeft.
    if [[ "$(psql -d "$source_db" -Atc "select count(*) from information_schema.columns where table_name = 'CompanySettings' and column_name = 'SalesPartijNumberFormat'")" == 1 ]]; then
        psql -d "$source_db" -Atc "copy (select \"PartijSequenceDigits\", \"SalesPartijNumberFormat\", \"SalesPartijSequenceDigits\" from \"CompanySettings\" order by \"Id\" limit 1) to stdout with csv" >"$work/numbering.csv"
        IFS=, read -r digits sales_format sales_digits <"$work/numbering.csv"
        psql -d "$database" -v ON_ERROR_STOP=1 -q -v digits="$digits" -v sales_format="$sales_format" -v sales_digits="$sales_digits" <<'SQL'
update "CompanySettings" set "PartijSequenceDigits" = :digits, "SalesPartijNumberFormat" = :'sales_format', "SalesPartijSequenceDigits" = :sales_digits;
SQL
    fi
    rm -rf "$work"
    trap - EXIT
fi

# Controle: alleen referentietabellen mogen rijen hebben.
reference='^(__EFMigrationsHistory|Countries|PaymentConditions|CompanySettings|CountryAliases|CostArticles)$'
unexpected=""
summary=""
while read -r table; do
    count="$(psql -d "$database" -Atc "select count(*) from \"$table\"")"
    [[ "$count" == 0 ]] && continue
    summary+="  $table: $count"$'\n'
    [[ "$table" =~ $reference ]] || unexpected+=" $table"
done < <(psql -d "$database" -Atc "select table_name from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1")
if [[ -n "$unexpected" ]]; then
    echo "Let op: onverwacht gegevens in$unexpected. De database $database blijft staan; kijk wat er gebeurd is." >&2
    exit 1
fi

rm -f "$log"
echo "Klaar: nieuwe lege database $database."
printf '%s' "$summary"
echo "Verbindingsreeks voor de API: Host=$pg_host;Port=$pg_port;Database=$database;Username=$user (zie docs/schone-start.md)."

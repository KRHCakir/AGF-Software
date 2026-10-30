#!/usr/bin/env bash
# Verse testdatabase voor de ontvangst- en afwijkingstests: een lege database, het schema via de gewone migraties (de API
# migreert bij het opstarten) en een kleine, consistente set gegevens via de gewone API's (geen SQL op tabellen):
#
#   Leveranciers  Nature's Pride, Fruitimport Zuid B.V.   Klant  Lokaal Fruit B.V.
#   Artikelen     Avocado Hass   Peru 18, Peru 20, Chili 18, Zuid-Afrika 18
#                 Mango Palmer   Brazilië 8, Brazilië 10, Peru 8
#                 Citroen Primofiori  Spanje 3, Spanje 4, Zuid-Afrika 4
#                 allemaal 4 kg doos, colli, 4 kg netto per colli (berekend), 240 colli per pallet, klasse I, geen merk
#   Orders        IO …: Avocado Hass Peru 18, 10 colli, 40 kg, partij TEST-001 (Nature's Pride)
#                 IO …: Mango Palmer Brazilië 8, 10 colli, 40 kg, partij TEST-002 (Fruitimport Zuid B.V.)
#
# Gebruik (vanuit de repo):
#   scripts/receipt-test-data.sh <database> [--recreate]
# De databasenaam moet "test" of "clean" bevatten (nooit agf_smoke). Bestaat de database al, dan alleen met --recreate
# (die wordt dan eerst verwijderd). Er draait tijdelijk een API op poort 5299; daarna wordt die weer gestopt.
set -euo pipefail

database="${1:-}"
recreate="${2:-}"
if [[ -z "$database" ]]; then
    echo "Gebruik: $0 <database> [--recreate]" >&2
    exit 2
fi
if [[ "$database" == "agf_smoke" || ! "$database" =~ (test|clean) ]]; then
    echo "Weigering: '$database' is geen testdatabase (de naam moet 'test' of 'clean' bevatten, nooit agf_smoke)." >&2
    exit 2
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
port="${RECEIPT_TEST_PORT:-5299}"
api="http://localhost:$port/api"
user="${PGUSER:-$(whoami)}"

if psql -lqt | cut -d '|' -f 1 | grep -qw "$database"; then
    if [[ "$recreate" != "--recreate" ]]; then
        echo "Database $database bestaat al; gebruik --recreate om hem opnieuw op te bouwen." >&2
        exit 2
    fi
    dropdb "$database"
fi
createdb "$database"

log="$(mktemp -t receipt-test-data-api)"
ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=$database;Username=$user" \
    ASPNETCORE_URLS="http://localhost:$port" \
    dotnet run --project "$root/AgfAdmin" --no-launch-profile >"$log" 2>&1 &
api_pid=$!
trap 'kill $api_pid 2>/dev/null || true; pkill -P $api_pid 2>/dev/null || true' EXIT

for _ in $(seq 1 120); do
    curl -sf -o /dev/null "$api/articles/reference" && break
    sleep 2
done
curl -sf -o /dev/null "$api/articles/reference" || { echo "API start niet; zie $log" >&2; exit 1; }

uuid() { uuidgen | tr 'A-Z' 'a-z'; }
post() {
    local path="$1" body="$2" out status
    out="$(curl -s -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST "$api$path" -d "$body")"
    status="${out##*$'\n'}"
    if [[ "$status" != 200 && "$status" != 201 ]]; then
        echo "POST $path gaf $status: ${out%$'\n'*}" >&2
        exit 1
    fi
    printf '%s' "${out%$'\n'*}"
}

relation() { # naam zoekcode rol
    post /relations "{\"requestId\":\"$(uuid)\",\"searchCode\":\"$2\",\"name\":\"$1\",\"preferredLanguage\":\"nl\",\"$3\":{}}" >/dev/null
}
relation "Nature's Pride" NATURE vendor
relation "Fruitimport Zuid B.V." FRUITZUID vendor
relation "Lokaal Fruit B.V." LOKAAL customer
vendor_id() { curl -sf "$api/purchase-invoices/vendors" | jq -r --arg n "$1" '.[] | select(.name == $n) | .id'; }

# Artikel: product ras snelcode landcode landnaam maat. Stuurt zowel de landnaam (Origin) als de landcode mee, zodat het
# script werkt met en zonder de landenlijst; de API negeert een veld dat hij (nog) niet kent.
article() {
    post /articles "{\"productName\":\"$1\",\"variantName\":\"$2\",\"shortcutCode\":\"$3\",\"originCountryCode\":\"$4\",\"origin\":\"$5\",\"sizeCategory\":\"$6\",\"packaging\":\"4 kg doos\",\"packagingUnit\":\"colli\",\"weightMethod\":\"Calculated\",\"defaultNetKgPerPackage\":4,\"defaultPackagesPerPallet\":240,\"tradeClass\":\"I\"}" | jq -r '.id'
}
avo_pe18=$(article Avocado Hass AVOPE18 PE Peru 18)
article Avocado Hass AVOPE20 PE Peru 20 >/dev/null
article Avocado Hass AVOCL18 CL Chili 18 >/dev/null
article Avocado Hass AVOZA18 ZA Zuid-Afrika 18 >/dev/null
man_br8=$(article Mango Palmer MANBR8 BR Brazilië 8)
article Mango Palmer MANBR10 BR Brazilië 10 >/dev/null
article Mango Palmer MANPE8 PE Peru 8 >/dev/null
article Citroen Primofiori CITES3 ES Spanje 3 >/dev/null
article Citroen Primofiori CITES4 ES Spanje 4 >/dev/null
article Citroen Primofiori CITZA4 ZA Zuid-Afrika 4 >/dev/null

expected_at="$(date -u -v+2d +%Y-%m-%dT08:00:00Z 2>/dev/null || date -u -d '+2 days' +%Y-%m-%dT08:00:00Z)"
order() { # leverancier herkomst variant partij referentie
    post /supply-commitments "{\"requestId\":\"$(uuid)\",\"vendorId\":$(vendor_id "$1"),\"route\":\"Warehouse\",\"origin\":\"$2\",\"expectedAt\":\"$expected_at\",\"supplierReference\":\"$5\",\"orderChannel\":\"Phone\",\"lines\":[{\"productVariantId\":$3,\"expectedQuantity\":10,\"expectedUnit\":\"colli\",\"expectedNetWeightKg\":40,\"weightSource\":\"Calculated\",\"lotNumber\":\"$4\"}]}" | jq -r '.orderNumber'
}
first=$(order "Nature's Pride" Peru "$avo_pe18" TEST-001 TEST-AVO-PE18)
second=$(order "Fruitimport Zuid B.V." Brazilië "$man_br8" TEST-002 TEST-MAN-BR8)

echo "Klaar: $database met 10 artikelen, 2 leveranciers, 1 klant en de orders $first (Avocado Hass Peru 18) en $second (Mango Palmer Brazilië 8)."

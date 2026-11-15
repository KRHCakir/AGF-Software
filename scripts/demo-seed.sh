#!/usr/bin/env bash
# Een NIEUWE, lege demo-database vullen met verzonnen demo-gegevens (voorvoegsel DEMO-), via de gewone API: dezelfde regels en
# bewakingen als in het scherm. Geen echte bedrijfsgegevens; e-mailadressen op .example (bestaat niet, en de demo verstuurt niets).
#
#   scripts/demo-seed.sh [<api-adres>] [--ai-aan]
#
#   <api-adres>  standaard http://localhost:8080/api (in de API-container; scripts/demo-reset.sh roept het zo aan).
#                Lokaal bijvoorbeeld http://localhost:5212/api na scripts/new-clean-db.sh.
#   --ai-aan     AI-lezen aanzetten (maandlimiet 5 euro); alleen als de klant weet dat geüploade documenten naar een externe
#                AI-dienst gaan. Zonder sleutel in de API blijft het uit, met een melding.
#
# Weigert een database waarin al relaties staan: het vult alleen een lege demo en verandert nooit bestaande gegevens.
# Nodig: bash, curl, jq (staan in het API-image).
set -euo pipefail

api="http://localhost:8080/api"
ai_on=false
for arg in "$@"; do
    case "$arg" in
        --ai-aan) ai_on=true ;;
        http://*|https://*) api="${arg%/}" ;;
        *) echo "Gebruik: $0 [<api-adres>] [--ai-aan]" >&2; exit 2 ;;
    esac
done

uuid() {
    if [[ -r /proc/sys/kernel/random/uuid ]]; then cat /proc/sys/kernel/random/uuid; else uuidgen | tr '[:upper:]' '[:lower:]'; fi
}
# Datum over n dagen (negatief = geleden), als jjjj-mm-dd.
day() { date -u -d "$1 days" +%Y-%m-%d 2>/dev/null || date -u -v"$1"d +%Y-%m-%d; }

# Een verzoek met JSON; bij een fout de melding van de API tonen en stoppen (er is dan niets half overschreven: alles is nieuw).
call() {
    local method="$1" path="$2" body="${3:-}" response status
    if [[ -n "$body" ]]; then
        response="$(curl -sS -X "$method" -H 'Content-Type: application/json' -H 'Accept: application/json' --data "$body" -w '\n%{http_code}' "$api$path")"
    else
        response="$(curl -sS -X "$method" -H 'Accept: application/json' -w '\n%{http_code}' "$api$path")"
    fi
    status="${response##*$'\n'}"
    response="${response%$'\n'*}"
    if [[ ! "$status" =~ ^2 ]]; then
        echo "Gestopt bij $method $path: HTTP $status" >&2
        echo "$response" >&2
        exit 1
    fi
    printf '%s' "$response"
}

echo "Demo-gegevens invullen via $api"
relations="$(call GET '/relations?includeInactive=true' | jq -r '.total')"
if [[ "$relations" != 0 ]]; then
    echo "Weigering: deze database heeft al $relations relaties. Dit script vult alleen een nieuwe, lege demo-database (scripts/demo-reset.sh)." >&2
    exit 2
fi

# --- Bedrijf, loods en afzender ---
call GET /settings >/dev/null
curl -sSf -o /dev/null -X PUT "$api/settings" \
    -F CompanyName="DEMO Groothandel B.V." -F Address="Handelsweg 12, 2681 AB Monster" -F Email="info@demo-groothandel.example" \
    -F Phone="+31 174 000 000" -F KvkNumber="12345678" -F VatNumber="NL000000000B01"
call PUT /settings/loading-slip '{"language":"nl","showCustomer":true,"showSupplier":true,"warehouseAddress":"DEMO-loods, Veilingweg 5, 2675 LS Honselersdijk"}' >/dev/null
call PUT /settings/mail '{"fromAddress":"verkoop@demo-groothandel.example","fromName":"DEMO Groothandel B.V.","signature":null}' >/dev/null
call PUT /settings/warehouse-recipients '{"expectedVersion":"0","recipients":[{"name":"DEMO-loods","email":"loods@demo-groothandel.example","role":"To","kinds":["LoadingSlip"]}]}' >/dev/null
echo "  bedrijf DEMO Groothandel B.V., loods met e-mailadres voor de laadbon"

# --- Relaties ---
relation() { call POST /relations "$1"; }
address() { printf '{"purpose":"%s","line1":"%s","postalCode":"%s","city":"%s","countryCode":"%s","isDefault":true}' "$@"; }

andes="$(relation "{\"requestId\":\"$(uuid)\",\"searchCode\":\"DEMO-ANDES\",\"name\":\"DEMO-Andes Fruit S.A.C.\",\"preferredLanguage\":\"en\",
    \"email\":\"sales@andes-fruit.example\",\"acknowledgeSimilarNames\":true,\"vendor\":{\"defaultCurrency\":\"EUR\"},
    \"addresses\":[$(address Main 'Av. Los Frutales 120' '15023' 'Lima' PE)]}")"
zuid="$(relation "{\"requestId\":\"$(uuid)\",\"searchCode\":\"DEMO-ZUIDVRUCHT\",\"name\":\"DEMO-Zuidvrucht Import B.V.\",\"preferredLanguage\":\"nl\",
    \"email\":\"inkoop@zuidvrucht.example\",\"acknowledgeSimilarNames\":true,\"vendor\":{\"defaultCurrency\":\"EUR\"},
    \"addresses\":[$(address Main 'Havenweg 8' '3089 JB' 'Rotterdam' NL)]}")"
noord="$(relation "{\"requestId\":\"$(uuid)\",\"searchCode\":\"DEMO-VERSNOORD\",\"name\":\"DEMO-Versmarkt Noord B.V.\",\"preferredLanguage\":\"nl\",
    \"email\":\"inkoop@versmarkt-noord.example\",\"acknowledgeSimilarNames\":true,\"customer\":{},
    \"addresses\":[$(address Main 'Marktplein 3' '9711 AA' 'Groningen' NL),$(address Delivery 'Distributieweg 40' '9723 JA' 'Groningen' NL)]}")"
westland="$(relation "{\"requestId\":\"$(uuid)\",\"searchCode\":\"DEMO-WESTLAND\",\"name\":\"DEMO-Fruithandel Het Westland\",\"preferredLanguage\":\"nl\",
    \"email\":\"bestellen@fruithandel-westland.example\",\"acknowledgeSimilarNames\":true,\"customer\":{},
    \"addresses\":[$(address Delivery 'Veilingweg 22' '2671 BS' 'Naaldwijk' NL)]}")"
carrier="$(relation "{\"requestId\":\"$(uuid)\",\"searchCode\":\"DEMO-SNELTRANS\",\"name\":\"DEMO-Snel Transport B.V.\",\"preferredLanguage\":\"nl\",
    \"email\":\"planning@snel-transport.example\",\"acknowledgeSimilarNames\":true,\"carrier\":true,
    \"addresses\":[$(address Main 'Transportweg 1' '2685 LN' 'Poeldijk' NL)]}")"

andes_vendor="$(jq -r '.vendor.vendorId' <<<"$andes")"
zuid_vendor="$(jq -r '.vendor.vendorId' <<<"$zuid")"
noord_customer="$(jq -r '.customer.customerId' <<<"$noord")"
noord_id="$(jq -r '.id' <<<"$noord")"
carrier_id="$(jq -r '.id' <<<"$carrier")"
# Mailconcepten: de transportopdracht gaat naar de vervoerder, de orderbevestiging en factuur van de klant liggen klaar.
call PUT "/relations/$carrier_id/document-recipients" '{"expectedVersion":"0","recipients":[
    {"name":"Planning","email":"planning@snel-transport.example","role":"To","kinds":["TransportOrder"]},
    {"name":"Kantoor","email":"kantoor@snel-transport.example","role":"Cc","kinds":["TransportOrder","Other"]}]}' >/dev/null
call PUT "/relations/$noord_id/document-recipients" '{"expectedVersion":"0","recipients":[
    {"name":"Inkoop","email":"inkoop@versmarkt-noord.example","role":"To","kinds":["OrderConfirmation","Invoice"]}]}' >/dev/null
echo "  relaties: 2 leveranciers, 2 klanten, 1 vervoerder (met e-mailadressen voor de mailconcepten)"

# --- Artikelen met snelcodes ---
article() {
    call POST /articles "{\"productName\":\"$1\",\"variantName\":\"$2\",\"shortcutCode\":\"$3\",\"originCountryCode\":\"$4\",\"sizeCategory\":\"$5\",
        \"tradeClass\":\"I\",\"packaging\":\"$6 kg doos\",\"packagingUnit\":\"colli\",\"weightMethod\":\"Calculated\",\"defaultNetKgPerPackage\":$6,
        \"defaultPackagesPerPallet\":$7,\"brand\":\"DEMO-Fresh\"}" | jq -r '.id'
}
avocado="$(article Avocado Hass DEMO-AVO18 PE 18 4 240)"
mango="$(article Mango Kent DEMO-MANGO8 PE 8 4 220)"
citroen="$(article Citroen Primofiori DEMO-CIT4 ES 4 15 60)"
echo "  artikelen: DEMO-AVO18 (avocado), DEMO-MANGO8 (mango), DEMO-CIT4 (citroen)"

# --- Inkooporders: één ontvangen, twee nog onderweg ---
order() {
    call POST /supply-commitments "{\"requestId\":\"$(uuid)\",\"vendorId\":$1,\"route\":\"Warehouse\",\"origin\":\"$2\",\"expectedAt\":\"$3T08:00:00Z\",
        \"supplierReference\":\"$4\",\"orderedOn\":\"$(day -5)\",\"orderChannel\":\"Email\",
        \"lines\":[{\"productVariantId\":$5,\"expectedQuantity\":$6,\"expectedUnit\":\"colli\",\"unitPrice\":$7}]}"
}
received="$(order "$andes_vendor" Peru "$(day -1)" DEMO-AF-1001 "$avocado" 240 12.50)"
order "$andes_vendor" Peru "$(day 3)" DEMO-AF-1002 "$mango" 220 9.80 >/dev/null
order "$zuid_vendor" Spanje "$(day 2)" DEMO-ZV-2001 "$citroen" 60 14.00 >/dev/null
received_line="$(jq -r '.lines[0].id' <<<"$received")"
echo "  inkooporders: $(jq -r '.orderNumber' <<<"$received") (ontvangen) en twee die nog komen"

# --- Ontvangst en keuring ---
pallet="$(call POST /pallets "{\"requestId\":\"$(uuid)\",\"logisticsStatus\":\"Received\",\"locationContext\":\"Warehouse\",\"warehouseLocation\":\"DEMO-A-01\"}" | jq -r '.id')"
content="$(call POST "/pallets/$pallet/content-lines" "{\"requestId\":\"$(uuid)\",\"supplyCommitmentLineId\":$received_line,\"lotNumber\":\"DEMO-LOT-AVO-01\",
    \"quantity\":240,\"unit\":\"colli\",\"weightMethod\":\"Calculated\",\"netKgPerPackage\":4}")"
content_id="$(jq -r '.id' <<<"$content")"
call POST "/pallets/$pallet/content-lines/$content_id/quality-status" "{\"requestId\":\"$(uuid)\",\"expectedContentLineVersion\":$(jq -r '.version' <<<"$content"),
    \"toStatus\":\"Approved\",\"inspectedBy\":\"DEMO-keurder\"}" >/dev/null
echo "  ontvangst: 240 colli avocado op een pallet, goedgekeurd"

# --- Verkooporder met reservering en vervoerder (nog niet bevestigd: Bevestigen maakt de laadbon en de mailconcepten) ---
sale="$(call POST /sales-orders "{\"requestId\":\"$(uuid)\",\"customerId\":$noord_customer,\"customerReference\":\"DEMO-VN-5001\",
    \"requestedDeliveryDate\":\"$(day 2)T00:00:00Z\",\"lines\":[{\"productVariantId\":$avocado,\"quantity\":40,\"unit\":\"colli\",\"supplySource\":\"Stock\"}]}")"
sale_id="$(jq -r '.id' <<<"$sale")"
call POST "/sales-orders/$sale_id/lines/$(jq -r '.lines[0].id' <<<"$sale")/reservations" \
    "{\"requestId\":\"$(uuid)\",\"palletContentLineId\":$content_id,\"quantity\":40,\"unit\":\"colli\",\"netWeightKg\":160}" >/dev/null
call PUT "/sales-orders/$sale_id/transport" "{\"mode\":\"Carrier\",\"carrierRelationId\":$carrier_id,\"unloadDate\":\"$(day 2)\",\"temperatureC\":6,
    \"remarks\":\"DEMO: laden voor 10:00\"}" >/dev/null
echo "  verkooporder: $(jq -r '.orderNumber' <<<"$sale") voor DEMO-Versmarkt Noord, 40 colli gereserveerd, vervoerder gekozen"

# --- Inkoopfactuur bij de ontvangen order ---
invoice="$(call POST /purchase-invoices "{\"requestId\":\"$(uuid)\",\"vendorId\":$andes_vendor,\"invoiceNumber\":\"DEMO-AF-F-2026-001\",\"invoiceDate\":\"$(day -1)\",
    \"currency\":\"EUR\",\"paymentTermsDays\":30,\"lines\":[{\"productVariantId\":$avocado,\"quantity\":240,\"unit\":\"colli\",\"unitPrice\":12.50,
    \"vatPercentage\":9,\"supplyCommitmentLineId\":$received_line}]}")"
echo "  inkoopfactuur: DEMO-AF-F-2026-001, totaal $(jq -r '.totalAmount' <<<"$invoice") euro"

# --- AI-lezen (alleen op verzoek) ---
if [[ "$ai_on" == true ]]; then
    if [[ "$(call GET /purchase-invoices/ai/status | jq -r '.configured')" == true ]]; then
        call PUT /settings/ai-reading '{"enabled":true,"customerInformed":true,"monthlyLimitEur":5}' >/dev/null
        echo "  AI-lezen staat aan (maandlimiet 5 euro; de demo begrenst ook per dag)"
    else
        echo "  AI-lezen blijft uit: er is in deze demo geen sleutel ingesteld (AGF_DEMO_ANTHROPIC_API_KEY)"
    fi
fi
echo "Klaar."

# Aantal invoeren eenvoudig maken (oktober 2026)

Eén veld "Aantal" met de eenheid erachter en live de kg volgens het artikel; "per pallet" alleen als hulp, met hele pallets en losse colli. Gemaakt met Playwright (Chrome) tegen een kopie van agf_smoke, label SMOKE-QTY-509F (artikel "Avocado · Hass 10 kg": netto 10 kg, bruto 10,3 kg, 120 colli per pallet).

1. `01-verkooporder-1560-colli-kg.png`: "1.560" colli; eronder 1.560 colli × 10 kg = 15.600,0 kg netto · 16.068,0 kg bruto.
2. `02-verkooporder-per-pallet-losse-colli.png`: tweede regel per pallet: 3 pallets × 120 colli + 15 losse colli = 375 colli (artikel zonder bruto kg: alleen netto). 3,5 pallets gaf eerst "Alleen hele pallets".
3. `03-verkooporder-390px.png`: hetzelfde formulier op 390 px; de regelvelden staan onder elkaar en alle getallen zijn volledig zichtbaar.
4. `04-ontvangst-390px.png`: ontvangstregel op 390 px; "15.000" past in het veld, met de rekenregel eronder.
5. `05-orderregel-verhouding.png`: de opgeslagen order toont "(3 pallets × 120 + 15 colli)" bij de regel.
6. `06-aanvoer-per-pallet.png`: aanvoer plannen per pallet met losse colli en de kg-rekenregel.

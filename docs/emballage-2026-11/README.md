# Emballage (fust): doorloop in Chrome, 10 november 2026

Doorloop met de geïnstalleerde Chrome bij 1440 en 390 px breed. Het script controleert elke stap en stopt bij de eerste fout.

- **Database:** `agf_emb_screens2`, een kopie van `agf_deviation_clean`. De test-API migreerde die van 45 naar 54.
  - In deze kopie kreeg Kiwi Hayward 120 colli per pallet, zodat het palletvoorstel iets te rekenen heeft.
  - Er is geen kiwi op voorraad, daarom staat er bij de goederenregel een tekort. Dat mag, en het staat los van de emballage.
- **Servers:** API op 5211 en frontend op 3011, met de hele stapel samen. De demo (3000 en 5197) en `agf_smoke` zijn niet aangeraakt.
- **390 px:** geen enkel scherm schuift horizontaal.

## Schermen

| Bestand | Wat je ziet |
|---|---|
| `01-artikelen-emballage.png` | Artikelen, tab Emballage. Pallet wegwerp krijgt een standaardprijs van 7,50; Euro-pallet en Krat hebben geen prijs. Alle drie hebben 9% btw. |
| `02-inkoopfactuur-groep-emballage.png` | Inkoopfactuur invoeren. "pallet" getypt in de artikelkeuze geeft de groep Emballage: een leverancier kan pallets in rekening brengen. |
| `03-order-kiwi-pallet-toevoegen.png` | Nieuwe verkooporder: 110 colli kiwi (= 0,9 pallets), daarna Pallet toevoegen. Het voorstel is 1 × Pallet wegwerp à 7,50 met 9% btw. Het aantal staat in focus en is aan te passen. |
| `04-order-opgeslagen.png` | Na opslaan: de goederenregel en daaronder het blok Emballage (1 st. × 7,50). |
| `05-order-detail-emballage.png` | Het orderdetail. Reserveren en verzenden gaan alleen over de kiwi-regel; de emballage staat apart, zonder "niet gereserveerd". |
| `06-orderlijst-emballage.png` | De orderlijst. De order met kiwi toont "Pallet wegwerp · 1 st."; een order met alleen 4 Euro-pallets à 0,00 (ruil) heet "Alleen emballage". |
| `07-mobiel-order-emballage.png` | 390 px: het blok Emballage in het orderformulier. De velden staan onder elkaar. |
| `08-mobiel-artikelen-emballage.png` | 390 px: Artikelen › Emballage. Achter een gebruikt artikel staat "1× gebruikt". |

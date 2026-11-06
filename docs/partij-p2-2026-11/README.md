# Partij P2: verkoop-partij en kosten verdelen over partijen

Doorloop in Chrome op 6 november 2026, bij 1440 en 390 px breed.

- **Database:** een kopie van `agf_deviation_clean` (`agf_p2_screens`, uit een dump). De test-API migreerde die van 45 naar 51 migraties, inclusief Partij P1. De twee bestaande verkooporders kregen daarbij verkoop-partij `2611001` en `2611002`.
- **Servers:** API op poort 5211 en frontend op poort 3011, met de nieuwe code. De demo (3000 en 5197) en de testomgeving van de eigenaar (3001 en 5198) zijn niet aangeraakt.
- **Script:** een Playwright-script met de geïnstalleerde Chrome. Het controleert elke stap ook via de API.
- **Pad:**
  1. nummering met een vaste 5 voor inkoop-partijen;
  2. een nieuwe verkooporder met het voorgestelde verkoop-partijnummer;
  3. de lijst en het detail van verkoop-partijen;
  4. een directe handlingfactuur van € 5.000, verdeeld over twee verkoop-partijen (€ 500 + € 1.000, rest € 3.500 algemene kosten);
  5. meer dan het regelbedrag, geweigerd;
  6. een zeevrachtfactuur van € 2.400 op een hele inkoop-partij (Verdeel naar kg);
  7. een elektrafactuur zonder partij;
  8. de kosten op de verkoop-partij en op de inkoop-partij.
- **390 px:** geen enkel scherm schoof horizontaal. Gecontroleerd: verdelen, kosten op de partij, de lijst met verkoop-partijen en het orderformulier.

## Schermen

| Bestand | Wat je ziet |
|---|---|
| `01-nummering-twee-reeksen.png` | Instellingen › Partijnummering. Inkoop-partijen met `5{JJ}{MM}{volgnr}` ("Volgende partij: 52611001"), 3 cijfers, per jaar. Daaronder de reeks voor verkoop-partijen met een eigen formaat en aantal cijfers, en een eigen voorbeeld. |
| `02-orderformulier-verkoop-partij.png` | Nieuwe verkooporder. Naast de klantreferentie staat het veld Verkoop-partij met het voorstel `2611003`. |
| `03-partijen-verkoop.png` | Voorraad › Partijen, tabblad Verkoop-partijen. Per partij de verkooporder en klant, het product, besteld, verkocht en uit welke inkoop-partijen. |
| `04-verkoop-partij-detail.png` | Verkoop-partij `2611001`: VO-2026-0001 van Lokaal Fruit B.V., besteld, gereserveerd en verzonden, en "Uit inkoop-partijen" met RD2-LOT-A. |
| `05-inkoopfactuur-direct-handling.png` | Inkoopfactuur invoeren met de zin "Voor kosten zoals handling, transport en autohuur. Goederen ontvang je via een inkooporder." Eén regel: kostenartikel Handling (groep Kosten), aantal 1, € 5.000. |
| `06-verdelen-twee-verkoop-partijen.png` | "Aan welke partij hoort dit?" met Verkoop-partij gekozen. `2611001` krijgt € 500 en `2611002` € 1.000, met per partij de klant, het VO-nummer, het product, de colli en de kg. "Rest 3.500,00 EUR telt als algemene kosten." |
| `07-meer-dan-regelbedrag.png` | € 500 + € 4.600 uit € 5.000. Naast Verdeling opslaan staat de harde melding "Samen 5.100,00 EUR: meer dan het regelbedrag 5.000,00 EUR excl. btw." Opslaan stuurt niets. |
| `08-zeevracht-hele-inkoop-partij.png` | Zeevracht van € 2.400 op inkoop-partij RD2-LOT-A. Met Verdeel naar kg gaat het hele bedrag naar de enige partij. |
| `09-factuur-zonder-partij.png` | Elektra loods oktober, € 312,40, zonder partij. Alles telt als algemene kosten. |
| `10-partij-kosten-verkoop.png` | Verkoop-partij `2611001`, blok Kosten: Handling, factuur BC-… (link), Fruitimport Zuid B.V., € 500, en het totaal. Daaronder staan apart de kosten op de inkoop-partij waar de colli vandaan komen; die tellen niet mee. |
| `10b-partij-kosten-inkoop.png` | Inkoop-partij RD2-LOT-A, blok Kosten: Zeevracht MAERSK-… € 2.400 en totaal € 2.400. Apart staat "kosten op de verkoop-partijen met colli uit deze partij: 2611001 500,00 EUR". Zo telt niets dubbel. |
| `11-kostenartikelen.png` | Instellingen › Kostenartikelen. De standaardsoorten, het aantal factuurregels, inactief maken en een nieuw kostenartikel toevoegen. Verwijderen kan niet. |
| `12-mobiel-verdelen.png` | Het verdeelpaneel op 390 px. |
| `13-mobiel-partij-kosten.png` | Kosten op de verkoop-partij op 390 px. |
| `14-mobiel-verkoop-partijen.png` | De verkoop-partijen als kaarten op 390 px. |
| `15-mobiel-orderformulier.png` | Het orderformulier met het veld Verkoop-partij op 390 px. |

## Rondleiding

Nieuw hoofdstuk 10, "Kosten verdelen over partijen" (zie `docs/demo-tour.md`). Het hoofdstuk Verkooporder heeft een stap die de verkoop-partij aanwijst.

De e2e-test (`npm run test:tour`) gaf telkens 20 van 20 groen:
- op een nieuwe lege database (`scripts/new-clean-db.sh`, `TOUR_EMPTY_DATABASE=true`);
- op een gevulde database (een kopie van agf_smoke, door de test-API gemigreerd van 48 naar 51).

Het hoofdstuk Kosten verdelen slaagde ook los gestart op een lege database. Het maakte daar zelf de verkoop aan.

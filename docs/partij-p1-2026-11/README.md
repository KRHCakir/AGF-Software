# Partij P1: de partij als eigen onderdeel

Doorloop in Chrome op 4 november 2026, bij 1440 en 390 px breed.

- **Database:** een kopie van `agf_deviation_clean` (`agf_deviation_clean_p1copy`), gemigreerd tot en met `AddPartijen`.
- **Servers:** API op poort 5199 en frontend op poort 3002, beide met de nieuwe code. De testomgeving van de eigenaar (3001 en 5198) en de demo (3000 en 5197) zijn niet aangeraakt.
- **Pad:**
  1. inkooporder met een nieuwe partij;
  2. tweede order op dezelfde partij;
  3. ontvangst;
  4. partijlijst en detail.
- **Breedte:** bij 390 px schoof geen enkel scherm horizontaal: partijlijst, partijdetail, inkooporder aanmaken, ontvangst en instellingen.
- **Menu:** met Tab kom je bij Voorraad › Partijen, en Enter opent het, breed en op 390 px.

## Schermen

| Bestand | Wat je ziet |
|---|---|
| `01-instellingen-partijnummering.png` (en `-390`) | Instellingen › Partijnummering. Modus Voorstellen, formaat `{JJ}{MM}{volgnr}`, per jaar, met "Volgende partij: 2611001". |
| `01b-instellingen-ongeldig-formaat.png` | Bij `{JJ}{MM}` verschijnt meteen een melding: precies één keer `{volgnr}` is nodig. Er staat geen voorbeeld en opslaan stuurt niets. |
| `02-inkooporder-voorgestelde-partij.png` (en `-390`) | Inkooporder aanmaken. Bovenaan staat het veld Partij met het voorstel, plus "Bestaande partij kiezen" en "Partij van de leverancier". |
| `03-opgeslagen-met-partij.png` | Na opslaan: IO-2026-0005 met "Partij 2611001 · Partij van de leverancier NP-LOT-88". |
| `04-partij-bestaat-al.png` | Tweede order, met "2611 001" getypt (met spatie). Het scherm vraagt "Partij 2611001 bestaat al, wil je die gebruiken?", met de knoppen **Bestaande partij gebruiken** en **Ander nummer**. |
| `05-tweede-order-bestaande-partij.png` | Na "Bestaande partij gebruiken" staat de bestaande partij in het veld, voor een order bij een andere leverancier. |
| `06-ontvangst-partij-van-de-order.png` | Ontvangst van IO-2026-0005. Er is geen lotveld: de partij 2611001 en de partij van de leverancier komen van de order. In de keten-strip staat "Partij 2611001" als link. |
| `07-partijlijst.png`, `07b-partijlijst-alle.png` (en `07-partijlijst-390`) | Voorraad › Partijen. Zoeken op "2611-001" vindt de partij. Per partij staan de leveranciers, de producten, ingekocht, ontvangen, op voorraad en verkocht, met de status. Op 390 px wordt elke partij een kaart. Ook de partijen uit de migratie staan erin (RD2-LOT-A en andere). |
| `08-partij-detail.png` (en `-390`) | Detail van 2611001. Twee inkooporders (Nature's Pride 60 en Fruitimport Zuid 30) en twee pallets: 60 goedgekeurd en 10 in quarantaine. Verder de verkooporders, het blokje "Kosten en resultaat komen in een volgende stap" en de historie. |
| `09-nummer-corrigeren-reden-verplicht.png` | "Nummer corrigeren" zonder reden wordt geweigerd met "Geef een reden.". |
| `10-inkooporder-keten-met-partij.png` | Inkooporderdetail met de partij als link, in de kop en in de keten-strip. |
| `11-order-zonder-partij-koppelen.png` | Een oudere order zonder partij (IO-2026-0002) toont "heeft nog geen partij", met het voorstel en "Partij koppelen". |

## Ontvangst en keuring in de doorloop

De ontvangst en de keuring van de twee pallets zijn via de API gedaan, met dezelfde endpoints als de schermen. Daarbij is geen lotnummer meegestuurd. De server nam partij 2611001 over van de orderregel en zette het nummer ook in `LotNumber`.

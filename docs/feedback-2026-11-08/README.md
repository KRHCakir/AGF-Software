# Feedback 8 november: menu boven, lijsten, datum, valuta en ECB-koers

Doorloop in Chrome op 8 november 2026, bij 1440 en 390 px breed.

- **Database:** een kopie van de schermdatabase van Partij P2 (`agf_p2_screens`, oorspronkelijk een kopie van `agf_deviation_clean`), als `agf_fb_screens6`. De test-API migreerde die van 51 naar 52.
- **Servers:** API op 5211 en frontend op 3011, met de hele stapel samengevoegd (inclusief de reviewfixes). De demo (3000 en 5197) is niet aangeraakt.
- **ECB:** de koers komt echt van de ECB (data-api.ecb.europa.eu). Zondag 8 november gaf dat de koers van vrijdag 6 november: 1 EUR = 1,1165 USD.
- **Script:** een Playwright-script met de geïnstalleerde Chrome. Het controleert elke stap en stopt bij de eerste fout.
- **390 px:** geen scherm schoof horizontaal. Alleen de gekleurde balk scrolt zelf, met het huidige scherm in beeld.

## Wat er veranderd is

1. **Menu boven in plaats van links.** De groepen Start, Inkoop, Voorraad, Verkoop en Stamgegevens staan als tabs in de kop.
   - Een groep aanklikken opent haar eerste scherm. De balk eronder toont de schermen van die groep.
   - Elke groep heeft een eigen kleur: Inkoop blauw, Voorraad groen, Verkoop oranje, Stamgegevens paars en Oud grijs.
   - Instellingen, Help en Oud (een uitklaplijst) staan rechts.
   - De pagina heeft nu de volle breedte. Onder 1024 px is er de knop Menu.
2. **Datum-sneltoetsen in elk datumveld.** V is vandaag, G gisteren, M morgen, E eergisteren en O overmorgen. In het Engels werken ook T en Y, en het woord voluit (vandaag, morgen, overmorgen, ...) werkt ook.
   - Het formulier krijgt de datum meteen, en de uitleg onder het veld zegt al "Wordt 09-11-2026".
   - Bij Tab of Enter staat de datum ook in het veld.
3. **Lijsten om en om gekleurd** (wit, grijs, wit). Erover bewegen kleurt de regel blauw.
   - Een klik op de hele regel opent hem, ook in Inkoopfacturen.
   - Een klik op een knop of link in de regel doet gewoon wat die knop of link doet.
4. **Valutaveld met suggesties.** Typ je E, dan staat EUR bovenaan. Tab of Enter neemt de suggestie over, pijltjes kiezen een andere.
   - Het veld zit in de inkoopfactuur, de factuurinstellingen en bij relaties.
   - Een andere code van drie letters mag nog steeds.
5. **Factuur in een andere valuta.** Niemand typt een koers.
   - De server haalt de ECB-koers van de factuurdatum op: de laatste publicatie op of vóór die dag, hoogstens 10 dagen oud.
   - Zolang er niets verdeeld is, wordt de koers bij elk openen opnieuw opgehaald. Zodra er kosten op een partij staan, ligt hij vast (regel 26), zodat het eurobedrag op een partij nooit vanzelf verandert.
   - Kosten op partijen staan altijd in euro, met het bedrag in de factuurvaluta erbij.
   - Heeft de ECB geen actuele koers, zoals bij MAD, dan zegt het scherm dat en blijft alles algemene kosten.

## Schermen

| Bestand | Wat je ziet |
|---|---|
| `01-menu-inkoop.png` | Inkoopfacturen. Inkoop is blauw in de kop, met de blauwe balk eronder: Inkooporders, Inkooporder aanmaken, Ontvangst, Keuring, Inkoopfacturen (gemarkeerd). |
| `02-menu-voorraad-partijen.png` | Partijen, met Voorraad groen. De lijst is om en om wit en grijs. |
| `03-menu-verkoop.png` | Verkooporders, met Verkoop oranje. |
| `04-menu-stamgegevens.png` | Relaties, met Stamgegevens paars. |
| `05-menu-oud-open.png` | De uitklaplijst Oud rechts in de kop, met de oude schermen. |
| `06-lijst-om-en-om-hover.png` | Inkoopfacturen om en om gekleurd. De muis staat op de tweede regel, die blauw kleurt. |
| `07-rij-klik-opent-factuur.png` | Een klik op de leverancier in die regel, niet op het factuurnummer, opent de factuur. |
| `08-datum-sneltoets-m.png` | Nieuwe verkooporder. In Gevraagde leverdatum is M getypt; onder het veld staat meteen "Wordt 09-11-2026." met de sneltoetsen. Na Tab staat 09-11-2026 in het veld. "overmorgen" voluit getypt gaf 10-11-2026. |
| `09-valuta-suggestie-e.png` | Inkoopfactuur invoeren. In Valuta is E getypt: EUR (gemarkeerd) en EGP. |
| `10-factuur-usd-ecb-koers.png` | Zeevracht van 2.400 USD. Onder Valuta: "ECB-koers 6 nov 2026: 1 EUR = 1,1165 USD · excl. btw ≈ 2.149,57 EUR". Er is geen veld om een koers te typen. |
| `11-verdelen-usd-in-euro.png` | De opgeslagen factuur. De ECB-koers staat op de factuur, de regel is "2.400,00 USD excl. btw = 2.149,57 EUR". Met Verdeel naar kg op RD2-LOT-A gaat 2.149,57 EUR naar de partij, met "≈ 2.400,00 USD" ernaast. |
| `12-koers-ligt-vast.png` | Na het verdelen: "Ligt vast: er zijn kosten van deze factuur op partijen gezet." |
| `13-partij-kosten-usd.png` | Inkoop-partij RD2-LOT-A, blok Kosten: 2.149,57 EUR, met "2.400,00 USD · koers 1,1165" eronder. |
| `14-mad-geen-ecb-koers.png` | Een factuur in MAD: "De ECB publiceert geen actuele koers voor MAD." Verdelen kan niet. |
| `15-mobiel-balk.png` | 390 px: de knop Menu, met de blauwe balk eronder waarin Inkoopfacturen in beeld staat. |
| `16-mobiel-menu-open.png` | 390 px: het menu open, met de groepen onder elkaar. |
| `17-mobiel-factuur-usd.png` | 390 px: de factuur in USD met de koers. |

## Keuzes

- **Groep aanklikken:** dit opent het eerste scherm van die groep, bijvoorbeeld Verkoop opent Verkooporders. Zo ben je met één klik ergens; de balk eronder toont de andere schermen.
- **Koersdatum:** de factuurdatum is de koersdatum, niet de dag van verdelen. Dat is ook wat de boekhouding en de Belastingdienst gebruiken. De koers wordt bij elk openen opnieuw opgehaald tot er verdeeld is. Daarna ligt hij vast: anders zou het eurobedrag op een partij vanzelf veranderen.
- **Bedragen bij een factuur in USD:** je verdeelt in euro, want kosten op partijen staan in euro. Het bedrag in USD staat ernaast, naar verhouding van de regel. De hele regel is dus precies het regelbedrag, zonder afrondingscent.
- **Valuta's zonder actuele ECB-koers**, zoals MAD en EGP, kunnen niet over partijen verdeeld worden. De ECB geeft daar alleen een oude indicatieve koers voor.
- **Koersen ophalen uitzetten:** zet `ExchangeRates:Source` op `Off`, bijvoorbeeld voor een afgeschermde testomgeving. Het scherm zegt dan dat de koers niet bereikbaar is.

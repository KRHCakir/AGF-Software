# Getallen invoeren en tonen

Eén regel voor de hele software (fixes 5a, feedback van 18 november). De code staat op één plek: `agf-frontend/src/lib/decimal.ts`
(lezen en schrijven van invoer), `agf-frontend/src/lib/weightDisplay.ts` (gewichten tonen) en `AgfAdmin/Services/WeightDisplay.cs`
(gewichten op documenten). Bouw geen tweede parser: de test `numberParsing.test.ts` faalt bij een eigen `parseFloat`, `toFixed` of
het zelf verwisselen van komma en punt.

## Invoeren

Het decimaalteken is de **komma**. Voorbeelden in het Nederlands; in het Engels is het precies andersom.

| Getypt of geplakt | Wordt | Waarom |
|---|---|---|
| `7.5`, `7.50`, `0.79`, `.5` | `7,5`, `7,50`, `0,79`, `0,5` | Een enkele punt met 1 of 2 cijfers aan het eind is het decimaalteken. De punt wordt tijdens het typen meteen een komma. |
| `15.000`, `7.500`, `1.234.567,5` | blijft zo (15000, 7500) | Een punt in kloppende groepen van 3 is een duizendtal. Wie `15.000` typt, ziet onderweg `15,0` en `15,00`; bij het derde cijfer staat de punt er weer. |
| `15 000,5`, `15'000,5` | 15000,5 | Spatie of apostrof als duizendtalteken (plakken uit een ander programma). |
| `2.272.5`, `15.0000`, `0.500`, `15,000.5` | **ongeldig**, met de aanbeveling `2.272,5`, `15,0000`, `0,500`, `15.000,5` | Nooit een gok. Bij het veld staat "Ongeldig getal. Bedoel je 2.272,5?" met **Overnemen** (of Alt+Enter in het veld). |
| `1.2.3`, `abc`, `1,5,5` | **ongeldig**, zonder aanbeveling | Er is geen waarde die waarschijnlijk bedoeld is. |
| `5.`, `5,` | 5 | Een teken aan het eind met niets erachter telt niet mee. |

- Het veld met een ongeldig getal is gemarkeerd, en opslaan lukt niet tot het klopt: elk formulier leest met dezelfde parser.
- Totalen rekenen direct mee met wat er getypt is: `7.50` geeft hetzelfde totaal als `7,50`.
- Op een telefoon is het toetsenbord numeriek met de komma (`inputMode="decimal"`).
- Bij het **verlaten** van een gewijzigd veld komt de notatie van de schermtaal met de vaste decimalen van het veld (`data-decimals`).
  Er wordt nooit afgerond: `1,125` blijft `1,125`. Een veld dat alleen is aangeklikt, verandert niet.

## Vaste decimalen per veld

| Soort veld | Velden | Bij verlaten | Hoogstens |
|---|---|---|---|
| Bedrag en prijs | prijs per eenheid (inkooporderregel, inkoopfactuurregel), prijs emballage (verkooporder, emballage-artikel), kredietlimiet en verzekerd bedrag (relatie), bedrag bij kosten verdelen, prijscorrectie bij definitief maken, bedrag van een factuurverwijzing, maandlimiet AI-lezen | 2 (`4` → `4,00`) | 2 |
| Kg per verpakking | netto en bruto kg per colli (artikel), kg per verpakking (gewichtsinstelling, nieuw artikel bij aanvoer, ontvangst) | 2 (`4` → `4,00`, `1,5` → `1,50`) | 3 |
| Kg op een regel | netto en bruto kg (order- en aanvoerregel), bruto kg (inkooporderregel), netto kg (ontvangst, reserveren, klantgebonden zending, afkeuren) | 2 (`4` → `4,00`) | 3 |
| Aantal | colli of andere eenheid op een regel, ontvangen, gereserveerd, afgekeurd | zoals getypt (`15000` → `15.000`) | 3 |
| Heel getal | pallets, colli per pallet, losse colli, emballage-aantal, betaaltermijn in dagen, kortingsdagen | alleen cijfers | 0 |
| Percentage | btw %, korting % (factuurregel), betalingskorting % | zoals getypt (`21`, `2,5`) | 2 |
| Temperatuur | transport | zoals getypt (`2`, `2,5`) | 1 |

De opslag verandert niet: bedragen 2 decimalen, hoeveelheden en gewichten 3.

## Tonen

- Een opgeslagen waarde staat altijd met de komma, ook in lijsten, details en PDF's.
- **Gewichtstotalen** (netto of bruto kg van een regel, een order, een bon of een document) staan als **hele kilo's**: `2.273`, niet
  `2.272,5`. Per bedrijf in te stellen onder **Instellingen › Gewichten tonen**: 0 decimalen (standaard) of 1.
- Afronden gebeurt **alleen bij het tonen**, halve kilo's omhoog (2.272,5 wordt 2.273). De opslag houdt drie decimalen; prijzen en
  pallets rekenen met de echte waarde.
- Een totaal naast zijn regels (orderregels, inkoopregels, verzenden, laadbon, transportopdracht) is de **som van de getoonde regels**,
  zodat het op papier optelt. Een totaal dat zonder regels van de server komt (een orderlijst), is het echte totaal, afgerond.
- Opslaan onder Instellingen geldt voor jezelf direct. Wijzigt een collega de instelling, dan zie je dat vanaf het tweede scherm dat je
  daarna opent: het scherm waarin je werkt, wordt nooit opnieuw opgebouwd.
- **Uitzondering:** het gewicht **per colli** houdt minstens twee decimalen (`1,50 kg`, `4,00 kg`), want daar zijn decimalen juist nodig.
- **Documenten:** een versie van de laadbon of de transportopdracht legt vast met hoeveel decimalen ze haar gewichten toont. Een
  bestaande versie houdt haar eigen weergave, en de instelling wijzigen maakt geen nieuwe versie.

## Schermafbeeldingen

In `docs/getallen-2026-11/`, gemaakt in Chrome op een schone database met alleen DEMO-gegevens:

| Bestand | Wat je ziet |
|---|---|
| `01-punt-wordt-komma.png` | `7.50` getypt staat als `7,50` en het totaal is 15,00 |
| `02-ongeldig-getal-overnemen.png` | `2.272.5` met de melding en Overnemen |
| `03-artikel-vaste-decimalen.png` | kg per colli: `4` wordt `4,00`, `4.5` wordt `4,50` |
| `04-verkooporder-hele-kilos.png` | 505 colli × 4,5 kg: bruto `2.273` en het totaal in hele kilo's |
| `05-instelling-gewichten.png` | Instellingen › Gewichten tonen |
| `06-390-ongeldig-getal.png` | de melding op 390 px |

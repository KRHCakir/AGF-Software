# Schone start: lege schermen en de Beginnen-kaart

Doorloop in Chrome op 5 november 2026, bij 1440 en 390 px breed.

- **Database:** `agf_clean_dev`, gemaakt met `scripts/new-clean-db.sh agf_clean_dev --settings-from agf_smoke`. Daarin staan alleen de
  landen, zes betaalcondities, de bedrijfsinstellingen en de migratiehistorie.
- **Servers:** API op poort 5199 en frontend op poort 3002. De demo (3000 en 5197) en de testomgeving van de eigenaar (3001 en 5198) zijn
  niet aangeraakt.
- **Controle op elk scherm:** geen fout in de console, geen `NaN`, `undefined` of `null` in de tekst, en op 390 px schuift niets
  opzij.

## Schermen

| Bestand | Wat je ziet |
|---|---|
| `01-start-beginnen.png` (en `-390`) | Start met de kaart **Beginnen**: 1 relaties (leveranciers en klanten), 2 artikelen, 3 de eerste inkooporder. Elke stap is een link. De kaart verdwijnt vanzelf zodra er relaties en artikelen zijn. |
| `02-relaties-leeg.png` (en `-390`) | "Nog geen relaties", met één zin en de knop **Eerste relatie toevoegen**. Die opent het formulier. |
| `03-artikelen-leeg.png` | "Nog geen artikelen", met de knop **Eerste artikel toevoegen**. De opschoonblokken voor maat, klasse en lege productsoorten staan er niet zolang er geen artikelen zijn. Snelkoppelingen voor landen kun je al wel maken. |
| `04-inkooporders-leeg.png` | "Nog geen open inkooporders", met **Inkooporder aanmaken**. |
| `05-inkooporder-aanmaken-eerst-nodig.png` (en `-390`) | Inkooporder aanmaken zonder leverancier en artikel in het systeem. Bovenaan staat **Eerst nodig**, met de links "Leverancier toevoegen" en "Artikel toevoegen". Het formulier blijft gewoon staan. |
| `06-ontvangst-leeg.png` | "Nog geen open inkooporder om te ontvangen", met **Inkooporder aanmaken**. |
| `07-keuring-leeg.png` | "Er zijn geen inhoudsregels om te beoordelen", met **Naar Ontvangst**. |
| `08-beschikbaarheid-leeg.png` | "Er is nog geen inkooporder of palletinhoud geregistreerd", met **Inkooporder aanmaken**. |
| `09-partijen-leeg.png` (en `-390`) | "Nog geen partijen". Een partij ontstaat met de eerste inkooporder. |
| `10-verkooporders-leeg.png` | "Er zijn nog geen verkooporders", met **Eerste verkooporder aanmaken**. |
| `11-nieuwe-verkooporder-eerst-nodig.png` | Een nieuwe verkooporder op een leeg systeem: eerst een klant en een artikel. |
| `12-klantgebonden-aanvoer-eerst-nodig.png` | Klantgebonden aanvoer: eerst een leverancier, een klant en een artikel. |
| `13-inkoopfacturen-leeg.png` | "Nog geen inkoopfacturen", met **Inkoopfactuur invoeren**. |

## De rondleiding op een lege database

Gemaakt tijdens `npm run test:tour` op `agf_tour_empty_run3`, een nieuwe lege database van `scripts/new-clean-db.sh`. De test liep met
`TOUR_EMPTY_DATABASE=true` en `TOUR_SHOTS_DIR`, in de stand Kijk mee en Snel, bij 1440 px. Per hoofdstuk zijn er twee screenshots:
`-bezig` (ongeveer 60% van de stappen) en `-klaar` (de afsluiting in het paneel). De volgorde is die van de test: eerst De rode draad
los gestart, op de nog lege database.

| Hoofdstuk | Bestanden | Wat je ziet |
|---|---|---|
| Kennismaken | `tour-screens-*` | Langs de vier menugroepen; bij Klaar staat de laatste stap, Artikelen. |
| De rode draad (los gestart, lege database) | `tour-chain-*` | Eerst maakt het hoofdstuk zelf leverancier, klant, artikel, inkooporder, ontvangst, keuring en verkoop aan; er wordt niets overgeslagen. Klaar staat op het partijdetail 2611001: ingekocht 20, ontvangen 20, op voorraad 16, met de inkooporder, de pallet en de verkooporder. |
| Stamgegevens | `tour-masterdata-*` (en `-bezig-390`) | Leverancier en klant met adres, daarna het artikel. Op 390 px staat het paneel boven het formulier en schuift niets opzij. |
| Inkooporder aanmaken | `tour-order-*` | Stamgegevens bestonden al: die stappen zijn overgeslagen. Order met partij, partij van de leverancier, datum met tweecijferig jaar en factuurnummer; Klaar staat op Voorraad › Partijen. |
| Ontvangst en keuring | `tour-receive-*` | Blind ontvangen, goedgekeurd met Wie beoordeelt, een deel teruggestuurd, Beschikbaarheid. |
| Afwijking bij ontvangst | `tour-deviation-*` | De open order ontbrak, dus eerst een nieuwe order. Daarna een ander land en 5% zwaarder, "Klopt dit?", en de voorraad op de exacte variant in Beschikbaarheid. |
| Voorraad en verkopen voor aankomst | `tour-presell-*` | Verkopen vanuit Beschikbaarheid met tekort, ontvangen en keuren, werklijst, reserveren met oudste partij eerst. |
| Verkooporder | `tour-sales-*` | VO-nummer, reservering met pallet, partij en inkooporder, verzonden. |
| Inkoopfactuur invoeren | `tour-purchase-invoice-*` | Factuur van de rondleidingsleverancier, regel 10 × 12 = 120,00, opgeslagen. |
| Hoofdstukkenlijst op 390 px | `tour-hoofdstukken-390.png` | De lijst met de tien hoofdstukken in het paneel. |

Uitslag van `npm run test:tour` (rondleiding en de flows datumveld, toetsenbord en verkoopketen):
- **Lege database** (`agf_tour_empty_run3`): 19 van 19 geslaagd.
- **Gevulde database** (`agf_tour_filled_test`, een kopie van `agf_smoke`): 19 van 19 geslaagd.

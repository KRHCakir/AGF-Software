# Fixes ronde 3: datum, toetsenbord, ingelogde gebruiker en opslaan van de verkooporder

Doorloop in Chrome op 4 november 2026, bij 1440 en 390 px breed.

- **Oude code:** de demo (main, poort 3000). Alle schrijfacties waren geblokkeerd, zodat er niets is opgeslagen.
- **Nieuwe code:** een kopie van `agf_deviation_clean` (`agf_deviation_r3`), met de API op poort 5199 en de frontend op poort 3002. De testomgeving van de eigenaar op poort 3001 en 5198 is niet aangeraakt.
- **Breedte:** bij 390 px schoof geen enkel scherm horizontaal.

## Wat er misging (punt 4)

**De oude code blokkeerde het opslaan stil.** De eigenaar zag bij Mango Fuerte na "Vastleggen" nog steeds "Verkocht 0". Dat klopt: de order was nooit opgeslagen. Dit is nagespeeld op de oude code, met dezelfde stappen als in de testlijst:

1. Beschikbaarheid
2. Verkopen
3. Klant
4. 50 colli
5. Splitsen
6. Verkooporder vastleggen

| Wat | Uitkomst |
|---|---|
| Verzoeken naar de server | geen |
| VO-nummer | geen |
| Melding "Vul bovenaan de medewerker in." | wel aanwezig, maar **buiten beeld** (bovenaan het formulier, zonder focus) |

**Oorzaak:** het verplichte veld "Medewerker" rechtsboven op Verkoop was leeg.

**Andere oorzaken uitgesloten:**

- De verkochte aantallen tellen op de exacte variant (`ProductVariantId`).
- Beschikbaarheid laadt bij openen opnieuw.

Na deze ronde is er geen medewerker-veld meer. Een fout staat altijd naast de knop, en de focus gaat naar het eerste veld met een fout.

## Schermen

| Bestand | Wat je ziet |
|---|---|
| `00-oud-opslaan-stil-geblokkeerd.png` | Oude code na "Verkooporder vastleggen": geen nummer en geen zichtbare melding. |
| `01-verkoop-ingelogd-als.png` | Verkoop zonder het veld "Medewerker"; rechtsboven en in de kop staat "Ingelogd als Admin". |
| `02-melding-naast-opslaan.png` (en `-390`) | Leeg opslaan: naast de knop staat "Niet opgeslagen: 5 velden kloppen nog niet. Het eerste is gemarkeerd."; de focus staat op Klant. De eenheid staat direct achter het aantal. |
| `03-beschikbaarheid-verkoop.png` (en `-390`) | De weergave Verkoop is één keer weer de standaard. Het testartikel: Voorraad 40, Verwacht 30 (20 besteld en 10 in keuring), Vrij 70. |
| `04-verkopen-50-splitsen.png` | Verkopen, 50 colli: "Voorraad 40 · Verwacht 30 · Vrij 70 colli", "Er is maar 40 colli vrij op voorraad" en de knop Splitsen. |
| `05-order-vastgelegd.png` | De order is vastgelegd met een VO-nummer; daarna staat Vrij op 20. |
| `06-datum-en-eenheid-na-aantal.png` | Inkooporder aanmaken: `04-11-26` werd `04-11-2026`. Na Tab vanaf het aantal staat de focus op de eenheid; de pijlen kiezen een eenheid. |
| `07-keuring-wie-beoordeelt.png` (en `-390`) | Keuring: bovenaan staat geen veld meer. "Wie beoordeelt" staat in het paneel, tussen de reden en de bevestigknop. "Kies wie beoordeelt." staat bij het veld, en de focus staat erop. |
| `08-gekeurd-door-jan-ingelogd-als.png` | Na het keuren staat bij de regel "Gekeurd door Jan, ingelogd als Admin". |

## Bekend en niet veranderd

Bij 390 px schuift de keuringstabel binnen haar eigen kader opzij. Zo was het al; de pagina zelf schuift niet. Het nieuwe veld maakt het paneel alleen hoger.

# Rode draad deel 2: verkopen gekoppeld aan voorraad, ook vóór aankomst

Doorloop in Chrome op `agf_deviation_clean` op 3 november 2026, op 1440 px en 390 px breed. Er was nergens horizontaal scrollen.

Het artikel is Citroen Primofiori 4, in colli.

- **Inkooporder IO-2026-0003:** 40 colli, ontvangen op pallet P-000005 en goedgekeurd (partij RD2-LOT-A).
- **Inkooporder IO-2026-0004:** 20 colli verwacht op 5 november.

## Het pad

| # | Scherm | Wat je ziet |
|---|---|---|
| 01 | Beschikbaarheid, weergave Verkoop | Voorraad 40 · Verwacht 20 · Verkocht 0 · Vrij te verkopen 60 |
| 02 | Rij uitgeklapt | Per partij RD2-LOT-A 40 goedgekeurd · per inkooporder IO-2026-0004 20 (verwacht 5 nov) · nog geen verkoop |
| 03 | Verkopen → nieuwe order, 50 colli | "Voorraad 40 · Verwacht 20 · Vrij 60", "= 0,2 pallets", "Er is maar 40 colli vrij op voorraad" met de knop Splitsen |
| 04 | Na Splitsen | 40 Uit voorraad + 10 Uit verwachte aanvoer |
| 05 | Opgeslagen | VO-2026-0001, geen tekort (Vrij 10) |
| 06 | Tweede order, 20 Uit verwachte aanvoer | "Je verkoopt 10 colli meer dan voorraad en verwachte aanvoer"; opslaan blijft mogelijk |
| 07 | Opgeslagen | VO-2026-0002 met de waarschuwing van de server: tekort 10 colli |
| 08 | Beschikbaarheid | Vrij −10 in rood met "TEKORT" |
| 09 | Verkooporders | VO-nummer, klantreferentie, Tekort en Wacht op voorraad |
| 10 | VO-2026-0002 | Keten-strip "wacht op inkooporder IO-2026-0004 (verwacht 05-11)" · "tekort 10 colli" |
| 11 | Na goedgekeurde ontvangst van de 20 | Voorraad 60 · Verwacht 0 · Verkocht 70 · Vrij nog −10 |
| 12 | Werklijst "Verkocht, wacht op voorraad" | Beide regels uit verwachte aanvoer zijn nu (deels) te reserveren; er is nog niets vanzelf gereserveerd |
| 13 | Reserveren (oudste partij eerst) | Voorstel RD2-LOT-A (oudste) eerst, met inkooporder en pallet per partij; kg volgt het aantal |
| 14 | Na bevestigen | Per reservering inkooporder, partij en pallet (herkomst per stuk) |
| 15–18 | 390 px | Kaarten onder elkaar, uitgeklapt per partij en order, het orderdetail en de nieuwe order |

## De regels van het model

- **Voorraad** is goedgekeurde inhoud op een ontvangen pallet in het eigen magazijn. Quarantaine, geblokkeerd en afgekeurd tellen niet mee. De fysieke voorraad wordt nooit negatief.
- **Verwacht** zijn de open inkoopregels van het eigen magazijn: nog niet ontvangen, plus ontvangen maar nog in keuring. Klantgebonden aanvoer (DirectTrade) telt nooit mee.
- **Verkocht** zijn de open verkooporderregels: besteld min verzonden, zowel gereserveerd als niet. Wat via een klantgebonden toewijzing loopt, telt niet mee.
- **Vrij te verkopen = Voorraad + Verwacht − Verkocht.** Dit getal mag negatief zijn. Een tekort is een waarschuwing, geen blokkade.
- **Vrij op voorraad = Voorraad − gereserveerd − niet gereserveerd verkocht uit voorraad.** Een regel "Uit voorraad" die daar niet in past, weigert de server met een 409.
- **Een exacte pallet reserveren kan alleen op goedgekeurde goederen die er zijn.** Een verkoop vóór aankomst is daarom een orderregel zonder reservering, tot de goederen er zijn.
- **Dekking per regel gaat op volgorde: oudste verkoop eerst.**
  - Eerst de regels uit voorraad, daarna die uit verwachte aanvoer, elk op aanmaakmoment van de order.
  - Elke regel neemt eerst vrije voorraad, dan aanvoer in keuring, dan de vroegst verwachte inkooporder.
  - Wat overblijft, is tekort.
- **Er wordt nergens op tekst vergeleken,** altijd op de exacte variant (`ProductVariantId`) en eenheid. Er wordt nooit iets "bijna gelijks" gekozen.

## Testtabel (backend: SalesBalanceTests, SalesShortageTests, FifoReservationTests, SalesOrderNumberTests)

| Scenario | Uitkomst |
|---|---|
| 40 voorraad / 20 verwacht / 0 verkocht | Vrij 60 |
| Verkoop 50 | Vrij 10 |
| Verkoop 70 | Vrij −10, waarschuwing en Tekort |
| Goedgekeurde ontvangst van de 20 | Voorraad 60, Verwacht 0, Vrij nog −10 |
| Tweede inkoop van 10 | Vrij 0, Tekort weg |
| Regel "Uit voorraad" boven de vrije voorraad | 409 met de splitsing in de reden; er wordt niets opgeslagen |
| Splitsen | Twee regels (Stock + Expected) van dezelfde variant en eenheid |
| FIFO over twee pallets en twee partijen | Oudste ontvangst eerst; de rest van de regel komt van de volgende pallet |
| Gelijke ontvangstdatum | Het partijnummer beslist |
| Alleen goedgekeurd telt | Quarantaine telt als "in keuring" (Verwacht), geblokkeerd telt nergens |
| Klantgebonden aanvoer | Telt niet als Verwacht |
| Gelijktijdige reserveringen | Precies één wint (versie van inhoudsregel, orderregel en pallet) |
| Replay via `RequestId` | Zelfde reserveringen terug; een andere inhoud geeft 409 |
| Twee varianten met bijna dezelfde naam | Nooit gemengd: aparte stand, 409 bij reserveren op de verkeerde variant |
| VO-nummering | Doorlopend per jaar; bestaande orders op volgorde van aanmaken (migratie, gecontroleerd op een kopie); guard-regel 22 |

## Houdbaarheid (FEFO)

Er bestaat geen houdbaarheidsdatum per partij: geen veld op `PalletContentLine` of `SupplyCommitmentLine`. FEFO is daarom niet gebouwd. De standaard is oudste ontvangst eerst. Zodra er een THT per partij wordt vastgelegd, kan `SalesOrderService.FifoOrder` daar als eerste op sorteren.

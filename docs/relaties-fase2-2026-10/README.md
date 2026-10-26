# Relaties fase 2 (PR 1): screenshots

Echte UI (Playwright met geïnstalleerde Chrome) tegen de lokale database `agf_smoke`, smokelabel `SMOKE-FIN-8914`; de vrije betalingstermijn (vervolg op #55) met smokelabel `SMOKE-TERMS-9FC3`.

| Bestand | Wat je ziet |
|---|---|
| `01-instellingen-betalingscondities.png` | Instellingen: nieuwe conditie 30EM (einde maand, 2% korting binnen 8 dagen) met voorbeeldvervaldatum. |
| `02-relatie-bank-krediet.png` | Klantrol: betalingstermijn 30 dagen na einde maand met 2% korting binnen 8 dagen (vrije invoer, geen keuzelijst meer), kredietlimiet, verzekering en kredietpositie met overschrijding, verzekeringsgat en "1 order zonder prijs, niet meegeteld". |
| `03-bankrekening-onbevestigd.png` | Nieuwe bankrekening: geldige IBAN maar "Niet bevestigd"; bevestigen kan pas met een naam bij "Wie". |
| `03b-bankwaarschuwing-banner.png` | Opvallende melding op de kaart: standaardbankrekening niet bevestigd, plus de e-mail- en verzekeringswaarschuwingen. |
| `04-bankrekening-bevestigd.png` | Na bevestigen: "Bevestigd door … op …" en de wijzigingsregels (toegevoegd, bevestigd). |
| `05-krediet-waarschuwing-order.png` | Nieuwe verkooporder boven de kredietlimiet: waarschuwing met limiet, openstaand en verschil; de order is gewoon vastgelegd. |
| `06-email-typfout-hint.png` | Typfout in het e-maildomein (gmial.com): klikbare suggestie, geen fout. |
| `07-btw-schrijfwijze-fout.png` | Btw-nummer GR123456789: fout met de suggestie EL; de hint zegt dat alleen de schrijfwijze is gecontroleerd, niet VIES. |
| `08-ggn-controle.png` | Leveranciers-GGN's: verlopen certificaat, nooit gecontroleerd, en "Controleer bij GLOBALG.A.P." met het GGN gekopieerd. |
| `09-inkoopfactuur-bank-waarschuwing.png` | Inkoopfactuur van een leverancier met een niet-bevestigde standaardrekening; vervaldatum volgens 30EM (einde maand). |
| `10-ontvangst-ggn-waarschuwing.png` | Ontvangst: gekozen GGN met verlopen certificaat geeft een waarschuwing (niet blokkerend). |
| `11-mobiel-390-relatie.png` | Relatiekaart op 390 px: krediet en bankrekeningen. |
| `12-mobiel-390-instellingen.png` | Instellingen op 390 px: de knoppen staan onder de omschrijving. |
| `13-relatie-en.png` | Relatiekaart (klantrol met vrije betalingstermijn en krediet) in het Engels. |
| `14-betalingsconditie-in-gebruik.png` | Verwijderen van een gebruikte conditie (30D) geweigerd, met de aantallen en "Inactief zetten". |
| `15-termijn-vrij-ingevuld.png` | Betalingstermijn getypt: 45 dagen, "na einde maand" aangevinkt, geen korting; de voorbeeldregel rekent bij elke toetsaanslag mee. Leeg veld toont de standaard grijs ("30 (standaard)"). |
| `16-termijn-met-korting.png` | Zelfde termijn met 2% korting binnen 10 dagen: de voorbeeldregel toont nu ook de kortingsdatum. Eén kortingsveld leeg gelaten = fout bij het lege veld en daar de focus. |
| `17-mobiel-390-termijn.png` | Termijnvelden op 390 px: dagen + "na einde maand" en korting % + dagen in twee gelijke kolommen. |

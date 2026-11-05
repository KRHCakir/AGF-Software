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

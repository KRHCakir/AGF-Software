# AI-lezen van inkoopfacturen (stap 7a)

De AI doet een **voorstel**; een gebruiker controleert het en klikt **Goedkeuren**. De AI slaat nooit zelf iets op.

## Aanzetten

AI-lezen staat standaard **uit**. Een beheerder zet het aan bij **Instellingen**. Daarbij bevestigt de beheerder dat de klant weet dat documenten naar een externe dienst gaan. Het systeem legt vast wie dat deed en wanneer.

De aanbieder is Anthropic (Claude), met verwerking in de VS. Het document gaat alleen bij een upload naar de aanbieder, alleen voor dat ene verzoek.

## Instellingen (`DocumentAi` in `appsettings.json`)

| Instelling | Standaard | Betekenis |
|---|---|---|
| `Reader` | `Claude` | `Demo`: een vaste uitkomst zonder netwerk, alleen voor tests en de rondleiding. Gebruik `Demo` nooit in de gewone werking. |
| `ApiKey` | (leeg) | De sleutel komt uit de gebruikersgeheimen of de omgeving (`DocumentAi__ApiKey`), anders uit `ANTHROPIC_API_KEY`. Zet hem nooit in de repo. |
| `PrimaryModel` | `claude-haiku-5-5` | Het model dat elk document leest. |
| `FallbackEnabled`, `FallbackModel` | `false`, (leeg) | Uit: alleen Haiku leest (besluit van de eigenaar, 12 november). Zie hieronder. |
| `Models` | Haiku 5.5 | De prijs per miljoen tokens, met de datum waarop die bij de aanbieder is gecontroleerd. Zonder prijs leest het systeem niet. |
| `EurPerUsd` | `0,86` | De koers waarmee het werkelijke verbruik in dollars naar euro wordt omgerekend. Die koers wordt per lezing vastgelegd. |
| `MaxFileBytes` | 10 MB | De grootste upload. Ondersteund zijn PDF, JPEG, PNG en HEIC. |
| `MaxUploadsPerMinute` | 6 | Het maximum per gebruiker. |
| `TimeoutSeconds` | 90 | De maximale wachttijd per verzoek. |
| `StoragePath` | `App_Data/documents` | De map met de documenten. |
| `RetentionDays` | 30 | Bestanden van niet-goedgekeurde lezingen worden na zoveel dagen verwijderd. |

De **maandlimiet** in euro (standaard 25) is een instelling per bedrijf bij Instellingen. Boven de limiet pauzeert het lezen tot de volgende maand of tot de beheerder de limiet verhoogt. Handmatig invoeren blijft gewoon werken.

## Alleen Haiku (geen vangnet)

Haiku leest het document; er wordt geen ander model gebruikt. Bij een tijdelijke fout probeert het systeem het één keer opnieuw met
Haiku. Klopt het voorstel niet, dan toont het scherm het met gele markeringen en vult de gebruiker het aan of verbetert het.

Tot 12 november las Sonnet als vangnet het document één keer extra als het voorstel niet klopte. Dat staat uit en Sonnet staat niet
meer in de instellingen. De code kan een tweede model nog alleen gebruiken als iemand dat bewust configureert (`FallbackEnabled` op
`true`, een `FallbackModel` en een prijs onder `Models`). Wanneer Haiku's voorstel als "niet kloppend" telt (de gele markeringen):
- de som van de regels, min een korting op het totaal, wijkt af van het totaal excl. btw;
- de btw rekent niet (met een korting op het totaal over de verlaagde grondslag);
- excl. btw plus btw is niet het totaal incl. btw;
- een verplicht veld ontbreekt (leverancier, factuurnummer, datum, totalen of regels);
- de AI twijfelt aan een verplicht veld;
- de uitvoer is ongeldig.

Bij elk voorstel staat welk model het las.

## Kortingen

Haiku leest twee kortingen apart, zoals ze op de factuur staan:
- **korting op de regel** (`korting_percentage`, in procenten): aantal × prijs min die korting is het regelbedrag. Die gaat naar het
  kortingveld van de regel in het formulier.
- **korting op het totaal** (`korting_totaal`, bedrag excl. btw): de regels min die korting zijn het totaal excl. btw. Een
  betalingskorting die pas geldt bij snel betalen telt niet; die verlaagt het totaal op de factuur niet.

Het factuurformulier kent alleen een korting per regel. Een korting op het totaal gaat daarom mee in de korting van elke regel, als dat
precies kan: de korting is een percentage met hoogstens twee decimalen van de som van de regels (3% van 5.739,60 = 172,19), en elke
samengestelde korting heeft hoogstens twee decimalen, en de regels van het formulier (per regel afgerond) komen samen op 2 cent na uit op
de regels min de korting. Bij 5% op de regel en 3% op het totaal is dat 100 − 95 × 97 / 100 = 7,85%. De
blauwe balk zegt dat erbij ("Korting op het totaal: 3% (€ 172,19) …"). Doordat het formulier per regel afrondt, kan het totaal een cent
afwijken van de factuur.

Kan het niet precies (een vast bedrag zoals € 50,00, 2,5% over 5% = 7,375%, of twintig regels van € 1,05 met 10% korting, die per
regel afgerond € 19,00 geven in plaats van € 18,90), dan blijft alleen de korting op de regel staan en
meldt de balk in geel dat de gebruiker de korting zelf verdeelt. Dat laat geen tweede model lezen.

De testbench (`AgfAiBenchmark`) leest zonder kortingen en zonder leverancierskenmerken (`SchemaParts.Base`), zodat zijn eerdere runs
vergelijkbaar blijven.

## Wat wordt vastgelegd (`AiExtractions`)

Per lezing legt het systeem vast:
- **het bestand:** naam, type, grootte en SHA-256-hash;
- **het model:** welk model het voorstel las, de versie die de aanbieder terugmeldt, en of het vangnet las;
- **elke poging:** model, versie, tokens, prijs, kosten en tijd;
- **het totaal:** tokens, kosten in dollar en euro, en de gebruikte koers;
- **de inhoud:** het voorstel en de controles;
- **de afloop:** wie het voorstel goedkeurde of afwees en wanneer, de goedgekeurde waarden, en wat de gebruiker vóór goedkeuren aanpaste.

Het logboek bevat nooit documentinhoud, alleen de soort fout.

## Documenten: back-ups en versleuteling

De documenten staan als bestanden in `StoragePath`. Elk bestand staat in een submap per maand en heeft een willekeurige naam, zonder bedrijfsnaam of factuurnummer.

- **Back-up:** neem deze map mee in de back-up van de server, samen met de database. Een bijlage verwijst naar haar bestand en de hash bewijst dat het bestand niet is veranderd.
- **Versleuteling:** de software versleutelt de bestanden niet zelf. Zet de map op een versleutelde schijf of een versleuteld volume (bijvoorbeeld FileVault, BitLocker, LUKS of de versleuteling van de cloudschijf).
- **Bewaren:**
  - Een goedgekeurd document is een bijlage bij de factuur. Het wordt nooit gewijzigd of verwijderd.
  - Een afgewezen, handmatig verder gezet of mislukt document wordt na `RetentionDays` verwijderd. De lezing zelf blijft staan, voor het verbruik per maand.

## Nog niet (stap 7b en verder)

Nog niet gebouwd:
- de mailbox (Microsoft Graph);
- verkoopfacturen, bonnen en andere documenttypen;
- automatisch goedkeuren;
- automatisch een leverancier of artikel aanmaken;
- leren van correcties;
- een lokaal model.

De oude upload onder `api/incominginvoices/upload` gaf nagebootste gegevens. Die route antwoordt nu met 410 en verwijst naar Inkoopfactuur invoeren.

# AI-testbench: welk model leest onze facturen en bonnen het best

Een los opdrachtregelprogramma (`AgfAiBenchmark`). Het stuurt dezelfde documenten met dezelfde instructie en hetzelfde uitvoerformaat naar meerdere AI-modellen. Daarna vergelijkt het per veld met de juiste antwoorden die jij vooraf invult, en schrijft het één overzicht met nauwkeurigheid, kosten en snelheid.

Het programma verandert niets aan de software. Het schrijft niets in de administratie, gebruikt geen database en roept geen API van `AgfAdmin` aan. Het schrijft alleen in de map met resultaten.

## 1. Eenmalig klaarzetten

- **.NET 9** (staat er al voor AgfAdmin).
- **Alleen voor Qwen:** `brew install poppler`. Qwen-VL leest geen PDF, dus het programma zet elke pagina eerst om naar een afbeelding (maximaal 10 pagina's). Zonder poppler krijgt een PDF bij Qwen de status `niet-ondersteund`.
- **Sleutels:** zet ze als gebruikersgeheim (buiten de repo, in je profiel) of als omgevingsvariabele. Ze komen nooit in de repo, in een log of in een resultaatbestand.

```
dotnet user-secrets set ANTHROPIC_API_KEY "<sleutel>" --project AgfAiBenchmark
dotnet user-secrets set GEMINI_API_KEY "<sleutel>" --project AgfAiBenchmark
dotnet user-secrets set DASHSCOPE_API_KEY "<sleutel>" --project AgfAiBenchmark   # alleen als je Qwen wilt testen
```

Ontbreekt een sleutel, dan slaat het programma dat model over en zegt dat.

## 2. Documenten en juiste antwoorden

Alles staat in `~/agf-ai-bench/` (een andere map kies je met `AGF_AI_BENCH`), dus buiten de repo:

```
~/agf-ai-bench/documenten/   PDF, JPG, PNG of WEBP (een iPhone-foto in HEIC eerst als JPG bewaren)
~/agf-ai-bench/juist/        per document een JSON-bestand met dezelfde naam: factuur-01.pdf → factuur-01.json
~/agf-ai-bench/resultaten/   wordt door het programma gevuld
```

Het programma weigert documenten, antwoorden of resultaten in de repo. De enige uitzondering zijn de twee verzonnen voorbeelden in `AgfAiBenchmark/samples/`.

Een leeg antwoordbestand in het juiste formaat maak je zo (een bestaand bestand wordt nooit overschreven):

```
dotnet run --project AgfAiBenchmark -- new-truth ~/agf-ai-bench/documenten/factuur-01.pdf
```

Invullen:

- **`_meta.bron`:** `digitaal`, `scan` of `foto`. **`_meta.taal`:** `nl`, `en` of `es`. Hiermee splitst het overzicht de uitkomsten op.
- **`soort`:** `inkoopfactuur`, `verkoopfactuur`, `bon` of `overig`.
- **`leverancier`:** het bedrijf dat het document uitgeeft. **`factuurnummer`:** bij een bon het bonnummer.
- **`ordernummer_leverancier`:** het bestelnummer waarmee we de inkooporder terugvinden, ook als het op de factuur "Uw referentie", "Your PO" of "Ihre Bestellung" heet. Staat er geen, dan het ordernummer van de leverancier, anders `null`.
- **Wat niet op het document staat:** `null` (niet leeg laten en niet raden). Een model dat daar toch iets invult, krijgt "verzonnen".
- **Getallen:** mogen in nl-notatie als tekst (`"1.250,00"`) of als gewoon getal (`1250`). **Datums:** `04-11-2026` of `2026-11-04`.
- **`regels`:** één object per artikel- of kostenregel (ook transport, handling en emballage), in de volgorde van het document. Subtotalen en btw-regels tellen niet als regel. **`prijs`** is per eenheid, **`bedrag`** het regelbedrag excl. btw.
- Een voorbeeld staat in `AgfAiBenchmark/samples/truth/`.
- Een bestand dat nog het lege sjabloon is, telt niet mee in de score: het programma meldt dan "nog niet ingevuld". Na het invullen draai je de run opnieuw. De modellen worden niet opnieuw aangeroepen, alleen de score wordt opnieuw berekend.

## 3. De run starten

```
dotnet run --project AgfAiBenchmark -- run --models claude-sonnet,claude-haiku,gemini-flash
dotnet run --project AgfAiBenchmark -- run --models claude-sonnet,qwen-vl --opt-in qwen-vl
```

- **Vóór het versturen:** het programma meldt per aanbieder dat de documenten daarheen gaan en vraagt één keer om `ja`. Met `--yes` sla je die vraag over.
- **Qwen (Alibaba, China)** draait alleen met `--opt-in qwen-vl`.
- **Ons bedrijf:** met `--wij "Verita BV"` staat de naam in de instructie, zodat het model weet dat een factuur aan ons een inkoopfactuur is. Een andere naam (of geen) is een andere instructie, en dan draait alles opnieuw.
- **Andere mappen:** `--docs`, `--truth` en `--out`.
- **Gelijktijdige verzoeken:** standaard 4, aan te passen met `--parallel 2`.
- **Opnieuw draaien:** een document dat al gedraaid is voor hetzelfde model en dezelfde instructie wordt overgeslagen, zodat je niet twee keer betaalt. `--force` draait alles opnieuw. Een ander document, model of andere instructie draait vanzelf opnieuw.
- **Fouten tijdens de run:**
  - Tijdelijke fouten (te druk, storing) worden tot 3 keer geprobeerd.
  - Een ongeldige uitvoer wordt één keer opnieuw gevraagd.
  - Een mislukt document stopt de run niet.
- **Proberen zonder iets te versturen:** `--models fake-goed,fake-slordig` gebruikt nagebootste modellen.

## 4. De uitkomst lezen

In de map met resultaten:

| Bestand | Wat erin staat |
|---|---|
| `overzicht.md` | Samenvatting per model, daarna uitsplitsingen. Zie de lijst onder deze tabel. |
| `oneens.md` | Per document de velden waar de modellen verschillen, naast het juiste antwoord. ✓ is goed. Hier zie je snel wie gelijk heeft. |
| `overzicht.csv`, `per-document.csv` | Dezelfde cijfers voor Excel: tokens, kosten in USD, seconden en het aantal aanroepen. |
| `run.json` | De modellen met API-naam, prijs en de datum waarop die prijs is gecontroleerd, plus de vingerafdruk van de instructie. |
| `ruw/<model>/<document>.<code>.json` | De ruwe uitvoer per model en document, met de modelversie die de aanbieder zelf terugmeldde. |

`overzicht.md` bevat:

- de samenvatting per model: percentage goed, apart voor kop, regels en bedragen, hoe vaak een model "hoog zeker" was terwijl het fout zat, kosten per document en in totaal (uit het echte tokenverbruik), en de tijd per document;
- het percentage goed per veld;
- de uitkomsten per bron (digitaal, scan, foto) en per taal;
- per document het percentage goed, de kosten en de tijd.

Elk veld krijgt één uitkomst:

| Uitkomst | Betekenis |
|---|---|
| goed | Gelijk aan het juiste antwoord, ook als beide `null` zijn. |
| fout | Een andere waarde dan het juiste antwoord. |
| ontbreekt | Het model gaf `null` waar wel een waarde staat. |
| verzonnen | Het model gaf een waarde waar `null` hoort, of een regel die er niet is. |

Bij de status `ongeldig`, `fout` of `niet-ondersteund` telt elk veld van dat document als fout.

## Hoe er vergeleken wordt

Voor het vergelijken worden alle waarden genormaliseerd:

| Soort waarde | Regel |
|---|---|
| Datums | Naar jaar-maand-dag. Bij 04-11-2026 komt de dag eerst. |
| Bedragen (prijs, bedrag, totalen) | Op centen. |
| Andere getallen (aantal, kg, btw-percentage) | Op drie decimalen. Tekst in nl-notatie wordt gelezen. |
| Factuur- en ordernummer | Hoofdletters en spaties tellen niet. |
| Overige tekst | Hoofdletters, accenten, leestekens en dubbele spaties tellen niet. |
| Omschrijving van een regel | Goed als alle woorden van jouw antwoord erin staan. Een artikelcode of extra woorden erachter mogen. |

**Regels koppelen:** elke juiste regel wordt eerst gekoppeld aan de modelregel met de meest overeenkomende omschrijving (minstens de helft van de woorden gelijk). Wat dan nog over is, gaat op volgorde.

Zo telt één overgeslagen of verwisselde regel niet als fout bij alle regels erna. Een model dat "Vracht" schrijft voor "Transport" valt via de volgorde toch op de goede regel, en krijgt dan alleen een fout op de omschrijving. Een regel die het model extra geeft, telt als verzonnen.

## Modellen en prijzen

De prijzen zijn per miljoen tokens, gecontroleerd op 11 november 2026. Ze staan in `AgfAiBenchmark/models.json`, en elke run legt ze vast in `run.json`. Controleer ze opnieuw vóór een echte run.

| id | Model | In / uit | Verwerking |
|---|---|---|---|
| `claude-sonnet` | Anthropic `claude-sonnet-5-5` | $2,00 / $10,00 | Anthropic, VS |
| `claude-haiku` | Anthropic `claude-haiku-5-5` | $0,10 / $0,50 (boven 100K invoertokens $0,50 / $2,50) | Anthropic, VS |
| `gemini-flash` | Google `gemini-3.8-flash` | $0,75 / $3,75 (vanaf 1-1-2027 $1,50 / $7,50) | Google |
| `gemini-flash-3.6` | Google `gemini-3.6-flash` (vorige Flash, voor als 3.8 overbelast is) | $0,75 / $3,75 (vanaf 1-1-2027 $1,50 / $7,50) | Google |
| `qwen-vl` | Alibaba `qwen3-vl-plus`, Singapore | $0,20 / $1,60 (tot 32K invoertokens) | Alibaba, alleen opt-in |

Qwen kan ook in Frankfurt (EU) draaien, met een eigen werkruimte en een eigen sleutel. Zet dan `basisUrl` in `models.json` op `https://<werkruimte-id>.eu-central-1.maas.aliyuncs.com/compatible-mode/v1`.

Een model toevoegen of vervangen doe je met één regel in `models.json`. Een nieuwe aanbieder is één kleine klasse achter `IDocumentAI` in `AgfDocumentAi`.

## Privacy

- Documenten, juiste antwoorden en resultaten blijven buiten de repo. Ze staan daarnaast in `.gitignore` (`agf-ai-bench/`, `resultaten/`, `ruw/`).
- Gebruik voor de test alleen documenten waarvoor je toestemming hebt. Sluit vóór gebruik in de software een verwerkersovereenkomst met de gekozen aanbieder.

## Welke 30 documenten verzamelen

Zo is elk soort document vertegenwoordigd en zegt elke uitsplitsing in het overzicht iets:

| Aantal | Soort | Waarop letten |
|---|---|---|
| 10 | Digitale inkoopfactuur (PDF uit het systeem van de leverancier) | Minstens 5 verschillende leveranciers. Ook een factuur met meerdere pagina's en een factuur met 15 of meer regels. |
| 5 | Gescande inkoopfactuur | Ook een scheve of vlekkerige scan. |
| 4 | Kostenfactuur: transport, handling, keuring, emballage | Eén regel, of een regel per rit of per pallet. |
| 5 | Bon: pakbon, weegbon, CMR of ontvangstbon | Kg netto en bruto, en geen prijzen. |
| 3 | Foto met de telefoon | Een bon of factuur op het bureau, bij gewoon licht. |
| 3 | Overig of lastig | Een creditnota, een factuur in USD, een handgeschreven aanvulling. |

Verdeel de talen ongeveer zo: 15 Nederlands, 8 Engels en 7 Spaans.

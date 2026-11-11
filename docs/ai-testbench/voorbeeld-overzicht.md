> Proefrun met de twee verzonnen voorbeelddocumenten uit `AgfAiBenchmark/samples` en de nagebootste modellen `fake-goed` en `fake-slordig`: er is niets naar een aanbieder gestuurd. De echte modellen zijn overgeslagen omdat er geen sleutel is (en Qwen zonder `--opt-in`). Bedragen en tijden van de nagebootste modellen zijn verzonnen; het gaat om de vorm van het overzicht.

# AI-testbench: overzicht

Run van 2026-11-11 17:02, instructie f75c3001fee1, 2 documenten, waarvan 2 met juiste antwoorden.

- claude-sonnet: geen sleutel ANTHROPIC_API_KEY gevonden, overgeslagen.
- claude-haiku: geen sleutel ANTHROPIC_API_KEY gevonden, overgeslagen.
- gemini-flash: geen sleutel GEMINI_API_KEY gevonden, overgeslagen.
- qwen-vl: draait alleen met --opt-in qwen-vl (Alibaba (China), Singapore), overgeslagen.

## Modellen

| Model | API-model | Gemelde versie | Prijs in / uit per 1M | Prijs gecontroleerd | Verwerking |
|---|---|---|---|---|---|
| fake-goed | fake-goed | fake-goed-1 | $1,00 / $5,00 | - | lokaal, niets verstuurd |
| fake-slordig | fake-slordig | fake-slordig-1 | $1,00 / $5,00 | - | lokaal, niets verstuurd |

## Samenvatting

| Model | Documenten | Geldig | Goed | Kop | Regels | Bedragen | Hoog zeker maar fout | Kosten totaal | Kosten per document | Tijd per document |
|---|---|---|---|---|---|---|---|---|---|---|
| fake-goed | 2 | 2 | 100,0% | 100,0% | 100,0% | 100,0% | 0 | $0,0056 | $0,0028 | 1,2 s |
| fake-slordig | 2 | 2 | 75,9% | 91,7% | 70,0% | 83,3% | 1 | $0,0054 | $0,0027 | 0,4 s |

## Per veld

|  | fake-goed | fake-slordig |
|---|---|---|
| soort | 100,0% | 100,0% |
| leverancier | 100,0% | 100,0% |
| ordernummer_leverancier | 100,0% | 100,0% |
| factuurnummer | 100,0% | 100,0% |
| datum | 100,0% | 100,0% |
| valuta | 100,0% | 50,0% |
| totaal_excl | 100,0% | 100,0% |
| btw_totaal | 100,0% | 100,0% |
| totaal_incl | 100,0% | 50,0% |
| regels.omschrijving | 100,0% | 60,0% |
| regels.aantal | 100,0% | 60,0% |
| regels.eenheid | 100,0% | 60,0% |
| regels.kg_netto | 100,0% | 80,0% |
| regels.kg_bruto | 100,0% | 80,0% |
| regels.prijs | 100,0% | 80,0% |
| regels.bedrag | 100,0% | 60,0% |
| regels.btw_percentage | 100,0% | 80,0% |

## Per soort bron (digitaal, scan, foto)

|  | fake-goed | fake-slordig |
|---|---|---|
| digitaal | 100,0% | 75,9% |

## Per taal

|  | fake-goed | fake-slordig |
|---|---|---|
| es | 100,0% | 76,0% |
| nl | 100,0% | 75,8% |

## Per document

| Document | fake-goed | fake-slordig |
|---|---|---|
| voorbeeld-bon-es.pdf | 100,0% · $0,0026 · 1,2 s | 76,0% · $0,0025 · 0,4 s |
| voorbeeld-factuur-nl.pdf | 100,0% · $0,0030 · 1,2 s | 75,8% · $0,0029 · 0,4 s |

Waar de modellen het oneens zijn: zie `oneens.md`. Ruwe uitvoer per model en document: map `ruw/`.

## Waar de modellen het oneens zijn

Alleen geldige uitvoer, genormaliseerd. ✓ = gelijk aan het juiste antwoord.

### voorbeeld-bon-es.pdf

| Veld | Juist | fake-goed | fake-slordig |
|---|---|---|---|
| aantal regels | 2 | 2 | 1 |
| valuta | null | null ✓ | eur |
| regels[1].omschrijving | naranja navel calibre 5 | naranja navel calibre 5 ✓ | null |
| regels[1].aantal | 60 | 60 ✓ | null |
| regels[1].eenheid | cajas | cajas ✓ | null |
| regels[1].kg_netto | 900 | 900 ✓ | null |
| regels[1].kg_bruto | 954 | 954 ✓ | null |

### voorbeeld-factuur-nl.pdf

| Veld | Juist | fake-goed | fake-slordig |
|---|---|---|---|
| aantal regels | 3 | 3 | 2 |
| totaal_incl | 1890.45 | 1890.45 ✓ | null |
| regels[0].bedrag | 1250.00 | 1250.00 ✓ | 1251.00 |
| regels[2].omschrijving | transport rotterdam barendrecht | transport rotterdam barendrecht ✓ | null |
| regels[2].aantal | 1 | 1 ✓ | null |
| regels[2].eenheid | rit | rit ✓ | null |
| regels[2].prijs | 85.00 | 85.00 ✓ | null |
| regels[2].bedrag | 85.00 | 85.00 ✓ | null |
| regels[2].btw_percentage | 21 | 21 ✓ | null |


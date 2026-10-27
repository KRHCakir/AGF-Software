# Review-benchmark

Testdata om te meten hoeveel echte bugs de AI-reviewers (DeepSeek, Qwen) vangen.
**Deze bestanden bevatten expres bugs.** Ze worden niet gebouwd, en de PR-review slaat deze map over.

Starten: **Actions → AI Review Benchmark → Run workflow**. Het resultaat staat in de samenvatting van de run:
per reviewer hoeveel bugs gevangen zijn, hoeveel valse alarmen er waren, en wat ze samen vangen.

## Een geval toevoegen

Maak een map `cases/<nummer>-<naam>/` met:

- `files/<pad zoals in de repo>`: het bestand of de bestanden zoals ze in een PR zouden staan.
- `case.json`:

```json
{
  "title": "PR-titel zoals een ontwikkelaar hem zou schrijven",
  "body": "PR-beschrijving (verklap de bug niet)",
  "bug": "Korte omschrijving van de ingebouwde bug (alleen voor het rapport)",
  "expect": { "path": "AgfAdmin/Services/Bestand.cs", "lines": [10, 12] }
}
```

Voor een foutloos controlegeval: `"bug": null, "expect": null`.

Een bug telt als gevangen bij een `blocker` of `major` in het juiste bestand, binnen 3 regels van `lines`
(of zonder regelnummer). Goede kandidaten: bugs die jullie echt eens gehad hebben.

# Factuur uploaden met AI (stap 7a, november 2026)

De screenshots zijn gemaakt in Chrome met de **echte lezer**: Claude Haiku 5.5, met Sonnet 5.5 als vangnet. Ze draaiden op `agf_aifac_screens2`, een kopie van `agf_deviation_clean`.

Alle documenten in deze screenshots zijn **verzonnen**: ze staan in `voorbeelden/`, en er zijn geen echte bedrijfsgegevens zichtbaar. Op de kopie zijn de relatie "Voorbeeld Kwekerij B.V." (KvK 87654321) en een inkooporder met ordernummer leverancier `45-21` klaargezet.

| Bestand | Wat je ziet |
|---|---|
| `01-ai-uit.png` | AI-lezen staat uit: de uploadknop is uitgeschakeld met de melding "AI-lezen staat niet aan" en een link naar Instellingen. Er komen geen nepgegevens. |
| `02-instelling-aan.png` | Instellingen: AI-lezen aangezet na de bevestiging dat de klant het weet (wie en wanneer), met maandlimiet en verbruik. |
| `03-voorstel-digitale-pdf.png` | Een digitale PDF: links het document, rechts het voorstel in het factuurformulier. De leverancier is gevonden op KvK, de inkooporder `45-21` voorgesteld, en Transport staat als voorstel bij de regel. |
| `04-goedgekeurd.png` | Na Goedkeuren: "AI-voorstel goedgekeurd door Admin · gelezen door Claude Haiku 5.5". |
| `05-factuurdetail-goedgekeurd-door.png` | Het factuurdetail: goedgekeurd door, op, model en versie, en het originele document als bijlage. |
| `06-voorstel-foto-heic-dubbel.png` | Een HEIC-foto van de telefoon, als JPEG getoond (met zoom). Hetzelfde factuurnummer van deze leverancier geeft al vóór opslaan de bestaande melding. |
| `07-onbekende-leverancier-zonder-order.png` | Een factuur zonder ordernummer van een onbekende leverancier: "Leverancier niet gevonden" met Relatie toevoegen, "directe factuur", en de kostenartikelen Transport en Handling als voorstel. |
| `08-voorstel-390.png`, `09-voorstel-390-ingeklapt.png` | 390 px: het document boven het formulier, in te klappen, zonder horizontaal scrollen. |

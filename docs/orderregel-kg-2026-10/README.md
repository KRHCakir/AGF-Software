# Kg op de orderregel en klant zoeken: screenshots

Echte UI (Playwright met geïnstalleerde Chrome) tegen een kopie van de lokale database (`agf_smoke_kg_copy`, migratie AddOrderLineWeights toegepast), smokelabel `SMOKE-KG-FAF4`. Artikel met kg per colli: Avocado · Hass · Doos · PE (4 kg netto, 4,5 kg bruto per colli). Artikel zonder kg per colli: Avocado · Green (variant 2, gewogen).

| Bestand | Wat je ziet |
|---|---|
| `01-berekende-kg.png` | 1.560 colli: Netto kg en Bruto kg staan grijs op aantal × artikelstandaard (6.240 en 7.020, "berekend · × 4 kg"), met het totaal onder de regels. |
| `02-kg-overschreven.png` | Netto kg zelf ingevuld (6.100, zwart, "ingevuld"): volgt het aantal niet meer; ↺ zet terug naar berekend. Bruto kg volgt nog wel. |
| `03-artikel-zonder-kg-per-colli.png` | Tweede regel Avocado · Green (221 colli): geen standaard, dus lege velden met "Geen kg per colli bij dit artikel." en een link naar het artikel; na 2.210 netto kg de controle "= 10,0 kg per colli". Totaal 1.781 colli · 8.450,0 kg netto · 9.310,0 kg bruto. |
| `04-klant-zoeken-lo.png` | Klant zoeken: na "Lo" direct suggesties (naam, plaats, zoekcode); de bovenste is gemarkeerd en Tab of Enter neemt hem over. |
| `05-orderregel-390px.png` | Orderregel op 390 px: velden onder elkaar, netto en bruto naast elkaar, niets afgesneden. |
| `06-orderdetail-kg.png` | Orderdetail: per regel "6.240,0 kg netto · 7.020,0 kg bruto (berekend)" en "(ingevuld)". |

# Inkooporders: ordernummer, lijst, factuurnummer en afkeuren — screenshots

Echte UI (Playwright met geïnstalleerde Chrome) tegen een kopie van de lokale database (`agf_smoke_po_copy`, migraties AddOrderLineWeights en AddPurchaseOrders toegepast), smokelabel `SMOKE-PO-113B`. Leverancier Example Supplier, artikel Mango · Kent (gewogen), 221 dozen.

| Bestand | Wat je ziet |
|---|---|
| `01-formulier-telefonische-order.png` | Aanvoer plannen: bovenaan leveranciersreferentie, besteldatum (vandaag) en "Besteld via" (Telefoon gekozen). |
| `02-eigen-ordernummer.png` | Na opslaan staat het eigen ordernummer groot in de bevestiging, met "Naar inkooporder". |
| `03-lijst-inkoopopdrachten.png` | Inkoopopdrachten: standaard open orders, nieuwste eerst; kolommen Ordernr · Leverancier · Leveranciersref · Besteld · Verwacht · Status · Colli · Netto kg · Factuurnr(s). Bestaande orders kregen bij de migratie een nummer op aanmaakvolgorde. |
| `04-zoeken-op-factuurnummer.png` | Zoeken op factuurnummer vindt de (ontvangen) order; het filter springt zichtbaar van Open naar Alle. |
| `05-afkeuren.png` | Afkeuren op een ontvangen regel: aantal colli en netto kg (standaard het hele afkeurbare deel), reden, toelichting, besluit en medewerker. Eronder het eerder afgesplitste, afgekeurde deel. |
| `06-creditnota-verwacht.png` | Na terugsturen of korting claimen: "58 van 221 colli afgekeurd" en de melding Creditnota verwacht, in lijst en detail. Een vastgelegde creditnota haalt die weg; een latere afkeuring zet hem terug. |
| `07-volledig-afgekeurd.png` | Alles wat ontvangen is afgekeurd (vernietigen): status Afgekeurd, inhoud Afgekeurd, geen creditnota verwacht; geschiedenis met keuring en afkeuring. |
| `08-lijst-390px.png` | 390 px: de lijst als kaarten, niets afgesneden. |
| `09-detail-390px.png` | 390 px: het detail, gestapeld. |
| `10-melding-nieuwe-inkoopfactuur.png` | Nieuwe inkoopfactuur: het getypte factuurnummer hangt al aan een order, dus "Hoort bij order IO-2026-0053" met link. |

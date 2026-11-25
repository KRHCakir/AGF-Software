# Verkoopfactuur 1: screenshots en voorbeeld-PDF

Gemaakt op 25 november 2026 in Chrome (zonder venster). De database was nieuw en leeg (`scripts/new-clean-db.sh`) en is gevuld met `scripts/demo-seed.sh`. Alleen verzonnen gegevens: DEMO-bedrijven, `.example`-adressen en een nep-IBAN. De verkooporder van de seed is via de API verzonden; daarna is alles in het scherm gedaan, behalve de voorbeeld-PDF.

| Bestand | Wat je ziet |
| --- | --- |
| `01-werklijst-nog-te-factureren.png` | Verkoop › Verkoopfacturen: de werklijst **Nog te factureren**. Per klant de leveringen met V-nummer, leverdatum, het aantal dagen sinds de levering en **Factuur maken**. |
| `02-levering-factureren.png` | De verkooporder: **Factureren** bij de levering. |
| `03-concept-prijs-aanvullen.png` | Het concept, zonder nummer. De demo-order had geen prijs, dus de prijs wordt in het concept aangevuld. Bij elke regel staat het maximum (nooit meer dan geleverd). |
| `04-definitief-bevestigen.png` | **Definitief** vraagt om een bevestiging. |
| `05-definitief-met-nummer-en-mailconcept.png` | De definitieve factuur heeft:<br>• nummer VF-2026-00001;<br>• wie en wanneer;<br>• btw per tarief;<br>• het mailconcept voor de factuur-ontvangers van de klant.<br>Het mailconcept wordt pas verstuurd na een klik. |
| `06-pdf-pagina-1.png`, `voorbeeld-verkoopfactuur.pdf` | De PDF (A4, zwart-wit) van een directe factuur:<br>• een productregel uit een partij;<br>• een vrije regel *Stroom koelcel november* onder *Emballage en diensten*;<br>• btw 9% en 21% apart;<br>• het IBAN in de voet. |
| `07-directe-factuur-invoer.png` | **Nieuwe factuur**, met het toetsenbord ingevuld:<br>• klant;<br>• productregel met dezelfde regelcomponent als de verkooporder, met verplichte partij;<br>• vrije regel met omzetsoort. |
| `08-directe-factuur-concept.png` | Het opgeslagen concept van de directe factuur. Er is een eigen V-nummer, en de voorraad is gereserveerd. |
| `09-beheer-omzetsoorten.png` | Instellingen › Omzetsoorten, met de standaard *Omzet diensten*. |
| `10-lijst-en-detail-390px.png` | De lijst en het detail op 390 px breed. De pagina scrolt niet zijwaarts; alleen de regeltabel scrolt binnen haar kader. |

De PDF in `05` (VF-2026-00001) is gemaakt vóór een kleine aanpassing aan de kolomruimte. Een definitieve factuur houdt altijd haar eigen bewaarde PDF. Daarom is de voorbeeld-PDF een nieuwe factuur (VF-2026-00003) met de huidige opmaak.

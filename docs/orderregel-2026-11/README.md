# Orderregel van de inkooporder (11 november 2026)

In Chrome op een kopie van `agf_deviation_clean`, eerst gemigreerd naar 55. Op die kopie hebben drie artikelen een bruto per colli en 252, 264 en 120 colli per pallet gekregen; Avocado Hass heeft het merk Westfalia en Mango Palmer de snelcode `2`.

De order heeft drie regels, ingevuld met alleen het toetsenbord:
1. Snelcode `avope18`, 100 colli à 12,50.
2. Snelcode `2`, 50 colli à 9,75; daarna is de bruto met "Bruto aanpassen" op 230 kg gezet.
3. Snelcode `CITES3`, 30 colli à 8.

Na de prijs van regel 1 en regel 2 maakte Tab een nieuwe regel.

| Screenshot | Wat je ziet |
|---|---|
| `01-invoer-1440.png` | Inkooporder aanmaken, 1440 px: alleen Product, Aantal en Prijs zijn invoer. Regel 2 is "Afwijkend van het artikel". De totalen staan onder Bruto, Netto, Aantal en Totaal: 0,84 pallets (0,40 + 0,19 + 0,25). |
| `02-detail-1440.png` | Dezelfde order in het detail, alleen lezen, met dezelfde kolommen en totalen. |
| `03-invoer-390.png` | Invoer op 390 px: elke regel is een blokje met de veldnamen, en de totalen staan als samenvatting onderaan. |
| `04-detail-390.png` | Het detail op 390 px. |

Het script controleert ook dat Tab de nieuwe regel maakt, dat het totaal 0,84 pallets is, en dat de pagina op 1440 en 390 px niet horizontaal scrolt.

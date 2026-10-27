# Getallen in Nederlandse notatie (oktober 2026)

Komma = decimaal, punt alleen als duizendtalscheiding in groepen van 3. Gemaakt met Playwright tegen agf_smoke; niets opgeslagen.

1. `01-ontvangst-15000-kg.png`: ontvangst, "1560" wordt na Tab "1.560" (= 1.560 doos); "15.000" netto kg = 15.000 kg.
2. `02-ontvangst-fout-15-5.png`: "15.5" is geen getal in het Nederlands; de melding vraagt om een komma, de invoer blijft staan.
3. `03-ontvangst-390px.png`: dezelfde regel op 390 px; de controle toont "= 15.000 doos" ook als het smalle veld de waarde afkapt.

'use client';

import DirectTradeArchive from '../../../components/supply/DirectTradeArchive';

// Feedback 22 november: Direct trade is geen apart menu-onderdeel meer; dit adres toont de klantgebonden aanvoer van vroeger (alleen lezen).
// Nieuwe directe handel is een gewone inkooporder met een ontvangstlocatie van de soort Onderweg.
export default function DirectTradePage() {
    return <DirectTradeArchive />;
}

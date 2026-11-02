'use client';

import SupplyWorkspace from '../../../components/supply/SupplyWorkspace';

// Klantgebonden aanvoer (DirectTrade): vroeger de keuze "Directe handel" op /supply. Gaat rechtstreeks naar de klant, nooit voorraad.
export default function DirectTradePage() {
    return <SupplyWorkspace task="direct-trade" />;
}

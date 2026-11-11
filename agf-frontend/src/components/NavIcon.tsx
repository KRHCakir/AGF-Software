// Eenvoudige lijn-iconen voor het menu, zelf getekend op een raster van 24 bij 24 (geen iconenbibliotheek nodig). Puur decoratief:
// de tekst ernaast is het label, dus aria-hidden. De kleur volgt de tekst (currentColor).
const PATHS: Readonly<Record<string, string>> = {
    // Start: huis.
    start: 'M3 11l9-7 9 7M5 10v10h5v-6h4v6h5V10',
    // Inkoop: vrachtwagen.
    purchase: 'M2 6h11v10H2zM13 9h4l4 4v3h-8M6 19a2 2 0 100-4 2 2 0 000 4zM17 19a2 2 0 100-4 2 2 0 000 4z',
    // Voorraad: gestapelde kratten.
    stock: 'M3 13h8v7H3zM13 13h8v7h-8zM8 4h8v7H8zM3 16.5h8M13 16.5h8M8 7.5h8',
    // Verkoop: winkelwagen.
    sales: 'M2 4h3l2.5 11h11L21 7H6.2M9 20a1.5 1.5 0 100-3 1.5 1.5 0 000 3zM18 20a1.5 1.5 0 100-3 1.5 1.5 0 000 3z',
    // Stamgegevens: boek.
    master: 'M5 4h11a3 3 0 013 3v13H8a3 3 0 01-3-3zM5 17a3 3 0 013-3h11M9 8h6',
    // Instellingen: tandwiel.
    settings: 'M12 15a3 3 0 100-6 3 3 0 000 6zM12 2v3M12 19v3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M2 12h3M19 12h3M4.9 19.1L7 17M17 7l2.1-2.1',
    // Help: vraagteken in een cirkel.
    help: 'M12 21a9 9 0 100-18 9 9 0 000 18zM9.5 9.5a2.5 2.5 0 114 2c-1 .7-1.5 1.2-1.5 2.5M12 17h.01',
    // De schermen van de groepen.
    'purchase-orders': 'M8 4h8v3H8zM6 5H5v16h14V5h-1M8 11h8M8 15h6',
    'purchase-order-new': 'M6 3h9l4 4v14H6zM15 3v4h4M12.5 11v6M9.5 14h6',
    receipts: 'M3 13h5l1.5 3h5L16 13h5v7H3zM12 3v8M8.5 7.5L12 11l3.5-3.5',
    inspection: 'M10.5 17a6.5 6.5 0 100-13 6.5 6.5 0 000 13zM15.5 15.5L21 21M7.5 10.5l2 2 3.5-4',
    'purchase-invoices': 'M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6M9 16h3',
    availability: 'M12 3l9 5-9 5-9-5zM3 13l9 5 9-5M3 17l9 5 9-5',
    partijen: 'M3 12V4h8l10 10-8 8zM7.5 8.5h.01',
    'sales-orders': 'M6 3h12v18H6zM9 8h6M9 12h6M9 16h4',
    'direct-trade': 'M4 8h14M14 4l4 4-4 4M20 16H6M10 12l-4 4 4 4',
    relations: 'M9 11a3.5 3.5 0 100-7 3.5 3.5 0 000 7zM2.5 20a6.5 6.5 0 0113 0M16 4.5a3.5 3.5 0 010 6.5M18 14a5.5 5.5 0 013.5 6',
    articles: 'M12 3l8 4.5v9L12 21l-8-4.5v-9zM4 7.5l8 4.5 8-4.5M12 12v9',
};

export default function NavIcon({ id, className = 'h-5 w-5' }: { id: string; className?: string }) {
    const d = PATHS[id];
    if (!d) return null;
    return (
        <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}
            strokeLinecap="round" strokeLinejoin="round" className={`shrink-0 ${className}`} data-nav-icon={id}>
            <path d={d} />
        </svg>
    );
}

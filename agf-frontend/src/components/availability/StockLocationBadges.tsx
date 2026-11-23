'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import type { AvailabilityStockLocation } from '../../types/availability';

// Feedback 22 november: waar voorraad buiten de eigen loods ligt ("240 · Onderweg naar Griekenland"); telt mee in de voorraad van de rij.
// In alle weergaven van Beschikbaarheid (Verkoop, Compact overzicht, Alle kolommen).
export default function StockLocationBadges({ locations }: { locations: AvailabilityStockLocation[] | undefined }) {
    const { locale } = useTranslation();
    if (!locations || locations.length === 0) return null;
    return (
        <span className="mt-0.5 block space-y-0.5" data-tour="availability-stock-location">
            {locations.map((location) => (
                <span key={location.name} className={`block rounded px-1 text-xs ${location.kind === 'InTransit' ? 'bg-sky-50 text-sky-900' : 'bg-gray-100 text-gray-800'}`}>
                    {formatQuantity(location.quantity, locale)} · {location.name}
                </span>
            ))}
        </span>
    );
}

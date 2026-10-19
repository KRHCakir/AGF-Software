'use client';

import { useTranslation } from '../../../i18n/LanguageContext';

// Placeholder: er is geen tracking-integratie. De pagina zegt dat expliciet en toont geen voorbeeldkaart of -gegevens.
export default function TrackingPage() {
    const { t } = useTranslation();
    return (
        <div className="p-4">
            <h1 className="text-2xl font-bold mb-4">{t('shipment_tracking')}</h1>
            <p role="note" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">{t('placeholder_not_available')}</p>
        </div>
    );
}

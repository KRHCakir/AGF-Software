'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { INVOICE_INDICATOR_LABEL_KEYS, ORDER_STATUS_LABEL_KEYS, labelKey } from '../../lib/purchaseOrderRules';

const STATUS_STYLES: Record<string, string> = {
    Ordered: 'border-blue-300 bg-blue-50 text-blue-900',
    PartiallyReceived: 'border-amber-300 bg-amber-50 text-amber-900',
    Received: 'border-green-300 bg-green-50 text-green-900',
    Rejected: 'border-red-300 bg-red-50 text-red-900',
    Cancelled: 'border-gray-300 bg-gray-100 text-gray-700',
};

const badge = 'inline-flex items-center whitespace-nowrap rounded border px-1.5 py-0.5 text-xs font-medium';

// Weergavestatus van de inkooporder (Besteld, Deels ontvangen, Ontvangen, Afgekeurd, Geannuleerd).
export function OrderStatusBadge({ status }: { status: string }) {
    const { t } = useTranslation();
    const key = labelKey(ORDER_STATUS_LABEL_KEYS, status);
    return <span className={`${badge} ${STATUS_STYLES[status] ?? STATUS_STYLES.Cancelled}`}>{key ? t(key) : status}</span>;
}

// Er is iets anders ontvangen dan besteld (bevestigd met "Klopt dit?").
export function DeviationBadge() {
    const { t } = useTranslation();
    return <span className={`${badge} border-amber-400 bg-amber-50 text-amber-900`} data-tour="po-deviation-badge">{t('po_deviation_badge')}</span>;
}

// Factuur: Geen / Ontvangen / Creditnota verwacht. Een verwachte creditnota valt op; "Geen" blijft rustig.
export function InvoiceIndicatorBadge({ indicator }: { indicator: string }) {
    const { t } = useTranslation();
    const key = labelKey(INVOICE_INDICATOR_LABEL_KEYS, indicator);
    const style = indicator === 'CreditNoteExpected' ? 'border-orange-400 bg-orange-50 text-orange-900'
        : indicator === 'Received' ? 'border-green-300 bg-green-50 text-green-900' : 'border-gray-200 bg-white text-gray-600';
    return <span className={`${badge} ${style}`}>{key ? t(key) : indicator}</span>;
}

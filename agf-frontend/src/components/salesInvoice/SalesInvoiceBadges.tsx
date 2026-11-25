'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { STATUS_LABEL_KEYS } from '../../lib/salesInvoice';
import type { SalesInvoiceStatus } from '../../types/salesInvoice';

const STATUS_CLASS: Record<SalesInvoiceStatus, string> = {
    Concept: 'border-amber-300 bg-amber-50 text-amber-900',
    Final: 'border-green-300 bg-green-50 text-green-900',
    Discarded: 'border-gray-300 bg-gray-100 text-gray-700',
};

// Status van een verkoopfactuur; een concept dat langer dan 7 dagen ligt, krijgt er een waarschuwing bij.
export function SalesInvoiceStatusBadge({ status, stale = false }: { status: SalesInvoiceStatus; stale?: boolean }) {
    const { t } = useTranslation();
    return (
        <span className="inline-flex flex-wrap items-center gap-1">
            <span data-testid="si-status" data-status={status} className={`rounded border px-1.5 py-0.5 text-xs font-semibold ${STATUS_CLASS[status]}`}>{t(STATUS_LABEL_KEYS[status])}</span>
            {stale && <span data-testid="si-stale" className="rounded border border-red-300 bg-red-50 px-1.5 py-0.5 text-xs font-semibold text-red-900">{t('si_stale')}</span>}
        </span>
    );
}

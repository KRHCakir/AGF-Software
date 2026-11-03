'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { unitLabelKey } from '../../lib/units';
import { toApiError } from '../../services/apiClient';
import { getAwaitingStock } from '../../services/salesOrderService';
import { AwaitingStockLine } from '../../types/salesSupply';
import { ErrorState, LoadState } from '../availability/StateViews';
import { primaryButtonClass } from '../supply/SuccessPanel';

// Werklijst "Verkocht, wacht op voorraad": regels die vóór aankomst verkocht zijn (uit verwachte aanvoer) en waarvoor nu goedgekeurde
// voorraad vrij is, vroegste leverdatum bovenaan. Het systeem reserveert nooit zelf: "Reserveren (oudste partij eerst)" opent de regel met
// het voorstel en de gebruiker bevestigt. Ingeklapt als er niets te doen is.
export default function AwaitingStockList({ token, onReserve }: { token: number; onReserve: (orderId: number, lineId: number) => void }) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<AwaitingStockLine[]>>({ status: 'loading' });
    const [retry, setRetry] = useState(0);
    const [open, setOpen] = useState(true);

    useEffect(() => {
        const controller = new AbortController();
        setState((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        getAwaitingStock(controller.signal)
            .then((data) => setState({ status: 'ready', data }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [token, retry]);

    if (state.status === 'loading') return null;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setRetry((n) => n + 1)} />;
    const lines = state.data;
    const q = (value: number) => formatQuantity(value, locale);

    return (
        <section aria-labelledby="awaiting-stock-title" className={`rounded-md border ${lines.length > 0 ? 'border-amber-300 bg-amber-50' : 'border-gray-200 bg-white'}`} data-tour="sales-awaiting-stock">
            <h2 id="awaiting-stock-title" className="flex items-center justify-between gap-2 px-3 py-2 text-sm font-semibold text-gray-900">
                <span>{t('sales_awaiting_title', { count: String(lines.length) })}</span>
                {lines.length > 0 && (
                    <button type="button" className="text-xs font-medium text-blue-800 underline underline-offset-2" aria-expanded={open} aria-controls="awaiting-stock-list" onClick={() => setOpen((value) => !value)}>
                        {open ? t('sales_breakdown_hide') : t('sales_awaiting_show')}
                    </button>
                )}
            </h2>
            {lines.length === 0 && <p className="px-3 pb-2 text-xs text-gray-600">{t('sales_awaiting_empty')}</p>}
            {lines.length > 0 && open && (
                <ul id="awaiting-stock-list" className="divide-y divide-amber-200 border-t border-amber-200">
                    {lines.map((line) => (
                        <li key={line.salesOrderLineId} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm" data-awaiting-line={line.salesOrderLineId}>
                            <div className="min-w-0 flex-1">
                                <p className="font-medium text-gray-900">{line.orderNumber} · {line.customerName ?? t('availability_unknown')}
                                    {line.customerReference && <span className="ml-1 text-xs font-normal text-gray-600">{t('sales_reference_short')} {line.customerReference}</span>}</p>
                                <p className="text-xs text-gray-700">
                                    {line.productName} · {line.variantName} · {t('sales_delivery_on', { date: formatCalendarDate(line.requestedDeliveryDate, locale) })}
                                </p>
                                <p className="text-xs text-gray-900">
                                    {t('sales_awaiting_line', { reservable: q(line.reservableNow), open: q(line.unreservedQuantity), unit: t(unitLabelKey(line.unit)).toLowerCase() })}
                                </p>
                            </div>
                            <button type="button" className={primaryButtonClass} data-tour="sales-awaiting-reserve" data-tour-key={line.salesOrderLineId}
                                onClick={() => onReserve(line.salesOrderId, line.salesOrderLineId)}>
                                {t('sales_fifo_start')}
                            </button>
                        </li>
                    ))}
                </ul>
            )}
        </section>
    );
}

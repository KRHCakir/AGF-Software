'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { toApiError } from '../../services/apiClient';
import { getSalesOrderAuditEvents } from '../../services/salesOrderService';
import { SalesOrderAuditEvent } from '../../types/salesOrder';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';

// Vertaalde namen per gebeurtenistype; een onbekend type (nieuwe backendgebeurtenis) blijft zichtbaar onder zijn eigen naam.
const EVENT_KEYS: Record<string, string> = {
    SalesOrderCancelled: 'sales_event_cancelled',
    SalesOrderDispatched: 'sales_event_dispatched',
    SalesOrderDispatchCorrected: 'sales_event_dispatch_corrected',
    DirectTradeAllocated: 'sales_event_allocated',
    DirectTradeReleased: 'sales_event_allocation_released',
    DirectTradeShipped: 'sales_event_direct_trade_shipped',
};

// Orderhistorie uit GET /api/sales-orders/{id}/audit-events (append-only, alleen lezen).
export default function OrderHistory({ orderId, token }: { orderId: number; token: number }) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<SalesOrderAuditEvent[]>>({ status: 'loading' });
    const [retry, setRetry] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getSalesOrderAuditEvents(orderId, controller.signal)
            .then((events) => setState({ status: 'ready', data: events }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [orderId, token, retry]);

    if (state.status === 'loading') return <LoadingRows rows={2} label={t('sales_history_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setRetry((n) => n + 1)} />;
    if (state.data.length === 0) return <p className="text-sm text-gray-700">{t('sales_history_empty')}</p>;
    return (
        <ol className="space-y-1 text-sm">
            {state.data.map((event) => (
                <li key={event.id} className="rounded border border-gray-200 bg-white px-2 py-1.5">
                    <span className="font-medium text-gray-900">{EVENT_KEYS[event.eventType] ? t(EVENT_KEYS[event.eventType]) : event.eventType}</span>
                    <span className="ml-2 text-xs text-gray-600">{formatDateTime(event.occurredAt, locale)}{event.actorReference && <> · {t('inspection_event_by', { actor: event.actorReference })}</>}</span>
                    {event.reason && <span className="block text-xs italic text-gray-700">“{event.reason}”</span>}
                </li>
            ))}
        </ol>
    );
}

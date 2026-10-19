'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { toApiError } from '../../services/apiClient';
import { listSalesDispatches } from '../../services/salesOrderService';
import { SalesDispatch } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';

// Verzendingen van een order uit GET /api/sales-orders/{id}/dispatches (append-only, alleen lezen).
export default function DispatchList({ orderId, token }: { orderId: number; token: number }) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<SalesDispatch[]>>({ status: 'loading' });
    const [retry, setRetry] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        listSalesDispatches(orderId, controller.signal)
            .then((dispatches) => setState({ status: 'ready', data: dispatches }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [orderId, token, retry]);

    if (state.status === 'loading') return <LoadingRows rows={2} label={t('sales_dispatches_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setRetry((n) => n + 1)} />;
    if (state.data.length === 0) return <p className="text-sm text-gray-700">{t('sales_dispatches_empty')}</p>;
    return (
        <ol className="space-y-2 text-sm">
            {state.data.map((dispatch) => (
                <li key={dispatch.id} className="rounded border border-gray-200 bg-white px-3 py-2">
                    <p className="font-medium text-gray-900">
                        {t('sales_dispatch_heading', { id: String(dispatch.id) })}{dispatch.reference && <> · {dispatch.reference}</>}
                        <span className="ml-2 text-xs font-normal text-gray-600">{formatDateTime(dispatch.dispatchedAt, locale)} · {t('inspection_event_by', { actor: dispatch.actorReference })}</span>
                    </p>
                    <ul className="mt-1 space-y-0.5 text-xs text-gray-800">
                        {dispatch.lines.map((line) => (
                            <li key={line.id} className="flex flex-wrap items-center gap-2">
                                <span className="font-mono">{line.palletReference ?? t('availability_unknown')}</span>
                                <span>{line.lotNumber ?? t('availability_unknown')}</span>
                                <span className="text-gray-700">{line.productName} · {line.productVariantName}</span>
                                <span className="ml-auto font-medium">{formatQuantity(line.quantity, locale)}</span>
                                <UnitBadge unit={line.unit} />
                                <span>{formatKg(line.netWeightKg, locale)}</span>
                            </li>
                        ))}
                    </ul>
                </li>
            ))}
        </ol>
    );
}

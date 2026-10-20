'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { formatDateOnly } from '../../lib/calendarDate';
import { DeliveryHistory } from './DeliveryHistory';
import { activeConfirmation } from '../../lib/deliveryState';
import { DeliveredBadge, ShippedNotConfirmedBadge } from './ReservationBadges';
import { toApiError } from '../../services/apiClient';
import { listSalesDispatches } from '../../services/salesOrderService';
import { SalesDispatch } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Verzendingen van een order uit GET /api/sales-orders/{id}/dispatches (append-only, alleen lezen).
export default function DispatchList({ orderId, token, onDispatches, canCorrect, correctingId, onCorrect, canConfirm = false, confirmingId = null, onConfirmDelivery, canRetract = false, retractingId = null, onRetractDelivery }: {
    orderId: number;
    token: number;
    // Geeft de geladen verzendingen door, zodat het detail een correctiepaneel kan openen (ook voor een onafgerond commando).
    onDispatches?: (dispatches: SalesDispatch[]) => void;
    canCorrect?: boolean;
    correctingId?: number | null;
    onCorrect?: (dispatch: SalesDispatch) => void;
    canConfirm?: boolean;
    confirmingId?: number | null;
    onConfirmDelivery?: (dispatch: SalesDispatch) => void;
    canRetract?: boolean;
    retractingId?: number | null;
    onRetractDelivery?: (dispatch: SalesDispatch) => void;
}) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<SalesDispatch[]>>({ status: 'loading' });
    const [retry, setRetry] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        listSalesDispatches(orderId, controller.signal)
            .then((dispatches) => {
                setState({ status: 'ready', data: dispatches });
                onDispatches?.(dispatches);
            })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [orderId, token, retry, onDispatches]);

    if (state.status === 'loading') return <LoadingRows rows={2} label={t('sales_dispatches_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setRetry((n) => n + 1)} />;
    if (state.data.length === 0) return <p className="text-sm text-gray-700">{t('sales_dispatches_empty')}</p>;
    return (
        <ol className="space-y-2 text-sm">
            {state.data.map((dispatch) => {
                const active = activeConfirmation(dispatch.deliveryHistory);
                return (
                <li key={dispatch.id} className="rounded border border-gray-200 bg-white px-3 py-2">
                    <p className="flex flex-wrap items-center gap-2 font-medium text-gray-900">
                        <span>{t('sales_dispatch_heading', { id: String(dispatch.id) })}{dispatch.reference && <> · {dispatch.reference}</>}</span>
                        <span className="text-xs font-normal text-gray-600">{formatDateTime(dispatch.dispatchedAt, locale)} · {t('inspection_event_by', { actor: dispatch.actorReference })}</span>
                        {dispatch.correction !== null && (
                            <span className="inline-flex items-center gap-1 rounded border border-amber-300 bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-900">
                                <span aria-hidden="true">↺</span>{t('sales_dispatch_corrected')}
                            </span>
                        )}
                        {dispatch.correction === null && (active
                            ? <DeliveredBadge date={formatDateOnly(active.deliveredOn, locale)} />
                            : <ShippedNotConfirmedBadge />)}
                        {canConfirm && dispatch.correction === null && active === null && confirmingId !== dispatch.id && onConfirmDelivery && (
                            <button type="button" className={`${secondaryButtonClass} ml-auto border-emerald-300 text-emerald-900 hover:bg-emerald-50`} onClick={() => onConfirmDelivery(dispatch)}>
                                {t('sales_delivery_confirm')}
                            </button>
                        )}
                        {canRetract && active !== null && retractingId !== dispatch.id && onRetractDelivery && (
                            <button type="button" className={`${secondaryButtonClass} ml-auto border-amber-300 text-amber-950 hover:bg-amber-50`} onClick={() => onRetractDelivery(dispatch)}>
                                {t('sales_retract_open')}
                            </button>
                        )}
                        {/* Een actieve afleverbevestiging blokkeert de correctie "goederen hebben het magazijn niet verlaten". */}
                        {canCorrect && dispatch.correction === null && active === null && correctingId !== dispatch.id && onCorrect && (
                            <button type="button" className={`${secondaryButtonClass} ml-auto border-amber-300 text-amber-950 hover:bg-amber-50`} onClick={() => onCorrect(dispatch)}>
                                {t('sales_correct_dispatch')}
                            </button>
                        )}
                    </p>
                    {active !== null && (
                        <p className="mt-1 text-xs text-emerald-900">
                            {t('sales_delivery_recorded', { date: formatDateOnly(active.deliveredOn, locale), actor: active.actorReference, recorded: formatDateTime(active.recordedAt, locale) })}
                            {active.receiptReference && <> · <span className="font-mono">{active.receiptReference}</span></>}
                            {' '}· {t('sales_delivery_correction_blocked')}
                        </p>
                    )}
                    <DeliveryHistory history={dispatch.deliveryHistory} />
                    {dispatch.correction !== null && (
                        <p className="mt-1 text-xs text-amber-950">
                            {t('sales_corrected_notice', { date: formatDateTime(dispatch.correction.correctedAt, locale), actor: dispatch.correction.actorReference })}
                            {' '}<span className="italic">“{dispatch.correction.reason}”</span>
                            {' '}· {t('sales_corrected_restored', { count: String(dispatch.correction.lines.length) })}
                        </p>
                    )}
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
                );
            })}
        </ol>
    );
}

'use client';

import { useCallback, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { blockedBy, cancelKey, correctDispatchKey, dispatchKey, shipDirectTradeKey } from '../../lib/pendingCommands';
import { isDispatchable, isShippableAllocation, isShippedAllocation, orderProgress } from '../../lib/salesOrderRules';
import { SaveResult } from '../../services/supplyService';
import { DirectTradeShipment, SalesDispatch, SalesDispatchCorrection, SalesOrder } from '../../types/salesOrder';
import { secondaryButtonClass } from '../supply/SuccessPanel';
import CancelOrderPanel from './CancelOrderPanel';
import CorrectDispatchPanel from './CorrectDispatchPanel';
import DispatchList from './DispatchList';
import DispatchPanel from './DispatchPanel';
import DirectTradeShipmentList from './DirectTradeShipmentList';
import DirectTradeShipmentPanel from './DirectTradeShipmentPanel';
import OrderHistory from './OrderHistory';
import OrderLinePanel from './OrderLinePanel';
import { usePendingCommands } from './PendingCommandsContext';
import { ExpectedAllocationBadge, OnHoldBadge, OrderStatusBadge } from './ReservationBadges';

// Detail van één verkooporder: kop, voortgang, annuleren (alleen Open) en per regel reserveren of vrijgeven.
export default function SalesOrderDetail({ order, actor, onOrderChanged, onRefresh, refreshing = false, refreshFailed = false }: {
    order: SalesOrder;
    actor: string;
    onOrderChanged: () => Promise<boolean>;
    // Expliciet vernieuwen (alleen lezen). Zonder onRefresh wordt onOrderChanged gebruikt.
    onRefresh?: () => Promise<boolean>;
    refreshing?: boolean;
    refreshFailed?: boolean;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [cancelling, setCancelling] = useState(false);
    const [showHistory, setShowHistory] = useState(false);
    const [historyToken, setHistoryToken] = useState(0);
    const [cancelNotice, setCancelNotice] = useState(false);
    // Telt alleen op na een geslaagde, bewuste herlaadactie vanuit het annuleringspaneel: dan vervalt de lokale
    // conflicttoestand en begint een nieuwe beslissing. Een onafgerond commando staat in de opslag en komt gewoon terug.
    const [decisionToken, setDecisionToken] = useState(0);
    const progress = orderProgress(order);
    const readOnly = order.status !== 'Open';
    const cancelled = order.status === 'Cancelled';
    const pendingCancel = commands[cancelKey(order.id)] !== undefined;
    const cancelBlocker = blockedBy(commands, { kind: 'cancel-order', orderId: order.id });
    // Een onafgeronde annulering blijft zichtbaar, ook als de order intussen Cancelled is (de server kan het commando
    // al hebben verwerkt terwijl het antwoord verloren ging): alleen de identieke herhaling rondt haar af.
    const showCancel = pendingCancel || (!readOnly && cancelling);
    // Verzenden: zelfde regels. Een onafgeronde verzending blijft zichtbaar, ook als een verse orderrespons de gekozen
    // reserveringen al als Consumed toont (verwerkt, antwoord verloren): alleen de identieke herhaling rondt haar af.
    const [dispatching, setDispatching] = useState(false);
    const [dispatchNotice, setDispatchNotice] = useState<SaveResult<SalesDispatch> | null>(null);
    const [dispatchToken, setDispatchToken] = useState(0);
    const [dispatchDecisionToken, setDispatchDecisionToken] = useState(0);
    const pendingDispatch = commands[dispatchKey(order.id)] !== undefined;
    const dispatchBlocker = blockedBy(commands, { kind: 'dispatch', orderId: order.id });
    const showDispatch = pendingDispatch || (!readOnly && dispatching);
    // Alleen fysieke reserveringen zijn te verzenden; een toewijzing van verwachte klantgebonden aanvoer nooit vanuit het magazijn.
    const hasDispatchable = order.lines.some((line) => line.reservations.some((reservation) => isDispatchable(reservation)));
    // Twee aparte begrippen: verzendhistorie (Consumed of Corrected) wordt getoond; alleen nog niet gecorrigeerd verbruik
    // (Consumed) blokkeert annuleren. Een order waarvan alle verzendingen gecorrigeerd zijn, kan dus weer geannuleerd worden.
    // Magazijnverzendhistorie: alleen fysieke reserveringen. DirectTrade-verzendingen hebben een eigen lijst.
    const hasDispatchHistory = order.lines.some((line) => line.reservations.some((reservation) => reservation.source !== 'DirectTrade' && (reservation.status === 'Consumed' || reservation.status === 'Corrected')));
    // DirectTrade-verzending: eigen paneel en lijst; een onafgeronde verzending blijft zichtbaar, ook als de toewijzingen al verzonden zijn.
    const [shippingDt, setShippingDt] = useState(false);
    const [dtNotice, setDtNotice] = useState<SaveResult<DirectTradeShipment> | null>(null);
    const [dtToken, setDtToken] = useState(0);
    const [dtDecisionToken, setDtDecisionToken] = useState(0);
    const pendingDtShipment = commands[shipDirectTradeKey(order.id)] !== undefined;
    const dtBlocker = blockedBy(commands, { kind: 'ship-direct-trade', orderId: order.id });
    const showDtShipment = pendingDtShipment || (!readOnly && shippingDt);
    const hasShippableAllocation = order.lines.some((line) => line.reservations.some((reservation) => isShippableAllocation(reservation)));
    const hasDtShipments = order.lines.some((line) => line.reservations.some((reservation) => isShippedAllocation(reservation)));
    const hasUncorrectedConsumption = order.lines.some((line) => line.reservations.some((reservation) => reservation.status === 'Consumed'));
    // Verzendcorrectie: paneel per verzending; een onafgerond correctiecommando opent het paneel ook na verversen.
    const [dispatches, setDispatches] = useState<SalesDispatch[]>([]);
    const [correctingId, setCorrectingId] = useState<number | null>(null);
    const [correctionNotice, setCorrectionNotice] = useState<SaveResult<SalesDispatchCorrection> | null>(null);
    const [correctionDecisionToken, setCorrectionDecisionToken] = useState(0);
    const onDispatchesLoaded = useCallback((loaded: SalesDispatch[]) => setDispatches(loaded), []);
    const pendingCorrectionDispatch = dispatches.find((dispatch) => commands[correctDispatchKey(order.id, dispatch.id)] !== undefined) ?? null;
    const correctingDispatch = pendingCorrectionDispatch ?? dispatches.find((dispatch) => dispatch.id === correctingId) ?? null;
    const correctionBlocker = blockedBy(commands, { kind: 'correct-dispatch', orderId: order.id });
    const canStartCorrection = !readOnly && (correctionBlocker === null || correctionBlocker.kind === 'correct-dispatch');

    const onCancelled = async () => {
        setCancelling(false);
        setCancelNotice(true);
        setHistoryToken((n) => n + 1);
        await onOrderChanged();
    };

    const reloadForNewDecision = async () => {
        if (await onOrderChanged()) {
            setDecisionToken((n) => n + 1);
        }
    };

    const onDispatched = async (saved: SaveResult<SalesDispatch>) => {
        setDispatching(false);
        setDispatchNotice(saved);
        setHistoryToken((n) => n + 1);
        setDispatchToken((n) => n + 1);
        await onOrderChanged();
    };

    const reloadForNewDispatchDecision = async () => {
        if (await onOrderChanged()) {
            setDispatchDecisionToken((n) => n + 1);
        }
    };

    const onCorrected = async (saved: SaveResult<SalesDispatchCorrection>) => {
        setCorrectingId(null);
        setCorrectionNotice(saved);
        setHistoryToken((n) => n + 1);
        setDispatchToken((n) => n + 1);
        await onOrderChanged();
    };

    const onDtShipped = async (saved: SaveResult<DirectTradeShipment>) => {
        setShippingDt(false);
        setDtNotice(saved);
        setHistoryToken((n) => n + 1);
        setDtToken((n) => n + 1);
        await onOrderChanged();
    };

    const reloadForNewDtDecision = async () => {
        if (await onOrderChanged()) {
            setDtDecisionToken((n) => n + 1);
        }
    };

    const reloadForNewCorrectionDecision = async () => {
        if (await onOrderChanged()) {
            setDispatchToken((n) => n + 1);
            setCorrectionDecisionToken((n) => n + 1);
        }
    };

    // Vernieuwen leest de order, de verzendingen en de historie opnieuw. Beslissingstellers veranderen niet, dus open
    // formulieren blijven gemonteerd; bevroren commando's staan in de opslag en worden hier niet aangeraakt.
    const [localRefreshing, setLocalRefreshing] = useState(false);
    const [localRefreshFailed, setLocalRefreshFailed] = useState(false);
    const busy = refreshing || localRefreshing;
    const refresh = async () => {
        if (busy) return;
        setLocalRefreshing(true);
        setLocalRefreshFailed(false);
        try {
            const ok = await (onRefresh ?? onOrderChanged)();
            if (ok) {
                setDispatchToken((n) => n + 1);
                setDtToken((n) => n + 1);
                setHistoryToken((n) => n + 1);
            } else {
                setLocalRefreshFailed(true);
            }
        } finally {
            setLocalRefreshing(false);
        }
    };
    const showRefreshFailed = refreshFailed || localRefreshFailed;

    return (
        <div className="space-y-3">
            <header className="rounded-md border border-gray-300 bg-white px-4 py-3">
                <div className="flex flex-wrap items-center gap-3">
                    <h2 className="text-lg font-semibold text-gray-900">{t('sales_order_heading', { id: String(order.id) })} · {order.customerName ?? t('availability_unknown')}</h2>
                    <OrderStatusBadge status={order.status} />
                    {progress.onHoldCount > 0 && <OnHoldBadge />}
                    {progress.allocatedLines > 0 && <ExpectedAllocationBadge />}
                    <div className="ml-auto flex flex-wrap gap-2">
                        <button type="button" className={secondaryButtonClass} onClick={() => void refresh()} disabled={busy} aria-busy={busy}>
                            <span aria-hidden="true">↻ </span>{busy ? t('sales_refreshing') : t('sales_refresh')}
                        </button>
                        <button type="button" className={secondaryButtonClass} onClick={() => setShowHistory((value) => !value)} aria-expanded={showHistory}>
                            {showHistory ? t('sales_history_hide') : t('sales_history')}
                        </button>
                        {!readOnly && !showDispatch && (
                            <button type="button" className={`${secondaryButtonClass} border-emerald-300 text-emerald-900 hover:bg-emerald-50`} onClick={() => { setDispatchNotice(null); setDispatching(true); }}
                                disabled={!hasDispatchable || (dispatchBlocker !== null && dispatchBlocker.kind !== 'dispatch')}>
                                {t('sales_dispatch_order')}
                            </button>
                        )}
                        {!readOnly && !showDtShipment && (
                            <button type="button" className={`${secondaryButtonClass} border-violet-300 text-violet-900 hover:bg-violet-50`} onClick={() => { setDtNotice(null); setShippingDt(true); }}
                                disabled={!hasShippableAllocation || (dtBlocker !== null && dtBlocker.kind !== 'ship-direct-trade')}>
                                <span aria-hidden="true">🔒 </span>{t('sales_ship_order')}
                            </button>
                        )}
                        {!readOnly && !showCancel && (
                            <button type="button" className={`${secondaryButtonClass} border-red-300 text-red-800 hover:bg-red-50`} onClick={() => { setCancelNotice(false); setCancelling(true); }}
                                disabled={hasUncorrectedConsumption || (cancelBlocker !== null && cancelBlocker.kind !== 'cancel-order')}
                                title={hasUncorrectedConsumption ? t('sales_cancel_after_dispatch') : undefined}>
                                {t('sales_cancel_order')}
                            </button>
                        )}
                    </div>
                </div>
                {showRefreshFailed && (
                    <p role="alert" className="mt-2 rounded border border-red-300 bg-red-50 px-3 py-1.5 text-sm text-red-900">{t('sales_refresh_failed')}</p>
                )}
                {cancelled && (
                    <div role="status" className="mt-2 rounded border border-gray-400 bg-gray-100 px-3 py-2 text-sm text-gray-900">
                        <p className="font-medium">{t('sales_cancelled_notice', { date: order.cancelledAt ? formatDateTime(order.cancelledAt, locale) : t('availability_unknown'), actor: order.cancelledBy ?? t('availability_unknown') })}</p>
                        <p className="mt-1">{t('sales_cancel_reason')}: <span className="italic">“{order.cancelReason ?? t('availability_unknown')}”</span></p>
                        {cancelNotice && <p className="mt-1 text-green-900">{t('sales_notice_cancelled')}</p>}
                    </div>
                )}
                <dl className="mt-2 grid grid-cols-4 gap-x-6 gap-y-1 text-sm">
                    <div>
                        <dt className="text-xs text-gray-600">{t('sales_delivery_date')}</dt>
                        <dd className="font-medium text-gray-900">{formatCalendarDate(order.requestedDeliveryDate, locale)}</dd>
                    </div>
                    <div>
                        <dt className="text-xs text-gray-600">{t('sales_customer_reference')}</dt>
                        <dd className="font-medium text-gray-900">{order.customerReference ?? t('sales_no_reference')}</dd>
                    </div>
                    <div>
                        <dt className="text-xs text-gray-600">{t('sales_col_progress')}</dt>
                        <dd className={`font-medium ${progress.fulfilled ? 'text-emerald-800' : progress.ready ? 'text-green-800' : progress.complete && progress.onHoldCount > 0 ? 'text-red-800' : progress.complete ? 'text-violet-900' : 'text-gray-900'}`}>
                            {progress.fulfilled
                                ? t('sales_order_fulfilled')
                                : <>{t('sales_lines_complete', { done: String(progress.linesComplete), total: String(progress.linesTotal) })}
                                    {progress.linesFulfilled > 0 && <span className="block text-xs">{t('sales_lines_fulfilled', { done: String(progress.linesFulfilled), total: String(progress.linesTotal) })}</span>}</>}
                            {progress.complete && !progress.ready && !progress.fulfilled && progress.onHoldCount > 0 && <span className="block text-xs">{t('sales_order_complete_on_hold')}</span>}
                            {progress.complete && !progress.ready && !progress.fulfilled && progress.onHoldCount === 0 && progress.allocatedLines > 0 && (
                                <span className="block text-xs text-violet-900">{t('sales_order_complete_expected', { count: String(progress.allocatedLines) })}</span>
                            )}
                        </dd>
                    </div>
                    <div>
                        <dt className="text-xs text-gray-600">{t('sales_created')}</dt>
                        <dd className="text-gray-900">{formatDateTime(order.createdAt, locale)} · {order.actorReference}</dd>
                    </div>
                </dl>
                {progress.onHoldCount > 0 && (
                    <p role="alert" className="mt-2 rounded border border-red-300 bg-red-50 px-2 py-1 text-sm text-red-900">
                        {t('sales_order_on_hold_notice', { count: String(progress.onHoldCount) })}
                    </p>
                )}
                {progress.allocatedLines > 0 && (
                    <p role="status" className="mt-2 rounded border border-violet-300 bg-violet-50 px-2 py-1 text-sm text-violet-950">
                        <span aria-hidden="true">🔒 </span>{t('sales_order_allocated_notice', { count: String(progress.allocatedLines) })}
                    </p>
                )}
                {readOnly && !cancelled && <p className="mt-2 text-sm text-gray-700">{t('sales_order_not_open')}</p>}
                {showHistory && (
                    <div className="mt-3 border-t border-gray-200 pt-2">
                        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-gray-600">{t('sales_history')}</h3>
                        <OrderHistory orderId={order.id} token={historyToken} />
                    </div>
                )}
            </header>

            {dispatchNotice && (
                <div role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">
                    <p className="font-medium"><span aria-hidden="true">✓ </span>{t('sales_notice_dispatched', { id: String(dispatchNotice.value.id), count: String(dispatchNotice.value.lines.length) })}</p>
                    {dispatchNotice.replayed && <p className="mt-1">{t('supply_saved_before')}</p>}
                </div>
            )}

            {showDispatch && (
                <DispatchPanel key={`${order.id}:${dispatchDecisionToken}`} order={order} actor={actor} onDispatched={onDispatched}
                    onReload={() => void reloadForNewDispatchDecision()} onClose={() => setDispatching(false)} />
            )}

            {dtNotice && (
                <div role="status" className="rounded border border-violet-300 bg-violet-50 px-3 py-2 text-sm text-violet-950">
                    <p className="font-medium"><span aria-hidden="true">🚚 </span>{t('sales_notice_dt_shipped', { id: String(dtNotice.value.id), count: String(dtNotice.value.lines.length) })}</p>
                    {dtNotice.replayed && <p className="mt-1">{t('supply_saved_before')}</p>}
                    <p className="mt-1">{t('sales_notice_dt_shipped_next')}</p>
                </div>
            )}

            {showDtShipment && (
                <DirectTradeShipmentPanel key={`${order.id}:${dtDecisionToken}`} order={order} actor={actor} onShipped={onDtShipped}
                    onReload={() => void reloadForNewDtDecision()} onClose={() => setShippingDt(false)} />
            )}

            {(hasDtShipments || dtNotice) && (
                <section aria-labelledby={`dt-shipments-${order.id}`} className="rounded-md border border-violet-300 bg-white px-4 py-3">
                    <h3 id={`dt-shipments-${order.id}`} className="mb-2 text-xs font-semibold uppercase tracking-wide text-violet-900">{t('sales_ship_list_title')}</h3>
                    <DirectTradeShipmentList orderId={order.id} token={dtToken} />
                </section>
            )}

            {showCancel && (
                <CancelOrderPanel key={`${order.id}:${decisionToken}`} order={order} actor={actor} onCancelled={onCancelled}
                    onReload={() => void reloadForNewDecision()} onClose={() => setCancelling(false)} />
            )}

            {correctionNotice && (
                <div role="status" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                    <p className="font-medium"><span aria-hidden="true">↺ </span>{t('sales_notice_corrected', { id: String(correctionNotice.value.salesDispatchId), count: String(correctionNotice.value.lines.length) })}</p>
                    {correctionNotice.replayed && <p className="mt-1">{t('supply_saved_before')}</p>}
                    <p className="mt-1">{t('sales_notice_corrected_next')}</p>
                </div>
            )}

            {correctingDispatch !== null && (
                <CorrectDispatchPanel key={`${correctingDispatch.id}:${correctionDecisionToken}`} order={order} dispatch={correctingDispatch} actor={actor}
                    onCorrected={onCorrected} onReload={() => void reloadForNewCorrectionDecision()} onClose={() => setCorrectingId(null)} />
            )}

            {(hasDispatchHistory || dispatchNotice || correctionNotice) && (
                <section aria-labelledby={`dispatches-${order.id}`} className="rounded-md border border-gray-300 bg-white px-4 py-3">
                    <h3 id={`dispatches-${order.id}`} className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-600">{t('sales_dispatches_title')}</h3>
                    <DispatchList orderId={order.id} token={dispatchToken} onDispatches={onDispatchesLoaded} canCorrect={canStartCorrection}
                        correctingId={correctingDispatch?.id ?? null} onCorrect={(dispatch) => { setCorrectionNotice(null); setCorrectingId(dispatch.id); }} />
                </section>
            )}

            {order.lines.map((line) => (
                <OrderLinePanel key={line.id} order={order} line={line} actor={actor} readOnly={readOnly} onOrderChanged={onOrderChanged} />
            ))}
        </div>
    );
}

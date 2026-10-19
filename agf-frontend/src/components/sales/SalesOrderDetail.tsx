'use client';

import { useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { blockedBy, cancelKey } from '../../lib/pendingCommands';
import { orderProgress } from '../../lib/salesOrderRules';
import { SalesOrder } from '../../types/salesOrder';
import { secondaryButtonClass } from '../supply/SuccessPanel';
import CancelOrderPanel from './CancelOrderPanel';
import OrderHistory from './OrderHistory';
import OrderLinePanel from './OrderLinePanel';
import { usePendingCommands } from './PendingCommandsContext';
import { OnHoldBadge, OrderStatusBadge } from './ReservationBadges';

// Detail van één verkooporder: kop, voortgang, annuleren (alleen Open) en per regel reserveren of vrijgeven.
export default function SalesOrderDetail({ order, actor, onOrderChanged }: {
    order: SalesOrder;
    actor: string;
    onOrderChanged: () => Promise<boolean>;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [cancelling, setCancelling] = useState(false);
    const [showHistory, setShowHistory] = useState(false);
    const [historyToken, setHistoryToken] = useState(0);
    const [cancelNotice, setCancelNotice] = useState(false);
    const progress = orderProgress(order);
    const readOnly = order.status !== 'Open';
    const cancelled = order.status === 'Cancelled';
    const pendingCancel = commands[cancelKey(order.id)] !== undefined;
    const cancelBlocker = blockedBy(commands, { kind: 'cancel-order', orderId: order.id });
    const showCancel = !readOnly && (cancelling || pendingCancel);

    const onCancelled = async () => {
        setCancelling(false);
        setCancelNotice(true);
        setHistoryToken((n) => n + 1);
        await onOrderChanged();
    };

    return (
        <div className="space-y-3">
            <header className="rounded-md border border-gray-300 bg-white px-4 py-3">
                <div className="flex flex-wrap items-center gap-3">
                    <h2 className="text-lg font-semibold text-gray-900">{t('sales_order_heading', { id: String(order.id) })} · {order.customerName ?? t('availability_unknown')}</h2>
                    <OrderStatusBadge status={order.status} />
                    {progress.onHoldCount > 0 && <OnHoldBadge />}
                    <div className="ml-auto flex flex-wrap gap-2">
                        <button type="button" className={secondaryButtonClass} onClick={() => setShowHistory((value) => !value)} aria-expanded={showHistory}>
                            {showHistory ? t('sales_history_hide') : t('sales_history')}
                        </button>
                        {!readOnly && !showCancel && (
                            <button type="button" className={`${secondaryButtonClass} border-red-300 text-red-800 hover:bg-red-50`} onClick={() => { setCancelNotice(false); setCancelling(true); }}
                                disabled={cancelBlocker !== null && cancelBlocker.kind !== 'cancel-order'}>
                                {t('sales_cancel_order')}
                            </button>
                        )}
                    </div>
                </div>
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
                        <dd className={`font-medium ${progress.ready ? 'text-green-800' : progress.complete ? 'text-red-800' : 'text-gray-900'}`}>
                            {t('sales_lines_complete', { done: String(progress.linesComplete), total: String(progress.linesTotal) })}
                            {progress.complete && !progress.ready && <span className="block text-xs">{t('sales_order_complete_on_hold')}</span>}
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
                {readOnly && !cancelled && <p className="mt-2 text-sm text-gray-700">{t('sales_order_not_open')}</p>}
                {showHistory && (
                    <div className="mt-3 border-t border-gray-200 pt-2">
                        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-gray-600">{t('sales_history')}</h3>
                        <OrderHistory orderId={order.id} token={historyToken} />
                    </div>
                )}
            </header>

            {showCancel && (
                <CancelOrderPanel key={`${order.id}:${order.version}`} order={order} actor={actor} onCancelled={onCancelled}
                    onReload={() => void onOrderChanged()} onClose={() => setCancelling(false)} />
            )}

            {order.lines.map((line) => (
                <OrderLinePanel key={line.id} order={order} line={line} actor={actor} readOnly={readOnly} onOrderChanged={onOrderChanged} />
            ))}
        </div>
    );
}

'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatDate, formatDateTime } from '../../lib/availability';
import { orderProgress } from '../../lib/salesOrderRules';
import { SalesOrder } from '../../types/salesOrder';
import OrderLinePanel from './OrderLinePanel';
import { OnHoldBadge, OrderStatusBadge } from './ReservationBadges';

// Detail van één verkooporder: kop, voortgang en per regel reserveren of vrijgeven. Annuleren volgt in een aparte PR.
export default function SalesOrderDetail({ order, actor, onOrderChanged }: {
    order: SalesOrder;
    actor: string;
    onOrderChanged: () => Promise<boolean>;
}) {
    const { t, locale } = useTranslation();
    const progress = orderProgress(order);
    const readOnly = order.status !== 'Open';

    return (
        <div className="space-y-3">
            <header className="rounded-md border border-gray-300 bg-white px-4 py-3">
                <div className="flex flex-wrap items-center gap-3">
                    <h2 className="text-lg font-semibold text-gray-900">{t('sales_order_heading', { id: String(order.id) })} · {order.customerName ?? t('availability_unknown')}</h2>
                    <OrderStatusBadge status={order.status} />
                    {progress.onHoldCount > 0 && <OnHoldBadge />}
                </div>
                <dl className="mt-2 grid grid-cols-4 gap-x-6 gap-y-1 text-sm">
                    <div>
                        <dt className="text-xs text-gray-600">{t('sales_delivery_date')}</dt>
                        <dd className="font-medium text-gray-900">{formatDate(order.requestedDeliveryDate, locale)}</dd>
                    </div>
                    <div>
                        <dt className="text-xs text-gray-600">{t('sales_customer_reference')}</dt>
                        <dd className="font-medium text-gray-900">{order.customerReference ?? t('sales_no_reference')}</dd>
                    </div>
                    <div>
                        <dt className="text-xs text-gray-600">{t('sales_col_progress')}</dt>
                        <dd className={`font-medium ${progress.complete ? 'text-green-800' : 'text-gray-900'}`}>
                            {t('sales_lines_complete', { done: String(progress.linesComplete), total: String(progress.linesTotal) })}
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
                {readOnly && <p className="mt-2 text-sm text-gray-700">{t('sales_order_not_open')}</p>}
            </header>

            {order.lines.map((line) => (
                <OrderLinePanel key={line.id} order={order} line={line} actor={actor} readOnly={readOnly} onOrderChanged={onOrderChanged} />
            ))}
        </div>
    );
}

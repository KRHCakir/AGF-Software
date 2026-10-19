'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { orderProgress } from '../../lib/salesOrderRules';
import { SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import { OnHoldBadge, OrderStatusBadge } from './ReservationBadges';

const th = 'px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';

// Overzicht van verkooporders: klant, gevraagde leverdatum, referentie en reserveringsvoortgang per regel.
export default function SalesOrderList({ orders, selectedId, onSelect }: {
    orders: SalesOrder[];
    selectedId: number | null;
    onSelect: (id: number) => void;
}) {
    const { t, locale } = useTranslation();
    return (
        <div className="overflow-x-auto rounded-md border border-gray-300 bg-white">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
                <caption className="sr-only">{t('sales_orders_title')}</caption>
                <thead className="bg-gray-50">
                    <tr>
                        <th scope="col" className={th}>{t('sales_col_order')}</th>
                        <th scope="col" className={th}>{t('sales_col_delivery')}</th>
                        <th scope="col" className={th}>{t('sales_col_progress')}</th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                    {orders.map((order) => {
                        const progress = orderProgress(order);
                        const selected = order.id === selectedId;
                        return (
                            <tr key={order.id} className={selected ? 'bg-blue-50' : 'hover:bg-blue-50/40'} aria-selected={selected}>
                                <td className="px-2 py-1.5 align-top">
                                    <button type="button" onClick={() => onSelect(order.id)}
                                        className="text-left font-medium text-blue-800 underline-offset-2 hover:underline focus:outline-none focus:ring-2 focus:ring-blue-500"
                                        aria-label={t('sales_open_order', { id: String(order.id), customer: order.customerName ?? '' })}
                                        aria-current={selected ? 'true' : undefined}>
                                        #{order.id} · {order.customerName ?? t('availability_unknown')}
                                    </button>
                                    <span className="block text-xs text-gray-600">
                                        {order.customerReference ? `${t('sales_reference_short')} ${order.customerReference}` : t('sales_no_reference')}
                                    </span>
                                    {order.status !== 'Open' && (
                                        <span className="mt-1 block">
                                            <OrderStatusBadge status={order.status} />
                                            {order.cancelReason && <span className="ml-1 text-xs italic text-gray-600">“{order.cancelReason}”</span>}
                                        </span>
                                    )}
                                </td>
                                <td className="whitespace-nowrap px-2 py-1.5 align-top">{formatCalendarDate(order.requestedDeliveryDate, locale)}</td>
                                <td className="px-2 py-1.5 align-top">
                                    {/* Groen alleen als alles gereserveerd is én niets on hold staat; volledig gereserveerd met on hold is een waarschuwing. */}
                                    <span className={`block text-xs font-medium ${progress.fulfilled ? 'text-emerald-800' : progress.ready ? 'text-green-800' : progress.complete ? 'text-red-800' : 'text-gray-800'}`}>
                                        {progress.fulfilled
                                            ? t('sales_order_fulfilled')
                                            : <>{t('sales_lines_complete', { done: String(progress.linesComplete), total: String(progress.linesTotal) })}
                                                {progress.partiallyDispatched && <> · {t('sales_lines_fulfilled', { done: String(progress.linesFulfilled), total: String(progress.linesTotal) })}</>}
                                                {progress.complete && !progress.ready && <> · {t('sales_on_hold')}</>}</>}
                                    </span>
                                    <ul className="mt-0.5 space-y-0.5 text-xs text-gray-700">
                                        {order.lines.map((line) => (
                                            <li key={line.id} className="flex flex-wrap items-center gap-1">
                                                <span className="truncate">{line.productName} · {line.productVariantName}</span>
                                                <span className="font-mono">{formatQuantity(line.reservedQuantity + line.dispatchedQuantity, locale)}/{formatQuantity(line.quantity, locale)}</span>
                                                {line.dispatchedQuantity > 0 && <span className="text-emerald-900">({t('sales_list_dispatched', { quantity: formatQuantity(line.dispatchedQuantity, locale) })})</span>}
                                                <UnitBadge unit={line.unit} />
                                            </li>
                                        ))}
                                    </ul>
                                    {progress.onHoldCount > 0 && <span className="mt-1 block"><OnHoldBadge /></span>}
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </div>
    );
}

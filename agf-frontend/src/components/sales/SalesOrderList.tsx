'use client';

import { useEffect, useRef } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { orderProgress } from '../../lib/salesOrderRules';
import { SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import { OnHoldBadge, OrderStatusBadge } from './ReservationBadges';

// Kopcellen blijven bovenaan staan binnen het eigen scrollgebied van de lijst (sticky per cel; werkt ook in tabellen).
const th = 'sticky top-0 z-10 bg-gray-50 px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-gray-600 shadow-[inset_0_-1px_0_rgb(229,231,235)]';

// Overzicht van verkooporders: klant, gevraagde leverdatum, referentie en reserveringsvoortgang per regel.
// De lijst is haar eigen scrollgebied (hoogte bepaald door de pagina via className); de gekozen order wordt in beeld gehouden
// als die buiten het zichtbare deel valt, bijvoorbeeld na openen via een URL-link of een onafgerond commando. Alleen de lijst
// scrolt daarbij, nooit de pagina.
export default function SalesOrderList({ orders, selectedId, onSelect, className = '' }: {
    orders: SalesOrder[];
    selectedId: number | null;
    onSelect: (id: number) => void;
    className?: string;
}) {
    const { t, locale } = useTranslation();
    const scrollRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        const container = scrollRef.current;
        if (!container || selectedId === null) return;
        const row = container.querySelector<HTMLElement>(`tr[data-order-id="${selectedId}"]`);
        if (!row) return;
        const header = container.querySelector<HTMLElement>('thead');
        const headerHeight = header?.getBoundingClientRect().height ?? 0;
        const box = container.getBoundingClientRect();
        const rowBox = row.getBoundingClientRect();
        if (rowBox.top < box.top + headerHeight) {
            container.scrollTop -= box.top + headerHeight - rowBox.top + 8;
        } else if (rowBox.bottom > box.bottom) {
            container.scrollTop += rowBox.bottom - box.bottom + 8;
        }
    }, [selectedId, orders]);

    return (
        <div ref={scrollRef} className={`overflow-auto overscroll-contain rounded-md border border-gray-300 bg-white ${className}`} data-testid="sales-order-list-scroll">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
                <caption className="sr-only">{t('sales_orders_title')}</caption>
                <thead>
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
                            <tr key={order.id} data-order-id={order.id} aria-selected={selected}
                                className={selected ? 'bg-blue-100 shadow-[inset_4px_0_0_rgb(29,78,216)]' : 'hover:bg-blue-50/60'}>
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
                                    <span className={`block text-xs font-medium ${progress.fulfilled ? 'text-emerald-800' : progress.ready ? 'text-green-800' : progress.complete && progress.onHoldCount > 0 ? 'text-red-800' : progress.complete ? 'text-violet-900' : 'text-gray-800'}`}>
                                        {progress.fulfilled
                                            ? (order.delivered ? t('sales_order_delivered') : t('sales_order_shipped_not_confirmed'))
                                            : <>{t('sales_lines_complete', { done: String(progress.linesComplete), total: String(progress.linesTotal) })}
                                                {progress.partiallyDispatched && <> · {t('sales_lines_fulfilled', { done: String(progress.linesFulfilled), total: String(progress.linesTotal) })}</>}
                                                {progress.complete && !progress.ready && progress.onHoldCount > 0 && <> · {t('sales_on_hold')}</>}
                                                {progress.allocatedLines > 0 && <> · {t('sales_list_allocated', { count: String(progress.allocatedLines) })}</>}</>}
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

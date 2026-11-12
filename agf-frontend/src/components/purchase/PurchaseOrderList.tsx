'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatCalendarDate, formatDateOnly } from '../../lib/calendarDate';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder';
import Link from 'next/link';
import { DeviationBadge, DocumentStatusBadge, InvoiceIndicatorBadge, OrderStatusBadge, BillingStatusBadge } from './PurchaseOrderBadges';
import { listEditable } from '../../lib/purchaseOrderEdit';
import { listRowClass, rowClick } from '../../lib/listRows';
import { orderNumbers } from '../../lib/purchaseOrderRules';
import OrderNumberText from './OrderNumberText';

// Lijst "Inkoopopdrachten": vanaf md een tabel, smaller kaarten met dezelfde gegevens. Het ordernummer van de leverancier is het hoofdnummer
// (een lang nummer afgekapt met …, volledig in het detail), het eigen IO-nummer staat er klein en grijs onder.
export default function PurchaseOrderList({ items, selectedId, onOpen }: {
    items: PurchaseOrderListItem[];
    selectedId: number | null;
    onOpen: (id: number) => void;
}) {
    const { t, locale } = useTranslation();
    const invoices = (item: PurchaseOrderListItem) => item.invoiceNumbers.length === 0 ? '–'
        : item.invoiceNumbers.map((ref) => (ref.kind === 'CreditNote' ? `${t('po_credit_note_short')} ${ref.invoiceNumber}` : ref.invoiceNumber)).join(', ');
    const kg = (item: PurchaseOrderListItem) => (item.expectedNetWeightKg === null ? '–' : formatKg(item.expectedNetWeightKg, locale));
    const expected = (item: PurchaseOrderListItem) => (item.expectedAt ? formatCalendarDate(item.expectedAt, locale) : '–');
    const rejected = (item: PurchaseOrderListItem) => item.rejectedQuantity > 0 && (
        <span className="block text-xs text-red-800">{t('po_rejected_of', { rejected: formatQuantity(item.rejectedQuantity, locale), total: formatQuantity(item.receivedQuantity, locale) })}</span>
    );
    const th = 'px-2 py-2';

    return (
        <>
            <div className="hidden overflow-x-auto rounded-md border border-gray-200 bg-white md:block">
                <table className="min-w-full divide-y divide-gray-200 text-sm">
                    <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                        <tr>
                            <th scope="col" className={th}>{t('po_col_number')}</th>
                            <th scope="col" className={th}>{t('po_col_vendor')}</th>
                            <th scope="col" className={th}>{t('partij_field')}</th>
                            <th scope="col" className={th}>{t('po_col_ordered_on')}</th>
                            <th scope="col" className={th}>{t('po_col_expected')}</th>
                            <th scope="col" className={th}>{t('po_col_status')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('po_col_packages')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('po_col_net_kg')}</th>
                            <th scope="col" className={th}>{t('po_col_invoices')}</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {items.map((item, index) => (
                            <tr key={item.id} aria-selected={item.id === selectedId} className={listRowClass(index, { selected: item.id === selectedId, clickable: true })} onClick={rowClick(() => onOpen(item.id))}>
                                <td className={`${th} whitespace-nowrap`}>
                                    <button type="button" data-tour="po-open" data-tour-key={item.orderNumber} className="text-left font-semibold text-blue-800 hover:underline" aria-label={t('po_open_order', { number: orderNumbers(item).main })} onClick={(event) => { event.stopPropagation(); onOpen(item.id); }}>
                                        <OrderNumberText orderNumber={item.orderNumber} supplierReference={item.supplierReference} stacked truncate />
                                    </button>
                                </td>
                                <td className={th}>{item.vendorName ?? '–'}{item.linesOrigin && <span className="block text-xs text-gray-600">{item.linesOrigin}</span>}</td>
                                <td className={`${th} font-mono`}>{item.partijNumber ? <span data-tour="po-list-partij">{item.partijNumber}</span> : '–'}</td>
                                <td className={`${th} whitespace-nowrap`}>{formatDateOnly(item.orderedOn, locale)}</td>
                                <td className={`${th} whitespace-nowrap`}>{expected(item)}</td>
                                <td className={th}>
                                    <span className="flex flex-wrap items-center gap-1"><OrderStatusBadge status={item.status} /><DocumentStatusBadge status={item.documentStatus} /><BillingStatusBadge status={item.billingStatus} />{item.hasDeviation && <DeviationBadge />}</span>
                                    {rejected(item)}
                                    {listEditable(item) && (
                                        <Link href={`/purchase-orders/edit?id=${item.id}`} onClick={(event) => event.stopPropagation()} data-tour="po-list-edit" data-tour-key={item.orderNumber}
                                            className="mt-1 inline-block text-xs font-medium text-blue-800 underline underline-offset-2">{t('po_edit')}</Link>
                                    )}
                                </td>
                                <td className={`${th} text-right font-mono`}>{formatQuantity(item.expectedQuantity, locale)}</td>
                                <td className={`${th} whitespace-nowrap text-right font-mono`}>{kg(item)}</td>
                                <td className={th}>
                                    <span className="font-mono">{invoices(item)}</span>
                                    {item.invoiceIndicator !== 'None' && <span className="ml-1"><InvoiceIndicatorBadge indicator={item.invoiceIndicator} /></span>}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            <ul className="space-y-2 md:hidden">
                {items.map((item) => (
                    <li key={item.id}>
                        <button type="button" data-tour="po-open" data-tour-key={item.orderNumber} onClick={() => onOpen(item.id)} aria-label={t('po_open_order', { number: orderNumbers(item).main })}
                            className={`w-full rounded-md border bg-white p-3 text-left text-sm ${item.id === selectedId ? 'border-blue-500' : 'border-gray-200'}`}>
                            <span className="flex items-start justify-between gap-2">
                                <span className="min-w-0 font-semibold text-blue-800"><OrderNumberText orderNumber={item.orderNumber} supplierReference={item.supplierReference} stacked truncate /></span>
                                <span className="flex shrink-0 flex-wrap justify-end gap-1"><OrderStatusBadge status={item.status} /><DocumentStatusBadge status={item.documentStatus} /><BillingStatusBadge status={item.billingStatus} />{item.hasDeviation && <DeviationBadge />}</span>
                            </span>
                            <span className="mt-1 block break-words font-medium text-gray-900">{item.vendorName ?? '–'}{item.linesOrigin && <span className="font-normal text-gray-700"> · {item.linesOrigin}</span>}</span>
                            {item.partijNumber && <span className="block break-words text-gray-700">{t('partij_field')} <span className="font-mono">{item.partijNumber}</span></span>}
                            <span className="block text-gray-700">{t('po_col_ordered_on')} {formatDateOnly(item.orderedOn, locale)} · {t('po_col_expected')} {expected(item)}</span>
                            <span className="block font-mono text-gray-900">{formatQuantity(item.expectedQuantity, locale)} {t('po_packages')} · {kg(item)}</span>
                            {rejected(item)}
                            <span className="mt-1 flex flex-wrap items-center gap-1 break-all">
                                <span className="text-gray-700">{t('po_col_invoices')}: <span className="font-mono">{invoices(item)}</span></span>
                                {item.invoiceIndicator !== 'None' && <InvoiceIndicatorBadge indicator={item.invoiceIndicator} />}
                            </span>
                        </button>
                        {listEditable(item) && (
                            <Link href={`/purchase-orders/edit?id=${item.id}`} className="mt-1 inline-block px-1 text-xs font-medium text-blue-800 underline underline-offset-2">{t('po_edit')}</Link>
                        )}
                    </li>
                ))}
            </ul>
        </>
    );
}

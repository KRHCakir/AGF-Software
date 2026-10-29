'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatCalendarDate, formatDateOnly } from '../../lib/calendarDate';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder';
import { InvoiceIndicatorBadge, OrderStatusBadge } from './PurchaseOrderBadges';

// Lijst "Inkoopopdrachten": vanaf md een tabel, smaller kaarten met dezelfde gegevens (niets afgekapt).
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
                            <th scope="col" className={th}>{t('po_col_supplier_reference')}</th>
                            <th scope="col" className={th}>{t('po_col_ordered_on')}</th>
                            <th scope="col" className={th}>{t('po_col_expected')}</th>
                            <th scope="col" className={th}>{t('po_col_status')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('po_col_packages')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('po_col_net_kg')}</th>
                            <th scope="col" className={th}>{t('po_col_invoices')}</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {items.map((item) => (
                            <tr key={item.id} aria-selected={item.id === selectedId} className={`cursor-pointer hover:bg-blue-50 ${item.id === selectedId ? 'bg-blue-50' : ''}`} onClick={() => onOpen(item.id)}>
                                <td className={`${th} whitespace-nowrap font-mono`}>
                                    <button type="button" data-tour="po-open" data-tour-key={item.orderNumber} className="font-semibold text-blue-800 hover:underline" aria-label={t('po_open_order', { number: item.orderNumber })} onClick={(event) => { event.stopPropagation(); onOpen(item.id); }}>
                                        {item.orderNumber}
                                    </button>
                                </td>
                                <td className={th}>{item.vendorName ?? '–'}</td>
                                <td className={`${th} font-mono`}>{item.supplierReference ?? '–'}</td>
                                <td className={`${th} whitespace-nowrap`}>{formatDateOnly(item.orderedOn, locale)}</td>
                                <td className={`${th} whitespace-nowrap`}>{expected(item)}</td>
                                <td className={th}><OrderStatusBadge status={item.status} />{rejected(item)}</td>
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
                        <button type="button" data-tour="po-open" data-tour-key={item.orderNumber} onClick={() => onOpen(item.id)} aria-label={t('po_open_order', { number: item.orderNumber })}
                            className={`w-full rounded-md border bg-white p-3 text-left text-sm ${item.id === selectedId ? 'border-blue-500' : 'border-gray-200'}`}>
                            <span className="flex flex-wrap items-center justify-between gap-2">
                                <span className="font-mono font-semibold text-blue-800">{item.orderNumber}</span>
                                <OrderStatusBadge status={item.status} />
                            </span>
                            <span className="mt-1 block break-words font-medium text-gray-900">{item.vendorName ?? '–'}</span>
                            <span className="block break-words text-gray-700">{t('po_col_supplier_reference')}: <span className="font-mono">{item.supplierReference ?? '–'}</span></span>
                            <span className="block text-gray-700">{t('po_col_ordered_on')} {formatDateOnly(item.orderedOn, locale)} · {t('po_col_expected')} {expected(item)}</span>
                            <span className="block font-mono text-gray-900">{formatQuantity(item.expectedQuantity, locale)} {t('po_packages')} · {kg(item)}</span>
                            {rejected(item)}
                            <span className="mt-1 flex flex-wrap items-center gap-1 break-all">
                                <span className="text-gray-700">{t('po_col_invoices')}: <span className="font-mono">{invoices(item)}</span></span>
                                {item.invoiceIndicator !== 'None' && <InvoiceIndicatorBadge indicator={item.invoiceIndicator} />}
                            </span>
                        </button>
                    </li>
                ))}
            </ul>
        </>
    );
}

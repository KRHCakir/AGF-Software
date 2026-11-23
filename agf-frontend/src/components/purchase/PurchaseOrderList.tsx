'use client';

import { Fragment, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatMoney, formatQuantity } from '../../lib/availability';
import { formatCalendarDate, formatDateOnly } from '../../lib/calendarDate';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder';
import Link from 'next/link';
import { DeviationBadge, DocumentStatusBadge, InvoiceIndicatorBadge, OrderStatusBadge, BillingStatusBadge } from './PurchaseOrderBadges';
import { listEditable } from '../../lib/purchaseOrderEdit';
import { listRowClass, rowClick } from '../../lib/listRows';
import { orderNumbers } from '../../lib/purchaseOrderRules';
import OrderNumberText from './OrderNumberText';
import { formatEuro } from '../../lib/documentCurrency';
import { isEuro } from '../../lib/exchangeRate';
import type { SortState } from '../../lib/listSort';
import { SortButton, SortHeader, SortSelect, type SortOption } from '../shared/SortHeader';
import { partialSummary } from '../../lib/orderLineProgress';
import { toApiError } from '../../services/apiClient';
import { getPurchaseOrder } from '../../services/purchaseOrderService';
import type { PurchaseOrderLine } from '../../types/purchaseOrder';
import type { LoadState } from '../availability/StateViews';
import OrderLineProgress from './OrderLineProgress';

// Sorteervelden van de inkooporderlijst (feedback 22 november): de server sorteert (de lijst bladert op de server) en kent precies deze
// velden (AgfAdmin/Services/ListSortFields.cs). "number" is het hoofdnummer zoals getoond (leveranciersnummer, anders IO-nummer).
export const PURCHASE_ORDER_SORT_KEYS = ['number', 'orderNumber', 'vendor', 'partij', 'orderedOn', 'expected', 'status', 'packages', 'netKg', 'amount', 'invoices'] as const;

// Lijst "Inkoopopdrachten": vanaf md een tabel, smaller kaarten met dezelfde gegevens. Het ordernummer van de leverancier is het hoofdnummer
// (een lang nummer afgekapt met …, volledig in het detail), het eigen IO-nummer staat er klein en grijs onder.
export default function PurchaseOrderList({ items, selectedId, onOpen, sort, onSort, onSortChange }: {
    items: PurchaseOrderListItem[];
    selectedId: number | null;
    onOpen: (id: number) => void;
    sort: SortState | null;
    onSort: (key: string) => void;
    onSortChange: (next: SortState | null) => void;
}) {
    const { t, locale } = useTranslation();
    const invoices = (item: PurchaseOrderListItem) => item.invoiceNumbers.length === 0 ? '–'
        : item.invoiceNumbers.map((ref) => (ref.kind === 'CreditNote' ? `${t('po_credit_note_short')} ${ref.invoiceNumber}` : ref.invoiceNumber)).join(', ');
    const kg = (item: PurchaseOrderListItem) => (item.expectedNetWeightKg === null ? '–' : formatKg(item.expectedNetWeightKg, locale));
    const expected = (item: PurchaseOrderListItem) => (item.expectedAt ? formatCalendarDate(item.expectedAt, locale) : '–');
    const rejected = (item: PurchaseOrderListItem) => item.rejectedQuantity > 0 && (
        <span className="block text-xs text-red-800">{t('po_rejected_of', { rejected: formatQuantity(item.rejectedQuantity, locale), total: formatQuantity(item.receivedQuantity, locale) })}</span>
    );
    // Feedback 22 november: per order uitklappen tot een overzicht per regel (besteld, ontvangen, open, partij, status), pas bij openen geladen.
    const [expanded, setExpanded] = useState<Record<number, LoadState<PurchaseOrderLine[]>>>({});
    const toggle = (id: number) => {
        if (expanded[id] !== undefined) {
            setExpanded((current) => { const next = { ...current }; delete next[id]; return next; });
            return;
        }
        setExpanded((current) => ({ ...current, [id]: { status: 'loading' } }));
        getPurchaseOrder(id)
            .then((order) => setExpanded((current) => (current[id] === undefined ? current : { ...current, [id]: { status: 'ready', data: order.lines } })))
            .catch((caught: unknown) => setExpanded((current) => (current[id] === undefined ? current : { ...current, [id]: { status: 'error', error: toApiError(caught) } })));
    };
    const partial = (item: PurchaseOrderListItem) => {
        const summary = partialSummary(item);
        return summary && (
            <span className="block text-xs text-amber-900" data-testid="po-list-partial" data-tour="po-list-partial">
                {t('po_partial_lines', { received: String(summary.received), lines: String(summary.lines), open: formatQuantity(summary.open, locale) })}
            </span>
        );
    };
    const expandButton = (item: PurchaseOrderListItem, controls: string) => (
        <button type="button" aria-expanded={expanded[item.id] !== undefined} aria-controls={controls} data-tour="po-list-expand" data-tour-key={item.orderNumber}
            onClick={(event) => { event.stopPropagation(); toggle(item.id); }}
            className="mt-1 inline-block text-xs font-medium text-blue-800 underline underline-offset-2">
            {expanded[item.id] !== undefined ? t('po_lines_collapse') : t('po_lines_expand')}
        </button>
    );
    const progress = (id: number) => {
        const state = expanded[id];
        if (state === undefined) return null;
        if (state.status === 'loading') return <p className="px-2 py-1 text-xs text-gray-700">{t('loading_dots')}</p>;
        if (state.status === 'error') return <p role="alert" className="px-2 py-1 text-xs text-red-800">{state.error.message}</p>;
        return <OrderLineProgress lines={state.data} />;
    };
    const th = 'px-2 py-2';
    const head = 'px-2 py-2 align-top';
    const sortOptions: SortOption[] = [
        { key: 'number', label: t('po_col_number'), kind: 'natural' },
        { key: 'orderNumber', label: t('po_internal_number'), kind: 'natural' },
        { key: 'vendor', label: t('po_col_vendor'), kind: 'text' },
        { key: 'partij', label: t('partij_field'), kind: 'natural' },
        { key: 'orderedOn', label: t('po_col_ordered_on'), kind: 'date' },
        { key: 'expected', label: t('po_col_expected'), kind: 'date' },
        { key: 'status', label: t('po_col_status'), kind: 'natural' },
        { key: 'packages', label: t('po_col_packages'), kind: 'number' },
        { key: 'netKg', label: t('po_col_net_kg'), kind: 'number' },
        { key: 'amount', label: t('po_col_amount'), kind: 'number' },
        { key: 'invoices', label: t('po_col_invoices'), kind: 'natural' },
    ];

    // Valuta-ronde: het bedrag in euro (de lijst telt alleen euro's op); bij een andere valuta het bedrag in die valuta eronder.
    const amount = (item: PurchaseOrderListItem) => item.amountEur == null ? '–' : (
        <>
            {formatEuro(item.amountEur, locale)}
            {item.currency && !isEuro(item.currency) && item.amount != null && <span className="block text-xs text-gray-600">{formatMoney(item.amount, item.currency, locale)}</span>}
        </>
    );

    return (
        <>
            <div className="hidden overflow-x-auto rounded-md border border-gray-200 bg-white md:block">
                <table className="min-w-full divide-y divide-gray-200 text-sm">
                    <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                        <tr>
                            <SortHeader className={head} label={t('po_col_number')} sortKey="number" sort={sort} onSort={onSort} alsoKeys={['orderNumber']}>
                                <SortButton small label={t('po_internal_number')} sortKey="orderNumber" sort={sort} onSort={onSort} />
                            </SortHeader>
                            <SortHeader className={head} label={t('po_col_vendor')} sortKey="vendor" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('partij_field')} sortKey="partij" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('po_col_ordered_on')} sortKey="orderedOn" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('po_col_expected')} sortKey="expected" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('po_col_status')} sortKey="status" sort={sort} onSort={onSort} />
                            <SortHeader className={`${head} text-right`} label={t('po_col_packages')} sortKey="packages" sort={sort} onSort={onSort} />
                            <SortHeader className={`${head} text-right`} label={t('po_col_net_kg')} sortKey="netKg" sort={sort} onSort={onSort} />
                            <SortHeader className={`${head} text-right`} label={t('po_col_amount')} sortKey="amount" sort={sort} onSort={onSort}><span className="block font-normal normal-case">{t('po_amount_eur_hint')}</span></SortHeader>
                            <SortHeader className={head} label={t('po_col_invoices')} sortKey="invoices" sort={sort} onSort={onSort} />
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {items.map((item, index) => (
                            <Fragment key={item.id}>
                            <tr aria-selected={item.id === selectedId} className={listRowClass(index, { selected: item.id === selectedId, clickable: true })} onClick={rowClick(() => onOpen(item.id))}>
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
                                    {partial(item)}
                                    <span className="flex flex-wrap gap-x-3">
                                        {expandButton(item, `po-lines-${item.id}`)}
                                        {listEditable(item) && (
                                            <Link href={`/purchase-orders/edit?id=${item.id}`} onClick={(event) => event.stopPropagation()} data-tour="po-list-edit" data-tour-key={item.orderNumber}
                                                className="mt-1 inline-block text-xs font-medium text-blue-800 underline underline-offset-2">{t('po_edit')}</Link>
                                        )}
                                    </span>
                                </td>
                                <td className={`${th} text-right font-mono`}>{formatQuantity(item.expectedQuantity, locale)}</td>
                                <td className={`${th} whitespace-nowrap text-right font-mono`}>{kg(item)}</td>
                                <td className={`${th} whitespace-nowrap text-right font-mono`} data-testid="po-list-amount">{amount(item)}</td>
                                <td className={th}>
                                    <span className="font-mono">{invoices(item)}</span>
                                    {item.invoiceIndicator !== 'None' && <span className="ml-1"><InvoiceIndicatorBadge indicator={item.invoiceIndicator} /></span>}
                                </td>
                            </tr>
                            {expanded[item.id] !== undefined && (
                                <tr id={`po-lines-${item.id}`}><td colSpan={10} className="bg-gray-50 px-2 py-2">{progress(item.id)}</td></tr>
                            )}
                            </Fragment>
                        ))}
                    </tbody>
                </table>
            </div>

            <SortSelect id="po-sort" className="mb-2 md:hidden" options={sortOptions} sort={sort} onChange={onSortChange} />
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
                            {item.amountEur != null && <span className="block font-mono text-gray-900">{amount(item)}</span>}
                            {rejected(item)}
                            {partial(item)}
                            <span className="mt-1 flex flex-wrap items-center gap-1 break-all">
                                <span className="text-gray-700">{t('po_col_invoices')}: <span className="font-mono">{invoices(item)}</span></span>
                                {item.invoiceIndicator !== 'None' && <InvoiceIndicatorBadge indicator={item.invoiceIndicator} />}
                            </span>
                        </button>
                        <span className="flex flex-wrap gap-x-3 px-1">
                            {expandButton(item, `po-lines-card-${item.id}`)}
                            {listEditable(item) && (
                                <Link href={`/purchase-orders/edit?id=${item.id}`} className="mt-1 inline-block text-xs font-medium text-blue-800 underline underline-offset-2">{t('po_edit')}</Link>
                            )}
                        </span>
                        {expanded[item.id] !== undefined && <div id={`po-lines-card-${item.id}`} className="mt-1">{progress(item.id)}</div>}
                    </li>
                ))}
            </ul>
        </>
    );
}

'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatCalendarDate } from '../../lib/calendarDate';
import { formatEuro } from '../../lib/documentCurrency';
import { listRowClass, rowClick } from '../../lib/listRows';
import type { SortState } from '../../lib/listSort';
import { invoiceTitle, selectable } from '../../lib/salesInvoice';
import type { SalesInvoiceListItem } from '../../types/salesInvoice';
import { SortHeader, SortSelect, type SortOption } from '../shared/SortHeader';
import { SalesInvoiceStatusBadge } from './SalesInvoiceBadges';

// De lijst Verkoopfacturen: vanaf md een tabel, smaller kaarten met dezelfde gegevens. Concepten met regels en prijzen zijn aan te vinken om
// samen definitief te maken.
export default function SalesInvoiceList({ items, selectedId, onOpen, sort, onSort, onSortChange, chosen, onChoose }: {
    items: SalesInvoiceListItem[];
    selectedId: number | null;
    onOpen: (id: number) => void;
    sort: SortState | null;
    onSort: (key: string) => void;
    onSortChange: (next: SortState | null) => void;
    chosen: Set<number>;
    onChoose: (id: number, on: boolean) => void;
}) {
    const { t, locale } = useTranslation();
    const date = (value: string | null) => (value ? formatCalendarDate(value, locale) : '–');
    const lots = (item: SalesInvoiceListItem) => (item.salesLotNumbers.length === 0 ? '–' : item.salesLotNumbers.join(', '));
    const title = (item: SalesInvoiceListItem) => invoiceTitle(item, t('si_concept'));
    const head = 'px-2 py-2 align-top';
    const cell = 'px-2 py-2';
    const sortOptions: SortOption[] = [
        { key: 'number', label: t('si_col_number'), kind: 'natural' },
        { key: 'customer', label: t('si_col_customer'), kind: 'text' },
        { key: 'invoiceDate', label: t('si_col_invoice_date'), kind: 'date' },
        { key: 'deliveryDate', label: t('si_col_delivery_date'), kind: 'date' },
        { key: 'dueDate', label: t('si_col_due_date'), kind: 'date' },
        { key: 'lots', label: t('si_col_lots'), kind: 'natural' },
        { key: 'status', label: t('si_col_status'), kind: 'natural' },
        { key: 'net', label: t('si_col_net'), kind: 'number' },
        { key: 'gross', label: t('si_col_gross'), kind: 'number' },
    ];
    const checkbox = (item: SalesInvoiceListItem) => selectable(item) && (
        <input type="checkbox" data-tour="si-choose" data-tour-key={String(item.id)} aria-label={t('si_choose', { customer: item.customerName ?? '' })}
            checked={chosen.has(item.id)} onClick={(event) => event.stopPropagation()} onChange={(event) => onChoose(item.id, event.target.checked)} className="h-4 w-4" />
    );

    return (
        <>
            <div className="hidden overflow-x-auto rounded-md border border-gray-200 bg-white md:block">
                <table className="min-w-full divide-y divide-gray-200 text-sm" data-testid="si-table">
                    <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                        <tr>
                            <th className={head}><span className="sr-only">{t('si_choose_column')}</span></th>
                            <SortHeader className={head} label={t('si_col_number')} sortKey="number" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('si_col_customer')} sortKey="customer" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('si_col_invoice_date')} sortKey="invoiceDate" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('si_col_delivery_date')} sortKey="deliveryDate" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('si_col_due_date')} sortKey="dueDate" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('si_col_lots')} sortKey="lots" sort={sort} onSort={onSort} />
                            <SortHeader className={head} label={t('si_col_status')} sortKey="status" sort={sort} onSort={onSort} />
                            <SortHeader className={`${head} text-right`} label={t('si_col_net')} sortKey="net" sort={sort} onSort={onSort} />
                            <SortHeader className={`${head} text-right`} label={t('si_col_gross')} sortKey="gross" sort={sort} onSort={onSort} />
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {items.map((item, index) => (
                            <tr key={item.id} aria-selected={item.id === selectedId} className={listRowClass(index, { selected: item.id === selectedId, clickable: true })} onClick={rowClick(() => onOpen(item.id))}>
                                <td className={cell}>{checkbox(item)}</td>
                                <td className={`${cell} whitespace-nowrap`}>
                                    <button type="button" data-tour="si-open" data-tour-key={String(item.id)} className="text-left font-semibold text-blue-800 hover:underline"
                                        onClick={(event) => { event.stopPropagation(); onOpen(item.id); }}>{title(item)}</button>
                                </td>
                                <td className={cell}>{item.customerName ?? '–'}{item.referenceNumber && <span className="block text-xs text-gray-600">{item.referenceNumber}</span>}</td>
                                <td className={`${cell} whitespace-nowrap`}>{item.status === 'Concept' ? '–' : date(item.invoiceDate)}</td>
                                <td className={`${cell} whitespace-nowrap`}>{date(item.deliveryDate)}</td>
                                <td className={`${cell} whitespace-nowrap`}>{date(item.dueDate)}</td>
                                <td className={`${cell} font-mono`}>{lots(item)}</td>
                                <td className={cell}><SalesInvoiceStatusBadge status={item.status} stale={item.stale} /></td>
                                <td className={`${cell} whitespace-nowrap text-right font-mono`}>{formatEuro(item.net, locale)}</td>
                                <td className={`${cell} whitespace-nowrap text-right font-mono`}>{formatEuro(item.gross, locale)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            <SortSelect id="si-sort" className="mb-2 md:hidden" options={sortOptions} sort={sort} onChange={onSortChange} />
            <ul className="space-y-2 md:hidden" data-testid="si-cards">
                {items.map((item) => (
                    <li key={item.id} className="flex items-start gap-2">
                        <span className="pt-3">{checkbox(item)}</span>
                        <button type="button" data-tour="si-open" data-tour-key={String(item.id)} onClick={() => onOpen(item.id)}
                            className={`min-w-0 flex-1 rounded-md border bg-white p-3 text-left text-sm ${item.id === selectedId ? 'border-blue-500' : 'border-gray-200'}`}>
                            <span className="flex items-start justify-between gap-2">
                                <span className="font-semibold text-blue-800">{title(item)}</span>
                                <SalesInvoiceStatusBadge status={item.status} stale={item.stale} />
                            </span>
                            <span className="mt-1 block break-words font-medium text-gray-900">{item.customerName ?? '–'}</span>
                            <span className="block break-words text-gray-700">{t('si_col_lots')} <span className="font-mono">{lots(item)}</span></span>
                            <span className="block text-gray-700">{t('si_col_delivery_date')} {date(item.deliveryDate)} · {t('si_col_due_date')} {date(item.dueDate)}</span>
                            <span className="block font-mono text-gray-900">{formatEuro(item.gross, locale)}</span>
                        </button>
                    </li>
                ))}
            </ul>
        </>
    );
}

'use client';

import Link from 'next/link';
import { Fragment, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { WarehouseRow, formatQuantity } from '../../lib/availability';
import { freeState, sellHref, sellQuantity } from '../../lib/salesSupply';
import { AvailabilityFocus } from '../../lib/availabilityLinks';
import { SalesBalance } from '../../types/salesSupply';
import { UnitBadge } from './QuantityCell';
import SalesBreakdown from './SalesBreakdown';
import WarningBadges from './WarningBadges';
import { listRowClass } from '../../lib/listRows';

const th = 'px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';
const td = 'px-3 py-2 align-top';
const focusClass = 'bg-amber-50 ring-2 ring-inset ring-amber-400';
const sellClass = 'inline-flex items-center rounded bg-blue-700 px-3 py-1 text-sm font-medium text-white hover:bg-blue-800 focus:outline-none focus:ring-2 focus:ring-blue-500';
const toggleClass = 'rounded border border-gray-300 bg-white px-2 py-1 text-xs font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500';

// Smal scherm (onder Tailwind md): kaarten onder elkaar. Zonder matchMedia (test) de tabel.
function useNarrow(): boolean {
    const [narrow, setNarrow] = useState(false);
    useEffect(() => {
        if (typeof window.matchMedia !== 'function') return;
        const media = window.matchMedia('(max-width: 767px)');
        const update = () => setNarrow(media.matches);
        update();
        media.addEventListener('change', update);
        return () => media.removeEventListener('change', update);
    }, []);
    return narrow;
}

// Verkoopweergave (standaard): per variant + eenheid Voorraad, Verwacht, Verkocht en Vrij te verkopen. Vrij mag negatief zijn: dan rood met
// het woord "Tekort" (een waarschuwing, geen blokkade). Per rij "Verkopen" (nieuwe verkooporder met dit artikel en het vrije aantal, minimaal 1)
// en uitklappen per partij, inkooporder en verkooporder. Op een smal scherm staan de rijen als kaarten onder elkaar; niets wordt afgekapt.
export default function SalesSupplyTable({ rows, onOpen, focus = null }: {
    rows: WarehouseRow[];
    onOpen: (row: WarehouseRow) => void;
    focus?: AvailabilityFocus | null;
}) {
    const { t, locale } = useTranslation();
    const narrow = useNarrow();
    const [open, setOpen] = useState<ReadonlySet<string>>(new Set());
    const toggle = (key: string) => setOpen((current) => {
        const next = new Set(current);
        if (next.has(key)) next.delete(key); else next.add(key);
        return next;
    });
    const q = (value: number) => formatQuantity(value, locale);
    const marked = (column: AvailabilityFocus) => (focus === column ? focusClass : '');
    const balanceOf = (row: WarehouseRow): SalesBalance | null => row.supply.sales ?? null;

    const free = (balance: SalesBalance | null) => {
        if (balance === null) return <span className="text-gray-400">–</span>;
        const state = freeState(balance.free);
        return (
            <span className={state === 'shortage' ? 'font-semibold text-red-800' : state === 'zero' ? 'text-gray-500' : 'text-base font-semibold text-emerald-900'} data-free-state={state}>
                {q(balance.free)}
                {state === 'shortage' && <span className="ml-1 rounded border border-red-300 bg-red-50 px-1.5 py-0.5 text-xs font-semibold uppercase text-red-800">{t('sales_shortage')}</span>}
            </span>
        );
    };
    const expected = (balance: SalesBalance | null) => balance === null ? <span className="text-gray-400">–</span> : (
        <>
            <span className={balance.expected === 0 ? 'text-gray-400' : 'text-gray-900'}>{q(balance.expected)}</span>
            {balance.expectedInInspection > 0 && <span className="block text-xs text-gray-600">{t('sales_in_inspection', { quantity: q(balance.expectedInInspection) })}</span>}
        </>
    );
    const sold = (balance: SalesBalance | null) => balance === null ? <span className="text-gray-400">–</span> : (
        <>
            <span className={balance.sold === 0 ? 'text-gray-400' : 'text-gray-900'}>{q(balance.sold)}</span>
            {balance.soldReserved > 0 && <span className="block text-xs text-gray-600">{t('sales_of_which_reserved', { quantity: q(balance.soldReserved) })}</span>}
        </>
    );
    const stock = (row: WarehouseRow) => {
        const value = balanceOf(row)?.stock ?? row.supply.availableBeforeReservations.quantity;
        return <span className={value === 0 ? 'text-gray-400' : 'text-gray-900'}>{q(value)}</span>;
    };
    const name = (row: WarehouseRow) => (
        <button type="button" onClick={() => onOpen(row)}
            className="text-left font-medium text-blue-800 underline-offset-2 hover:underline focus:outline-none focus:ring-2 focus:ring-blue-500"
            aria-label={t('availability_open_details', { name: `${row.variant.productName} · ${row.variant.variantName}`, unit: row.supply.unit ?? t('availability_unit_unknown') })}>
            {row.variant.productName}
            <span className="block text-xs font-normal text-gray-600">{row.variant.variantName}</span>
        </button>
    );
    const actions = (row: WarehouseRow, idSuffix: string) => {
        const expandedId = `sales-breakdown-${idSuffix}-${row.key}`;
        return (
            <div className="flex flex-wrap items-center gap-2">
                {row.supply.unit !== null && (
                    <Link href={sellHref(row.variant.productVariantId, row.supply.unit, sellQuantity(balanceOf(row)))} className={sellClass}
                        data-tour="availability-sell" data-tour-key={row.variant.productVariantId}
                        aria-label={t('sales_sell_label', { name: `${row.variant.productName} · ${row.variant.variantName}` })}>
                        {t('sales_sell')}
                    </Link>
                )}
                {row.supply.unit !== null && (
                    <button type="button" className={toggleClass} aria-expanded={open.has(row.key)} aria-controls={expandedId} onClick={() => toggle(row.key)}>
                        {open.has(row.key) ? t('sales_breakdown_hide') : t('sales_breakdown_show')}
                    </button>
                )}
            </div>
        );
    };

    return (
        <div data-view="sales">
            {!narrow && <div className="overflow-x-auto rounded-md border border-gray-300 bg-white">
                <table className="min-w-full divide-y divide-gray-200 text-sm">
                    <caption className="sr-only">{t('availability_warehouse_title')}</caption>
                    <thead className="bg-gray-50">
                        <tr>
                            <th scope="col" className={th}>{t('availability_col_product')}</th>
                            <th scope="col" className={th}>{t('availability_col_unit')}</th>
                            <th scope="col" className={th}>{t('sales_col_stock')}<span className="block font-normal normal-case text-gray-500">{t('sales_col_stock_hint')}</span></th>
                            <th scope="col" className={`${th} ${marked('expected')} ${marked('quarantine')}`}>{t('sales_col_expected')}<span className="block font-normal normal-case text-gray-500">{t('sales_col_expected_hint')}</span></th>
                            <th scope="col" className={th}>{t('sales_col_sold')}<span className="block font-normal normal-case text-gray-500">{t('sales_col_sold_hint')}</span></th>
                            <th scope="col" className={`${th} bg-emerald-50 text-emerald-900`}>{t('sales_col_free')}<span className="block font-normal normal-case text-emerald-800">{t('sales_col_free_hint')}</span></th>
                            <th scope="col" className={th}>{t('availability_col_signals')}</th>
                            <th scope="col" className={th}><span className="sr-only">{t('sales_col_actions')}</span></th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {rows.map((row, index) => {
                            const balance = balanceOf(row);
                            return (
                                <Fragment key={row.key}>
                                    <tr className={listRowClass(index)} data-row-key={row.key}>
                                        <th scope="row" className={`${td} text-left font-normal`}>{name(row)}</th>
                                        <td className={td}><UnitBadge unit={row.supply.unit} /></td>
                                        <td className={td} data-tour="availability-available" data-tour-key={row.variant.productVariantId} data-col="stock">{stock(row)}</td>
                                        <td className={`${td} ${marked('expected')} ${marked('quarantine')}`} data-col="expected">{expected(balance)}</td>
                                        <td className={td} data-col="sold">{sold(balance)}</td>
                                        <td className={`${td} bg-emerald-50/60`} data-tour="availability-free" data-tour-key={row.variant.productVariantId} data-col="free">{free(balance)}</td>
                                        <td className={td}><WarningBadges warnings={row.supply.warnings} /></td>
                                        <td className={td}>{actions(row, 'table')}</td>
                                    </tr>
                                    {open.has(row.key) && row.supply.unit !== null && (
                                        <tr id={`sales-breakdown-table-${row.key}`} className="bg-gray-50">
                                            <td colSpan={8} className="px-3"><SalesBreakdown productVariantId={row.variant.productVariantId} unit={row.supply.unit} /></td>
                                        </tr>
                                    )}
                                </Fragment>
                            );
                        })}
                    </tbody>
                </table>
            </div>}

            {narrow && <ul className="space-y-2" aria-label={t('availability_warehouse_title')}>
                {rows.map((row) => {
                    const balance = balanceOf(row);
                    return (
                        <li key={row.key} className="rounded-md border border-gray-300 bg-white p-3" data-card-key={row.key}>
                            <div className="flex items-start justify-between gap-2">
                                {name(row)}
                                <UnitBadge unit={row.supply.unit} />
                            </div>
                            <dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
                                <dt className="text-xs text-gray-600">{t('sales_col_stock')}</dt><dd className="text-right">{stock(row)}</dd>
                                <dt className="text-xs text-gray-600">{t('sales_col_expected')}</dt><dd className="text-right">{expected(balance)}</dd>
                                <dt className="text-xs text-gray-600">{t('sales_col_sold')}</dt><dd className="text-right">{sold(balance)}</dd>
                                <dt className="text-xs font-semibold text-emerald-900">{t('sales_col_free')}</dt><dd className="text-right">{free(balance)}</dd>
                            </dl>
                            <div className="mt-2"><WarningBadges warnings={row.supply.warnings} /></div>
                            <div className="mt-2">{actions(row, 'card')}</div>
                            {open.has(row.key) && row.supply.unit !== null && (
                                <div id={`sales-breakdown-card-${row.key}`} className="mt-2 border-t border-gray-200"><SalesBreakdown productVariantId={row.variant.productVariantId} unit={row.supply.unit} /></div>
                            )}
                        </li>
                    );
                })}
            </ul>}
        </div>
    );
}

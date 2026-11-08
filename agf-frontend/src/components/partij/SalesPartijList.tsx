'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { unitLabelKey } from '../../lib/units';
import { PACKAGE_UNITS, type PackageUnit } from '../../types/availability';
import type { PartijTotals } from '../../types/partij';
import { listRowClass, rowClick } from '../../lib/listRows';

const th = 'px-2 py-1.5 align-top';

// Verkoop-partijen: nummer, verkooporder en klant, product(en), besteld en verkocht (gereserveerd of verzonden) en uit welke inkoop-partijen
// de colli komen. Breed een tabel, op 390 px per partij een kaart.
export default function SalesPartijList({ items, selectedId, onOpen }: { items: PartijTotals[]; selectedId: number | null; onOpen: (id: number) => void }) {
    const { t, locale } = useTranslation();
    const qty = (value: number) => formatQuantity(value, locale);
    const unit = (item: PartijTotals) => (item.unit !== null && (PACKAGE_UNITS as readonly string[]).includes(item.unit) ? t(unitLabelKey(item.unit as PackageUnit)) : t('partij_unit_mixed'));
    const sources = (item: PartijTotals) => item.sourcePartijen.join(', ') || t('partij_sources_none_short');
    return (
        <>
            <div className="hidden overflow-x-auto rounded border border-gray-200 bg-white md:block">
                <table className="min-w-full text-sm">
                    <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                        <tr>
                            <th scope="col" className={th}>{t('partij_col_number')}</th>
                            <th scope="col" className={th}>{t('partij_col_sales_order')}</th>
                            <th scope="col" className={th}>{t('partij_col_products')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('partij_col_ordered')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('partij_col_sold')}</th>
                            <th scope="col" className={th}>{t('partij_col_sources')}</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {items.map((item, index) => (
                            <tr key={item.id} aria-selected={item.id === selectedId} className={listRowClass(index, { selected: item.id === selectedId, clickable: true })} onClick={rowClick(() => onOpen(item.id))}>
                                <td className={`${th} whitespace-nowrap font-mono`}>
                                    <button type="button" data-tour="partij-open" data-tour-key={item.number} className="font-semibold text-blue-800 hover:underline"
                                        aria-label={t('partij_open', { number: item.number })} onClick={(event) => { event.stopPropagation(); onOpen(item.id); }}>{item.number}</button>
                                </td>
                                <td className={th}>
                                    <span className="font-mono">{item.salesOrderNumber ?? '–'}</span> · {item.customerName ?? '–'}
                                    {item.salesOrderStatus === 'Cancelled' && <span className="block text-xs text-gray-600">{t('sales_status_cancelled')}</span>}
                                </td>
                                <td className={th}>{item.products.join(', ') || '–'}</td>
                                <td className={`${th} text-right font-mono`}>{qty(item.orderedQuantity)}</td>
                                <td className={`${th} text-right font-mono`}>{qty(item.soldQuantity)} <span className="text-xs text-gray-600">{unit(item)}</span></td>
                                <td className={`${th} font-mono`}>{sources(item)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            <ul className="space-y-2 md:hidden">
                {items.map((item) => (
                    <li key={item.id}>
                        <button type="button" data-tour="partij-open" data-tour-key={item.number} onClick={() => onOpen(item.id)} aria-label={t('partij_open', { number: item.number })}
                            className={`w-full rounded-md border bg-white p-3 text-left text-sm ${item.id === selectedId ? 'border-blue-500' : 'border-gray-200'}`}>
                            <span className="flex flex-wrap items-center justify-between gap-2">
                                <span className="font-mono font-semibold text-blue-800">{item.number}</span>
                                <span className="font-mono text-gray-700">{item.salesOrderNumber ?? '–'}</span>
                            </span>
                            <span className="mt-1 block break-words font-medium text-gray-900">{item.customerName ?? '–'}</span>
                            <span className="block break-words text-gray-700">{item.products.join(', ') || '–'}</span>
                            <span className="block font-mono text-gray-900">{t('partij_card_sales_amounts', { ordered: qty(item.orderedQuantity), sold: qty(item.soldQuantity) })} {unit(item)}</span>
                            <span className="block break-words text-xs text-gray-600">{t('partij_col_sources')}: <span className="font-mono">{sources(item)}</span></span>
                        </button>
                    </li>
                ))}
            </ul>
        </>
    );
}

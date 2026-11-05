'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { unitLabelKey } from '../../lib/units';
import { PACKAGE_UNITS, type PackageUnit } from '../../types/availability';
import type { PartijTotals } from '../../types/partij';
import PartijStatusBadge from './PartijStatusBadge';

const th = 'px-2 py-1.5 align-top';

// Partijlijst: nummer, leverancier(s), product(en), ingekocht, ontvangen, op voorraad, verkocht (colli) en status. Breed een tabel,
// op 390 px per partij een kaart onder elkaar (niets afgekapt).
export default function PartijList({ items, selectedId, onOpen }: { items: PartijTotals[]; selectedId: number | null; onOpen: (id: number) => void }) {
    const { t, locale } = useTranslation();
    const qty = (value: number) => formatQuantity(value, locale);
    const unit = (item: PartijTotals) => (item.unit !== null && (PACKAGE_UNITS as readonly string[]).includes(item.unit) ? t(unitLabelKey(item.unit as PackageUnit)) : t('partij_unit_mixed'));
    return (
        <>
            <div className="hidden overflow-x-auto rounded border border-gray-200 bg-white md:block">
                <table className="min-w-full text-sm">
                    <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                        <tr>
                            <th scope="col" className={th}>{t('partij_col_number')}</th>
                            <th scope="col" className={th}>{t('partij_col_vendors')}</th>
                            <th scope="col" className={th}>{t('partij_col_products')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('partij_col_purchased')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('partij_col_received')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('partij_col_on_hand')}</th>
                            <th scope="col" className={`${th} text-right`}>{t('partij_col_sold')}</th>
                            <th scope="col" className={th}>{t('partij_col_status')}</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {items.map((item) => (
                            <tr key={item.id} aria-selected={item.id === selectedId} className={`cursor-pointer hover:bg-blue-50 ${item.id === selectedId ? 'bg-blue-50' : ''}`} onClick={() => onOpen(item.id)}>
                                <td className={`${th} whitespace-nowrap font-mono`}>
                                    <button type="button" data-tour="partij-open" data-tour-key={item.number} className="font-semibold text-blue-800 hover:underline"
                                        aria-label={t('partij_open', { number: item.number })} onClick={(event) => { event.stopPropagation(); onOpen(item.id); }}>{item.number}</button>
                                    {item.supplierLotReferences.length > 0 && <span className="block text-xs font-normal text-gray-600">{item.supplierLotReferences.join(', ')}</span>}
                                </td>
                                <td className={th}>{item.vendors.join(', ') || '–'}</td>
                                <td className={th}>{item.products.join(', ') || '–'}</td>
                                <td className={`${th} text-right font-mono`}>{qty(item.purchasedQuantity)}</td>
                                <td className={`${th} text-right font-mono`}>{qty(item.receivedQuantity)}</td>
                                <td className={`${th} text-right font-mono`}>{qty(item.onHandQuantity)}{item.inInspectionQuantity > 0 && <span className="block text-xs text-gray-600">{t('partij_in_inspection', { quantity: qty(item.inInspectionQuantity) })}</span>}</td>
                                <td className={`${th} text-right font-mono`}>{qty(item.soldQuantity)} <span className="text-xs text-gray-600">{unit(item)}</span></td>
                                <td className={th}><PartijStatusBadge status={item.status} /></td>
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
                                <PartijStatusBadge status={item.status} />
                            </span>
                            <span className="mt-1 block break-words font-medium text-gray-900">{item.products.join(', ') || '–'}</span>
                            <span className="block break-words text-gray-700">{item.vendors.join(', ') || '–'}</span>
                            {item.supplierLotReferences.length > 0 && <span className="block break-words text-xs text-gray-600">{item.supplierLotReferences.join(', ')}</span>}
                            <span className="block font-mono text-gray-900">
                                {t('partij_card_amounts', { purchased: qty(item.purchasedQuantity), received: qty(item.receivedQuantity), onHand: qty(item.onHandQuantity), sold: qty(item.soldQuantity) })} {unit(item)}
                            </span>
                            {item.inInspectionQuantity > 0 && <span className="block text-xs text-gray-600">{t('partij_in_inspection', { quantity: qty(item.inInspectionQuantity) })}</span>}
                            <span className="block text-xs text-gray-600">{t('partij_created_on', { date: formatCalendarDate(item.createdAt, locale) })}</span>
                        </button>
                    </li>
                ))}
            </ul>
        </>
    );
}

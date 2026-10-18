'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { DirectTradeRow, breakdownTotal } from '../../lib/availability';
import { ExpectedCell, QuantityCell, UnitBadge } from './QuantityCell';
import WarningBadges from './WarningBadges';

const th = 'px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-violet-900';
const td = 'px-3 py-2 align-top';

// Klantgebonden aanvoer. Bewust geen kolom "beschikbaar": dit is nooit algemeen magazijnaanbod.
export default function DirectTradeSupplyTable({ rows, onOpen }: { rows: DirectTradeRow[]; onOpen: (row: DirectTradeRow) => void }) {
    const { t } = useTranslation();
    return (
        <div className="overflow-x-auto rounded-md border-2 border-violet-300 bg-white">
            <table className="min-w-full divide-y divide-violet-100 text-sm">
                <caption className="sr-only">{t('availability_direct_title')}</caption>
                <thead className="bg-violet-50">
                    <tr>
                        <th scope="col" className={th}>{t('availability_col_customer')}</th>
                        <th scope="col" className={th}>{t('availability_col_product')}</th>
                        <th scope="col" className={th}>{t('availability_col_unit')}</th>
                        <th scope="col" className={th}>{t('availability_col_outside_warehouse')}</th>
                        <th scope="col" className={th}>{t('availability_col_expected_on_pallets')}</th>
                        <th scope="col" className={th}>{t('availability_col_in_transit')}</th>
                        <th scope="col" className={th}>{t('availability_col_expected_unregistered')}</th>
                        <th scope="col" className={th}>{t('availability_col_signals')}</th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-violet-50">
                    {rows.map((row) => (
                        <tr key={row.key} className="hover:bg-violet-50/50">
                            <td className={td}>
                                <span className="inline-flex items-center gap-1 rounded border border-violet-300 bg-violet-100 px-2 py-0.5 text-xs font-semibold text-violet-900">
                                    <span aria-hidden="true">🔒</span>
                                    {t('availability_bound_to_customer')}
                                </span>
                                <span className="mt-1 block font-medium text-gray-900">{row.supply.customerName ?? t('availability_customer_unknown')}</span>
                            </td>
                            <th scope="row" className={`${td} text-left font-normal`}>
                                <button type="button" onClick={() => onOpen(row)}
                                    className="text-left font-medium text-violet-900 underline-offset-2 hover:underline focus:outline-none focus:ring-2 focus:ring-violet-500"
                                    aria-label={t('availability_open_details', { name: `${row.variant.productName} · ${row.variant.variantName}`, unit: row.supply.unit ?? t('availability_unit_unknown') })}>
                                    {row.variant.productName}
                                    <span className="block text-xs font-normal text-gray-600">{row.variant.variantName}</span>
                                </button>
                            </th>
                            <td className={td}><UnitBadge unit={row.supply.unit} /></td>
                            <td className={td}><QuantityCell value={breakdownTotal(row.supply.receivedOutsideWarehouse)} /></td>
                            <td className={td}><QuantityCell value={breakdownTotal(row.supply.expectedOnPallets)} /></td>
                            <td className={td}><QuantityCell value={breakdownTotal(row.supply.inTransit)} /></td>
                            <td className={td}><ExpectedCell value={row.supply.expectedUnregistered} /></td>
                            <td className={td}><WarningBadges warnings={row.supply.warnings} /></td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

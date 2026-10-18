'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { WarehouseRow, breakdownTotal } from '../../lib/availability';
import QualityBadge from './QualityBadge';
import { ExpectedCell, QuantityCell, UnitBadge } from './QuantityCell';
import WarningBadges from './WarningBadges';

const th = 'px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';
const td = 'px-3 py-2 align-top';

// Magazijnaanbod: één rij per variant + eenheid. "Nu beschikbaar" staat altijd met "vóór reserveringen".
export default function WarehouseSupplyTable({ rows, onOpen }: { rows: WarehouseRow[]; onOpen: (row: WarehouseRow) => void }) {
    const { t } = useTranslation();
    return (
        <div className="overflow-x-auto rounded-md border border-gray-300 bg-white">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
                <caption className="sr-only">{t('availability_warehouse_title')}</caption>
                <thead className="bg-gray-50">
                    <tr>
                        <th scope="col" rowSpan={2} className={th}>{t('availability_col_product')}</th>
                        <th scope="col" rowSpan={2} className={th}>{t('availability_col_unit')}</th>
                        <th scope="col" rowSpan={2} className={`${th} bg-green-50 text-green-900`}>
                            {t('availability_now_available')}
                            <span className="block font-normal normal-case text-green-800">{t('availability_before_reservations')}</span>
                        </th>
                        <th scope="colgroup" colSpan={4} className={`${th} border-l border-gray-200 text-center`}>{t('availability_group_in_warehouse')}</th>
                        <th scope="col" rowSpan={2} className={`${th} border-l border-gray-200`}>{t('availability_col_outside_warehouse')}</th>
                        <th scope="colgroup" colSpan={3} className={`${th} border-l border-gray-200 text-center`}>{t('availability_group_incoming')}</th>
                        <th scope="col" rowSpan={2} className={`${th} border-l border-gray-200`}>{t('availability_col_signals')}</th>
                    </tr>
                    <tr>
                        <th scope="col" className={`${th} border-l border-gray-200`}>{t('availability_col_total')}</th>
                        <th scope="col" className={th}><QualityBadge status="Quarantine" /></th>
                        <th scope="col" className={th}><QualityBadge status="Blocked" /></th>
                        <th scope="col" className={th}><QualityBadge status="Rejected" /></th>
                        <th scope="col" className={`${th} border-l border-gray-200`}>{t('availability_col_expected_on_pallets')}</th>
                        <th scope="col" className={th}>{t('availability_col_in_transit')}</th>
                        <th scope="col" className={th}>{t('availability_col_expected_unregistered')}</th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                    {rows.map((row) => (
                        <tr key={row.key} className="hover:bg-blue-50/40">
                            <th scope="row" className={`${td} text-left font-normal`}>
                                <button type="button" onClick={() => onOpen(row)}
                                    className="text-left font-medium text-blue-800 underline-offset-2 hover:underline focus:outline-none focus:ring-2 focus:ring-blue-500"
                                    aria-label={t('availability_open_details', { name: `${row.variant.productName} · ${row.variant.variantName}`, unit: row.supply.unit ?? t('availability_unit_unknown') })}>
                                    {row.variant.productName}
                                    <span className="block text-xs font-normal text-gray-600">{row.variant.variantName}</span>
                                </button>
                            </th>
                            <td className={td}><UnitBadge unit={row.supply.unit} /></td>
                            <td className={`${td} bg-green-50/60`}><QuantityCell value={row.supply.availableBeforeReservations} emphasis /></td>
                            <td className={`${td} border-l border-gray-100`}><QuantityCell value={breakdownTotal(row.supply.warehouse)} /></td>
                            <td className={td}><QuantityCell value={row.supply.warehouse.quarantine} /></td>
                            <td className={td}><QuantityCell value={row.supply.warehouse.blocked} /></td>
                            <td className={td}><QuantityCell value={row.supply.warehouse.rejected} /></td>
                            <td className={`${td} border-l border-gray-100`}><QuantityCell value={breakdownTotal(row.supply.receivedOutsideWarehouse)} /></td>
                            <td className={`${td} border-l border-gray-100`}><QuantityCell value={breakdownTotal(row.supply.expectedOnPallets)} /></td>
                            <td className={td}><QuantityCell value={breakdownTotal(row.supply.inTransit)} /></td>
                            <td className={td}><ExpectedCell value={row.supply.expectedUnregistered} /></td>
                            <td className={`${td} border-l border-gray-100`}><WarningBadges warnings={row.supply.warnings} /></td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

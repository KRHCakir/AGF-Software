'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { WarehouseRow, breakdownTotal } from '../../lib/availability';
import { AvailabilityFocus } from '../../lib/availabilityLinks';
import QualityBadge from './QualityBadge';
import { ExpectedCell, QuantityCell, UnitBadge } from './QuantityCell';
import { WeightBasisBadge } from '../shared/WeightBadge';
import WarningBadges from './WarningBadges';

const th = 'px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';
const td = 'px-3 py-2 align-top';
// Alleen visuele markering vanuit een contextlink; filtert niets weg.
const focusClass = 'bg-amber-50 ring-2 ring-inset ring-amber-400';

// Kolommen die alleen in de uitgebreide weergave staan; een contextlink die daarop focust, dwingt de uitgebreide weergave af.
export const DETAILED_ONLY_FOCUS: readonly AvailabilityFocus[] = ['available', 'inTransit'];

export type AvailabilityView = 'compact' | 'detailed';

// Magazijnaanbod: één rij per variant + eenheid. Compact toont fysiek in magazijn, gereserveerd, beschikbaar na reserveringen,
// quarantaine en verwachte aanvoer; "Alle kolommen" toont de volledige uitsplitsing. Aantal en kg staan altijd apart.
export default function WarehouseSupplyTable({ rows, onOpen, focus = null, view = 'compact' }: {
    rows: WarehouseRow[];
    onOpen: (row: WarehouseRow) => void;
    focus?: AvailabilityFocus | null;
    view?: AvailabilityView;
}) {
    const { t } = useTranslation();
    const detailed = view === 'detailed';
    const marked = (column: AvailabilityFocus) => (focus === column ? focusClass : '');
    const markLabel = (column: AvailabilityFocus) => focus === column && <span className="block font-normal normal-case text-amber-900">{t('availability_focus_marked')}</span>;
    const hint = (key: string) => <span className="block font-normal normal-case text-gray-500">{t(key)}</span>;
    const nameCell = (row: WarehouseRow) => (
        <th scope="row" className={`${td} text-left font-normal`}>
            <button type="button" onClick={() => onOpen(row)}
                className="text-left font-medium text-blue-800 underline-offset-2 hover:underline focus:outline-none focus:ring-2 focus:ring-blue-500"
                aria-label={t('availability_open_details', { name: `${row.variant.productName} · ${row.variant.variantName}`, unit: row.supply.unit ?? t('availability_unit_unknown') })}>
                {row.variant.productName}
                <span className="block text-xs font-normal text-gray-600">{row.variant.variantName}</span>
            </button>
        </th>
    );

    if (!detailed) {
        return (
            <div className="overflow-x-auto rounded-md border border-gray-300 bg-white" data-view="compact">
                <table className="min-w-full divide-y divide-gray-200 text-sm">
                    <caption className="sr-only">{t('availability_warehouse_title')}</caption>
                    <thead className="bg-gray-50">
                        <tr>
                            <th scope="col" className={th}>{t('availability_col_product')}</th>
                            <th scope="col" className={th}>{t('availability_col_unit')}</th>
                            <th scope="col" className={th}>{t('availability_col_on_hand')}{hint('availability_col_on_hand_hint')}</th>
                            <th scope="col" className={th}>{t('availability_col_reserved')}</th>
                            <th scope="col" className={`${th} bg-emerald-50 text-emerald-900`}>{t('availability_col_available_after')}</th>
                            <th scope="col" className={`${th} ${marked('quarantine')}`}><QualityBadge status="Quarantine" />{markLabel('quarantine')}</th>
                            <th scope="col" className={`${th} ${marked('expected')}`}>{t('availability_col_expected_supply')}{hint('availability_col_expected_supply_hint')}{markLabel('expected')}</th>
                            <th scope="col" className={th}>{t('availability_col_signals')}</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {rows.map((row) => (
                            <tr key={row.key} className="hover:bg-blue-50/40">
                                {nameCell(row)}
                                <td className={td}><UnitBadge unit={row.supply.unit} /><WeightBasisBadge basis={row.supply.weightBasis} /></td>
                                <td className={td}><QuantityCell value={breakdownTotal(row.supply.warehouse)} /></td>
                                <td className={td}><QuantityCell value={row.supply.reserved} /></td>
                                <td className={`${td} bg-emerald-50/60`} data-tour="availability-available" data-tour-key={row.variant.productVariantId}><QuantityCell value={row.supply.availableAfterReservations} emphasis /></td>
                                <td className={`${td} ${marked('quarantine')}`} data-tour="availability-quarantine" data-tour-key={row.variant.productVariantId}><QuantityCell value={row.supply.warehouse.quarantine} /></td>
                                <td className={`${td} ${marked('expected')}`}><ExpectedCell value={row.supply.expectedUnregistered} /></td>
                                <td className={td}><WarningBadges warnings={row.supply.warnings} /></td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        );
    }

    return (
        <div className="overflow-x-auto rounded-md border border-gray-300 bg-white" data-view="detailed">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
                <caption className="sr-only">{t('availability_warehouse_title')}</caption>
                <thead className="bg-gray-50">
                    <tr>
                        <th scope="col" rowSpan={2} className={th}>{t('availability_col_product')}</th>
                        <th scope="col" rowSpan={2} className={th}>{t('availability_col_unit')}</th>
                        <th scope="col" rowSpan={2} className={`${th} bg-green-50 text-green-900 ${marked('available')}`}>
                            {t('availability_now_available')}
                            <span className="block font-normal normal-case text-green-800">{t('availability_before_reservations')}</span>
                            {markLabel('available')}
                        </th>
                        <th scope="col" rowSpan={2} className={th}>{t('availability_col_reserved')}</th>
                        <th scope="col" rowSpan={2} className={`${th} bg-emerald-50 text-emerald-900`}>
                            {t('availability_col_available_after')}
                        </th>
                        <th scope="col" rowSpan={2} className={th}>{t('availability_col_reserved_on_hold')}</th>
                        <th scope="colgroup" colSpan={4} className={`${th} border-l border-gray-200 text-center`}>{t('availability_group_in_warehouse')}</th>
                        <th scope="col" rowSpan={2} className={`${th} border-l border-gray-200`}>{t('availability_col_outside_warehouse')}</th>
                        <th scope="colgroup" colSpan={3} className={`${th} border-l border-gray-200 text-center`}>{t('availability_group_incoming')}</th>
                        <th scope="col" rowSpan={2} className={`${th} border-l border-gray-200`}>{t('availability_col_signals')}</th>
                    </tr>
                    <tr>
                        <th scope="col" className={`${th} border-l border-gray-200`}>{t('availability_col_total')}</th>
                        <th scope="col" className={`${th} ${marked('quarantine')}`}><QualityBadge status="Quarantine" />{markLabel('quarantine')}</th>
                        <th scope="col" className={th}><QualityBadge status="Blocked" /></th>
                        <th scope="col" className={th}><QualityBadge status="Rejected" /></th>
                        <th scope="col" className={`${th} border-l border-gray-200`}>{t('availability_col_expected_on_pallets')}</th>
                        <th scope="col" className={`${th} ${marked('inTransit')}`}>{t('availability_col_in_transit')}{markLabel('inTransit')}</th>
                        <th scope="col" className={`${th} ${marked('expected')}`}>{t('availability_col_expected_unregistered')}{markLabel('expected')}</th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                    {rows.map((row) => (
                        <tr key={row.key} className="hover:bg-blue-50/40">
                            {nameCell(row)}
                            <td className={td}><UnitBadge unit={row.supply.unit} /><WeightBasisBadge basis={row.supply.weightBasis} /></td>
                            <td className={`${td} bg-green-50/60 ${marked('available')}`} data-tour="availability-available" data-tour-key={row.variant.productVariantId}><QuantityCell value={row.supply.availableBeforeReservations} emphasis /></td>
                            <td className={td}><QuantityCell value={row.supply.reserved} /></td>
                            <td className={`${td} bg-emerald-50/60`}><QuantityCell value={row.supply.availableAfterReservations} emphasis /></td>
                            <td className={td}><QuantityCell value={row.supply.reservedOnHold} /></td>
                            <td className={`${td} border-l border-gray-100`}><QuantityCell value={breakdownTotal(row.supply.warehouse)} /></td>
                            <td className={`${td} ${marked('quarantine')}`}><QuantityCell value={row.supply.warehouse.quarantine} /></td>
                            <td className={td}><QuantityCell value={row.supply.warehouse.blocked} /></td>
                            <td className={td}><QuantityCell value={row.supply.warehouse.rejected} /></td>
                            <td className={`${td} border-l border-gray-100`}><QuantityCell value={breakdownTotal(row.supply.receivedOutsideWarehouse)} /></td>
                            <td className={`${td} border-l border-gray-100`}><QuantityCell value={breakdownTotal(row.supply.expectedOnPallets)} /></td>
                            <td className={`${td} ${marked('inTransit')}`}><QuantityCell value={breakdownTotal(row.supply.inTransit)} /></td>
                            <td className={`${td} ${marked('expected')}`}><ExpectedCell value={row.supply.expectedUnregistered} /></td>
                            <td className={`${td} border-l border-gray-100`}><WarningBadges warnings={row.supply.warnings} /></td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

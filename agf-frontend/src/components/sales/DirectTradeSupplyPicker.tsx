'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { expectedAfterRequested } from '../../lib/salesOrderRules';
import { toApiError } from '../../services/apiClient';
import { getDirectTradeSupply } from '../../services/salesOrderService';
import { DirectTradeSupplyLine, SalesOrder, SalesOrderLine } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { secondaryButtonClass } from '../supply/SuccessPanel';
import { ExpectedAllocationBadge } from './ReservationBadges';

const th = 'px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-violet-900';
const td = 'px-2 py-1.5 align-top';

export function sourceLabel(source: { vendorName: string | null; origin: string | null; destination: string | null }, unknown: string): string {
    return `${source.vendorName ?? unknown} · ${source.origin ?? unknown} → ${source.destination ?? unknown}`;
}

// Klantgebonden DirectTrade-aanvoer die aan één orderregel kan worden toegewezen, rechtstreeks uit GET …/direct-trade-supply:
// zelfde klant als de order, zelfde variant en eenheid, toezegging Verwacht of Deels ontvangen. Dit is verwachte aanvoer:
// geen voorraad, geen kwaliteit, geen gemeten kg. Bewust violet, zoals de klantgebonden tabel in Beschikbaarheid.
export default function DirectTradeSupplyPicker({ order, line, token, selectedId, onSelect, onLoaded, selectable = true }: {
    order: SalesOrder;
    line: SalesOrderLine;
    token: number;
    selectedId: number | null;
    onSelect: (supply: DirectTradeSupplyLine) => void;
    onLoaded: (rows: DirectTradeSupplyLine[]) => void;
    // Onwaar zolang er een onafgeronde toewijzing op deze regel is: geen tweede keuze naast een open commando.
    selectable?: boolean;
}) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<DirectTradeSupplyLine[]>>({ status: 'loading' });
    const [retry, setRetry] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getDirectTradeSupply(order.id, line.id, controller.signal)
            .then((rows) => {
                setState({ status: 'ready', data: rows });
                onLoaded(rows);
            })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [order.id, line.id, token, retry, onLoaded]);

    if (state.status === 'loading') return <LoadingRows rows={2} label={t('sales_allocate_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setRetry((n) => n + 1)} />;
    if (state.data.length === 0) {
        return <EmptyState title={t('sales_allocate_empty')} description={t('sales_allocate_empty_hint', { customer: order.customerName ?? String(order.customerId), unit: line.unit })} />;
    }

    return (
        <div className="overflow-x-auto rounded border-2 border-violet-300 bg-white">
            <table className="min-w-full divide-y divide-violet-100 text-sm">
                <caption className="sr-only">{t('sales_allocate_title')}</caption>
                <thead className="bg-violet-50">
                    <tr>
                        <th scope="col" className={th}>{t('sales_col_source')}</th>
                        <th scope="col" className={th}>{t('sales_col_expected_at')}</th>
                        <th scope="col" className={th}>{t('availability_col_lot')}</th>
                        <th scope="col" className={`${th} text-right`}>{t('sales_col_expected_quantity')}</th>
                        <th scope="col" className={`${th} text-right`}>{t('sales_col_allocated')}</th>
                        <th scope="col" className={`${th} text-right`}>{t('sales_col_remaining')}</th>
                        <th scope="col" className={th}>{t('supply_unit')}</th>
                        <th scope="col" className={`${th} text-right`}>{t('sales_col_declared_kg')}</th>
                        <th scope="col" className={th}><span className="sr-only">{t('supply_select')}</span></th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-violet-50">
                    {state.data.map((supply) => {
                        const selected = supply.supplyCommitmentLineId === selectedId;
                        const late = expectedAfterRequested(supply.expectedAt, order.requestedDeliveryDate);
                        return (
                            <tr key={supply.supplyCommitmentLineId} className={selected ? 'bg-violet-50' : undefined} aria-selected={selected}>
                                <td className={td}>
                                    <ExpectedAllocationBadge />
                                    <span className="mt-1 block font-medium text-gray-900">{sourceLabel(supply, t('availability_unknown'))}</span>
                                    <span className="block text-xs text-gray-600">{supply.customerName ?? t('availability_customer_unknown')} · {supply.commitmentStatus}</span>
                                </td>
                                <td className={`${td} ${late ? 'text-amber-900' : ''}`}>
                                    {supply.expectedAt ? formatCalendarDate(supply.expectedAt, locale) : t('availability_unknown')}
                                    {late && <span className="block text-xs font-medium">{t('sales_allocate_late_marker')}</span>}
                                </td>
                                <td className={td}>{supply.lotNumber ?? t('availability_unknown')}</td>
                                <td className={`${td} text-right`}>{formatQuantity(supply.expectedQuantity, locale)}</td>
                                <td className={`${td} text-right`}>
                                    {formatQuantity(supply.allocatedQuantity, locale)}
                                    {supply.allocatedToThisLineQuantity > 0 && (
                                        <span className="block text-xs font-normal text-gray-500">{t('sales_allocated_to_this_line', { quantity: formatQuantity(supply.allocatedToThisLineQuantity, locale) })}</span>
                                    )}
                                </td>
                                <td className={`${td} text-right font-semibold`}>{formatQuantity(supply.remainingQuantity, locale)}</td>
                                <td className={td}><UnitBadge unit={supply.unit} /></td>
                                <td className={`${td} text-right text-gray-700`} title={t('sales_declared_kg_hint')}>
                                    {supply.declaredExpectedNetWeightKg === null ? '—' : formatKg(supply.declaredExpectedNetWeightKg, locale)}
                                    <span className="block text-xs font-normal text-gray-500">{t('sales_declared_kg_whole_line')}</span>
                                </td>
                                <td className={td}>
                                    <button type="button" className={secondaryButtonClass} onClick={() => onSelect(supply)} aria-pressed={selected} disabled={!selectable}
                                        aria-label={t('sales_select_supply', { vendor: supply.vendorName ?? t('availability_unknown'), lot: supply.lotNumber ?? t('availability_unknown') })}>
                                        {selected ? t('sales_selected') : t('supply_select')}
                                    </button>
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </div>
    );
}

'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { toApiError } from '../../services/apiClient';
import { getReservableStock } from '../../services/salesOrderService';
import { ReservableStockLine, SalesOrderLine } from '../../types/salesOrder';
import QualityBadge from '../availability/QualityBadge';
import { UnitBadge } from '../availability/QuantityCell';
import { WeightBadge } from '../shared/WeightBadge';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { secondaryButtonClass } from '../supply/SuccessPanel';

const th = 'px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';
const td = 'px-2 py-1.5 align-top';

// Reserveerbare voorraad voor één orderregel, rechtstreeks uit GET .../reservable-stock:
// alleen Approved inhoud op een Received pallet in het magazijn, route Warehouse, zelfde variant en eenheid.
export default function ReservableStockTable({ orderId, line, token, selectedId, onSelect, onLoaded, selectable = true }: {
    orderId: number;
    line: SalesOrderLine;
    token: number;
    selectedId: number | null;
    onSelect: (stock: ReservableStockLine) => void;
    onLoaded: (rows: ReservableStockLine[]) => void;
    // Onwaar zolang er een onafgeronde reservering op deze regel is: geen tweede keuze naast een open commando.
    selectable?: boolean;
}) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<ReservableStockLine[]>>({ status: 'loading' });
    const [retry, setRetry] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getReservableStock(orderId, line.id, controller.signal)
            .then((rows) => {
                setState({ status: 'ready', data: rows });
                onLoaded(rows);
            })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [orderId, line.id, token, retry, onLoaded]);

    if (state.status === 'loading') return <LoadingRows rows={3} label={t('sales_stock_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setRetry((n) => n + 1)} />;
    if (state.data.length === 0) {
        return <EmptyState title={t('sales_stock_empty')} description={t('sales_stock_empty_hint', { unit: line.unit })} />;
    }

    return (
        <div className="overflow-x-auto rounded border border-gray-200 bg-white">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
                <caption className="sr-only">{t('sales_stock_title')}</caption>
                <thead className="bg-gray-50">
                    <tr>
                        <th scope="col" className={th}>{t('availability_col_pallet')}</th>
                        <th scope="col" className={th}>{t('availability_col_lot')}</th>
                        <th scope="col" className={th}>{t('supply_variant')}</th>
                        <th scope="col" className={th}>{t('sales_col_bin')}</th>
                        <th scope="col" className={`${th} text-right`}>{t('sales_col_available_quantity')}</th>
                        <th scope="col" className={th}>{t('supply_unit')}</th>
                        <th scope="col" className={`${th} text-right`}>{t('sales_col_available_kg')}</th>
                        <th scope="col" className={th}>{t('supply_quality')}</th>
                        <th scope="col" className={th}><span className="sr-only">{t('supply_select')}</span></th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                    {state.data.map((stock) => {
                        const selected = stock.palletContentLineId === selectedId;
                        return (
                            <tr key={stock.palletContentLineId} className={selected ? 'bg-blue-50' : undefined} aria-selected={selected}>
                                <td className={`${td} font-mono`}>{stock.palletReference}</td>
                                <td className={td}>{stock.lotNumber}</td>
                                <td className={td}>{line.productName} · {line.productVariantName}</td>
                                <td className={`${td} font-mono text-xs`}>{stock.warehouseLocation ?? t('supply_no_bin')}</td>
                                <td className={`${td} text-right font-semibold`}>
                                    {formatQuantity(stock.availableQuantity, locale)}
                                    <span className="block text-xs font-normal text-gray-500">{t('sales_of_total', { total: formatQuantity(stock.quantity, locale) })}</span>
                                </td>
                                <td className={td}><UnitBadge unit={stock.unit} /></td>
                                <td className={`${td} text-right font-semibold`}>
                                    {formatKg(stock.availableNetWeightKg, locale)}
                                    <span className="block text-xs font-normal text-gray-500">{t('sales_of_total', { total: formatKg(stock.netWeightKg, locale) })}</span>
                                    <span className="mt-1 block font-normal"><WeightBadge method={stock.weightMethod} kgPerPackage={stock.netKgPerPackage} unit={stock.unit} /></span>
                                </td>
                                <td className={td}><QualityBadge status={stock.qualityStatus} /></td>
                                <td className={td}>
                                    <button type="button" className={secondaryButtonClass} onClick={() => onSelect(stock)} aria-pressed={selected} disabled={!selectable}
                                        aria-label={t('sales_select_stock', { pallet: stock.palletReference, lot: stock.lotNumber })}>
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

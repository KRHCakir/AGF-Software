'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { coverageParts } from '../../lib/salesSupply';
import { getAvailabilityVariant, toAvailabilityError } from '../../services/availabilityService';
import { AvailabilityDrillDown, PackageUnit } from '../../types/availability';
import QualityBadge from './QualityBadge';
import CoverageText from './CoverageText';
import { ErrorState, LoadState, LoadingRows } from './StateViews';

const h3 = 'text-xs font-semibold uppercase tracking-wide text-gray-600';

// Uitklapbaar onder een rij van de verkoopweergave: dezelfde stand uitgesplitst per partij (goedgekeurd en in keuring), per inkooporder
// (nog verwacht) en per verkooporder (open, met dekking oudste verkoop eerst). Alleen lezen, uit de bestaande drill-down van de API.
export default function SalesBreakdown({ productVariantId, unit }: { productVariantId: number; unit: PackageUnit }) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<AvailabilityDrillDown>>({ status: 'loading' });
    const [token, setToken] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getAvailabilityVariant(productVariantId, { route: 'Warehouse', unit, customerId: null, includeClosed: false }, controller.signal)
            .then((data) => setState({ status: 'ready', data }))
            .catch((caught: unknown) => {
                const apiError = toAvailabilityError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [productVariantId, unit, token]);

    if (state.status === 'loading') return <LoadingRows rows={2} label={t('availability_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setToken((n) => n + 1)} />;

    const data = state.data;
    const q = (value: number) => formatQuantity(value, locale);
    const lots = data.contentLines.filter((line) => line.category === 'warehouse' && line.unit === unit
        && (line.qualityStatus === 'Approved' || line.qualityStatus === 'Quarantine') && line.onHandQuantity > 0);
    const purchases = (data.expectedSupplies ?? []).filter((supply) => supply.unit === unit && supply.notReceived + supply.inInspection > 0);
    const sales = (data.salesLines ?? []).filter((line) => line.unit === unit && line.openQuantity > 0);

    return (
        <div className="grid gap-4 py-2 md:grid-cols-3" data-testid="sales-breakdown">
            <section>
                <h3 className={h3}>{t('sales_breakdown_lots')}</h3>
                {lots.length === 0 ? <p className="text-sm text-gray-500">{t('sales_breakdown_none')}</p> : (
                    <ul className="mt-1 space-y-1 text-sm">
                        {lots.map((line) => (
                            <li key={line.palletContentLineId} className="flex flex-wrap items-center gap-x-2">
                                <span className="font-mono">{line.lotNumber}</span>
                                <span className="text-gray-600">{line.palletReference}</span>
                                <QualityBadge status={line.qualityStatus} />
                                <span className="ml-auto font-medium">{q(line.qualityStatus === 'Approved' ? line.availableQuantity : line.onHandQuantity)}</span>
                            </li>
                        ))}
                    </ul>
                )}
            </section>
            <section>
                <h3 className={h3}>{t('sales_breakdown_purchases')}</h3>
                {purchases.length === 0 ? <p className="text-sm text-gray-500">{t('sales_breakdown_none')}</p> : (
                    <ul className="mt-1 space-y-1 text-sm">
                        {purchases.map((supply) => (
                            <li key={supply.supplyCommitmentLineId} className="flex flex-wrap items-center gap-x-2">
                                <Link href={`/purchase-orders?id=${supply.supplyCommitmentId}`} className="font-medium text-blue-800 underline underline-offset-2">
                                    {supply.supplyOrderNumber ?? `#${supply.supplyCommitmentId}`}
                                </Link>
                                {supply.expectedAt && <span className="text-gray-600">{t('sales_expected_on', { date: formatCalendarDate(supply.expectedAt, locale) })}</span>}
                                {supply.inInspection > 0 && <span className="text-xs text-gray-500">{t('sales_breakdown_in_inspection', { quantity: q(supply.inInspection) })}</span>}
                                <span className="ml-auto font-medium">{q(supply.notReceived + supply.inInspection)}</span>
                            </li>
                        ))}
                    </ul>
                )}
            </section>
            <section>
                <h3 className={h3}>{t('sales_breakdown_sales')}</h3>
                {sales.length === 0 ? <p className="text-sm text-gray-500">{t('sales_breakdown_none')}</p> : (
                    <ul className="mt-1 space-y-1.5 text-sm">
                        {sales.map((line) => (
                            <li key={line.salesOrderLineId}>
                                <div className="flex flex-wrap items-center gap-x-2">
                                    <Link href={`/sales-orders?orderId=${line.salesOrderId}`} className="font-medium text-blue-800 underline underline-offset-2">{line.orderNumber}</Link>
                                    <span className="text-gray-700">{line.customerName}</span>
                                    <span className="text-xs text-gray-500">{t(line.supplySource === 'Expected' ? 'sales_source_expected' : 'sales_source_stock')}</span>
                                    <span className="ml-auto font-medium">{q(line.openQuantity)}</span>
                                </div>
                                <CoverageText parts={coverageParts(line.coverage)} reserved={line.reservedQuantity} />
                            </li>
                        ))}
                    </ul>
                )}
            </section>
        </div>
    );
}

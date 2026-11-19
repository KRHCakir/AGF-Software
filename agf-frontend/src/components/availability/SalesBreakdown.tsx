'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity, locationKey } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { coverageParts } from '../../lib/salesSupply';
import { getAvailabilityVariant, toAvailabilityError } from '../../services/availabilityService';
import { AvailabilityDrillDown, PackageUnit } from '../../types/availability';
import type { AvailabilitySalesLine } from '../../types/salesSupply';
import PartijNumberLink from '../partij/PartijNumberLink';
import QualityBadge from './QualityBadge';
import CoverageText from './CoverageText';
import { ErrorState, LoadState, LoadingRows } from './StateViews';

const h3 = 'text-xs font-semibold uppercase tracking-wide text-gray-600';

// Uitklapbaar onder een rij van de verkoopweergave: dezelfde stand uitgesplitst in de volgorde van de kolommen (fixes 5b): Besteld per
// inkooporder, Voorraad per partij (goedgekeurd en in keuring) en Verkocht per verkooporder (met dekking, oudste verkoop eerst). Bij elk
// punt staat waar de handel is: de plek van de pallet (Pallet.LocationContext en WarehouseLocation), "nog niet binnen" voor besteld en
// "nog niet toegewezen" voor verkocht zonder reservering. Alleen lezen, uit de bestaande drill-down van de API.
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
    // Plek van een pallet: de locatie (Magazijn, Onderweg, …) en de plek in het magazijn uit de pallet zelf (bijvoorbeeld Cel 3).
    const where = (context: string, bin: string | null) => {
        const key = locationKey(context);
        const place = key === null ? context : t(key);
        return context === 'Warehouse' ? `${place} · ${bin ?? t('sales_location_no_bin')}` : place;
    };
    const inspectionOf = (supplyLineId: number) => data.contentLines.filter((line) => line.supplyCommitmentLineId === supplyLineId && line.category === 'warehouse'
        && line.unit === unit && line.qualityStatus === 'Quarantine' && line.onHandQuantity > 0);
    const unassigned = (line: AvailabilitySalesLine) => Math.max(0, line.openQuantity - (line.locations ?? []).reduce((sum, location) => sum + location.quantity, 0));

    return (
        <div className="grid gap-4 py-2 md:grid-cols-3" data-testid="sales-breakdown">
            <section>
                <h3 className={h3}>{t('sales_breakdown_purchases')}</h3>
                {purchases.length === 0 ? <p className="text-sm text-gray-500">{t('sales_breakdown_none')}</p> : (
                    <ul className="mt-1 space-y-1 text-sm">
                        {purchases.map((supply) => (
                            <li key={supply.supplyCommitmentLineId}>
                                <div className="flex flex-wrap items-center gap-x-2">
                                <Link href={`/purchase-orders?id=${supply.supplyCommitmentId}`} className="font-medium text-blue-800 underline underline-offset-2">
                                    {supply.supplyOrderNumber ?? `#${supply.supplyCommitmentId}`}
                                </Link>
                                {supply.expectedAt && <span className="text-gray-600">{t('sales_expected_on', { date: formatCalendarDate(supply.expectedAt, locale) })}</span>}
                                {supply.inInspection > 0 && <span className="text-xs text-gray-500">{t('sales_breakdown_in_inspection', { quantity: q(supply.inInspection) })}</span>}
                                <span className="ml-auto font-medium">{q(supply.notReceived + supply.inInspection)}</span>
                                </div>
                                {/* Waar het besteld deel is: nog niet binnen, of al in keuring op een pallet in het magazijn. */}
                                <ul className="text-xs text-gray-700" data-testid="purchase-locations">
                                    {supply.notReceived > 0 && <li>{q(supply.notReceived)} {t('sales_location_not_in_yet')}</li>}
                                    {inspectionOf(supply.supplyCommitmentLineId).map((line) => (
                                        <li key={line.palletContentLineId}>{t('sales_location_on_pallet', { quantity: q(line.onHandQuantity), pallet: line.palletReference })} · {where(line.locationContext, line.warehouseLocation)}</li>
                                    ))}
                                </ul>
                            </li>
                        ))}
                    </ul>
                )}
            </section>
            <section>
                <h3 className={h3}>{t('sales_breakdown_lots')}</h3>
                {lots.length === 0 ? <p className="text-sm text-gray-500">{t('sales_breakdown_none')}</p> : (
                    <ul className="mt-1 space-y-1 text-sm">
                        {lots.map((line) => (
                            <li key={line.palletContentLineId} className="flex flex-wrap items-center gap-x-2">
                                <PartijNumberLink partijId={line.partijId} partijNumber={line.partijNumber} lotNumber={line.lotNumber} />
                                <span className="text-gray-600">{line.palletReference}</span>
                                <QualityBadge status={line.qualityStatus} />
                                <span className="text-gray-700" data-testid="lot-location">{where(line.locationContext, line.warehouseLocation)}</span>
                                {/* Voorraad van de partij (telt op tot de kolom Voorraad); wat daarvan al gereserveerd is, staat erbij. */}
                                {line.reservedQuantity > 0 && <span className="text-xs text-gray-500">{t('sales_of_which_reserved', { quantity: q(line.reservedQuantity) })}</span>}
                                <span className="ml-auto font-medium">{q(line.onHandQuantity)}</span>
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
                                {/* Waar de verkochte handel nu ligt: per reservering de pallet en haar plek; de rest is nog niet toegewezen. */}
                                <ul className="text-xs text-gray-700" data-testid="sale-locations">
                                    {(line.locations ?? []).map((location) => (
                                        <li key={location.palletId}>{t('sales_location_on_pallet', { quantity: q(location.quantity), pallet: location.palletReference })} · {where(location.locationContext, location.warehouseLocation)}</li>
                                    ))}
                                    {unassigned(line) > 0 && <li>{t(line.supplySource === 'Expected' ? 'sales_location_unassigned_expected' : 'sales_location_unassigned', { quantity: q(unassigned(line)) })}</li>}
                                </ul>
                            </li>
                        ))}
                    </ul>
                )}
            </section>
        </div>
    );
}

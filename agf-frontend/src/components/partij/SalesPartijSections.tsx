'use client';

import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { unitLabelKey } from '../../lib/units';
import { PACKAGE_UNITS, type PackageUnit } from '../../types/availability';
import type { PartijDetail } from '../../types/partij';
import { sectionClass } from '../purchase/sectionClass';

const h3 = 'mb-2 text-sm font-semibold text-gray-900';
const link = 'font-mono text-blue-800 underline underline-offset-2';

// Verkoop-partij (één verkooporder, één klant): kop met order en klant, besteld/gereserveerd/verzonden, en uit welke inkoop-partijen de
// colli komen (20 + 120 + 360 = 500). Afgeleid uit de reserveringen op palletregels; er is geen eigen verdeeltabel.
export function SalesPartijSummary({ partij }: { partij: PartijDetail }) {
    const { t, locale } = useTranslation();
    const qty = (value: number) => formatQuantity(value, locale);
    const unit = partij.unit !== null && (PACKAGE_UNITS as readonly string[]).includes(partij.unit) ? t(unitLabelKey(partij.unit as PackageUnit)) : t('partij_unit_mixed');
    return (
        <>
            <p className="mt-1 break-words text-sm text-gray-900" data-tour="sales-partij-order">
                {partij.salesOrderId !== null && <Link href={`/sales-orders?orderId=${partij.salesOrderId}`} className={`${link} font-semibold`}>{partij.salesOrderNumber ?? `#${partij.salesOrderId}`}</Link>}
                {' · '}{partij.customerName ?? '–'} · {partij.products.join(', ') || '–'}
                {partij.salesOrderStatus === 'Cancelled' && <span className="ml-1 text-xs text-gray-600">({t('sales_status_cancelled')})</span>}
            </p>
            <dl className="mt-2 grid grid-cols-3 gap-2 text-sm" data-tour="partij-totals">
                {([['partij_col_ordered', partij.orderedQuantity], ['partij_col_reserved', partij.reservedQuantity], ['partij_col_shipped', partij.shippedQuantity]] as const).map(([key, value]) => (
                    <div key={key} className="rounded border border-gray-200 bg-gray-50 px-2 py-1">
                        <dt className="text-xs text-gray-600">{t(key)}</dt>
                        <dd className="font-mono font-semibold text-gray-900">{qty(value)} <span className="text-xs font-normal text-gray-600">{unit}</span></dd>
                    </div>
                ))}
            </dl>
        </>
    );
}

export function SalesPartijSources({ partij }: { partij: PartijDetail }) {
    const { t, locale } = useTranslation();
    const qty = (value: number) => formatQuantity(value, locale);
    const unitText = (unit: string) => ((PACKAGE_UNITS as readonly string[]).includes(unit) ? t(unitLabelKey(unit as PackageUnit)) : unit);
    return (
        <section className={sectionClass} aria-labelledby="partij-sources">
            <h3 id="partij-sources" className={h3}>{t('partij_sources')}</h3>
            {partij.sources.length === 0 ? <p className="text-sm text-gray-600">{t('partij_sources_none')}</p> : (
                <ul className="divide-y divide-gray-100 text-sm">
                    {partij.sources.map((source) => (
                        <li key={`${source.partijId}-${source.product}-${source.unit}`} className="flex flex-wrap items-center gap-x-2 gap-y-0.5 py-1.5" data-tour="partij-source" data-tour-key={source.number ?? ''}>
                            {source.partijId > 0
                                ? <Link href={`/partijen?id=${source.partijId}`} className={`${link} font-semibold`}>{source.number ?? `#${source.partijId}`}</Link>
                                : <span className="font-mono text-gray-600">{t('partij_source_unknown')}</span>}
                            <span>· {source.vendorName ?? '–'} · {source.product ?? '–'}</span>
                            {source.reservedQuantity > 0 && <span className="font-mono">· {t('partij_reserved', { quantity: qty(source.reservedQuantity) })} {unitText(source.unit)}</span>}
                            {source.shippedQuantity > 0 && <span className="font-mono">· {t('partij_shipped', { quantity: qty(source.shippedQuantity) })} {unitText(source.unit)}</span>}
                        </li>
                    ))}
                </ul>
            )}
        </section>
    );
}

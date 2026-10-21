'use client';

import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { availabilityHref } from '../../lib/availabilityLinks';
import { visibleLines } from '../../lib/inspectionList';
import { countsAsAvailableNow } from '../../lib/qualityTransitions';
import { AvailableNow, InspectionPallet } from '../../services/inspectionService';
import { QualityStatus } from '../../types/availability';
import { PalletDetailLine } from '../../types/inspection';
import QualityBadge from '../availability/QualityBadge';
import InspectionLineRow from './InspectionLineRow';

export interface InspectionNotice {
    lineLabel: string;
    status: QualityStatus;
    productVariantId: number;
    unit: string;
    availableNow: AvailableNow | null;
}

// Context per pallet; elke actie hoort bij één regel. Alleen Warehouse-regels die nog te beoordelen zijn.
export default function PalletGroup({ context, actor, statusFilter, search, readOnly, notice, error, reloadToken, onSucceeded, onReload }: {
    context: InspectionPallet;
    actor: string;
    statusFilter: Record<QualityStatus, boolean>;
    search: string;
    readOnly: boolean;
    notice: InspectionNotice | null;
    error: string | null;
    reloadToken: number;
    onSucceeded: (line: PalletDetailLine, toStatus: QualityStatus) => Promise<void>;
    onReload: () => void;
}) {
    const { t, locale } = useTranslation();
    const { pallet } = context;
    // Dezelfde regelfilter als de pagina gebruikt om pallets te verbergen en te pagineren.
    const lines = visibleLines(pallet, statusFilter, search);

    return (
        <section aria-labelledby={`pallet-${pallet.id}`} className="rounded-md border border-gray-300 bg-white">
            <header className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-gray-200 bg-gray-50 px-3 py-2 text-sm">
                <h2 id={`pallet-${pallet.id}`} className="font-semibold text-gray-900">{t('inspection_pallet', { reference: pallet.reference })}</h2>
                <span className="text-gray-700">{t('inspection_sscc')}: <span className="font-mono">{pallet.sscc ?? t('inspection_none')}</span></span>
                <span className="text-gray-700">{t('supply_pallet_bin')}: {pallet.warehouseLocation ?? t('inspection_none')}</span>
                <span className="text-gray-700">
                    {t('inspection_receipt_registered')}: {context.receiptRegisteredAt ? formatDateTime(context.receiptRegisteredAt, locale) : t('availability_unknown')}
                </span>
            </header>

            {error && <p role="alert" className="mx-3 mt-2 rounded border border-red-300 bg-red-50 px-2 py-1 text-sm text-red-900">{error}</p>}

            {notice && (
                <div role="status" className={`mx-3 mt-2 rounded border px-3 py-2 text-sm ${countsAsAvailableNow(notice.status) ? 'border-green-300 bg-green-50 text-green-900' : 'border-gray-300 bg-gray-50 text-gray-900'}`}>
                    <p className="flex flex-wrap items-center gap-2 font-medium">
                        <span aria-hidden="true">✓</span>{notice.lineLabel}: <QualityBadge status={notice.status} />
                    </p>
                    <p className="mt-1">{countsAsAvailableNow(notice.status) ? t('inspection_notice_counts') : t('inspection_notice_not_counts')}</p>
                    <p className="mt-1">
                        {notice.availableNow === null
                            ? t('inspection_notice_no_supply', { unit: notice.unit })
                            : t('inspection_notice_current', {
                                unit: notice.unit,
                                quantity: formatQuantity(notice.availableNow.quantity, locale),
                                kg: formatKg(notice.availableNow.netWeightKg, locale),
                            })}
                    </p>
                    <Link href={availabilityHref({ route: 'Warehouse', focus: 'available', productVariantId: notice.productVariantId })}
                        className="mt-1 inline-block font-medium text-blue-800 underline underline-offset-2">
                        {t('supply_view_availability')}
                    </Link>
                </div>
            )}

            {readOnly ? null : lines.length === 0 ? (
                <p className="px-3 py-3 text-sm text-gray-700">{t('inspection_no_lines_in_filter')}</p>
            ) : (
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200 text-sm">
                        <caption className="sr-only">{t('inspection_pallet', { reference: pallet.reference })}</caption>
                        <thead className="text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-3 py-2">{t('supply_variant')}</th>
                                <th scope="col" className="px-3 py-2 text-right">{t('supply_quantity')}</th>
                                <th scope="col" className="px-3 py-2">{t('supply_unit')}</th>
                                <th scope="col" className="px-3 py-2 text-right">{t('supply_actual_net_kg')}</th>
                                <th scope="col" className="px-3 py-2">{t('inspection_supplier_origin')}</th>
                                <th scope="col" className="px-3 py-2">{t('supply_quality')}</th>
                                <th scope="col" className="px-3 py-2">{t('inspection_actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {lines.map((line) => (
                                <InspectionLineRow key={`${line.id}:${line.version}:${reloadToken}`} context={context} line={line} actor={actor}
                                    readOnly={readOnly} onSucceeded={onSucceeded} onReload={onReload} />
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </section>
    );
}

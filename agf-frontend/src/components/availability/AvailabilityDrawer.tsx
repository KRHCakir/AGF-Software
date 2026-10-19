'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import {
    categoryKey,
    commitmentStatusKey,
    formatDateTime,
    formatKg,
    formatQuantity,
    locationKey,
    logisticsKey,
} from '../../lib/availability';
import { getAvailabilityVariant, toAvailabilityError } from '../../services/availabilityService';
import { AvailabilityDrillDown, ContentLineAvailability, PackageUnit, SupplyRoute } from '../../types/availability';
import QualityBadge from './QualityBadge';
import { UnitBadge } from './QuantityCell';
import { EmptyState, ErrorState, LoadState, LoadingRows } from './StateViews';
import WarningBadges from './WarningBadges';

export interface DrillDownSelection {
    productVariantId: number;
    productName: string;
    variantName: string;
    route: SupplyRoute;
    unit: PackageUnit | null;
    customerId: number | null;
    customerName: string | null;
}

const th = 'px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';
const td = 'px-2 py-1.5 align-top';

// Zijpaneel met bronregels en palletinhoud. Alleen lezen; gesloten historie alleen na bewuste keuze.
export default function AvailabilityDrawer({ selection, onClose }: { selection: DrillDownSelection; onClose: () => void }) {
    const { t, locale } = useTranslation();
    const [includeClosed, setIncludeClosed] = useState(false);
    const [reloadToken, setReloadToken] = useState(0);
    const [state, setState] = useState<LoadState<AvailabilityDrillDown>>({ status: 'loading' });
    const closeButton = useRef<HTMLButtonElement>(null);

    const { productVariantId, route, unit, customerId } = selection;

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getAvailabilityVariant(productVariantId, { route, unit, customerId, includeClosed }, controller.signal)
            .then((data) => setState({ status: 'ready', data }))
            .catch((error: unknown) => {
                const apiError = toAvailabilityError(error);
                if (apiError.kind !== 'aborted') {
                    setState({ status: 'error', error: apiError });
                }
            });
        return () => controller.abort();
    }, [productVariantId, route, unit, customerId, includeClosed, reloadToken]);

    useEffect(() => {
        closeButton.current?.focus();
        const onKeyDown = (event: KeyboardEvent) => {
            if (event.key === 'Escape') {
                onClose();
            }
        };
        document.addEventListener('keydown', onKeyDown);
        return () => document.removeEventListener('keydown', onKeyDown);
    }, [onClose]);

    const label = (key: string | null, fallback: string) => (key === null ? fallback : t(key));
    const quantity = (value: number | null) => (value === null ? t('availability_unknown') : formatQuantity(value, locale));

    const renderContentRows = (lines: ContentLineAvailability[], history: boolean) => lines.map((line) => (
        <tr key={line.palletContentLineId} className={history ? 'bg-gray-50 text-gray-600' : undefined}>
            <td className={td}>
                <span className="font-mono">{line.palletReference}</span>
                {history && (
                    <span className="ml-1 inline-flex rounded border border-gray-400 bg-gray-200 px-1.5 text-xs font-medium text-gray-800">
                        {t('availability_history_badge')}
                    </span>
                )}
            </td>
            <td className={td}>{line.lotNumber}</td>
            <td className={td}>{label(categoryKey(line.category), line.category)}</td>
            <td className={td}>{label(logisticsKey(line.logisticsStatus), line.logisticsStatus)}</td>
            <td className={td}>
                {label(locationKey(line.locationContext), line.locationContext)}
                {line.warehouseLocation && <span className="block font-mono text-xs text-gray-500">{line.warehouseLocation}</span>}
            </td>
            <td className={td}><QualityBadge status={line.qualityStatus} /></td>
            <td className={`${td} text-right`}>
                {formatQuantity(line.onHandQuantity, locale)}
                {line.dispatchedQuantity > 0 && <span className="block text-xs text-gray-500">{t('availability_received_of', { quantity: formatQuantity(line.quantity, locale) })}</span>}
                {line.depleted && <span className="mt-1 inline-flex rounded border border-gray-400 bg-gray-200 px-1.5 text-xs font-medium text-gray-800">{t('sales_depleted')}</span>}
            </td>
            <td className={td}><UnitBadge unit={line.unit} /></td>
            <td className={`${td} text-right`}>
                {formatKg(line.onHandNetWeightKg, locale)}
                {line.dispatchedNetWeightKg > 0 && <span className="block text-xs text-gray-500">{t('availability_received_of', { quantity: formatKg(line.netWeightKg, locale) })}</span>}
            </td>
            <td className={`${td} text-right`}>
                {formatQuantity(line.reservedQuantity, locale)}
                {line.onHold && (
                    <span className="ml-1 inline-flex rounded border border-amber-300 bg-amber-50 px-1.5 text-xs font-medium text-amber-900">
                        {t('availability_on_hold_badge')}
                    </span>
                )}
            </td>
            <td className={`${td} text-right`}>
                {formatQuantity(line.availableQuantity, locale)}
                <span className="block text-xs text-gray-500">{formatKg(line.availableNetWeightKg, locale)}</span>
            </td>
        </tr>
    ));

    const contentHead = (
        <thead className="bg-gray-50">
            <tr>
                <th scope="col" className={th}>{t('availability_col_pallet')}</th>
                <th scope="col" className={th}>{t('availability_col_lot')}</th>
                <th scope="col" className={th}>{t('availability_col_category')}</th>
                <th scope="col" className={th}>{t('availability_col_logistics_status')}</th>
                <th scope="col" className={th}>{t('availability_col_location')}</th>
                <th scope="col" className={th}>{t('availability_col_quality')}</th>
                <th scope="col" className={`${th} text-right`}>{t('availability_col_quantity')}</th>
                <th scope="col" className={th}>{t('availability_col_unit')}</th>
                <th scope="col" className={`${th} text-right`}>{t('availability_col_actual_kg')}</th>
                <th scope="col" className={`${th} text-right`}>{t('availability_col_reserved')}</th>
                <th scope="col" className={`${th} text-right`}>{t('availability_col_available_after')}</th>
            </tr>
        </thead>
    );

    return (
        <div className="fixed inset-0 z-40 flex justify-end">
            <div aria-hidden="true" className="absolute inset-0 bg-black/30" onClick={onClose} />
            <section role="dialog" aria-modal="true" aria-labelledby="availability-drawer-title"
                className="relative flex h-full w-[56rem] max-w-[95vw] flex-col bg-white shadow-xl">
                <header className={`border-b px-5 py-4 ${route === 'DirectTrade' ? 'border-violet-300 bg-violet-50' : 'border-gray-200 bg-gray-50'}`}>
                    <div className="flex items-start justify-between gap-4">
                        <div>
                            <h2 id="availability-drawer-title" className="text-lg font-semibold text-gray-900">
                                {selection.productName} · {selection.variantName}
                            </h2>
                            <div className="mt-1 flex flex-wrap items-center gap-2 text-sm">
                                <UnitBadge unit={unit} />
                                {route === 'DirectTrade' ? (
                                    <span className="inline-flex items-center gap-1 rounded border border-violet-300 bg-violet-100 px-2 py-0.5 text-xs font-semibold text-violet-900">
                                        <span aria-hidden="true">🔒</span>
                                        {t('availability_bound_to', { customer: selection.customerName ?? t('availability_customer_unknown') })}
                                    </span>
                                ) : (
                                    <span className="inline-flex rounded border border-gray-300 bg-white px-2 py-0.5 text-xs text-gray-800">
                                        {t('availability_warehouse_title')}
                                    </span>
                                )}
                            </div>
                        </div>
                        <button ref={closeButton} type="button" onClick={onClose}
                            className="rounded border border-gray-300 bg-white px-3 py-1 text-sm text-gray-800 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500">
                            {t('availability_close')}
                        </button>
                    </div>
                    <label className="mt-3 flex items-start gap-2 text-sm text-gray-800">
                        <input type="checkbox" checked={includeClosed} onChange={(event) => setIncludeClosed(event.target.checked)} className="mt-0.5" />
                        <span>
                            {t('availability_show_closed_history')}
                            <span className="block text-xs text-gray-600">{t('availability_show_closed_history_hint')}</span>
                        </span>
                    </label>
                </header>

                <div className="flex-1 space-y-6 overflow-y-auto px-5 py-4">
                    {state.status === 'loading' && <LoadingRows rows={5} label={t('availability_loading')} />}
                    {state.status === 'error' && <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />}
                    {state.status === 'ready' && (() => {
                        const active = state.data.contentLines.filter((line) => !line.closed);
                        const history = state.data.contentLines.filter((line) => line.closed);
                        return (
                            <>
                                <section aria-labelledby="availability-source-lines">
                                    <h3 id="availability-source-lines" className="mb-2 font-semibold text-gray-900">{t('availability_source_lines_title')}</h3>
                                    {state.data.sourceLines.length === 0 ? (
                                        <EmptyState title={t('availability_source_lines_empty')} />
                                    ) : (
                                        <div className="overflow-x-auto rounded border border-gray-200">
                                            <table className="min-w-full divide-y divide-gray-200 text-sm">
                                                <thead className="bg-gray-50">
                                                    <tr>
                                                        <th scope="col" className={th}>{t('availability_col_commitment')}</th>
                                                        <th scope="col" className={th}>{t('availability_col_status')}</th>
                                                        <th scope="col" className={th}>{t('availability_col_expected_at')}</th>
                                                        <th scope="col" className={th}>{t('availability_col_provisional_lot')}</th>
                                                        <th scope="col" className={`${th} text-right`}>{t('availability_col_expected')}</th>
                                                        <th scope="col" className={`${th} text-right`}>{t('availability_col_declared_kg')}</th>
                                                        <th scope="col" className={`${th} text-right`}>{t('availability_col_registered')}</th>
                                                        <th scope="col" className={`${th} text-right`}>{t('availability_col_unregistered')}</th>
                                                        <th scope="col" className={th}>{t('availability_col_signals')}</th>
                                                    </tr>
                                                </thead>
                                                <tbody className="divide-y divide-gray-100">
                                                    {state.data.sourceLines.map((line) => (
                                                        <tr key={line.supplyCommitmentLineId}>
                                                            <td className={td}>
                                                                #{line.supplyCommitmentId}
                                                                {line.customerName && <span className="block text-xs text-violet-900">🔒 {line.customerName}</span>}
                                                            </td>
                                                            <td className={td}>{label(commitmentStatusKey(line.commitmentStatus), line.commitmentStatus)}</td>
                                                            <td className={td}>{line.expectedAt ? formatDateTime(line.expectedAt, locale) : '–'}</td>
                                                            <td className={td}>{line.lotNumber ?? '–'}</td>
                                                            <td className={`${td} text-right`}>{formatQuantity(line.expectedQuantity, locale)}</td>
                                                            <td className={`${td} text-right`}>
                                                                {line.declaredExpectedNetWeightKg === null ? '–' : formatKg(line.declaredExpectedNetWeightKg, locale)}
                                                            </td>
                                                            <td className={`${td} text-right`}>{quantity(line.registeredQuantity)}</td>
                                                            <td className={`${td} text-right`}>{formatQuantity(line.unregisteredQuantity, locale)}</td>
                                                            <td className={td}><WarningBadges warnings={line.warnings} /></td>
                                                        </tr>
                                                    ))}
                                                </tbody>
                                            </table>
                                        </div>
                                    )}
                                </section>

                                <section aria-labelledby="availability-content-lines">
                                    <h3 id="availability-content-lines" className="mb-2 font-semibold text-gray-900">{t('availability_content_lines_title')}</h3>
                                    {active.length === 0 ? (
                                        <EmptyState title={t('availability_content_lines_empty')} />
                                    ) : (
                                        <div className="overflow-x-auto rounded border border-gray-200">
                                            <table className="min-w-full divide-y divide-gray-200 text-sm">
                                                {contentHead}
                                                <tbody className="divide-y divide-gray-100">{renderContentRows(active, false)}</tbody>
                                            </table>
                                        </div>
                                    )}
                                </section>

                                {includeClosed && (
                                    <section aria-labelledby="availability-history-lines">
                                        <h3 id="availability-history-lines" className="mb-1 font-semibold text-gray-700">{t('availability_history_title')}</h3>
                                        <p className="mb-2 text-xs text-gray-600">{t('availability_history_hint')}</p>
                                        {history.length === 0 ? (
                                            <EmptyState title={t('availability_history_empty')} />
                                        ) : (
                                            <div className="overflow-x-auto rounded border border-dashed border-gray-300">
                                                <table className="min-w-full divide-y divide-gray-200 text-sm">
                                                    {contentHead}
                                                    <tbody className="divide-y divide-gray-100">{renderContentRows(history, true)}</tbody>
                                                </table>
                                            </div>
                                        )}
                                    </section>
                                )}

                                <p className="text-xs text-gray-500">{t('availability_generated_at', { time: formatDateTime(state.data.generatedAt, locale) })}</p>
                            </>
                        );
                    })()}
                </div>
            </section>
        </div>
    );
}

'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import AvailabilityDrawer, { DrillDownSelection } from '../../../components/availability/AvailabilityDrawer';
import DataBasisNote from '../../../components/availability/DataBasisNote';
import DirectTradeSupplyTable from '../../../components/availability/DirectTradeSupplyTable';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import WarehouseSupplyTable, { AvailabilityView, DETAILED_ONLY_FOCUS } from '../../../components/availability/WarehouseSupplyTable';
import { useTranslation } from '../../../i18n/LanguageContext';
import { DirectTradeRow, UnitFilter, WarehouseRow, directTradeRows, formatTime, warehouseRows } from '../../../lib/availability';
import { unitLabelKey } from '../../../lib/units';
import { parseAvailabilityParams } from '../../../lib/availabilityLinks';
import { getAvailabilityOverview, toAvailabilityError } from '../../../services/availabilityService';
import { AvailabilityOverview, PACKAGE_UNITS } from '../../../types/availability';

// Alleen-lezen overzicht van de beschikbaarheid uit de echte Availability API. Geen schrijfacties.
// useSearchParams vraagt in Next.js 15 een Suspense-grens; zo blijft de pagina statisch te genereren.
export default function AvailabilityPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('availability_loading')} />}>
            <AvailabilityWorkspace />
        </Suspense>
    );
}

const VIEW_STORAGE_KEY = 'agf_availability_view';

function AvailabilityWorkspace() {
    const { t, locale } = useTranslation();
    // URL-filters uit contextlinks (bijvoorbeeld vanuit Aanvoer): echte API-filters; focus markeert alleen een kolom.
    const searchParams = useSearchParams();
    const linkFilters = useMemo(() => parseAvailabilityParams(searchParams), [searchParams]);
    const { productVariantId, route, customerId, unit } = linkFilters.query;
    const focus = linkFilters.focus;
    const hasLinkFilters = route !== undefined || productVariantId !== undefined || customerId !== undefined || unit !== undefined || focus !== null;
    const [state, setState] = useState<LoadState<AvailabilityOverview>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);
    const [search, setSearch] = useState('');
    const [unitFilter, setUnitFilter] = useState<UnitFilter>('all');
    const [showWarehouse, setShowWarehouse] = useState(true);
    const [showDirectTrade, setShowDirectTrade] = useState(true);
    const [selection, setSelection] = useState<DrillDownSelection | null>(null);
    // Compact is de standaard; de keuze wordt per browser onthouden. Een contextlink die een kolom markeert die alleen in
    // de uitgebreide weergave staat, toont die weergave altijd.
    const [view, setViewState] = useState<AvailabilityView>('compact');
    useEffect(() => {
        try {
            if (localStorage.getItem(VIEW_STORAGE_KEY) === 'detailed') setViewState('detailed');
        } catch {
            // Opslag niet beschikbaar: compact blijft de standaard.
        }
    }, []);
    const setView = (next: AvailabilityView) => {
        setViewState(next);
        try {
            localStorage.setItem(VIEW_STORAGE_KEY, next);
        } catch {
            // Alleen een gemak per browser; zonder opslag werkt de keuze voor deze pagina.
        }
    };
    const forcedDetailed = focus !== null && DETAILED_ONLY_FOCUS.includes(focus);
    const effectiveView: AvailabilityView = forcedDetailed ? 'detailed' : view;

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getAvailabilityOverview({ productVariantId, route, customerId, unit }, controller.signal)
            .then((data) => setState({ status: 'ready', data }))
            .catch((error: unknown) => {
                const apiError = toAvailabilityError(error);
                if (apiError.kind !== 'aborted') {
                    setState({ status: 'error', error: apiError });
                }
            });
        return () => controller.abort();
    }, [reloadToken, productVariantId, route, customerId, unit]);

    const reload = useCallback(() => setReloadToken((n) => n + 1), []);
    const closeDrawer = useCallback(() => setSelection(null), []);

    const variants = state.status === 'ready' ? state.data.variants : null;
    const warehouse = useMemo(() => (variants ? warehouseRows(variants, search, unitFilter) : []), [variants, search, unitFilter]);
    const directTrade = useMemo(() => (variants ? directTradeRows(variants, search, unitFilter) : []), [variants, search, unitFilter]);
    const filtersActive = search.trim() !== '' || unitFilter !== 'all';

    const openWarehouse = (row: WarehouseRow) => setSelection({
        productVariantId: row.variant.productVariantId,
        productName: row.variant.productName,
        variantName: row.variant.variantName,
        route: 'Warehouse',
        unit: row.supply.unit,
        customerId: null,
        customerName: null,
    });

    const openDirectTrade = (row: DirectTradeRow) => setSelection({
        productVariantId: row.variant.productVariantId,
        productName: row.variant.productName,
        variantName: row.variant.variantName,
        route: 'DirectTrade',
        unit: row.supply.unit,
        customerId: row.supply.customerId,
        customerName: row.supply.customerName,
    });

    const clearFilters = (
        <button type="button" onClick={() => { setSearch(''); setUnitFilter('all'); }}
            className="rounded border border-gray-300 bg-white px-3 py-1 text-sm text-gray-800 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500">
            {t('availability_clear_filters')}
        </button>
    );

    return (
        <div className="mx-auto max-w-[110rem] space-y-4 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 data-tour="page-title" className="text-2xl font-bold text-gray-900">{t('availability')}</h1>
                    <p className="text-sm text-gray-600">{t('availability_subtitle')}</p>
                </div>
                <div className="flex items-center gap-3 text-sm text-gray-600">
                    {state.status === 'ready' && <span>{t('availability_updated_at', { time: formatTime(state.data.generatedAt, locale) })}</span>}
                    <button type="button" onClick={reload} disabled={state.status === 'loading'}
                        className="rounded border border-gray-300 bg-white px-3 py-1 font-medium text-gray-800 hover:bg-gray-100 disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-blue-500">
                        <span aria-hidden="true">↻ </span>{t('availability_refresh')}
                    </button>
                </div>
            </div>

            {state.status === 'ready' && <DataBasisNote basis={state.data.basis} />}

            {(hasLinkFilters || linkFilters.invalid.length > 0) && (
                <div role="status" className="flex flex-wrap items-center gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
                    {hasLinkFilters && <span className="font-medium">{t('availability_link_filtered')}</span>}
                    {route !== undefined && (
                        <span className="rounded border border-amber-300 bg-white px-2 py-0.5">
                            {route === 'DirectTrade' ? t('availability_direct_title') : t('availability_warehouse_title')}
                        </span>
                    )}
                    {productVariantId !== undefined && (
                        <span className="rounded border border-amber-300 bg-white px-2 py-0.5">
                            {(() => {
                                const variant = state.status === 'ready' ? state.data.variants.find((v) => v.productVariantId === productVariantId) : undefined;
                                return variant ? `${variant.productName} · ${variant.variantName}` : t('availability_link_variant', { id: String(productVariantId) });
                            })()}
                        </span>
                    )}
                    {customerId !== undefined && (
                        <span className="rounded border border-violet-300 bg-white px-2 py-0.5 text-violet-900">
                            🔒 {(() => {
                                const name = state.status === 'ready'
                                    ? state.data.variants.flatMap((v) => v.directTradeSupply).find((d) => d.customerId === customerId)?.customerName
                                    : undefined;
                                return name ?? t('availability_link_customer', { id: String(customerId) });
                            })()}
                        </span>
                    )}
                    {unit !== undefined && <span className="rounded border border-amber-300 bg-white px-2 py-0.5">{t(unitLabelKey(unit))}</span>}
                    {focus !== null && <span className="rounded border border-amber-400 bg-white px-2 py-0.5">{t('availability_link_focus', { column: t(`availability_focus_${focus}`) })}</span>}
                    {linkFilters.invalid.length > 0 && <span>{t('availability_link_invalid', { fields: linkFilters.invalid.join(', ') })}</span>}
                    <Link href="/availability" className="ml-auto font-medium text-blue-800 underline underline-offset-2">{t('availability_link_show_all')}</Link>
                </div>
            )}

            <div className="flex flex-wrap items-end gap-4 rounded-md border border-gray-200 bg-white px-3 py-2">
                <label className="flex flex-col text-xs font-medium text-gray-700">
                    {t('availability_search_label')}
                    <input type="search" value={search} onChange={(event) => setSearch(event.target.value)}
                        placeholder={t('availability_search_placeholder')}
                        className="mt-1 w-72 rounded border border-gray-300 px-2 py-1 text-sm font-normal text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </label>
                <label className="flex flex-col text-xs font-medium text-gray-700">
                    {t('availability_unit_filter_label')}
                    <select value={unitFilter} onChange={(event) => setUnitFilter(event.target.value as UnitFilter)}
                        className="mt-1 rounded border border-gray-300 px-2 py-1 text-sm font-normal text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-500">
                        <option value="all">{t('availability_unit_all')}</option>
                        {PACKAGE_UNITS.map((unit) => <option key={unit} value={unit}>{t(unitLabelKey(unit))}</option>)}
                        <option value="unknown">{t('availability_unit_unknown')}</option>
                    </select>
                </label>
                <fieldset className="flex items-center gap-4 text-sm text-gray-800">
                    <legend className="sr-only">{t('availability_sections_label')}</legend>
                    <label className="flex items-center gap-1">
                        <input type="checkbox" checked={showWarehouse} onChange={(event) => setShowWarehouse(event.target.checked)} />
                        {t('availability_warehouse_title')}
                    </label>
                    <label className="flex items-center gap-1">
                        <input type="checkbox" checked={showDirectTrade} onChange={(event) => setShowDirectTrade(event.target.checked)} />
                        {t('availability_direct_title')}
                    </label>
                </fieldset>
            </div>

            {state.status === 'loading' && <LoadingRows label={t('availability_loading')} />}
            {state.status === 'error' && <ErrorState error={state.error} onRetry={reload} />}

            {state.status === 'ready' && state.data.variants.length === 0 && (
                <EmptyState title={t('availability_empty_title')} description={t('availability_empty_description')} />
            )}

            {state.status === 'ready' && state.data.variants.length > 0 && (
                <>
                    {showWarehouse && route !== 'DirectTrade' && (
                        <section aria-labelledby="availability-warehouse" className="space-y-2">
                            <div className="flex flex-wrap items-end justify-between gap-3">
                                <div>
                                    <h2 id="availability-warehouse" className="text-lg font-semibold text-gray-900">{t('availability_warehouse_title')}</h2>
                                    <p className="text-sm text-gray-600">{t('availability_warehouse_hint')}</p>
                                </div>
                                <div role="group" aria-label={t('availability_sections_label')} className="inline-flex rounded border border-gray-300 bg-white text-sm">
                                    <button type="button" onClick={() => setView('compact')} aria-pressed={effectiveView === 'compact'} disabled={forcedDetailed}
                                        className={`rounded-l px-3 py-1 font-medium focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:cursor-not-allowed disabled:opacity-50 ${effectiveView === 'compact' ? 'bg-blue-100 text-blue-900' : 'text-gray-800 hover:bg-gray-100'}`}>
                                        {t('availability_view_compact')}
                                    </button>
                                    <button type="button" onClick={() => setView('detailed')} aria-pressed={effectiveView === 'detailed'}
                                        className={`rounded-r border-l border-gray-300 px-3 py-1 font-medium focus:outline-none focus:ring-2 focus:ring-blue-500 ${effectiveView === 'detailed' ? 'bg-blue-100 text-blue-900' : 'text-gray-800 hover:bg-gray-100'}`}>
                                        {t('availability_view_detailed')}
                                    </button>
                                </div>
                            </div>
                            {effectiveView === 'compact' && <p className="text-xs text-gray-600">{t('availability_view_hint_compact')}</p>}
                            {warehouse.length === 0
                                ? <EmptyState title={filtersActive ? t('availability_no_results', { search: search.trim() }) : t('availability_warehouse_empty')}
                                    action={filtersActive ? clearFilters : undefined} />
                                : <WarehouseSupplyTable rows={warehouse} onOpen={openWarehouse} focus={focus} view={effectiveView} />}
                        </section>
                    )}

                    {showDirectTrade && route !== 'Warehouse' && (
                        <section aria-labelledby="availability-direct" className="space-y-2">
                            <div>
                                <h2 id="availability-direct" className="flex items-center gap-2 text-lg font-semibold text-violet-900">
                                    <span aria-hidden="true">🔒</span>{t('availability_direct_title')}
                                </h2>
                                <p className="text-sm text-violet-900">{t('availability_direct_hint')}</p>
                            </div>
                            {directTrade.length === 0
                                ? <EmptyState title={filtersActive ? t('availability_no_results', { search: search.trim() }) : t('availability_direct_empty')}
                                    action={filtersActive ? clearFilters : undefined} />
                                : <DirectTradeSupplyTable rows={directTrade} onOpen={openDirectTrade} focus={focus} />}
                        </section>
                    )}
                </>
            )}

            {selection && (
                <AvailabilityDrawer
                    key={`${selection.productVariantId}:${selection.route}:${selection.customerId ?? ''}:${selection.unit ?? 'unknown'}`}
                    selection={selection}
                    onClose={closeDrawer}
                />
            )}
        </div>
    );
}

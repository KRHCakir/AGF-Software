'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import PalletGroup, { InspectionNotice } from '../../../components/inspection/PalletGroup';
import { inputClass } from '../../../components/supply/FieldError';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatTime } from '../../../lib/availability';
import { filterPallets, paginate } from '../../../lib/inspectionList';
import { isInspectable } from '../../../lib/qualityTransitions';
import { toApiError } from '../../../services/apiClient';
import { InspectionPallet, getAvailableNow, isReceivedInWarehouse, loadInspectionQueue, loadPalletContext } from '../../../services/inspectionService';
import { QualityStatus } from '../../../types/availability';
import { PalletDetailLine } from '../../../types/inspection';

// Kwaliteitswerkplek "Keuring": uitsluitend per PalletContentLine op een pallet die Received + Warehouse is.
// Geen bulk, geen palletbrede goedkeuring, geen DirectTrade-keuring, geen foto's en geen voorraadmutaties.
export default function InspectionPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('inspection_loading')} />}>
            <InspectionWorkspace />
        </Suspense>
    );
}

const FILTERABLE: QualityStatus[] = ['Quarantine', 'Blocked', 'Approved'];

// Vaste demokeuzes voor wie beoordeelt; "Anders" laat een naam typen. Geen authenticatie.
const DEMO_INSPECTORS = ['Jan', 'Steve'] as const;

function parsePalletId(value: string | null): number | null | 'invalid' {
    if (value === null) return null;
    return /^[1-9][0-9]{0,9}$/.test(value) ? Number(value) : 'invalid';
}

function InspectionWorkspace() {
    const { t, locale } = useTranslation();
    const searchParams = useSearchParams();
    const palletParam = useMemo(() => parsePalletId(searchParams.get('palletId')), [searchParams]);
    // Wie beoordeelt: bewust niet vooraf gekozen en niet uit de browseropslag; geen beveiligde accountidentiteit.
    const [actorChoice, setActorChoice] = useState<'' | (typeof DEMO_INSPECTORS)[number] | 'other'>('');
    const [actorOther, setActorOther] = useState('');
    const actor = actorChoice === 'other' ? actorOther : actorChoice;
    const [state, setState] = useState<LoadState<InspectionPallet[]>>({ status: 'loading' });
    const [loadedAt, setLoadedAt] = useState<string | null>(null);
    const [reloadToken, setReloadToken] = useState(0);
    const [palletTokens, setPalletTokens] = useState<Record<number, number>>({});
    const [notices, setNotices] = useState<Record<number, InspectionNotice>>({});
    const [groupErrors, setGroupErrors] = useState<Record<number, string>>({});
    // Zoekterm mag uit de link komen (?search=DEMO, bijvoorbeeld vanuit de rondleiding).
    const searchParam = searchParams.get('search');
    const [search, setSearch] = useState(() => searchParam ?? '');
    const [statusFilter, setStatusFilter] = useState<Record<QualityStatus, boolean>>({ Quarantine: true, Blocked: true, Approved: true, Rejected: false });
    const [page, setPage] = useState(1);
    useEffect(() => { if (searchParam !== null) setSearch(searchParam); }, [searchParam]);
    // Gewijzigde filters: terug naar pagina 1. Een te hoge pagina na een actie wordt door paginate() zelf teruggezet.
    useEffect(() => { setPage(1); }, [search, statusFilter]);

    // Met ?palletId= laden we die pallet rechtstreeks (met alleen zijn eigen audit- en aanvoercontext), nooit eerst de wachtrij.
    useEffect(() => {
        if (palletParam === 'invalid') return;
        const controller = new AbortController();
        setState({ status: 'loading' });
        const load = palletParam === null
            ? loadInspectionQueue(controller.signal)
            : loadPalletContext(palletParam, controller.signal).then((context) => [context]);
        load
            .then((data) => {
                setState({ status: 'ready', data });
                setLoadedAt(new Date().toISOString());
            })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') {
                    setState({ status: 'error', error: apiError });
                }
            });
        return () => controller.abort();
    }, [palletParam, reloadToken]);

    const reloadPallet = useCallback(async (palletId: number): Promise<InspectionPallet | null> => {
        try {
            const fresh = await loadPalletContext(palletId);
            setState((current) => current.status === 'ready'
                ? { status: 'ready', data: current.data.map((context) => (context.pallet.id === palletId ? fresh : context)) }
                : current);
            setPalletTokens((current) => ({ ...current, [palletId]: (current[palletId] ?? 0) + 1 }));
            setGroupErrors((current) => {
                const next = { ...current };
                delete next[palletId];
                return next;
            });
            setLoadedAt(new Date().toISOString());
            return fresh;
        } catch {
            setGroupErrors((current) => ({ ...current, [palletId]: t('inspection_reload_failed') }));
            return null;
        }
    }, [t]);

    // Na een geslaagde keuring: eerst de pallet en de beschikbaarheid echt herladen, pas daarna de melding tonen.
    const onSucceeded = useCallback(async (line: PalletDetailLine, toStatus: QualityStatus) => {
        setNotices((current) => {
            const next = { ...current };
            delete next[line.palletId];
            return next;
        });
        const fresh = await reloadPallet(line.palletId);
        if (fresh === null) return;
        const reloaded = fresh.pallet.contentLines.find((candidate) => candidate.id === line.id);
        let availableNow = null;
        try {
            availableNow = await getAvailableNow(line.productVariantId, line.unit);
        } catch {
            setGroupErrors((current) => ({ ...current, [line.palletId]: t('inspection_availability_failed') }));
            return;
        }
        setNotices((current) => ({
            ...current,
            [line.palletId]: {
                lineLabel: `${line.productName ?? ''} · ${line.productVariantName ?? ''} · ${t('supply_lot')} ${line.lotNumber}`,
                status: reloaded?.qualityStatus ?? toStatus,
                productVariantId: line.productVariantId,
                unit: line.unit,
                availableNow,
            },
        }));
    }, [reloadPallet, t]);

    const single = palletParam !== null && palletParam !== 'invalid';

    return (
        <div className="mx-auto max-w-[96rem] space-y-4 p-2">
            <div className="flex flex-wrap items-end justify-between gap-4">
                <div>
                    <h1 data-tour="page-title" className="text-2xl font-bold text-gray-900">{t('inspection')}</h1>
                    <p className="text-sm text-gray-600">{t('inspection_subtitle')}</p>
                </div>
                <div className="flex items-end gap-4">
                    <div className="w-72">
                        <label htmlFor="inspection-actor" className="text-xs font-medium text-gray-700">{t('inspection_actor_label')} *</label>
                        <select id="inspection-actor" data-tour="inspection-actor" value={actorChoice} onChange={(event) => setActorChoice(event.target.value as typeof actorChoice)}
                            className={inputClass} aria-describedby="inspection-actor-hint">
                            <option value="">{t('inspection_actor_choose')}</option>
                            {DEMO_INSPECTORS.map((name) => <option key={name} value={name}>{name}</option>)}
                            <option value="other">{t('inspection_actor_other')}</option>
                        </select>
                        {actorChoice === 'other' && (
                            <input id="inspection-actor-other" data-tour="inspection-actor-other" aria-label={t('inspection_actor_other')} value={actorOther} maxLength={100} onChange={(event) => setActorOther(event.target.value)}
                                placeholder={t('supply_actor_placeholder')} className={inputClass} />
                        )}
                        <span id="inspection-actor-hint" className="block text-xs text-gray-500">{t('inspection_actor_hint')}</span>
                    </div>
                    <div className="flex items-center gap-3 text-sm text-gray-600">
                        {loadedAt && <span>{t('availability_updated_at', { time: formatTime(loadedAt, locale) })}</span>}
                        <button type="button" onClick={() => setReloadToken((n) => n + 1)} disabled={state.status === 'loading' || palletParam === 'invalid'}
                            className="rounded border border-gray-300 bg-white px-3 py-1 font-medium text-gray-800 hover:bg-gray-100 disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-blue-500">
                            <span aria-hidden="true">↻ </span>{t('availability_refresh')}
                        </button>
                    </div>
                </div>
            </div>

            <aside className="flex gap-2 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-sm text-sky-900">
                <span aria-hidden="true">ⓘ</span>
                <ul className="space-y-0.5">
                    <li>{t('inspection_scope_note')}</li>
                    <li>{t('inspection_available_note')}</li>
                </ul>
            </aside>

            {single && (
                <p className="flex flex-wrap items-center gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
                    {t('inspection_single_pallet')}
                    <Link href="/inspection" className="ml-auto font-medium text-blue-800 underline underline-offset-2">{t('inspection_show_queue')}</Link>
                </p>
            )}

            {palletParam === 'invalid' && (
                <p role="alert" className="rounded-md border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-900">
                    {t('inspection_invalid_pallet_link')} <Link href="/inspection" className="font-medium underline">{t('inspection_show_queue')}</Link>
                </p>
            )}

            {!single && palletParam !== 'invalid' && (
                <div className="flex flex-wrap items-end gap-4 rounded-md border border-gray-200 bg-white px-3 py-2">
                    <label className="flex flex-col text-xs font-medium text-gray-700">
                        {t('availability_search_label')}
                        <input type="search" value={search} onChange={(event) => setSearch(event.target.value)}
                            placeholder={t('inspection_search_placeholder')} className={`${inputClass} w-72`} />
                    </label>
                    <fieldset className="flex items-center gap-4 text-sm text-gray-800">
                        <legend className="sr-only">{t('inspection_status_filter')}</legend>
                        {FILTERABLE.map((status) => (
                            <label key={status} className="flex items-center gap-1">
                                <input type="checkbox" checked={statusFilter[status]}
                                    onChange={(event) => setStatusFilter((current) => ({ ...current, [status]: event.target.checked }))} />
                                {t(`availability_quality_${status.toLowerCase()}`)}
                            </label>
                        ))}
                    </fieldset>
                </div>
            )}

            {state.status === 'loading' && palletParam !== 'invalid' && <LoadingRows label={t('inspection_loading')} />}
            {state.status === 'error' && palletParam !== 'invalid' && (
                <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />
            )}

            {state.status === 'ready' && palletParam !== 'invalid' && (() => {
                if (!single && state.data.length === 0) {
                    return (
                        <div className="rounded-md border border-dashed border-gray-300 bg-white px-4 py-8 text-center">
                            <p className="font-medium text-gray-800">{t('inspection_queue_empty')}</p>
                            <p className="mt-1 text-sm text-gray-600">{t('inspection_queue_empty_hint')}</p>
                        </div>
                    );
                }
                // Eerst zoeken en kwaliteitsfilters, dan pagineren; pallets zonder passende regels verdwijnen volledig.
                const visible = single ? state.data : filterPallets(state.data, statusFilter, search);
                if (!single && visible.length === 0) {
                    return (
                        <p role="status" className="rounded-md border border-dashed border-gray-300 bg-white px-4 py-6 text-center text-sm text-gray-800">
                            {search.trim() === '' ? t('inspection_no_pallets_in_filter') : t('inspection_no_pallets_found', { query: search.trim() })}
                        </p>
                    );
                }
                const paged = single ? paginate(visible, 1, visible.length || 1) : paginate(visible, page);
                const pager = !single && (
                    <nav aria-label={t('inspection_pagination_label')} className="flex flex-wrap items-center justify-between gap-2 text-sm text-gray-800">
                        <span>{t('inspection_pagination', { page: String(paged.page), pages: String(paged.pageCount), count: String(paged.total) })}</span>
                        <span className="flex gap-2">
                            <button type="button" onClick={() => setPage(paged.page - 1)} disabled={paged.page <= 1}
                                className="rounded border border-gray-300 bg-white px-3 py-1 font-medium hover:bg-gray-100 disabled:opacity-50">{t('inspection_previous')}</button>
                            <button type="button" onClick={() => setPage(paged.page + 1)} disabled={paged.page >= paged.pageCount}
                                className="rounded border border-gray-300 bg-white px-3 py-1 font-medium hover:bg-gray-100 disabled:opacity-50">{t('inspection_next')}</button>
                        </span>
                    </nav>
                );
                return (<>{pager}{paged.items.map((context) => {
                    const inWarehouse = isReceivedInWarehouse(context.pallet);
                    const inspectableLines = context.pallet.contentLines.filter((line) => line.route === 'Warehouse' && isInspectable(line.qualityStatus));
                    const readOnly = !inWarehouse || inspectableLines.length === 0;
                    return (
                        <div key={context.pallet.id} className="space-y-2">
                            {single && !inWarehouse && (
                                <p role="status" className="rounded-md border border-gray-300 bg-gray-50 px-3 py-2 text-sm text-gray-900">
                                    {t('inspection_not_in_warehouse', { status: context.pallet.logisticsStatus, location: context.pallet.locationContext })}
                                </p>
                            )}
                            {single && inWarehouse && inspectableLines.length === 0 && !notices[context.pallet.id] && (
                                <p role="status" className="rounded-md border border-gray-300 bg-gray-50 px-3 py-2 text-sm text-gray-900">
                                    {t('inspection_nothing_to_inspect')}
                                </p>
                            )}
                            <PalletGroup
                                context={context}
                                actor={actor}
                                statusFilter={single ? { Quarantine: true, Blocked: true, Approved: true, Rejected: false } : statusFilter}
                                search={single ? '' : search}
                                readOnly={readOnly}
                                notice={notices[context.pallet.id] ?? null}
                                error={groupErrors[context.pallet.id] ?? null}
                                reloadToken={palletTokens[context.pallet.id] ?? 0}
                                onSucceeded={onSucceeded}
                                onReload={() => { void reloadPallet(context.pallet.id); }}
                            />
                        </div>
                    );
                })}{paged.items.length > 3 && pager}</>);
            })()}
        </div>
    );
}

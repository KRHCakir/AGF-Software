'use client';

import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { formatDecimalInput } from '../../lib/decimal';
import { newRequestId } from '../../lib/requestId';
import { DEFAULT_ADDRESS, OTHER_ADDRESS, type TransportDraft, type TransportProblem, draftFrom, requestFrom } from '../../lib/salesTransport';
import { ApiError, toApiError } from '../../services/apiClient';
import { addRelationRole, createCarrier, listRelations } from '../../services/relationService';
import { deleteSalesOrderTransport, getSalesOrderTransport, saveSalesOrderTransport } from '../../services/salesOrderTransportService';
import type { SalesOrder } from '../../types/salesOrder';
import type { RelationListItem, SimilarRelation } from '../../types/relation';
import type { SalesOrderTransport as Transport } from '../../types/salesOrderTransport';
import DateField from '../shared/DateField';
import LookupField, { type LookupMatch } from '../shared/LookupField';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Blok Transport op de verkooporder (documenten deel 2): alleen invullen als er transport is. Vervoerder (een relatie met de rol
// vervoerder, zoeken of snel aanmaken) of eigen transport / klant haalt op; laden op (dezelfde als op de laadbon), losdatum, temperatuur,
// laad- en losplaats (leeg = de standaard), referenties, soort pallet en opmerkingen. Na opslaan haalt de pagina de order en de
// documenten opnieuw op: een bevestigde order krijgt zo de transportopdracht of een nieuwe versie. Nooit een prijs of tarief.
export default function SalesOrderTransport({ order, onChanged }: { order: SalesOrder; onChanged?: () => void }) {
    const { t, locale } = useTranslation();
    const [loaded, setLoaded] = useState<{ orderId: number; transport: Transport } | null>(null);
    // Alleen gegevens die bij de huidige order horen: bij een orderwissel is er direct niets te zien of te bewerken.
    const transport = loaded !== null && loaded.orderId === order.id ? loaded.transport : null;
    const setTransport = (next: Transport) => setLoaded({ orderId: order.id, transport: next });
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [editing, setEditing] = useState(false);
    const [draft, setDraft] = useState<TransportDraft>(() => draftFrom(null));
    const [problems, setProblems] = useState<TransportProblem[]>([]);
    const [busy, setBusy] = useState(false);
    const [carriers, setCarriers] = useState<RelationListItem[]>([]);
    const [query, setQuery] = useState('');
    const [similar, setSimilar] = useState<SimilarRelation[] | null>(null);
    const cancelled = order.status === 'Cancelled';
    const id = (field: string) => `transport-${field}-${order.id}`;

    useEffect(() => {
        const controller = new AbortController();
        setLoadError(null);
        getSalesOrderTransport(order.id, controller.signal)
            .then((found) => { setLoaded({ orderId: order.id, transport: found }); setLoadError(null); })
            .catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, [order.id, order.version]);

    // Een andere order: niets van de vorige blijft open staan.
    useEffect(() => { setEditing(false); setError(null); setSimilar(null); setProblems([]); }, [order.id]);

    useEffect(() => {
        if (!editing) return;
        const controller = new AbortController();
        // Alle pagina's ophalen, zodat ook vervoerders na de eerste 200 te vinden zijn.
        const pageSize = 200;
        const fetchAll = async () => {
            const all: RelationListItem[] = [];
            for (let page = 1; ; page++) {
                const list = await listRelations({ role: 'carrier', query: '', includeInactive: false, page, pageSize }, controller.signal);
                all.push(...list.items);
                if (list.items.length === 0 || all.length >= list.total) return all;
            }
        };
        fetchAll()
            .then((items) => setCarriers(items))
            .catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [editing]);

    const change = (patch: Partial<TransportDraft>) => { setDraft((current) => ({ ...current, ...patch })); setProblems([]); };
    const start = () => { setDraft(draftFrom(transport)); setProblems([]); setError(null); setSimilar(null); setEditing(true); };
    const chooseCarrier = (carrier: { id: number; name: string }) => { change({ carrierId: carrier.id, carrierName: carrier.name }); setSimilar(null); setQuery(''); };

    const search = useCallback((text: string): LookupMatch<RelationListItem>[] => {
        const term = text.trim().toLowerCase();
        return carriers
            .filter((carrier) => term === '' || carrier.name.toLowerCase().includes(term) || (carrier.searchCode ?? '').toLowerCase().startsWith(term))
            .map((carrier) => ({ key: String(carrier.id), item: carrier, description: carrier.city ? `${carrier.name} · ${carrier.city}` : carrier.name, exact: (carrier.searchCode ?? '').toLowerCase() === term, badge: carrier.searchCode }));
    }, [carriers]);
    const selected = useMemo(() => (draft.carrierId === null ? null : { description: draft.carrierName }), [draft.carrierId, draft.carrierName]);
    const noMatch = selected === null && query.trim() !== '' && search(query).length === 0;

    const call = async (work: () => Promise<void>) => {
        if (busy) return;
        setBusy(true);
        setError(null);
        try { await work(); } catch (caught) { setError(toApiError(caught)); } finally { setBusy(false); }
    };
    const createNew = (acknowledge: boolean) => void call(async () => {
        const outcome = await createCarrier(query.trim(), newRequestId(), acknowledge);
        if (outcome.outcome === 'similar-names') { setSimilar(outcome.similar); return; }
        setCarriers((list) => [...list, { id: outcome.relation.id, name: outcome.relation.name, searchCode: outcome.relation.searchCode } as RelationListItem]);
        chooseCarrier(outcome.relation);
    });
    const takeExisting = (relation: SimilarRelation) => void call(async () => { chooseCarrier(await addRelationRole(relation.id, 'carrier')); });

    const save = (event: FormEvent) => {
        event.preventDefault();
        const { request, problems: found } = requestFrom(draft, transport?.exists ? transport.version : null, transport?.loadAt != null);
        setProblems(found);
        if (request === null) return;
        void call(async () => { setTransport(await saveSalesOrderTransport(order.id, request)); setEditing(false); onChanged?.(); });
    };
    const remove = () => void call(async () => { setTransport(await deleteSalesOrderTransport(order.id, transport?.version ?? 0)); setEditing(false); onChanged?.(); });

    const problem = (key: TransportProblem, message: string) => problems.includes(key) && <p role="alert" className="text-xs text-red-800" data-testid={`transport-problem-${key}`}>{message}</p>;
    const defaultHint = (value: string | null | undefined) => value ? <span className="block text-xs text-gray-600">{t('so_transport_default', { value })}</span> : null;
    const field = (key: keyof TransportDraft, label: string, extra: { maxLength?: number; dataTour?: string; placeholder?: string } = {}) => (
        <div>
            <label htmlFor={id(key)} className="text-xs font-medium text-gray-700">{label}</label>
            <input id={id(key)} value={String(draft[key] ?? '')} maxLength={extra.maxLength} placeholder={extra.placeholder} onChange={(event) => change({ [key]: event.target.value } as Partial<TransportDraft>)}
                className={inputClass} data-tour={extra.dataTour} />
        </div>
    );

    const shown = transport?.exists ? transport : null;
    return (
        <section aria-labelledby={id('title')} className="space-y-2 rounded-md border border-gray-300 bg-white px-4 py-3" data-testid="so-transport" data-tour="so-transport">
            <div className="flex flex-wrap items-center gap-2">
                <h3 id={id('title')} className="font-semibold text-gray-900">{t('so_transport')}</h3>
                {shown && !editing && !cancelled && <button type="button" className="text-sm text-blue-800 underline underline-offset-2" onClick={start} data-tour="so-transport-edit">{t('so_transport_edit')}</button>}
            </div>
            {loadError && <ErrorNotice error={loadError} />}
            {error && <ErrorNotice error={error} />}
            {transport && !shown && !editing && (
                <div className="flex flex-wrap items-center gap-2">
                    <p className="text-sm text-gray-700">{t('so_transport_none')}</p>
                    {!cancelled && <button type="button" className={secondaryButtonClass} onClick={start} data-tour="so-transport-add">{t('so_transport_add')}</button>}
                </div>
            )}
            {shown && !editing && (
                <dl className="grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-2" data-testid="so-transport-summary">
                    <div><dt className="text-xs text-gray-600">{t('so_transport_carrier')}</dt><dd className="font-medium">{shown.mode === 'Own' ? t('so_transport_mode_own') : shown.carrierName}</dd></div>
                    <div><dt className="text-xs text-gray-600">{t('so_transport_load_date')}</dt><dd>{shown.loadAt ? formatDateTime(shown.loadAt, locale) : '–'}{shown.unloadDate ? ` → ${formatCalendarDate(shown.unloadDate, locale)}` : ''}{shown.temperatureC != null ? ` · ${formatDecimalInput(shown.temperatureC, 1, locale)} °C` : ''}</dd></div>
                    <div><dt className="text-xs text-gray-600">{t('so_transport_loading_place')}</dt><dd>{shown.loadingPlace ?? shown.defaultLoadingPlace}</dd></div>
                    <div><dt className="text-xs text-gray-600">{t('so_transport_unloading_place')}</dt><dd>{shown.unloadingPlace ?? (shown.unloadingAddressId != null ? shown.unloadingAddresses.find((address) => address.id === shown.unloadingAddressId)?.text : null) ?? shown.defaultUnloadingPlace}</dd></div>
                </dl>
            )}
            {editing && transport && (
                <form onSubmit={save} className="space-y-3" data-testid="so-transport-form">
                    <fieldset disabled={busy} className="space-y-3">
                        <fieldset>
                            <legend className="text-xs font-medium text-gray-700">{t('so_transport_mode')}</legend>
                            <div className="flex flex-wrap gap-4 text-sm">
                                <label className="flex items-center gap-2"><input type="radio" name={id('mode')} checked={draft.mode === 'Carrier'} onChange={() => change({ mode: 'Carrier' })} data-tour="so-transport-mode-carrier" /> {t('so_transport_mode_carrier')}</label>
                                <label className="flex items-center gap-2"><input type="radio" name={id('mode')} checked={draft.mode === 'Own'} onChange={() => change({ mode: 'Own' })} data-tour="so-transport-mode-own" /> {t('so_transport_mode_own')}</label>
                            </div>
                            {problem('mode', t('so_transport_problem_mode'))}
                        </fieldset>
                        {draft.mode === 'Carrier' && (
                            <div className="max-w-md">
                                <label htmlFor={`${id('carrier')}-input`} className="text-xs font-medium text-gray-700">{t('so_transport_carrier')}</label>
                                <LookupField<RelationListItem> id={id('carrier')} dataTour="so-transport-carrier" ariaLabel={t('so_transport_carrier')} selected={selected} search={search}
                                    onSelect={chooseCarrier} onClear={() => change({ carrierId: null, carrierName: '' })} onQueryChange={(value) => { setQuery(value); setSimilar(null); }} takeHighlighted
                                    placeholderKey="so_transport_carrier_placeholder" hintKey="so_transport_carrier_hint" noneKey="so_transport_carrier_none" moreKey="product_picker_more" changeKey="so_transport_carrier_change" />
                                {noMatch && similar === null && (
                                    <button type="button" className={`${secondaryButtonClass} mt-1 px-2 py-1 text-xs`} onClick={() => createNew(false)} data-tour="so-transport-carrier-new">
                                        {t('so_transport_carrier_new', { name: query.trim() })}
                                    </button>
                                )}
                                {similar && (
                                    <div className="mt-1 space-y-1 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-xs text-amber-950" data-testid="so-transport-carrier-similar">
                                        <p>{t('so_transport_carrier_similar')}</p>
                                        {similar.map((relation) => <button key={relation.id} type="button" className="mr-2 underline" onClick={() => takeExisting(relation)}>{t('so_transport_carrier_use', { name: relation.name })}</button>)}
                                        <button type="button" className="underline" onClick={() => createNew(true)}>{t('so_transport_carrier_create_anyway')}</button>
                                    </div>
                                )}
                                {problem('carrier', t('so_transport_problem_carrier'))}
                            </div>
                        )}
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-4">
                            <div>
                                <label htmlFor={id('load-date')} className="text-xs font-medium text-gray-700">{t('so_transport_load_date')}</label>
                                <DateField id={id('load-date')} value={draft.loadDate} onChange={(value) => change({ loadDate: value })} dataTour="so-transport-load-date" />
                                {problem('loadDate', t('so_transport_problem_load_date'))}
                            </div>
                            <div>
                                <label htmlFor={id('load-time')} className="text-xs font-medium text-gray-700">{t('so_transport_load_time')}</label>
                                <input id={id('load-time')} value={draft.loadTime} inputMode="numeric" placeholder="06:30" onChange={(event) => change({ loadTime: event.target.value })} className={inputClass} data-tour="so-transport-load-time" />
                                {problem('loadTime', t('so_transport_problem_load_time'))}
                            </div>
                            <div>
                                <label htmlFor={id('unload-date')} className="text-xs font-medium text-gray-700">{t('so_transport_unload_date')}</label>
                                <DateField id={id('unload-date')} value={draft.unloadDate} onChange={(value) => change({ unloadDate: value })} dataTour="so-transport-unload-date" />
                            </div>
                            <div>
                                <label htmlFor={id('temperature')} className="text-xs font-medium text-gray-700">{t('so_transport_temperature')}</label>
                                <input id={id('temperature')} value={draft.temperature} inputMode="decimal" placeholder="2" onChange={(event) => change({ temperature: event.target.value })} className={inputClass} data-tour="so-transport-temperature" />
                                {problem('temperature', t('so_transport_problem_temperature'))}
                            </div>
                        </div>
                        <p className="text-xs text-gray-600">{t('so_transport_load_hint')}</p>
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                            <div>
                                <label htmlFor={id('loadingPlace')} className="text-xs font-medium text-gray-700">{t('so_transport_loading_place')}</label>
                                <textarea id={id('loadingPlace')} rows={2} maxLength={300} value={draft.loadingPlace} placeholder={transport.defaultLoadingPlace ?? ''} onChange={(event) => change({ loadingPlace: event.target.value })} className={inputClass} data-tour="so-transport-loading-place" />
                                {defaultHint(transport.defaultLoadingPlace)}
                            </div>
                            <div>
                                <label htmlFor={id('unloading')} className="text-xs font-medium text-gray-700">{t('so_transport_unloading_place')}</label>
                                <select id={id('unloading')} value={draft.unloadingChoice} onChange={(event) => change({ unloadingChoice: event.target.value })} className={inputClass} data-tour="so-transport-unloading">
                                    <option value={DEFAULT_ADDRESS}>{t('so_transport_unloading_default')}</option>
                                    {transport.unloadingAddresses.map((address) => <option key={address.id} value={String(address.id)}>{address.text ?? `#${address.id}`}</option>)}
                                    <option value={OTHER_ADDRESS}>{t('so_transport_unloading_other')}</option>
                                </select>
                                {draft.unloadingChoice === OTHER_ADDRESS
                                    ? <textarea id={id('unloadingPlace')} aria-label={t('so_transport_unloading_other')} rows={2} maxLength={300} value={draft.unloadingPlace} onChange={(event) => change({ unloadingPlace: event.target.value })} className={`${inputClass} mt-1`} />
                                    : defaultHint(transport.defaultUnloadingPlace)}
                            </div>
                        </div>
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                            {field('loadingReference', t('so_transport_loading_reference'), { maxLength: 64, dataTour: 'so-transport-loading-reference' })}
                            {field('unloadingReference', t('so_transport_unloading_reference'), { maxLength: 64, placeholder: transport.defaultUnloadingReference ?? '' })}
                            {field('palletKind', t('so_transport_pallet_kind'), { maxLength: 100, placeholder: transport.defaultPalletKind ?? '' })}
                        </div>
                        <div>
                            <label htmlFor={id('remarks')} className="text-xs font-medium text-gray-700">{t('so_transport_remarks')}</label>
                            <textarea id={id('remarks')} rows={2} maxLength={1000} value={draft.remarks} onChange={(event) => change({ remarks: event.target.value })} className={inputClass} />
                        </div>
                    </fieldset>
                    <div className="flex flex-wrap gap-2">
                        <button type="submit" className={primaryButtonClass} disabled={busy} data-tour="so-transport-save">{busy ? t('supply_saving') : t('so_transport_save')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setEditing(false)}>{t('cancel')}</button>
                        {shown && !shown.hasTransportOrder && <button type="button" className={secondaryButtonClass} disabled={busy} onClick={remove}>{t('so_transport_remove')}</button>}
                    </div>
                </form>
            )}
        </section>
    );
}

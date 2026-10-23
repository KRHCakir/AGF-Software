'use client';

import { KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import RelationForm from '../../../components/relations/RelationForm';
import { inputClass } from '../../../components/supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { relationRole } from '../../../lib/relationRules';
import { isMacPlatform, matchShortcut, shortcutLabel } from '../../../lib/shortcuts';
import { toApiError } from '../../../services/apiClient';
import { getRelation, getRelationReference, listRelations } from '../../../services/relationService';
import type { Relation, RelationList, RelationReference, RelationRoleFilter } from '../../../types/relation';

const PAGE_SIZE = 50;

// Relaties: één werkplek voor klanten/debiteuren en leveranciers/crediteuren. Filters Alle / Klanten / Leveranciers, zoeken op
// code, naam, handelsnaam, btw, plaats en contact (⌥F, type-ahead met pijltjes en Enter), nieuw (⌥N), bewerken in één
// formulier boven de begrensde, gepagineerde lijst. Inactieve relaties blijven vindbaar via het vinkje; verwijderen bestaat niet.
export default function RelationsPage() {
    const { t } = useTranslation();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    const [role, setRole] = useState<RelationRoleFilter>('all');
    const [includeInactive, setIncludeInactive] = useState(false);
    const [query, setQuery] = useState('');
    const [debounced, setDebounced] = useState('');
    const [page, setPage] = useState(1);
    const [reloadToken, setReloadToken] = useState(0);
    const [list, setList] = useState<LoadState<RelationList>>({ status: 'loading' });
    const [reference, setReference] = useState<LoadState<RelationReference>>({ status: 'loading' });
    const [editing, setEditing] = useState<{ relation: Relation | null; key: number } | null>(null);
    const [opening, setOpening] = useState<number | null>(null);
    const [highlight, setHighlight] = useState(0);
    const searchInput = useRef<HTMLInputElement | null>(null);

    useEffect(() => {
        const handle = setTimeout(() => setDebounced(query.trim()), 200);
        return () => clearTimeout(handle);
    }, [query]);
    useEffect(() => { setPage(1); }, [role, includeInactive, debounced]);

    useEffect(() => {
        const controller = new AbortController();
        getRelationReference(controller.signal)
            .then((data) => setReference({ status: 'ready', data }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setReference({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [reloadToken]);

    useEffect(() => {
        const controller = new AbortController();
        setList((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        listRelations({ role, query: debounced, includeInactive, page, pageSize: PAGE_SIZE }, controller.signal)
            .then((data) => { setList({ status: 'ready', data }); setHighlight(0); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setList({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [role, debounced, includeInactive, page, reloadToken]);

    // De server bepaalt de definitieve resultaten en volgorde (ook tweede contactpersonen, telefoons en e-mails van alle contacten);
    // de werkplek filtert niet opnieuw, zodat zij nooit een treffer van de server weer weghaalt.
    const shown = useMemo(() => (list.status === 'ready' ? list.data.items : []), [list]);
    const total = list.status === 'ready' ? list.data.total : 0;
    const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

    const openNew = () => setEditing({ relation: null, key: Date.now() });
    const openRelation = async (id: number) => {
        setOpening(id);
        try {
            const relation = await getRelation(id);
            setEditing({ relation, key: relation.id });
        } catch (caught) {
            setList({ status: 'error', error: toApiError(caught) });
        } finally {
            setOpening(null);
        }
    };

    const handler = useRef<(event: globalThis.KeyboardEvent) => void>(() => {});
    handler.current = (event: globalThis.KeyboardEvent) => {
        const action = matchShortcut(event, isMac);
        if (action === 'focusSearch') { event.preventDefault(); searchInput.current?.focus(); searchInput.current?.select(); }
        if (action === 'newDocument') { event.preventDefault(); openNew(); }
    };
    useEffect(() => {
        const listener = (event: globalThis.KeyboardEvent) => handler.current(event);
        document.addEventListener('keydown', listener, true);
        return () => document.removeEventListener('keydown', listener, true);
    }, []);

    // Pijltjes kiezen een regel in de lijst, Enter opent haar; Enter verstuurt nooit iets.
    const onSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'ArrowDown') { event.preventDefault(); setHighlight((current) => Math.min(current + 1, Math.max(shown.length - 1, 0))); }
        else if (event.key === 'ArrowUp') { event.preventDefault(); setHighlight((current) => Math.max(current - 1, 0)); }
        else if (event.key === 'Enter') { event.preventDefault(); const item = shown[highlight]; if (item) void openRelation(item.id); }
        else if (event.key === 'Escape') { setQuery(''); }
    };

    const onSaved = (saved: Relation, created: boolean) => {
        setEditing((current) => ({ relation: saved, key: current?.key ?? saved.id }));
        // Lijst en keuzelijsten verversen (nieuwe rollen, gewijzigde namen, legacy-GGN's).
        setReloadToken((n) => n + 1);
        void created;
    };

    const roleBadge = (item: { customerId: number | null; vendorId: number | null }) => {
        const value = relationRole(item);
        const color = value === 'both' ? 'bg-violet-100 text-violet-900' : value === 'customer' ? 'bg-blue-100 text-blue-900' : value === 'vendor' ? 'bg-emerald-100 text-emerald-900' : 'bg-gray-100 text-gray-700';
        return <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${color}`}>{t(`relations_role_${value}`)}</span>;
    };

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('relations_title')}</h1>
                    <p className="text-sm text-gray-600">{t('relations_subtitle')}</p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                    <button type="button" onClick={() => setReloadToken((n) => n + 1)} className={secondaryButtonClass}>↻ {t('availability_refresh')}</button>
                    <button type="button" onClick={openNew} className={primaryButtonClass}>+ {t('relations_new')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('newDocument', isMac)}</kbd></button>
                </div>
            </div>

            {reference.status === 'error' && <ErrorState error={reference.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {reference.status === 'ready' && reference.data.legacyArticleGgns.length > 0 && (
                <div role="note" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900" data-testid="legacy-ggn-notice">
                    <p className="font-medium">{t('relations_legacy_ggn_title', { count: String(reference.data.legacyArticleGgns.length) })}</p>
                    <p className="text-xs">{t('relations_legacy_ggn_hint')}</p>
                    <ul className="mt-1 list-disc pl-5 text-xs">
                        {reference.data.legacyArticleGgns.map((item) => <li key={item.productVariantId}>#{item.productVariantId} {item.description} · <span className="font-mono">{item.ggn}</span></li>)}
                    </ul>
                </div>
            )}

            {editing && reference.status === 'ready' && (
                <RelationForm key={editing.key} relation={editing.relation} reference={reference.data} onSaved={onSaved} onClose={() => setEditing(null)} />
            )}

            <div className="flex flex-wrap items-center gap-3">
                <div role="radiogroup" aria-label={t('relations_filter_role')} className="inline-flex gap-1 rounded bg-gray-100 p-0.5">
                    {(['all', 'customer', 'vendor'] as const).map((option) => (
                        <button key={option} type="button" role="radio" aria-checked={role === option} onClick={() => setRole(option)}
                            className={`rounded px-2 py-0.5 text-sm ${role === option ? 'bg-white font-medium text-blue-800 shadow-sm' : 'text-gray-600 hover:text-gray-900'}`}>
                            {t(`relations_filter_${option}`)}
                        </button>
                    ))}
                </div>
                <label className="flex items-center gap-1 text-sm text-gray-800">
                    <input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} />
                    {t('relations_filter_inactive')}
                </label>
                <label htmlFor="relation-search" className="sr-only">{t('relations_search_label')}</label>
                <input id="relation-search" ref={searchInput} type="search" role="combobox" aria-expanded={shown.length > 0} aria-controls="relation-list" aria-autocomplete="list" autoComplete="off"
                    value={query} onChange={(event) => { setQuery(event.target.value); setHighlight(0); }} onKeyDown={onSearchKeyDown}
                    placeholder={t('relations_search_placeholder')} className={`${inputClass} mt-0 max-w-xl flex-1`} />
                <span className="text-xs text-gray-600">{t('relations_count_total', { shown: String(shown.length), total: String(total) })} · {t('relations_shortcut_hint', { search: shortcutLabel('focusSearch', isMac), new: shortcutLabel('newDocument', isMac), save: shortcutLabel('save', isMac) })}</span>
            </div>

            {list.status === 'loading' && <LoadingRows label={t('relations_loading')} />}
            {list.status === 'error' && <ErrorState error={list.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {list.status === 'ready' && (
                <div className="overflow-x-auto rounded-md border border-gray-300 bg-white">
                    <table id="relation-list" role="listbox" aria-label={t('relations_title')} className="min-w-full text-sm">
                        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-3 py-2">{t('relations_col_code')}</th>
                                <th scope="col" className="px-3 py-2">{t('relations_col_name')}</th>
                                <th scope="col" className="px-3 py-2">{t('relations_col_role')}</th>
                                <th scope="col" className="px-3 py-2">{t('relations_col_status')}</th>
                                <th scope="col" className="px-3 py-2">{t('relations_col_place')}</th>
                                <th scope="col" className="px-3 py-2">{t('relations_col_contact')}</th>
                                <th scope="col" className="px-3 py-2"><span className="sr-only">{t('relations_edit_action')}</span></th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {shown.length === 0 && <tr><td colSpan={7} className="px-3 py-6 text-center text-gray-600">{t('relations_none')}</td></tr>}
                            {shown.map((item, index) => (
                                <tr key={item.id} role="option" aria-selected={index === highlight} data-testid={`relation-row-${item.id}`} onClick={() => void openRelation(item.id)}
                                    className={`cursor-pointer hover:bg-blue-50 ${editing?.relation?.id === item.id ? 'bg-blue-50' : index === highlight && query !== '' ? 'bg-blue-50/60' : ''}`}>
                                    <td className="px-3 py-2 font-mono text-xs">{item.searchCode ?? '–'}</td>
                                    <td className="px-3 py-2">
                                        <span className="font-medium text-gray-900">{item.name}</span>
                                        {item.tradeName && <span className="block text-xs text-gray-600">{item.tradeName}</span>}
                                    </td>
                                    <td className="px-3 py-2">{roleBadge(item)}</td>
                                    <td className="px-3 py-2">
                                        <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${item.isActive ? 'bg-green-100 text-green-900' : 'bg-gray-200 text-gray-800'}`}>{item.isActive ? t('relations_active') : t('relations_inactive')}</span>
                                    </td>
                                    <td className="px-3 py-2 text-xs text-gray-800">{[item.city, item.countryCode].filter(Boolean).join(', ') || '–'}</td>
                                    <td className="px-3 py-2 text-xs text-gray-800">
                                        {item.primaryContactName && <span className="block">{item.primaryContactName}</span>}
                                        {(item.email || item.phone) && <span className="block text-gray-600">{[item.email, item.phone].filter(Boolean).join(' · ')}</span>}
                                        {!item.primaryContactName && !item.email && !item.phone && '–'}
                                    </td>
                                    <td className="px-3 py-2 text-right">
                                        <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} disabled={opening === item.id} onClick={(event) => { event.stopPropagation(); void openRelation(item.id); }} aria-label={`${t('relations_edit_action')} #${item.id}`}>✎</button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    {pages > 1 && (
                        <div className="flex items-center justify-between border-t border-gray-200 px-3 py-2 text-xs text-gray-700">
                            <button type="button" className={secondaryButtonClass} disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>‹ {t('relations_page_previous')}</button>
                            <span>{t('relations_page_of', { page: String(page), pages: String(pages) })}</span>
                            <button type="button" className={secondaryButtonClass} disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>{t('relations_page_next')} ›</button>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}

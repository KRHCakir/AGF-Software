'use client';

import { FormEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatMoney, formatQuantity } from '../../lib/availability';
import { type CostTarget, type DistributeMode, checkAllocation, distribute } from '../../lib/costAllocation';
import { formatAmountInput } from '../../lib/decimal';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { listPartijen } from '../../services/partijService';
import { saveLineAllocation } from '../../services/purchaseInvoiceService';
import type { InvoiceAllocations, InvoiceLineAllocation } from '../../types/partijCost';
import type { PartijTotals } from '../../types/partij';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

type Mode = 'General' | 'Purchase' | 'Sales';
interface Row { target: CostTarget; amount: string }

// Een gevonden partij als verdeeldoel: wat het is (leverancier of klant met VO-nummer, product) en de basis voor verdelen.
export function targetFromTotals(item: PartijTotals): CostTarget {
    const sales = item.kind === 'Sales';
    return {
        partijId: item.id, number: item.number, kind: sales ? 'Sales' : 'Purchase',
        party: sales ? [item.customerName, item.salesOrderNumber].filter(Boolean).join(' · ') || null : item.vendors.join(', ') || null,
        products: item.products.join(', ') || null,
        quantity: sales ? item.orderedQuantity : item.receivedQuantity, unit: item.unit, netKg: item.netKg ?? 0,
    };
}

const rowsFrom = (line: InvoiceLineAllocation): Row[] => line.allocations.map((allocation) => ({
    target: {
        partijId: allocation.partijId, number: allocation.number, kind: allocation.partijKind, party: allocation.party ?? null, products: allocation.products ?? null,
        quantity: allocation.quantity ?? 0, unit: allocation.unit ?? null, netKg: allocation.netKg ?? 0,
    },
    amount: formatAmountInput(allocation.amount),
}));

// "Aan welke partij hoort dit?" bij één regel van een inkoopfactuur: algemene kosten (standaard), of een of meer inkoop- en/of
// verkoop-partijen met elk een bedrag excl. btw. Samen nooit meer dan het regelbedrag (harde melding naast Opslaan); een rest is
// algemene kosten (waarschuwing). Verdeel gelijk / naar kg / naar colli vullen de bedragen voor; opslaan schrijft historie.
export default function AllocationPanel({ invoiceId, line, canAllocate, onSaved, onReload }: {
    invoiceId: number;
    line: InvoiceLineAllocation;
    canAllocate: boolean;
    onSaved: (allocations: InvoiceAllocations) => void;
    onReload: () => void;
}) {
    const { t, locale } = useTranslation();
    const money = (value: number) => formatMoney(value, 'EUR', locale);
    const [mode, setMode] = useState<Mode>(() => (line.allocations.length === 0 ? 'General' : line.allocations[0].partijKind));
    const [rows, setRows] = useState<Row[]>(() => rowsFrom(line));
    const [query, setQuery] = useState('');
    const [found, setFound] = useState<PartijTotals[]>([]);
    const [skipped, setSkipped] = useState<{ ids: number[]; mode: DistributeMode } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [conflict, setConflict] = useState(false);
    const [busy, setBusy] = useState(false);
    const [saved, setSaved] = useState(false);
    const requestId = useRef<string | null>(null);
    const prefix = `line-${line.lineId}`;

    // Nieuwe stand van de server (na opslaan of opnieuw laden): de regels volgen; de melding "opgeslagen" blijft staan tot de gebruiker iets wijzigt.
    // Alleen bij een nieuwe stand van déze regel (andere versie): zo wist opslaan op een andere regel van de factuur geen concept hier.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    useEffect(() => { setRows(rowsFrom(line)); setSkipped(null); }, [line.lineId, line.version]);

    // Zoeken binnen de gekozen soort op nummer, leverancier, klant, VO-nummer en product (de server zoekt).
    useEffect(() => {
        if (mode === 'General') { setFound([]); return; }
        const controller = new AbortController();
        const timer = window.setTimeout(() => {
            listPartijen({ query, status: 'all', kind: mode, page: 1, pageSize: 8 }, controller.signal)
                .then((list) => setFound(list.items))
                .catch(() => { if (!controller.signal.aborted) setFound([]); });
        }, query.trim() === '' ? 0 : 150);
        return () => { window.clearTimeout(timer); controller.abort(); };
    }, [mode, query]);

    // Een partij die bij "verdeel naar kg / colli" niet meedeed (geen basis) en leeg is gebleven, gaat niet mee en blokkeert niets;
    // vult de gebruiker toch een bedrag in, dan doet ze wel mee.
    const shown = (mode === 'General' ? [] : rows).filter((row) => !(skipped?.ids.includes(row.target.partijId) && row.amount.trim() === ''));
    const check = checkAllocation(line.lineTotal, shown.map((row) => row.amount));
    const touched = () => { setSaved(false); setError(null); setConflict(false); requestId.current = null; };

    const add = (item: PartijTotals) => {
        if (rows.some((row) => row.target.partijId === item.id)) return;
        setRows((current) => [...current, { target: targetFromTotals(item), amount: '' }]);
        touched();
    };
    const remove = (partijId: number) => { setRows((current) => current.filter((row) => row.target.partijId !== partijId)); touched(); };
    const setAmount = (partijId: number, amount: string) => { setRows((current) => current.map((row) => (row.target.partijId === partijId ? { ...row, amount } : row))); touched(); };

    // Over alle gekozen partijen, ook een die bij een vorige verdeling niet meedeed.
    const spread = (how: DistributeMode) => {
        const all = mode === 'General' ? [] : rows;
        const result = distribute(line.lineTotal, all.map((row) => row.target), how);
        setRows((current) => current.map((row) => {
            const index = all.findIndex((candidate) => candidate.target.partijId === row.target.partijId);
            const amount = index === -1 ? null : result.amounts[index];
            return amount === null || amount === undefined ? { ...row, amount: index === -1 ? row.amount : '' } : { ...row, amount: formatAmountInput(amount) };
        }));
        setSkipped(result.skipped.length > 0 ? { ids: result.skipped, mode: how } : null);
        touched();
    };

    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (busy || !check.ok) return;
        requestId.current ??= newRequestId();
        setBusy(true);
        setError(null);
        try {
            const result = await saveLineAllocation(invoiceId, line.lineId, {
                requestId: requestId.current, expectedVersion: line.version,
                allocations: shown.map((row, index) => ({ partijId: row.target.partijId, amount: check.amounts[index] })),
            });
            requestId.current = null;
            setSaved(true);
            onSaved(result);
        } catch (caught) {
            const apiError = toApiError(caught);
            // Onbekende uitkomst: dezelfde requestId blijft, zodat opnieuw proberen nooit dubbel verdeelt.
            if (!outcomeUnknown(apiError)) requestId.current = null;
            if (apiError.status === 409) setConflict(true); else setError(apiError);
        } finally {
            setBusy(false);
        }
    };

    const basis = (target: CostTarget) => `${formatQuantity(target.quantity, locale)}${target.unit ? ` ${target.unit}` : ''} · ${formatQuantity(target.netKg, locale)} kg`;

    return (
        <form noValidate onSubmit={save} className="space-y-2 rounded border border-gray-200 bg-gray-50 p-2" data-tour="cost-allocation" data-tour-key={line.lineId} aria-labelledby={`${prefix}-title`}>
            <fieldset className="flex flex-wrap items-center gap-x-4 gap-y-1" disabled={!canAllocate || busy}>
                <legend id={`${prefix}-title`} className="mb-1 text-sm font-semibold text-gray-900">{t('allocation_question')}</legend>
                {(['General', 'Purchase', 'Sales'] as const).map((option) => (
                    <label key={option} className="flex items-center gap-1 text-sm text-gray-900">
                        <input type="radio" name={`${prefix}-mode`} value={option} checked={mode === option} data-tour="allocation-mode" data-tour-key={option}
                            onChange={() => { setMode(option); touched(); }} />
                        {t(`allocation_mode_${option.toLowerCase()}`)}
                    </label>
                ))}
            </fieldset>
            {!canAllocate && <p className="text-sm text-amber-900">{t('allocation_not_euro')}</p>}

            {mode !== 'General' && canAllocate && (
                <div className="space-y-2">
                    <div>
                        <label htmlFor={`${prefix}-search`} className="text-xs font-medium text-gray-700">{t(mode === 'Sales' ? 'allocation_search_sales' : 'allocation_search_purchase')}</label>
                        <input id={`${prefix}-search`} type="search" value={query} onChange={(event) => setQuery(event.target.value)} className={inputClass} data-tour="allocation-search"
                            placeholder={t(mode === 'Sales' ? 'partij_list_search_sales_placeholder' : 'partij_list_search_placeholder')} />
                        <ul className="mt-1 max-h-48 divide-y divide-gray-100 overflow-auto rounded border border-gray-200 bg-white text-sm">
                            {found.filter((item) => !rows.some((row) => row.target.partijId === item.id)).map((item) => {
                                const target = targetFromTotals(item);
                                return (
                                    <li key={item.id}>
                                        <button type="button" className="w-full px-2 py-1 text-left hover:bg-blue-50 focus:bg-blue-50" data-tour="allocation-pick" data-tour-key={item.number} onClick={() => add(item)}>
                                            <span className="font-mono font-semibold text-blue-800">{item.number}</span>
                                            <span className="text-gray-800"> · {[target.party, target.products, basis(target)].filter(Boolean).join(' · ')}</span>
                                        </button>
                                    </li>
                                );
                            })}
                            {found.length === 0 && <li className="px-2 py-1 text-gray-600">{t('allocation_search_none')}</li>}
                        </ul>
                    </div>

                    {rows.length > 0 && (
                        <ul className="space-y-1" aria-label={t('allocation_chosen')}>
                            {rows.map((row) => (
                                <li key={row.target.partijId} className="flex flex-wrap items-center gap-2 rounded border border-gray-200 bg-white px-2 py-1 text-sm" data-tour="allocation-row" data-tour-key={row.target.number}>
                                    <span className="min-w-0 flex-1 break-words">
                                        <span className="font-mono font-semibold">{row.target.number}</span>{' '}
                                        <span className="text-xs text-gray-600">{t(row.target.kind === 'Sales' ? 'partij_kind_sales' : 'partij_kind_purchase')}</span>
                                        <span className="block text-gray-800">{[row.target.party, row.target.products, basis(row.target)].filter(Boolean).join(' · ')}</span>
                                        {skipped?.ids.includes(row.target.partijId) && <span className="block text-xs text-amber-900">{t(skipped.mode === 'kg' ? 'allocation_skipped_kg' : 'allocation_skipped_colli')}</span>}
                                    </span>
                                    <label htmlFor={`${prefix}-amount-${row.target.partijId}`} className="sr-only">{t('allocation_amount_for', { number: row.target.number })}</label>
                                    <input id={`${prefix}-amount-${row.target.partijId}`} inputMode="decimal" value={row.amount} onChange={(event) => setAmount(row.target.partijId, event.target.value)}
                                        aria-invalid={shown.includes(row) && check.amountErrors[shown.indexOf(row)] !== null ? true : undefined} data-tour="allocation-amount" data-tour-key={row.target.number}
                                        className={`${inputClass} !w-32 text-right font-mono`} placeholder="0,00" />
                                    <button type="button" className="rounded border border-gray-300 px-2 py-1 text-xs text-gray-700 hover:bg-gray-100" aria-label={t('allocation_remove', { number: row.target.number })}
                                        onClick={() => remove(row.target.partijId)}>✕</button>
                                </li>
                            ))}
                        </ul>
                    )}
                    {rows.length > 0 && (
                        <div className="flex flex-wrap gap-2">
                            {(['equal', 'kg', 'colli'] as const).map((how) => (
                                <button key={how} type="button" className={secondaryButtonClass} data-tour="allocation-spread" data-tour-key={how} onClick={() => spread(how)}>{t(`allocation_spread_${how}`)}</button>
                            ))}
                        </div>
                    )}
                </div>
            )}

            <p className="text-sm text-gray-800" data-testid={`${prefix}-summary`} data-tour="allocation-summary">
                {t('allocation_summary', { allocated: money(check.allocated), total: money(line.lineTotal) })}
                {check.general > 0 && <span className={`ml-1 ${shown.length > 0 ? 'font-medium text-amber-900' : 'text-gray-700'}`} data-testid={`${prefix}-general`}>{t('allocation_general', { amount: money(check.general) })}</span>}
            </p>
            {error && <ErrorNotice error={error} />}
            {conflict && (
                <p role="alert" className="rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-900">
                    {t('allocation_conflict')} <button type="button" className="underline" onClick={onReload}>{t('allocation_reload')}</button>
                </p>
            )}
            <div className="flex flex-wrap items-center gap-2">
                <button type="submit" className={primaryButtonClass} disabled={!canAllocate || busy} data-tour="allocation-save">{busy ? t('supply_saving') : t('allocation_save')}</button>
                {check.over && <span role="alert" className="text-sm font-medium text-red-800" data-testid={`${prefix}-over`}>{t('allocation_over', { allocated: money(check.allocated), total: money(line.lineTotal) })}</span>}
                {!check.over && check.amountErrors.some((amountError) => amountError !== null) && <span role="alert" className="text-sm font-medium text-red-800">{t('allocation_amount_invalid')}</span>}
                {saved && <span role="status" className="text-sm text-green-900" data-tour="allocation-saved">{t('allocation_saved')}</span>}
            </div>
            {line.history.length > 0 && (
                <details className="text-xs text-gray-700">
                    <summary className="cursor-pointer">{t('allocation_history', { count: String(line.history.length) })}</summary>
                    <ul className="mt-1 space-y-0.5">
                        {line.history.map((event, index) => (
                            <li key={index}>
                                {formatDateTime(event.occurredAt, locale)} · {event.actorReference} · <span className="font-mono">{event.number ?? `#${event.partijId}`}</span>:{' '}
                                {event.type === 'Allocated' && t('allocation_event_allocated', { amount: money(event.newAmount ?? 0) })}
                                {event.type === 'Changed' && t('allocation_event_changed', { from: money(event.oldAmount ?? 0), to: money(event.newAmount ?? 0) })}
                                {event.type === 'Removed' && t('allocation_event_removed', { amount: money(event.oldAmount ?? 0) })}
                            </li>
                        ))}
                    </ul>
                </details>
            )}
        </form>
    );
}

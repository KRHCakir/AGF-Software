'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateOnly, todayInAmsterdam } from '../../lib/calendarDate';
import { PAYMENT_CONDITION_BASES, PaymentConditionBasis, PaymentConditionDraft, conditionDueDate, emptyConditionDraft, validateConditionDraft } from '../../lib/paymentCondition';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { createPaymentCondition, deletePaymentCondition, listPaymentConditions, updatePaymentCondition } from '../../services/paymentConditionService';
import type { PaymentCondition, PaymentConditionInUse, SavePaymentConditionRequest } from '../../types/paymentCondition';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';
import { outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Beheer van betalingscondities (Instellingen). Lijst met inactieve op verzoek, toevoegen, wijzigen op versie, (de)activeren en
// verwijderen. Verwijderen kan alleen als geen klant of leverancier de conditie gebruikt; anders meldt de server de aantallen
// (409) en stelt het scherm inactief zetten voor: bestaande rollen houden haar, nieuw kiezen kan dan niet meer.
// Aanmaken bevriest payload en requestId bij een onbekende uitkomst: alleen exact dezelfde herhaling, nooit een tweede conditie.

interface Editing { id: number | null; version: number | null; draft: PaymentConditionDraft }
interface Pending { requestId: string; payload: SavePaymentConditionRequest }

const draftFrom = (condition: PaymentCondition): PaymentConditionDraft => ({
    code: condition.code, descriptionNl: condition.descriptionNl, descriptionEn: condition.descriptionEn, days: String(condition.days), basis: condition.basis,
    discountPercent: condition.discountPercent === null ? '' : String(condition.discountPercent), discountDays: condition.discountDays === null ? '' : String(condition.discountDays),
    sortOrder: String(condition.sortOrder), isActive: condition.isActive,
});

const payloadFrom = (condition: PaymentCondition, isActive: boolean, expectedVersion: number): SavePaymentConditionRequest => ({
    expectedVersion, code: condition.code, descriptionNl: condition.descriptionNl, descriptionEn: condition.descriptionEn, days: condition.days, basis: condition.basis,
    discountPercent: condition.discountPercent, discountDays: condition.discountDays, isActive, sortOrder: condition.sortOrder,
});

export default function PaymentConditionsAdmin() {
    const { t, locale } = useTranslation();
    const [includeInactive, setIncludeInactive] = useState(false);
    const [reloadToken, setReloadToken] = useState(0);
    const [conditions, setConditions] = useState<PaymentCondition[] | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [editing, setEditing] = useState<Editing | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    const [serverError, setServerError] = useState<ApiError | null>(null);
    const [pending, setPending] = useState<Pending | null>(null);
    const [busy, setBusy] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);
    const [confirmDelete, setConfirmDelete] = useState<number | null>(null);
    const [inUse, setInUse] = useState<{ condition: PaymentCondition; detail: PaymentConditionInUse } | null>(null);
    const inFlight = useRef(false);
    const today = todayInAmsterdam();

    useEffect(() => {
        const controller = new AbortController();
        listPaymentConditions(includeInactive, controller.signal)
            .then((data) => { setConditions(data); setLoadError(null); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, [includeInactive, reloadToken]);

    const reload = () => setReloadToken((n) => n + 1);
    const description = (c: { descriptionNl: string; descriptionEn: string }) => (locale === 'en' ? c.descriptionEn : c.descriptionNl);
    const basisLabel = (basis: PaymentConditionBasis) => t(`payment_conditions_basis_${basis}`);

    const open = (condition: PaymentCondition | null) => {
        if (pending !== null) return;
        setEditing(condition === null ? { id: null, version: null, draft: emptyConditionDraft() } : { id: condition.id, version: condition.version, draft: draftFrom(condition) });
        setErrors({});
        setServerError(null);
        setNotice(null);
        setInUse(null);
        setTimeout(() => document.getElementById('condition-code-input')?.focus(), 0);
    };
    const edit = (patch: Partial<PaymentConditionDraft>) => {
        if (pending !== null) return;
        setEditing((current) => (current === null ? current : { ...current, draft: { ...current.draft, ...patch } }));
        setErrors({});
        setServerError(null);
    };

    const run = async (action: () => Promise<void>) => {
        if (inFlight.current) return;
        inFlight.current = true;
        setBusy(true);
        try {
            await action();
        } finally {
            inFlight.current = false;
            setBusy(false);
        }
    };

    const sendCreate = (command: Pending) => run(async () => {
        try {
            const result = await createPaymentCondition({ ...command.payload, requestId: command.requestId });
            setPending(null);
            setEditing(null);
            setNotice(t(result.replayed ? 'payment_conditions_saved_before' : 'payment_conditions_created', { code: result.condition.code }));
            reload();
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(command);
            } else {
                setPending(null);
                setErrors(apiError.fieldErrors);
            }
            setServerError(apiError);
        }
    });

    const save = async () => {
        if (editing === null) return;
        if (pending !== null) { await sendCreate(pending); return; }
        const result = validateConditionDraft(editing.draft);
        if (!result.ok) {
            const next: FieldErrors = {};
            for (const error of result.errors) (next[error.field] ??= []).push(t(`payment_conditions_error_${error.code}`));
            setErrors(next);
            setTimeout(() => document.getElementById(`condition-${result.errors[0].field}-input`)?.focus(), 0);
            return;
        }
        const payload: SavePaymentConditionRequest = { ...result.value };
        if (editing.id === null) {
            const command: Pending = { requestId: newRequestId(), payload };
            await sendCreate(command);
            return;
        }
        const id = editing.id;
        await run(async () => {
            try {
                const saved = await updatePaymentCondition(id, { ...payload, expectedVersion: editing.version ?? 0 });
                setEditing(null);
                setNotice(t('payment_conditions_saved', { code: saved.code }));
                reload();
            } catch (caught) {
                const apiError = toApiError(caught);
                setErrors(apiError.fieldErrors);
                setServerError(apiError);
            }
        });
    };

    const setActive = (condition: PaymentCondition, isActive: boolean) => run(async () => {
        try {
            const saved = await updatePaymentCondition(condition.id, payloadFrom(condition, isActive, condition.version));
            setInUse(null);
            setNotice(t(isActive ? 'payment_conditions_activated' : 'payment_conditions_deactivated', { code: saved.code }));
            reload();
        } catch (caught) {
            setServerError(toApiError(caught));
        }
    });

    const remove = (condition: PaymentCondition) => run(async () => {
        setConfirmDelete(null);
        try {
            const result = await deletePaymentCondition(condition.id);
            if (result.outcome === 'in-use') {
                setInUse({ condition, detail: result.inUse });
                return;
            }
            setNotice(t('payment_conditions_deleted', { code: condition.code }));
            reload();
        } catch (caught) {
            setServerError(toApiError(caught));
        }
    });

    const e = (key: string) => errors[key];
    const field = (key: keyof PaymentConditionDraft & string, labelKey: string, options: { maxLength?: number; numeric?: boolean; hintKey?: string; span?: string } = {}) => (
        <div className={options.span ?? ''}>
            <label htmlFor={`condition-${key}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}</label>
            <input {...fieldProps(`condition-${key}`, e(key))} value={String(editing?.draft[key] ?? '')} maxLength={options.maxLength ?? 200} inputMode={options.numeric ? 'decimal' : undefined}
                disabled={busy || pending !== null} onChange={(event) => edit({ [key]: event.target.value } as Partial<PaymentConditionDraft>)} className={`${inputClass}${key === 'code' ? ' font-mono' : ''}`} />
            {options.hintKey && <span className="block text-xs text-gray-500">{t(options.hintKey)}</span>}
            <FieldError id={`condition-${key}`} messages={e(key)} />
        </div>
    );

    const preview = (() => {
        if (editing === null) return null;
        const days = Number(editing.draft.days);
        if (!/^\d{1,3}$/.test(editing.draft.days.trim()) || days > 365) return null;
        const due = conditionDueDate(today, days, editing.draft.basis);
        return due === null ? null : t('payment_conditions_example', { invoice: formatDateOnly(today, locale), due: formatDateOnly(due, locale) });
    })();

    return (
        <section className="space-y-3 rounded-md border border-gray-300 bg-white p-3" data-testid="payment-conditions-admin" aria-labelledby="payment-conditions-title">
            <div className="flex flex-wrap items-start justify-between gap-2">
                <div>
                    <h2 id="payment-conditions-title" className="text-lg font-semibold text-gray-900">{t('payment_conditions_title')}</h2>
                    <p className="text-sm text-gray-600">{t('payment_conditions_subtitle')}</p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                    <label className="flex items-center gap-1 text-sm text-gray-800">
                        <input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} data-testid="payment-conditions-include-inactive" />
                        {t('payment_conditions_include_inactive')}
                    </label>
                    <button type="button" className={primaryButtonClass} disabled={busy || editing !== null} onClick={() => open(null)}>+ {t('payment_conditions_new')}</button>
                </div>
            </div>
            {notice && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{notice}</p>}
            {serverError && editing === null && <ErrorNotice error={serverError} />}
            {inUse && (
                <div role="alert" className="rounded border border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950" data-testid="payment-condition-in-use">
                    <p className="font-medium">{t('payment_conditions_in_use_title', { code: inUse.condition.code })}</p>
                    <p className="mt-1">{t('payment_conditions_in_use_counts', { customers: String(inUse.detail.customerCount), vendors: String(inUse.detail.vendorCount) })}</p>
                    <p className="mt-1 text-xs">{t('payment_conditions_in_use_hint')}</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                        {inUse.condition.isActive && <button type="button" className={`${secondaryButtonClass} border-amber-400`} disabled={busy} onClick={() => void setActive(inUse.condition, false)}>{t('payment_conditions_deactivate')}</button>}
                        <button type="button" className={secondaryButtonClass} onClick={() => setInUse(null)}>{t('relations_close')}</button>
                    </div>
                </div>
            )}

            {editing !== null && (
                <form noValidate aria-busy={busy} onSubmit={(event) => { event.preventDefault(); void save(); }} className="space-y-3 rounded-md border border-blue-200 bg-blue-50/40 p-3" data-testid="payment-condition-form">
                    <p className="text-sm font-semibold text-gray-900">{editing.id === null ? t('payment_conditions_new') : t('payment_conditions_edit', { code: editing.draft.code })}</p>
                    {serverError && pending === null && <ErrorNotice error={serverError} />}
                    {pending !== null && (
                        <div role="alert" className="rounded border border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                            <p className="font-medium">{t('payment_conditions_pending', { code: pending.payload.code })}</p>
                            <p className="mt-1 text-xs">{t('supply_error_outcome_unknown')}</p>
                        </div>
                    )}
                    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4">
                        {field('code', 'payment_conditions_code', { maxLength: 16, hintKey: 'payment_conditions_code_hint' })}
                        {field('descriptionNl', 'payment_conditions_description_nl', { span: 'sm:col-span-2 md:col-span-3' })}
                        {field('days', 'payment_conditions_days', { maxLength: 3, numeric: true })}
                        <div>
                            <label htmlFor="condition-basis-input" className="text-xs font-medium text-gray-700">{t('payment_conditions_basis')}</label>
                            <select {...fieldProps('condition-basis', e('basis'))} value={editing.draft.basis} disabled={busy || pending !== null} className={inputClass}
                                onChange={(event) => edit({ basis: event.target.value as PaymentConditionBasis })}>
                                {PAYMENT_CONDITION_BASES.map((basis) => <option key={basis} value={basis}>{basisLabel(basis)}</option>)}
                            </select>
                            <FieldError id="condition-basis" messages={e('basis')} />
                        </div>
                        {field('descriptionEn', 'payment_conditions_description_en', { span: 'sm:col-span-2' })}
                        {field('discountPercent', 'payment_conditions_discount_percent', { maxLength: 5, numeric: true })}
                        {field('discountDays', 'payment_conditions_discount_days', { maxLength: 3, numeric: true, hintKey: 'payment_conditions_discount_hint' })}
                        {field('sortOrder', 'payment_conditions_sort_order', { maxLength: 4, numeric: true, hintKey: 'payment_conditions_sort_order_hint' })}
                        <label className="flex items-end gap-1 pb-1 text-sm text-gray-900">
                            <input type="checkbox" checked={editing.draft.isActive} disabled={busy || pending !== null} onChange={(event) => edit({ isActive: event.target.checked })} />
                            {t('relations_active')}
                        </label>
                    </div>
                    {preview && <p className="text-xs text-gray-700" data-testid="payment-condition-preview">{preview}</p>}
                    {editing.id !== null && <p className="text-xs text-gray-600">{t('payment_conditions_days_change_hint')}</p>}
                    <div className="flex flex-wrap gap-2">
                        <button type="submit" className={primaryButtonClass} disabled={busy}>{pending !== null ? t('supply_retry_same') : busy ? t('supply_saving') : t('payment_conditions_save')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => { setEditing(null); setPending(null); setErrors({}); setServerError(null); }}>{t('inspection_cancel')}</button>
                    </div>
                </form>
            )}

            {loadError && <ErrorNotice error={loadError} actions={<button type="button" className={secondaryButtonClass} onClick={reload}>{t('availability_refresh')}</button>} />}
            {conditions === null && !loadError && <p className="text-sm text-gray-600">{t('payment_conditions_loading')}</p>}
            {conditions !== null && conditions.length === 0 && <p className="text-sm text-gray-600">{t('payment_conditions_empty')}</p>}
            {conditions !== null && conditions.length > 0 && (
                <ul className="divide-y divide-gray-200 rounded border border-gray-200">
                    {conditions.map((condition) => {
                        const used = condition.customerCount + condition.vendorCount;
                        return (
                            <li key={condition.id} className="flex flex-wrap items-start justify-between gap-2 px-3 py-2" data-testid={`payment-condition-row-${condition.id}`}>
                                <div className="min-w-60 flex-1">
                                    <p className="flex flex-wrap items-center gap-2">
                                        <span className="font-mono text-sm font-semibold text-gray-900">{condition.code}</span>
                                        <span className="text-sm text-gray-900">{description(condition)}</span>
                                        {!condition.isActive && <span className="rounded bg-gray-200 px-1.5 py-0.5 text-xs font-medium text-gray-800">{t('relations_inactive')}</span>}
                                    </p>
                                    <p className="text-xs text-gray-600">
                                        {t('payment_conditions_summary', { days: String(condition.days), basis: basisLabel(condition.basis) })}
                                        {condition.discountPercent !== null && condition.discountDays !== null && <> · {t('payment_conditions_discount_summary', { percent: String(condition.discountPercent), days: String(condition.discountDays) })}</>}
                                        {' · '}{t('payment_conditions_usage', { customers: String(condition.customerCount), vendors: String(condition.vendorCount) })}
                                    </p>
                                </div>
                                <div className="flex flex-wrap items-center gap-2">
                                    <button type="button" className={`${secondaryButtonClass} px-2 py-1 text-xs`} disabled={busy || editing !== null} onClick={() => open(condition)}>{t('payment_conditions_edit_button')}</button>
                                    <button type="button" className={`${secondaryButtonClass} px-2 py-1 text-xs`} disabled={busy} onClick={() => void setActive(condition, !condition.isActive)}>
                                        {condition.isActive ? t('payment_conditions_deactivate') : t('payment_conditions_activate')}
                                    </button>
                                    {confirmDelete === condition.id ? (
                                        <button type="button" className={`${secondaryButtonClass} border-red-300 px-2 py-1 text-xs text-red-800`} disabled={busy} onClick={() => void remove(condition)}>{t('payment_conditions_delete_confirm')}</button>
                                    ) : (
                                        <button type="button" className="px-2 py-1 text-xs text-red-800 underline underline-offset-2" disabled={busy} title={used > 0 ? t('payment_conditions_delete_used_hint') : undefined}
                                            onClick={() => setConfirmDelete(condition.id)}>{t('payment_conditions_delete')}</button>
                                    )}
                                </div>
                            </li>
                        );
                    })}
                </ul>
            )}
        </section>
    );
}

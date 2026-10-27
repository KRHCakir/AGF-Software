'use client';

import { KeyboardEvent as ReactKeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { newRequestId } from '../../lib/requestId';
import { unitLabelKey } from '../../lib/units';
import {
    InvoiceDraft, LineDraft, buildPurchaseInvoicePayload, changeCurrency, changeDueDate, changeInvoiceDate, changePaymentTerms, draftTotals, dueDateDeviation,
    emptyDraft, emptyLine, followPaymentTerms, groupErrors, isBlankLine, lineAmounts, newLineKey, selectVendor,
} from '../../lib/purchaseInvoiceRules';
import { PendingSave, clearDraft, clearPending, loadDraft, loadExtraOpen, loadPending, saveDraft, saveExtraOpen, savePending } from '../../lib/purchaseInvoiceStore';
import { ShortcutAction, isMacPlatform, matchShortcut, shortcutLabel } from '../../lib/shortcuts';
import { ApiError, toApiError } from '../../services/apiClient';
import { createPurchaseInvoice } from '../../services/purchaseInvoiceService';
import { PACKAGE_UNITS } from '../../types/availability';
import type { ProductOption } from '../../types/referenceData';
import type { PurchaseInvoice, PurchaseInvoiceSettings, PurchaseOptionalField, PurchaseVendor } from '../../types/purchaseInvoice';
import NumberEcho from '../shared/NumberEcho';
import ProductVariantPicker from '../shared/ProductVariantPicker';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import VendorPicker from './VendorPicker';

// Eén compacte werkplek voor een inkoopfactuur: documentkop, direct bewerkbare regels, live totalen en een ingeklapt
// paneel "Extra gegevens". Alles werkt zonder muis (Tab, Enter, pijltjes, sneltoetsen) en blijft ook met de muis bruikbaar.
// Financieel en logistiek blijven gescheiden: opslaan raakt nooit voorraad, pallets of kwaliteit.
// Concept en bevroren opslagopdracht leven in sessionStorage (lib/purchaseInvoiceStore): een fout, verversing of
// weg- en terugnavigeren wist niets; een verloren antwoord wordt met exact dezelfde requestId hersteld.

function todayIso(): string {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

function codeKey(code: string): string {
    return `purchase_error_${code}`;
}

export default function PurchaseInvoiceForm({ vendors, products, settings, onSaved }: {
    vendors: PurchaseVendor[];
    products: ProductOption[];
    settings: PurchaseInvoiceSettings;
    onSaved?: (invoice: PurchaseInvoice) => void;
}) {
    const { t, locale } = useTranslation();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    const [draft, setDraft] = useState<InvoiceDraft>(() => loadDraft() ?? emptyDraft(todayIso(), settings));
    const [pending, setPending] = useState<PendingSave | null>(() => loadPending());
    const [errors, setErrors] = useState<FieldErrors>({});
    const [serverError, setServerError] = useState<ApiError | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [result, setResult] = useState<{ invoice: PurchaseInvoice; replayed: boolean } | null>(null);
    const [extraOpen, setExtraOpen] = useState<boolean>(() => loadExtraOpen());
    const [activeLine, setActiveLine] = useState<string | null>(null);
    // Laatst opgeslagen document bij "Opslaan en nieuw": korte bevestiging boven het lege formulier.
    const [lastSaved, setLastSaved] = useState<{ invoice: PurchaseInvoice; replayed: boolean } | null>(null);
    const inFlight = useRef(false);
    const vendorInput = useRef<HTMLInputElement | null>(null);
    const itemInputs = useRef(new Map<string, HTMLInputElement>());
    const focusLine = useRef<string | null>(null);

    const vendor = useMemo(() => vendors.find((candidate) => String(candidate.id) === draft.header.vendorId) ?? null, [vendors, draft.header.vendorId]);
    const totals = useMemo(() => draftTotals(draft.lines), [draft.lines]);
    const locked = submitting || pending !== null;
    const dirty = result === null && (draft.header.vendorId !== '' || draft.header.invoiceNumber.trim() !== '' || draft.lines.some((line) => !isBlankLine(line)));

    // Concept bewaren bij elke wijziging; na succesvol opslaan is er geen concept meer.
    useEffect(() => { if (result === null) saveDraft(draft); }, [draft, result]);
    useEffect(() => { saveExtraOpen(extraOpen); }, [extraOpen]);
    useEffect(() => {
        if (!dirty) return;
        const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); };
        window.addEventListener('beforeunload', warn);
        return () => window.removeEventListener('beforeunload', warn);
    }, [dirty]);
    useEffect(() => {
        if (focusLine.current === null) return;
        const input = itemInputs.current.get(focusLine.current);
        focusLine.current = null;
        input?.focus();
    }, [draft.lines]);

    const update = (next: InvoiceDraft) => {
        if (locked) return;
        setDraft(next);
        setErrors({});
        setServerError(null);
    };
    const updateHeader = (field: 'invoiceNumber' | 'internalNote', value: string) => update({ ...draft, header: { ...draft.header, [field]: value } });
    const updateExtra = (field: PurchaseOptionalField, value: string) => update({ ...draft, header: { ...draft.header, extra: { ...draft.header.extra, [field]: value } } });
    const updateLine = (key: string, patch: Partial<LineDraft>) => update({ ...draft, lines: draft.lines.map((line) => (line.key === key ? { ...line, ...patch } : line)) });

    const addLine = (afterKey?: string) => {
        if (locked) return;
        const line = emptyLine(settings.defaultVatPercentage);
        const index = afterKey === undefined ? draft.lines.length : draft.lines.findIndex((candidate) => candidate.key === afterKey) + 1;
        focusLine.current = line.key;
        update({ ...draft, lines: [...draft.lines.slice(0, index), line, ...draft.lines.slice(index)] });
    };
    const duplicateLine = (key: string) => {
        if (locked) return;
        const source = draft.lines.find((line) => line.key === key);
        if (!source) return;
        const copy = { ...source, key: newLineKey() };
        const index = draft.lines.indexOf(source) + 1;
        focusLine.current = copy.key;
        update({ ...draft, lines: [...draft.lines.slice(0, index), copy, ...draft.lines.slice(index)] });
    };
    const removeLine = (key: string) => {
        if (locked) return;
        const index = draft.lines.findIndex((line) => line.key === key);
        if (index < 0) return;
        const remaining = draft.lines.filter((line) => line.key !== key);
        const lines = remaining.length === 0 ? [emptyLine(settings.defaultVatPercentage)] : remaining;
        // Focus blijft logisch: de regel op dezelfde plaats, anders de vorige.
        focusLine.current = (lines[index] ?? lines[index - 1]).key;
        update({ ...draft, lines });
    };

    const startNew = () => {
        if (submitting) return;
        if (dirty && !window.confirm(t('purchase_confirm_discard'))) return;
        clearDraft();
        clearPending();
        setPending(null);
        setResult(null);
        setErrors({});
        setServerError(null);
        setDraft(emptyDraft(todayIso(), settings));
        setTimeout(() => vendorInput.current?.focus(), 0);
    };

    const submit = async (mode: 'save' | 'saveAndNew') => {
        if (inFlight.current || result !== null) return;
        let command = pending;
        if (command === null) {
            const built = buildPurchaseInvoicePayload(draft, settings);
            if (!built.ok) {
                const grouped = groupErrors(built.errors);
                setErrors(Object.fromEntries(Object.entries(grouped).map(([field, codes]) => [field, codes.map((code) => t(codeKey(code)))])));
                focusFirstError(grouped);
                return;
            }
            const requestId = newRequestId();
            command = { requestId, payload: { requestId, ...built.value }, mode, attempts: 0, lastError: null, frozenAt: new Date().toISOString() };
            savePending(command);
            setPending(command);
        }
        inFlight.current = true;
        setSubmitting(true);
        setServerError(null);
        try {
            const saved = await createPurchaseInvoice(command.payload);
            clearPending();
            clearDraft();
            setPending(null);
            onSaved?.(saved.value);
            if (command.mode === 'saveAndNew') {
                // Pas een leeg document zodra de opslag bevestigd is.
                setDraft(emptyDraft(todayIso(), settings));
                setLastSaved({ invoice: saved.value, replayed: saved.replayed });
                setTimeout(() => vendorInput.current?.focus(), 0);
            } else {
                setResult({ invoice: saved.value, replayed: saved.replayed });
            }
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                // Onbekende uitkomst: dezelfde opdracht blijft herstelbaar (zelfde requestId), nooit een nieuwe.
                const next = { ...command, attempts: command.attempts + 1, lastError: apiError.message };
                savePending(next);
                setPending(next);
            } else {
                clearPending();
                setPending(null);
                setErrors(apiError.fieldErrors);
            }
            setServerError(apiError);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const abandonPending = () => {
        clearPending();
        setPending(null);
        setServerError(null);
    };

    const focusFirstError = (grouped: Record<string, unknown>) => {
        const first = Object.keys(grouped)[0];
        if (!first) return;
        const id = first === 'lines' ? null : first.endsWith('.description') ? `${first.replace('.description', '.item')}-input` : `${first}-input`;
        if (id) setTimeout(() => document.getElementById(id)?.focus(), 0);
    };

    // Sneltoetsen op het hele formulier; Escape sluit eerst een open zoeklijst (LookupField stopt die zelf) en gooit nooit iets weg.
    const onShortcut = (action: ShortcutAction) => {
        switch (action) {
            case 'save': void submit('save'); return;
            case 'saveAndNew': void submit('saveAndNew'); return;
            case 'newDocument': startNew(); return;
            case 'focusSearch': vendorInput.current?.focus(); return;
            case 'toggleExtra': setExtraOpen((open) => !open); return;
            case 'duplicateLine': if (activeLine) duplicateLine(activeLine); return;
            case 'removeLine': if (activeLine) removeLine(activeLine); return;
        }
    };
    // Sneltoetsen gelden op de hele pagina zolang de werkplek open staat (ook als niets focus heeft), zodat ⌥F/Alt+F
    // altijd naar het leveranciersveld springt. Regelacties werken op de regel waarin de focus staat.
    const shortcutHandler = useRef<(event: KeyboardEvent) => void>(() => {});
    shortcutHandler.current = (event: KeyboardEvent) => {
        const action = matchShortcut(event, isMac);
        if (action === null) return;
        event.preventDefault();
        const target = event.target instanceof HTMLElement ? event.target : null;
        const lineKey = target?.closest('tr[data-line-key]')?.getAttribute('data-line-key') ?? activeLine;
        if (action === 'duplicateLine') { if (lineKey) duplicateLine(lineKey); return; }
        if (action === 'removeLine') { if (lineKey) removeLine(lineKey); return; }
        onShortcut(action);
    };
    useEffect(() => {
        const listener = (event: KeyboardEvent) => shortcutHandler.current(event);
        document.addEventListener('keydown', listener, true);
        return () => document.removeEventListener('keydown', listener, true);
    }, []);

    // Enter in de regelgrid: naar de volgende cel; op de laatste cel van de laatste regel een nieuwe regel.
    const onLineKeyDown = (event: ReactKeyboardEvent<HTMLTableRowElement>, line: LineDraft) => {
        if (event.key !== 'Enter' || event.metaKey || event.ctrlKey || event.altKey || event.defaultPrevented) return;
        const target = event.target as HTMLElement;
        if (target.getAttribute('aria-expanded') === 'true') return; // de zoeklijst kiest zelf
        const cells = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('input:not([tabindex="-1"]), select')).filter((el) => !el.hasAttribute('disabled'));
        const index = cells.indexOf(target);
        if (index < 0) return;
        event.preventDefault();
        if (index < cells.length - 1) { cells[index + 1].focus(); return; }
        const lineIndex = draft.lines.indexOf(line);
        if (lineIndex === draft.lines.length - 1) addLine(line.key);
        else itemInputs.current.get(draft.lines[lineIndex + 1].key)?.focus();
    };

    const e = (key: string) => errors[key];
    const deviation = dueDateDeviation(draft);
    const defaultsNote = (field: 'paymentTermsDays' | 'currency') => {
        if (!draft.touched[field] || vendor === null) return null;
        const standard = field === 'paymentTermsDays' ? String(vendor.paymentTermsDays) : (vendor.defaultCurrency ?? settings.defaultCurrency);
        return standard === draft.header[field] ? null : t('purchase_manual_vs_default', { value: standard });
    };
    const money = (value: number) => formatMoney(value, null, locale);
    const unknown = serverError !== null && outcomeUnknown(serverError);

    if (result !== null) {
        return (
            <section role="status" className="rounded-md border border-green-300 bg-green-50 p-4">
                <h2 className="text-lg font-semibold text-green-900">{t('purchase_saved_title', { number: result.invoice.invoiceNumber ?? '' })}</h2>
                {result.replayed && <p className="mt-1 text-sm text-green-900">{t('supply_saved_before')}</p>}
                <p className="mt-1 text-sm text-gray-800">{t('purchase_saved_summary', { vendor: result.invoice.vendorName ?? '', total: money(result.invoice.totalAmount), currency: result.invoice.currency ?? '', status: result.invoice.status })}</p>
                <p className="mt-1 text-xs text-gray-600">{t('purchase_no_stock_effect')}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                    <button type="button" className={primaryButtonClass} onClick={startNew}>{t('purchase_new_document')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('newDocument', isMac)}</kbd></button>
                </div>
            </section>
        );
    }

    return (
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit('save'); }} className="space-y-3">
            {lastSaved !== null && (
                <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">
                    {t('purchase_saved_title', { number: lastSaved.invoice.invoiceNumber ?? '' })} · {money(lastSaved.invoice.totalAmount)} {lastSaved.invoice.currency}{lastSaved.replayed ? ` · ${t('supply_saved_before')}` : ''}
                </p>
            )}
            {pending !== null && (
                <div role="status" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
                    <p className="font-medium">{t('purchase_pending', { number: pending.payload.invoiceNumber, attempts: String(pending.attempts) })}</p>
                    {serverError && unknown && <p className="mt-1">{t('supply_error_outcome_unknown')}</p>}
                    <div className="mt-2 flex flex-wrap gap-2">
                        <button type="button" className={primaryButtonClass} disabled={submitting} onClick={() => void submit(pending.mode)}>{t('supply_retry_same')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={submitting} onClick={abandonPending}>{t('purchase_pending_abandon')}</button>
                    </div>
                </div>
            )}
            {serverError && !unknown && <ErrorNotice error={serverError} />}
            {e('lines') && <FieldError id="lines" messages={e('lines')} />}

            {/* Documentkop */}
            <fieldset className="grid grid-cols-2 gap-3 rounded-md border border-gray-200 bg-white p-3 md:grid-cols-7" disabled={locked}>
                <legend className="px-1 text-sm font-semibold text-gray-900">{t('purchase_header')}</legend>
                <div className="col-span-2">
                    <label htmlFor="vendorId-input" className="text-xs font-medium text-gray-700">{t('purchase_vendor')} *</label>
                    <VendorPicker id="vendorId" ariaLabel={t('purchase_vendor')} vendors={vendors} value={draft.header.vendorId} disabled={locked} invalid={!!e('vendorId')}
                        describedBy={e('vendorId') ? 'vendorId' : undefined} inputRef={(element) => { vendorInput.current = element; }}
                        onChange={(chosen) => update(selectVendor(draft, chosen, settings))} />
                    <FieldError id="vendorId" messages={e('vendorId')} />
                    {/* Betalen naar een niet-bevestigde rekening is het risico: duidelijk tonen, niet blokkeren (de factuur zelf mag erin). */}
                    {vendor?.defaultBankAccountUnconfirmed && (
                        <p role="alert" className="mt-1 rounded border-2 border-red-400 bg-red-50 px-2 py-1 text-xs font-medium text-red-950" data-testid="purchase-bank-unconfirmed">
                            ⚠ {t('purchase_bank_unconfirmed')}
                        </p>
                    )}
                    {vendor !== null && vendor.paymentConditionBasis === 'EndOfMonth' && <span className="block text-xs text-gray-600">{t('purchase_condition_end_of_month', { code: vendor.paymentConditionCode ?? '' })}</span>}
                </div>
                <div>
                    <label htmlFor="invoiceNumber-input" className="text-xs font-medium text-gray-700">{t('invoice_number')} *</label>
                    <input {...fieldProps('invoiceNumber', e('invoiceNumber'))} value={draft.header.invoiceNumber} maxLength={100} onChange={(event) => updateHeader('invoiceNumber', event.target.value)} className={inputClass} />
                    <FieldError id="invoiceNumber" messages={e('invoiceNumber')} />
                </div>
                <div>
                    <label htmlFor="invoiceDate-input" className="text-xs font-medium text-gray-700">{t('invoice_date')} *</label>
                    <input {...fieldProps('invoiceDate', e('invoiceDate'))} type="date" value={draft.header.invoiceDate} onChange={(event) => update(changeInvoiceDate(draft, event.target.value))} className={inputClass} />
                    <FieldError id="invoiceDate" messages={e('invoiceDate')} />
                </div>
                <div>
                    <label htmlFor="paymentTermsDays-input" className="text-xs font-medium text-gray-700">{t('purchase_payment_terms')} *</label>
                    <input {...fieldProps('paymentTermsDays', e('paymentTermsDays'))} inputMode="numeric" value={draft.header.paymentTermsDays} onChange={(event) => update(changePaymentTerms(draft, event.target.value))} className={inputClass} />
                    {defaultsNote('paymentTermsDays') && <span className="block text-xs text-gray-500">{defaultsNote('paymentTermsDays')}</span>}
                    <FieldError id="paymentTermsDays" messages={e('paymentTermsDays')} />
                </div>
                <div>
                    <label htmlFor="dueDate-input" className="text-xs font-medium text-gray-700">{t('purchase_due_date')} *</label>
                    <input {...fieldProps('dueDate', e('dueDate'))} type="date" value={draft.header.dueDate} onChange={(event) => update(changeDueDate(draft, event.target.value))} className={inputClass} />
                    {draft.touched.dueDate && (
                        <span className="block text-xs text-gray-600">
                            {deviation === null ? t('purchase_due_manual') : t('purchase_due_deviation', { days: String(deviation) })}{' '}
                            <button type="button" tabIndex={-1} onClick={() => update(followPaymentTerms(draft))} className="font-medium text-blue-800 underline underline-offset-2">{t('purchase_due_follow_terms')}</button>
                        </span>
                    )}
                    <FieldError id="dueDate" messages={e('dueDate')} />
                </div>
                <div>
                    <label htmlFor="currency-input" className="text-xs font-medium text-gray-700">{t('purchase_currency')} *</label>
                    <input {...fieldProps('currency', e('currency'))} value={draft.header.currency} maxLength={3} onChange={(event) => update(changeCurrency(draft, event.target.value))} className={`${inputClass} font-mono uppercase`} />
                    {defaultsNote('currency') && <span className="block text-xs text-gray-500">{defaultsNote('currency')}</span>}
                    <span className="block text-xs text-gray-500">{t('purchase_currency_hint')}</span>
                    <FieldError id="currency" messages={e('currency')} />
                </div>
            </fieldset>

            {/* Extra gegevens: standaard dicht, zonder apart scherm; alleen de door het bedrijf ingeschakelde velden. */}
            {settings.optionalFields.length > 0 && (
                <section className="rounded-md border border-gray-200 bg-white">
                    <button type="button" aria-expanded={extraOpen} aria-controls="purchase-extra" onClick={() => setExtraOpen((open) => !open)}
                        className="flex w-full items-center justify-between px-3 py-2 text-left text-sm font-semibold text-gray-900">
                        <span>{extraOpen ? '▾' : '▸'} {t('purchase_extra')}</span>
                        <kbd className="text-xs font-normal text-gray-500">{shortcutLabel('toggleExtra', isMac)}</kbd>
                    </button>
                    {extraOpen && (
                        <div id="purchase-extra" className="grid grid-cols-2 gap-3 border-t border-gray-200 p-3 md:grid-cols-4">
                            {settings.optionalFields.map((field) => (
                                <div key={field} className={field === 'remarks' ? 'col-span-2 md:col-span-4' : ''}>
                                    <label htmlFor={`${field}-input`} className="text-xs font-medium text-gray-700">{t(`purchase_field_${field}`)}</label>
                                    {field === 'remarks' ? (
                                        <textarea {...fieldProps(field, e(field))} rows={2} maxLength={1000} value={draft.header.extra[field] ?? ''} disabled={locked} onChange={(event) => updateExtra(field, event.target.value)} className={inputClass} />
                                    ) : (
                                        <input {...fieldProps(field, e(field))} type={field === 'receivedDate' ? 'date' : 'text'} inputMode={['grossWeightKg', 'netWeightKg', 'customsValue'].includes(field) ? 'decimal' : undefined}
                                            value={draft.header.extra[field] ?? ''} maxLength={200} disabled={locked} onChange={(event) => updateExtra(field, event.target.value)} className={inputClass} />
                                    )}
                                    <FieldError id={field} messages={e(field)} />
                                </div>
                            ))}
                            <p className="col-span-2 text-xs text-gray-500 md:col-span-4">{t('purchase_extra_hint')}</p>
                        </div>
                    )}
                </section>
            )}

            {/* Regels */}
            <div className="overflow-x-auto rounded-md border border-gray-200 bg-white">
                <table className="min-w-full divide-y divide-gray-200 text-sm">
                    <caption className="sr-only">{t('purchase_lines')}</caption>
                    <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                        <tr>
                            <th scope="col" className="px-2 py-1.5">{t('purchase_col_item')} *</th>
                            <th scope="col" className="px-2 py-1.5">{t('supply_quantity')} *</th>
                            <th scope="col" className="px-2 py-1.5">{t('supply_unit')}</th>
                            <th scope="col" className="px-2 py-1.5">{t('purchase_col_price')} *</th>
                            <th scope="col" className="px-2 py-1.5">{t('purchase_col_discount')}</th>
                            <th scope="col" className="px-2 py-1.5">{t('purchase_col_vat')}</th>
                            <th scope="col" className="px-2 py-1.5 text-right">{t('purchase_col_amount')}</th>
                            <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('purchase_line_actions')}</span></th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {draft.lines.map((line, index) => {
                            const prefix = `lines[${index}]`;
                            const amounts = lineAmounts(line);
                            return (
                                <tr key={line.key} data-line-key={line.key} className="align-top" onKeyDown={(event) => onLineKeyDown(event, line)} onFocusCapture={() => setActiveLine(line.key)}>
                                    <td className="w-80 px-2 py-1.5">
                                        <ProductVariantPicker id={`${prefix}.item`} ariaLabel={`${t('purchase_col_item')} ${index + 1}`} products={products} value={line.productVariantId}
                                            allowFreeText freeText={line.description} disabled={locked} invalid={!!e(`${prefix}.description`) || !!e(`${prefix}.productVariantId`)}
                                            describedBy={e(`${prefix}.description`) ? `${prefix}.description` : undefined}
                                            inputRef={(element) => { if (element) itemInputs.current.set(line.key, element); else itemInputs.current.delete(line.key); }}
                                            onChange={(variantId) => {
                                                const variant = products.flatMap((product) => product.variants.map((candidate) => ({ product, variant: candidate }))).find((pair) => String(pair.variant.id) === variantId);
                                                // Artikelstandaard: de verpakkingseenheid vult alleen een nog lege eenheid voor; handmatige keuze blijft staan.
                                                const unit = line.unit === '' && variant?.variant.packagingUnit ? { unit: variant.variant.packagingUnit } : {};
                                                updateLine(line.key, { productVariantId: variantId, description: variant ? `${variant.product.name} · ${variant.variant.name}` : '', ...unit });
                                            }}
                                            onFreeText={(text) => { if (line.productVariantId === '' && text !== line.description) updateLine(line.key, { description: text }); }} />
                                        <FieldError id={`${prefix}.description`} messages={e(`${prefix}.description`) ?? e(`${prefix}.productVariantId`)} />
                                    </td>
                                    <td className="w-24 px-2 py-1.5">
                                        <label htmlFor={`${prefix}.quantity-input`} className="sr-only">{t('supply_quantity')} {index + 1}</label>
                                        <input {...fieldProps(`${prefix}.quantity`, e(`${prefix}.quantity`))} inputMode="decimal" value={line.quantity} disabled={locked} onChange={(event) => updateLine(line.key, { quantity: event.target.value })} className={inputClass} />
                                        <NumberEcho value={line.quantity} unit={line.unit === '' ? undefined : t(unitLabelKey(line.unit)).toLowerCase()} testId={`${prefix}.quantity-echo`} />
                                        <FieldError id={`${prefix}.quantity`} messages={e(`${prefix}.quantity`)} />
                                    </td>
                                    <td className="w-28 px-2 py-1.5">
                                        <label htmlFor={`${prefix}.unit-input`} className="sr-only">{t('supply_unit')} {index + 1}</label>
                                        <select {...fieldProps(`${prefix}.unit`, e(`${prefix}.unit`))} value={line.unit} disabled={locked} onChange={(event) => updateLine(line.key, { unit: event.target.value as LineDraft['unit'] })} className={inputClass}>
                                            <option value="">–</option>
                                            {PACKAGE_UNITS.map((unit) => <option key={unit} value={unit}>{t(unitLabelKey(unit))}</option>)}
                                        </select>
                                    </td>
                                    <td className="w-28 px-2 py-1.5">
                                        <label htmlFor={`${prefix}.unitPrice-input`} className="sr-only">{t('purchase_col_price')} {index + 1}</label>
                                        <input {...fieldProps(`${prefix}.unitPrice`, e(`${prefix}.unitPrice`))} inputMode="decimal" value={line.unitPrice} disabled={locked} onChange={(event) => updateLine(line.key, { unitPrice: event.target.value })} className={inputClass} />
                                        <FieldError id={`${prefix}.unitPrice`} messages={e(`${prefix}.unitPrice`)} />
                                    </td>
                                    <td className="w-20 px-2 py-1.5">
                                        <label htmlFor={`${prefix}.discountPercentage-input`} className="sr-only">{t('purchase_col_discount')} {index + 1}</label>
                                        <input {...fieldProps(`${prefix}.discountPercentage`, e(`${prefix}.discountPercentage`))} inputMode="decimal" value={line.discount} disabled={locked} onChange={(event) => updateLine(line.key, { discount: event.target.value })} className={inputClass} />
                                        <FieldError id={`${prefix}.discountPercentage`} messages={e(`${prefix}.discountPercentage`)} />
                                    </td>
                                    <td className="w-20 px-2 py-1.5">
                                        <label htmlFor={`${prefix}.vatPercentage-input`} className="sr-only">{t('purchase_col_vat')} {index + 1}</label>
                                        <input {...fieldProps(`${prefix}.vatPercentage`, e(`${prefix}.vatPercentage`))} inputMode="decimal" value={line.vat} disabled={locked} onChange={(event) => updateLine(line.key, { vat: event.target.value })} className={inputClass} />
                                        <FieldError id={`${prefix}.vatPercentage`} messages={e(`${prefix}.vatPercentage`)} />
                                    </td>
                                    <td className="w-28 px-2 py-1.5 text-right font-mono" data-testid={`${prefix}.amount`}>
                                        {amounts === null ? <span className="text-gray-400">–</span> : <>{money(amounts.net)}<span className="block text-xs text-gray-500">{t('purchase_vat_short')} {money(amounts.vat)}</span></>}
                                    </td>
                                    <td className="whitespace-nowrap px-2 py-1.5">
                                        <button type="button" tabIndex={-1} onClick={() => duplicateLine(line.key)} disabled={locked} title={`${t('purchase_line_duplicate')} (${shortcutLabel('duplicateLine', isMac)})`} aria-label={`${t('purchase_line_duplicate')} ${index + 1}`} className="rounded border border-gray-300 px-2 py-1 text-xs text-gray-700 hover:bg-gray-50 disabled:opacity-40">⧉</button>
                                        <button type="button" tabIndex={-1} onClick={() => removeLine(line.key)} disabled={locked} title={`${t('purchase_line_remove')} (${shortcutLabel('removeLine', isMac)})`} aria-label={`${t('purchase_line_remove')} ${index + 1}`} className="ml-1 rounded border border-gray-300 px-2 py-1 text-xs text-gray-700 hover:bg-gray-50 disabled:opacity-40">✕</button>
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>
            <div className="flex flex-wrap items-center gap-3">
                <button type="button" tabIndex={-1} onClick={() => addLine()} disabled={locked} className={secondaryButtonClass}>+ {t('purchase_line_add')}</button>
                <span className="text-xs text-gray-600">{t('purchase_grid_hint')}</span>
            </div>

            {/* Interne opmerking (na de regels, zodat de toetsenbordroute naar regel 1 gelijk blijft) en live totalen */}
            <div className="flex flex-wrap items-start gap-3">
                <div className="min-w-[16rem] flex-1">
                    <label htmlFor="internalNote-input" className="text-xs font-medium text-gray-700">{t('internal_note_label')}</label>
                    <input {...fieldProps('internalNote', e('internalNote'))} value={draft.header.internalNote} maxLength={1000} disabled={locked} placeholder={t('internal_note_placeholder')}
                        onChange={(event) => updateHeader('internalNote', event.target.value)} className={inputClass} />
                    <span className="block text-xs text-gray-500">{t('internal_note_hint')}</span>
                    <FieldError id="internalNote" messages={e('internalNote')} />
                </div>
            <dl className="ml-auto grid w-full max-w-sm grid-cols-2 gap-x-4 gap-y-1 rounded-md border border-gray-200 bg-white px-3 py-2 text-sm" data-testid="purchase-totals">
                <dt className="text-gray-600">{t('purchase_total_net')}</dt><dd className="text-right font-mono" data-testid="total-net">{money(totals.net)}</dd>
                <dt className="text-gray-600">{t('purchase_total_vat')}</dt><dd className="text-right font-mono" data-testid="total-vat">{money(totals.vat)}</dd>
                <dt className="font-semibold text-gray-900">{t('purchase_total_gross', { currency: draft.header.currency })}</dt><dd className="text-right font-mono font-semibold" data-testid="total-gross">{money(totals.gross)}</dd>
            </dl>
            </div>

            <div className="flex flex-wrap items-center gap-2 border-t border-gray-200 pt-3">
                <button type="submit" className={primaryButtonClass} disabled={locked && !unknown}>{submitting ? t('supply_saving') : t('purchase_save')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('save', isMac)}</kbd></button>
                <button type="button" className={secondaryButtonClass} disabled={locked && !unknown} onClick={() => void submit('saveAndNew')}>{t('purchase_save_and_new')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('saveAndNew', isMac)}</kbd></button>
                <button type="button" className={secondaryButtonClass} disabled={submitting} onClick={startNew}>{t('purchase_new_document')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('newDocument', isMac)}</kbd></button>
                <span className="text-xs text-gray-600">{t('purchase_footer_hint', { search: shortcutLabel('focusSearch', isMac) })}</span>
                {lastSaved === null && draft.header.invoiceDate !== '' && <span className="ml-auto text-xs text-gray-500">{t('purchase_date_today_hint', { date: formatCalendarDate(`${draft.header.invoiceDate}T00:00:00`, locale) })}</span>}
            </div>
        </form>
    );
}

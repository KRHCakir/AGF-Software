'use client';

import Link from 'next/link';
import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatQuantity } from '../../lib/availability';
import { formatDateOnly } from '../../lib/calendarDate';
import { formatAmountInput } from '../../lib/decimal';
import { parseUnitPrice } from '../../lib/packaging';
import { changeFieldKey, isDefinitive } from '../../lib/purchaseOrderEdit';
import { MAX_NOTE_LENGTH, MAX_SUPPLIER_REFERENCE_LENGTH } from '../../lib/purchaseOrderRules';
import { newRequestId } from '../../lib/requestId';
import { type ApiError, toApiError } from '../../services/apiClient';
import { correctPurchaseOrder, finalizePurchaseOrder } from '../../services/purchaseOrderService';
import type { CorrectPurchaseOrderRequest, PurchaseOrderChange, PurchaseOrderDetail } from '../../types/purchaseOrder';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { decimalErrorKey, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

const MAX_REASON_LENGTH = 500;
const NUMBER_FIELDS: ReadonlySet<string> = new Set(['unitPrice', 'expectedQuantity', 'expectedNetWeightKg', 'expectedGrossWeightKg']);
const DATE_FIELDS: ReadonlySet<string> = new Set(['orderedOn', 'expectedAt']);
const CONFLICT_KEYS: Readonly<Record<string, string>> = { stale: 'po_edit_stale', definitive: 'po_edit_definitive_now', open: 'po_edit_stale', notEditable: 'po_edit_not_editable' };
const conflictKey = (error: ApiError) => {
    const code = (error.body as { code?: unknown } | null | undefined)?.code;
    return typeof code === 'string' ? CONFLICT_KEYS[code] ?? null : null;
};

// Documentstatus op de order (stap 4a): zolang ze Open is Aanpassen en Definitief maken; daarna de melding wie en wanneer, alleen-lezen,
// en Correctie (prijs per regel, ordernummer van de leverancier, notitie) met verplichte reden.
export default function PurchaseOrderDocument({ order, onChanged }: { order: PurchaseOrderDetail; onChanged: (order: PurchaseOrderDetail) => void }) {
    const { t, locale } = useTranslation();
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [correcting, setCorrecting] = useState(false);
    // Eerst de vraag op de pagina (geen browservenster, zodat ook de rondleiding hem kan beantwoorden), dan pas versturen.
    const [confirming, setConfirming] = useState(false);
    const finalizeRequest = useRef<string | null>(null);

    const finalize = async () => {
        if (busy) return;
        setBusy(true);
        setError(null);
        finalizeRequest.current ??= newRequestId();
        try {
            onChanged(await finalizePurchaseOrder(order.id, { requestId: finalizeRequest.current, expectedVersion: order.editVersion ?? 0 }));
            finalizeRequest.current = null;
            setConfirming(false);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    if (isDefinitive(order)) {
        return (
            <div className="mt-2 space-y-2" data-testid="po-document">
                <p role="status" className="rounded border border-slate-300 bg-slate-50 px-3 py-2 text-sm text-slate-900" data-tour="po-definitive" data-testid="po-definitive">
                    <span className="font-semibold">{t('po_definitive_since', { date: order.finalizedAt ? formatDateTime(order.finalizedAt, locale) : '–', name: order.finalizedBy ?? '–' })}</span>
                    {order.finalizedVia === 'Invoice' && <> ({t('po_definitive_via_invoice')})</>}. {t('po_definitive_readonly')}
                </p>
                {!correcting && (
                    <button type="button" className={secondaryButtonClass} data-tour="po-correct" onClick={() => setCorrecting(true)}>{t('po_correct')}</button>
                )}
                {correcting && <CorrectionPanel order={order} onClose={() => setCorrecting(false)} onSaved={(saved) => { setCorrecting(false); onChanged(saved); }} />}
            </div>
        );
    }
    if (order.editable === false) return null;
    return (
        <div className="mt-2 space-y-2" data-testid="po-document">
            <div className="flex flex-wrap gap-2">
                <Link href={`/purchase-orders/edit?id=${order.id}`} className={secondaryButtonClass} data-tour="po-edit">{t('po_edit')}</Link>
                <button type="button" className={secondaryButtonClass} data-tour="po-finalize" disabled={busy || confirming} onClick={() => setConfirming(true)}>{t('po_finalize')}</button>
            </div>
            {confirming && (
                <div role="group" aria-label={t('po_finalize')} className="rounded border border-slate-400 bg-slate-50 px-3 py-2 text-sm text-slate-900" data-testid="po-finalize-question">
                    <p>{t('po_finalize_confirm')}</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                        <button type="button" className={primaryButtonClass} data-tour="po-finalize-yes" disabled={busy} onClick={() => void finalize()}>{t('po_finalize_yes')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => { setConfirming(false); setError(null); }}>{t('cancel')}</button>
                    </div>
                </div>
            )}
            {error && <p role="alert" className="text-sm text-red-800" data-testid="po-document-error">{conflictKey(error) ? t(conflictKey(error)!) : error.message}</p>}
        </div>
    );
}

function CorrectionPanel({ order, onClose, onSaved }: { order: PurchaseOrderDetail; onClose: () => void; onSaved: (order: PurchaseOrderDetail) => void }) {
    const { t } = useTranslation();
    const [reference, setReference] = useState(order.supplierReference ?? '');
    const [note, setNote] = useState(order.note ?? '');
    const [prices, setPrices] = useState<Record<number, string>>(() => Object.fromEntries(order.lines.map((line) => [line.id, line.unitPrice == null ? '' : formatAmountInput(line.unitPrice)])));
    const [reason, setReason] = useState('');
    const [errors, setErrors] = useState<Record<string, string[]>>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);
    // Bevroren verzoek: een herhaling na een verloren antwoord stuurt exact hetzelfde (de server herkent de requestId).
    const pending = useRef<CorrectPurchaseOrderRequest | null>(null);
    const edited = () => { pending.current = null; setErrors({}); setError(null); };

    const build = (): CorrectPurchaseOrderRequest | null => {
        const found: Record<string, string[]> = {};
        if (reason.trim() === '') found.reason = [t('po_correct_reason_required')];
        const lines: { id: number; unitPrice: number | null }[] = [];
        for (const line of order.lines) {
            // Een leeg prijsveld laat de prijs staan (een correctie wist geen prijs); alleen een andere prijs gaat mee.
            const parsed = parseUnitPrice(prices[line.id] ?? '');
            if (!parsed.ok) { found[`price-${line.id}`] = [t(decimalErrorKey(parsed.error))]; continue; }
            if (parsed.value !== null && parsed.value !== (line.unitPrice ?? null)) lines.push({ id: line.id, unitPrice: parsed.value });
        }
        const blank = (value: string) => (value.trim() === '' ? null : value.trim());
        if (lines.length === 0 && blank(reference) === (order.supplierReference ?? null) && blank(note) === (order.note ?? null)) found.correction = [t('po_correct_nothing')];
        setErrors(found);
        if (Object.keys(found).length > 0) return null;
        // De volledige nieuwe tekst; leeg wist het veld (weggelaten zou "ongewijzigd" betekenen).
        return { requestId: newRequestId(), expectedVersion: order.editVersion ?? 0, reason: reason.trim(), supplierReference: reference.trim(), note: note.trim(), lines };
    };

    const save = async () => {
        if (busy) return;
        const request = pending.current ?? build();
        if (request === null) return;
        pending.current = request;
        setBusy(true);
        setError(null);
        try {
            onSaved(await correctPurchaseOrder(order.id, request));
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            setErrors(apiError.fieldErrors);
            if (!outcomeUnknown(apiError)) pending.current = null;
        } finally {
            setBusy(false);
        }
    };

    return (
        <form noValidate className="space-y-3 rounded border border-slate-300 bg-white p-3" data-testid="po-correction" data-tour="po-correction"
            onSubmit={(event) => { event.preventDefault(); void save(); }}>
            <h3 className="text-sm font-semibold text-gray-900">{t('po_correct_title')}</h3>
            <p className="text-xs text-gray-700">{t('po_correct_hint')}</p>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div>
                    <label htmlFor="correction-reference-input" className="text-xs font-medium text-gray-700">{t('po_supplier_reference')}</label>
                    <input {...fieldProps('correction-reference', errors.supplierReference)} value={reference} maxLength={MAX_SUPPLIER_REFERENCE_LENGTH} className={`${inputClass} font-mono`}
                        onChange={(event) => { setReference(event.target.value); edited(); }} />
                    <FieldError id="correction-reference" messages={errors.supplierReference} />
                </div>
                <div>
                    <label htmlFor="correction-note-input" className="text-xs font-medium text-gray-700">{t('po_note')}</label>
                    <input {...fieldProps('correction-note', errors.note)} value={note} maxLength={MAX_NOTE_LENGTH} className={inputClass}
                        onChange={(event) => { setNote(event.target.value); edited(); }} />
                    <FieldError id="correction-note" messages={errors.note} />
                </div>
            </div>
            <ul className="space-y-1">
                {order.lines.map((line) => (
                    <li key={line.id} className="flex flex-wrap items-center gap-2 text-sm">
                        <label htmlFor={`price-${line.id}-input`} className="min-w-0 flex-1 break-words text-gray-900">
                            {[line.productName, line.productVariantName].filter(Boolean).join(' · ')} · {t('po_change_price')}
                        </label>
                        <input {...fieldProps(`price-${line.id}`, errors[`price-${line.id}`])} inputMode="decimal" value={prices[line.id] ?? ''} data-tour="po-correction-price" data-tour-key={line.id}
                            className={`${inputClass} !mt-0 w-28 text-right`} onChange={(event) => { setPrices((current) => ({ ...current, [line.id]: event.target.value })); edited(); }} />
                        <FieldError id={`price-${line.id}`} messages={errors[`price-${line.id}`]} />
                    </li>
                ))}
            </ul>
            <div>
                <label htmlFor="correction-reason-input" className="text-xs font-medium text-gray-700">{t('po_correct_reason')}</label>
                <textarea id="correction-reason-input" value={reason} maxLength={MAX_REASON_LENGTH} rows={2} data-tour="po-correction-reason"
                    aria-invalid={errors.reason ? true : undefined} aria-describedby={errors.reason ? 'correction-reason' : undefined}
                    className={inputClass} onChange={(event) => { setReason(event.target.value); edited(); }} />
                <FieldError id="correction-reason" messages={errors.reason} />
            </div>
            <FieldError id="correction" messages={errors.correction} />
            {error && !error.fieldErrors?.reason && <p role="alert" className="text-sm text-red-800">{conflictKey(error) ? t(conflictKey(error)!) : error.message}</p>}
            <div className="flex flex-wrap gap-2">
                <button type="submit" className={primaryButtonClass} disabled={busy} data-tour="po-correction-save">{t('po_correct_save')}</button>
                <button type="button" className={secondaryButtonClass} disabled={busy} onClick={onClose}>{t('cancel')}</button>
            </div>
        </form>
    );
}

// De auditregels onder de geschiedenis: per wijziging wie, wanneer, waarom (correctie) en per veld oud → nieuw.
export function PurchaseOrderChanges({ order }: { order: PurchaseOrderDetail }) {
    const { t, locale } = useTranslation();
    const changes = order.changes ?? [];
    if (changes.length === 0) return null;
    const lineName = (id: number | null) => {
        const line = id === null ? undefined : order.lines.find((candidate) => candidate.id === id);
        return line ? [line.productName, line.productVariantName].filter(Boolean).join(' ') : null;
    };
    // Getallen en datums van de bekende getal- en datumvelden in de notatie van de schermtaal (opgeslagen als vaste tekst: 12.5,
    // 2026-11-14); alle andere velden precies zoals vastgelegd (een ordernummer "0045" blijft "0045").
    const value = (text: string | null, field = '') => {
        if (text === null || text === '') return t('po_change_empty');
        if (NUMBER_FIELDS.has(field) && /^-?\d+(\.\d+)?$/.test(text)) return field === 'unitPrice' ? formatAmountInput(Number(text)) : formatQuantity(Number(text), locale);
        if (DATE_FIELDS.has(field) && /^\d{4}-\d{2}-\d{2}$/.test(text)) return formatDateOnly(text, locale);
        return text;
    };
    const kind = (change: PurchaseOrderChange) => t(change.kind === 'Edit' ? 'po_edit' : change.kind === 'Finalize' ? 'po_finalize' : 'po_correct');
    return (
        <div className="mt-3" data-testid="po-changes" data-tour="po-changes">
            <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-gray-700">{t('po_changes')}</h4>
            <ul className="space-y-2 text-sm">
                {changes.map((change) => (
                    <li key={change.id} className="break-words">
                        <span className="text-gray-600">{formatDateTime(change.createdAt, locale)}</span> · <span className="font-medium">{kind(change)}</span> · {change.actorReference}
                        {change.reason && <> · {change.reason}</>}
                        {change.kind !== 'Finalize' && (
                            <ul className="ml-4 list-disc text-gray-800">
                                {change.fields.map((field, index) => {
                                    const label = changeFieldKey(field.field);
                                    const name = [label ? t(label) : field.field, lineName(field.lineId)].filter(Boolean).join(' · ');
                                    if (field.action === 'added') return <li key={index}>{t('po_change_line')} {t('po_change_added')}: {value(field.new)}</li>;
                                    if (field.action === 'removed') return <li key={index}>{t('po_change_line')} {t('po_change_removed')}: {value(field.old)}</li>;
                                    return <li key={index}>{name}: {value(field.old, field.field)} → {value(field.new, field.field)}</li>;
                                })}
                            </ul>
                        )}
                    </li>
                ))}
            </ul>
        </div>
    );
}

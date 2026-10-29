'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDecimalInput, parseLocaleDecimal } from '../../lib/decimal';
import { InvoiceRefCommand, invoiceRefKey, withAttempt } from '../../lib/pendingCommands';
import { MAX_INVOICE_NUMBER_LENGTH, normalizeInvoiceNumber } from '../../lib/purchaseOrderRules';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { addInvoiceRef } from '../../services/purchaseOrderService';
import type { AddInvoiceRefRequest, PurchaseOrderDetail } from '../../types/purchaseOrder';
import { PendingNotice } from '../sales/PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from '../sales/PendingCommandsContext';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, addError, decimalErrorKey, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { useActorReference } from '../supply/useActorReference';

type Kind = AddInvoiceRefRequest['kind'];
const KINDS: readonly Kind[] = ['Invoice', 'CreditNote'];

// "Factuur ontvangen" en "Creditnota ontvangen": nummer (verplicht), datum en bedrag excl. btw (optioneel) bij de order.
// Een opdracht met onbekende uitkomst staat in de opslag per tabblad en houdt haar formulier open (ook na weg- en
// terugnavigeren): alleen exact dezelfde herhaling. Een dubbel nummer is een definitieve weigering.
export default function InvoiceRefActions({ orderId, orderNumber, onDone }: { orderId: number; orderNumber: string; onDone: (order: PurchaseOrderDetail) => void }) {
    const { t } = useTranslation();
    const { commands } = usePendingCommands();
    const [opened, setOpened] = useState<Kind | null>(null);
    const open = KINDS.filter((kind) => kind === opened || invoiceRefKey(orderId, kind) in commands);
    const label = (kind: Kind) => (kind === 'Invoice' ? t('po_invoice_received_action') : t('po_credit_note_received_action'));

    return (
        <>
            {open.map((kind) => (
                <InvoiceRefForm key={kind} kind={kind} orderId={orderId} orderNumber={orderNumber} label={label(kind)}
                    onClose={() => setOpened(null)} onDone={(order) => { setOpened(null); onDone(order); }} />
            ))}
            {open.length === 0 && (
                <div className="mt-2 flex flex-wrap gap-2">
                    {KINDS.map((kind) => <button key={kind} type="button" data-tour="po-add-invoice" data-tour-key={kind} className={secondaryButtonClass} onClick={() => setOpened(kind)}>+ {label(kind)}</button>)}
                </div>
            )}
        </>
    );
}

function InvoiceRefForm({ kind, orderId, orderNumber, label, onClose, onDone }: {
    kind: Kind; orderId: number; orderNumber: string; label: string; onClose: () => void; onDone: (order: PurchaseOrderDetail) => void;
}) {
    const { t } = useTranslation();
    const [actor] = useActorReference();
    const [pending, setPending, clearPending] = usePendingCommand<InvoiceRefCommand>(invoiceRefKey(orderId, kind));
    const frozen = pending?.payload;
    const [number, setNumber] = useState(frozen?.invoiceNumber ?? '');
    const [date, setDate] = useState(frozen?.invoiceDate ?? '');
    const [amount, setAmount] = useState(frozen?.amountExclVat == null ? '' : formatDecimalInput(frozen.amountExclVat, 2));
    const [errors, setErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const inFlight = useRef(false);
    const locked = submitting || pending !== null;

    const edit = (apply: () => void) => { if (locked) return; apply(); setError(null); setErrors({}); };

    const submit = async () => {
        if (inFlight.current) return;
        let command = pending;
        if (command === null) {
            const found: FieldErrors = {};
            const invoiceNumber = normalizeInvoiceNumber(number);
            if (invoiceNumber === null) addError(found, 'invoiceNumber', t('supply_error_required'));
            const parsed = amount.trim() === '' ? null : parseLocaleDecimal(amount, undefined, { maxDecimals: 2, allowNegative: true });
            if (parsed !== null && !parsed.ok) addError(found, 'amountExclVat', t(decimalErrorKey(parsed.error)));
            setErrors(found);
            if (Object.keys(found).length > 0 || invoiceNumber === null || (parsed !== null && !parsed.ok)) return;
            const requestId = newRequestId();
            command = {
                kind: 'invoice-ref', key: invoiceRefKey(orderId, kind), orderId, requestId,
                payload: { requestId, kind, invoiceNumber, invoiceDate: date === '' ? null : date, amountExclVat: parsed === null ? null : parsed.value, actorReference: actor.trim() === '' ? null : actor.trim() },
                display: { orderNumber }, attempts: 0, lastError: null, frozenAt: new Date().toISOString(),
            };
            setPending(command);
        }
        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const result = await addInvoiceRef(orderId, command.payload);
            clearPending();
            onDone(result.value);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as InvoiceRefCommand);
            } else {
                clearPending();
                setError(apiError);
                setErrors(apiError.fieldErrors);
            }
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const duplicate = error?.status === 409 && typeof error.body === 'object' && error.body !== null && (error.body as Record<string, unknown>).code === 'INVOICE_REF_DUPLICATE';
    const e = (key: string) => errors[key];
    return (
        <form noValidate aria-busy={submitting} aria-label={label}
            onSubmit={(event) => { event.preventDefault(); void submit(); }} className="mt-2 space-y-2 rounded-md border border-gray-300 bg-gray-50 p-3">
            <p className="text-sm font-semibold text-gray-900">{label}</p>
            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {error && (duplicate
                ? <p role="alert" className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-900">{t('po_invoice_duplicate')}</p>
                : <ErrorNotice error={error} />)}
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                <div>
                    <label htmlFor={`invoiceRef-${kind}-number`} className="text-xs font-medium text-gray-700">{kind === 'Invoice' ? t('invoice_number') : t('po_credit_note_number')} *</label>
                    <input {...fieldProps('invoiceNumber', e('invoiceNumber'))} id={`invoiceRef-${kind}-number`} data-tour="po-invoice-number" value={number} maxLength={MAX_INVOICE_NUMBER_LENGTH} disabled={locked} autoFocus
                        onChange={(event) => edit(() => setNumber(event.target.value))} className={inputClass} />
                    <FieldError id="invoiceNumber" messages={e('invoiceNumber')} />
                </div>
                <div>
                    <label htmlFor={`invoiceRef-${kind}-date`} className="text-xs font-medium text-gray-700">{t('invoice_date')}</label>
                    <input id={`invoiceRef-${kind}-date`} type="date" value={date} disabled={locked} onChange={(event) => edit(() => setDate(event.target.value))} className={inputClass} />
                </div>
                <div>
                    <label htmlFor={`invoiceRef-${kind}-amount`} className="text-xs font-medium text-gray-700">{t('po_amount_excl_vat')}</label>
                    <input {...fieldProps('amountExclVat', e('amountExclVat'))} id={`invoiceRef-${kind}-amount`} inputMode="decimal" value={amount} disabled={locked}
                        onChange={(event) => edit(() => setAmount(event.target.value))} className={inputClass} />
                    <FieldError id="amountExclVat" messages={e('amountExclVat')} />
                </div>
            </div>
            {pending === null && (
                <div className="flex flex-wrap gap-2">
                    <button type="submit" data-tour="po-invoice-save" className={primaryButtonClass} disabled={locked}>{submitting ? t('supply_saving') : t('po_invoice_save')}</button>
                    <button type="button" className={secondaryButtonClass} disabled={locked} onClick={onClose}>{t('po_cancel')}</button>
                </div>
            )}
        </form>
    );
}

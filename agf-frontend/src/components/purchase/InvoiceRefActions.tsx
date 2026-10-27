'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { parseLocaleDecimal } from '../../lib/decimal';
import { MAX_INVOICE_NUMBER_LENGTH, normalizeInvoiceNumber } from '../../lib/purchaseOrderRules';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { addInvoiceRef } from '../../services/purchaseOrderService';
import type { AddInvoiceRefRequest, PurchaseOrderDetail } from '../../types/purchaseOrder';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, addError, decimalErrorKey, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { useActorReference } from '../supply/useActorReference';

type Kind = AddInvoiceRefRequest['kind'];

// "Factuur ontvangen" en "Creditnota ontvangen": nummer (verplicht), datum en bedrag excl. btw (optioneel) bij de order.
// Zelfde regels als bij afkeuren: een onbekende uitkomst bevriest het verzoek; een dubbel nummer is een definitieve weigering.
export default function InvoiceRefActions({ orderId, onDone }: { orderId: number; onDone: (order: PurchaseOrderDetail) => void }) {
    const { t } = useTranslation();
    const [actor] = useActorReference();
    const [kind, setKind] = useState<Kind | null>(null);
    const [number, setNumber] = useState('');
    const [date, setDate] = useState('');
    const [amount, setAmount] = useState('');
    const [errors, setErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [pending, setPending] = useState<AddInvoiceRefRequest | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const inFlight = useRef(false);
    const locked = submitting || (error !== null && outcomeUnknown(error));

    const openForm = (value: Kind) => { setKind(value); setNumber(''); setDate(''); setAmount(''); setErrors({}); setError(null); setPending(null); };
    const edit = (apply: () => void) => { if (locked) return; apply(); setPending(null); setError(null); setErrors({}); };

    const submit = async () => {
        if (inFlight.current || kind === null) return;
        let request = pending;
        if (request === null) {
            const found: FieldErrors = {};
            const invoiceNumber = normalizeInvoiceNumber(number);
            if (invoiceNumber === null) addError(found, 'invoiceNumber', t('supply_error_required'));
            const parsed = amount.trim() === '' ? null : parseLocaleDecimal(amount, undefined, { maxDecimals: 2, allowNegative: true });
            if (parsed !== null && !parsed.ok) addError(found, 'amountExclVat', t(decimalErrorKey(parsed.error)));
            setErrors(found);
            if (Object.keys(found).length > 0 || invoiceNumber === null || (parsed !== null && !parsed.ok)) return;
            request = {
                requestId: newRequestId(), kind, invoiceNumber, invoiceDate: date === '' ? null : date,
                amountExclVat: parsed === null ? null : parsed.value, actorReference: actor.trim() === '' ? null : actor.trim(),
            };
            setPending(request);
        }
        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const result = await addInvoiceRef(orderId, request);
            setKind(null);
            setPending(null);
            onDone(result.value);
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            setErrors(apiError.fieldErrors);
            if (!outcomeUnknown(apiError)) setPending(null);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    if (kind === null) {
        return (
            <div className="mt-2 flex flex-wrap gap-2">
                <button type="button" className={secondaryButtonClass} onClick={() => openForm('Invoice')}>+ {t('po_invoice_received_action')}</button>
                <button type="button" className={secondaryButtonClass} onClick={() => openForm('CreditNote')}>+ {t('po_credit_note_received_action')}</button>
            </div>
        );
    }

    const duplicate = error?.status === 409 && typeof error.body === 'object' && error.body !== null && (error.body as Record<string, unknown>).code === 'INVOICE_REF_DUPLICATE';
    const e = (key: string) => errors[key];
    return (
        <form noValidate aria-busy={submitting} aria-label={kind === 'Invoice' ? t('po_invoice_received_action') : t('po_credit_note_received_action')}
            onSubmit={(event) => { event.preventDefault(); void submit(); }} className="mt-2 space-y-2 rounded-md border border-gray-300 bg-gray-50 p-3">
            <p className="text-sm font-semibold text-gray-900">{kind === 'Invoice' ? t('po_invoice_received_action') : t('po_credit_note_received_action')}</p>
            {error && (duplicate
                ? <p role="alert" className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-900">{t('po_invoice_duplicate')}</p>
                : <ErrorNotice error={error} actions={outcomeUnknown(error) ? <button type="submit" className={primaryButtonClass} disabled={submitting}>{t('supply_retry_same')}</button> : undefined} />)}
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                <div>
                    <label htmlFor="invoiceRef-number" className="text-xs font-medium text-gray-700">{kind === 'Invoice' ? t('invoice_number') : t('po_credit_note_number')} *</label>
                    <input {...fieldProps('invoiceNumber', e('invoiceNumber'))} id="invoiceRef-number" value={number} maxLength={MAX_INVOICE_NUMBER_LENGTH} disabled={locked} autoFocus
                        onChange={(event) => edit(() => setNumber(event.target.value))} className={inputClass} />
                    <FieldError id="invoiceNumber" messages={e('invoiceNumber')} />
                </div>
                <div>
                    <label htmlFor="invoiceRef-date" className="text-xs font-medium text-gray-700">{t('invoice_date')}</label>
                    <input id="invoiceRef-date" type="date" value={date} disabled={locked} onChange={(event) => edit(() => setDate(event.target.value))} className={inputClass} />
                </div>
                <div>
                    <label htmlFor="invoiceRef-amount" className="text-xs font-medium text-gray-700">{t('po_amount_excl_vat')}</label>
                    <input {...fieldProps('amountExclVat', e('amountExclVat'))} id="invoiceRef-amount" inputMode="decimal" value={amount} disabled={locked}
                        onChange={(event) => edit(() => setAmount(event.target.value))} className={inputClass} />
                    <FieldError id="amountExclVat" messages={e('amountExclVat')} />
                </div>
            </div>
            <div className="flex flex-wrap gap-2">
                <button type="submit" className={primaryButtonClass} disabled={locked}>{submitting ? t('supply_saving') : t('po_invoice_save')}</button>
                <button type="button" className={secondaryButtonClass} disabled={locked} onClick={() => setKind(null)}>{t('po_cancel')}</button>
            </div>
        </form>
    );
}

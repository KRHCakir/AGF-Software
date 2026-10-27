'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatDecimalInput, parsePositiveDecimal } from '../../lib/decimal';
import { MAX_EXPLANATION_LENGTH, REJECTION_DECISIONS, REJECTION_REASONS, defaultRejectedKg } from '../../lib/purchaseOrderRejection';
import { DECISION_LABEL_KEYS, REASON_LABEL_KEYS, RejectionDecision } from '../../lib/purchaseOrderRules';
import { RejectReceiptCommand, rejectReceiptKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { rejectReceipt } from '../../services/purchaseOrderService';
import type { PurchaseOrderDetail, PurchaseOrderReceiptLine, RejectRequest } from '../../types/purchaseOrder';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, addError, decimalErrorKey, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { useActorReference } from '../supply/useActorReference';
import { PendingNotice } from '../sales/PendingCommandsBanner';
import { usePendingCommand } from '../sales/PendingCommandsContext';

// "Afkeuren" van één ontvangen regel: hoeveelheid en netto kg (standaard het hele afkeurbare deel), reden, besluit en
// medewerker. Een onbekende uitkomst bevriest het verzoek in de opslag per tabblad (usePendingCommand): ook na weg- en
// terugnavigeren alleen exact dezelfde herhaling, geen annuleren of weggooien. Een definitief antwoord ruimt het op.
export default function RejectReceiptForm({ orderId, orderNumber, receipt, onDone, onCancel, onFailed }: {
    orderId: number;
    orderNumber: string;
    receipt: PurchaseOrderReceiptLine;
    onDone: (order: PurchaseOrderDetail) => void;
    // Definitieve fout: de ouder houdt het formulier open zodat de foutmelding zichtbaar blijft.
    onFailed: () => void;
    onCancel: () => void;
}) {
    const { t, locale } = useTranslation();
    const [actor, setActor] = useActorReference();
    const calculated = receipt.weightMethod === 'Calculated' && receipt.netKgPerPackage !== null;
    const kgFor = (quantity: number) => defaultRejectedKg(quantity, receipt.onHandQuantity, receipt.onHandNetWeightKg, receipt.weightMethod, receipt.netKgPerPackage);
    const [pending, setPending, clearPending] = usePendingCommand<RejectReceiptCommand>(rejectReceiptKey(orderId, receipt.palletContentLineId));
    // Een bevroren opdracht toont exact haar eigen waarden; anders het hele afkeurbare deel als voorstel.
    const frozen = pending?.payload;
    const [quantity, setQuantity] = useState(formatDecimalInput(frozen?.lines[0].quantity ?? receipt.rejectableQuantity));
    const [kg, setKg] = useState(formatDecimalInput(frozen?.lines[0].netWeightKg ?? kgFor(receipt.rejectableQuantity)));
    const [kgTouched, setKgTouched] = useState(false);
    const [reason, setReason] = useState(frozen?.reason ?? '');
    const [explanation, setExplanation] = useState(frozen?.explanation ?? '');
    const [decision, setDecision] = useState<RejectionDecision>((frozen?.decision as RejectionDecision | undefined) ?? 'ReturnToSupplier');
    const [errors, setErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const inFlight = useRef(false);
    const locked = submitting || pending !== null;

    const edited = () => { setError(null); setErrors({}); };
    const changeQuantity = (value: string) => {
        if (locked) return;
        setQuantity(value);
        // Kg volgt het aantal zolang de gebruiker het niet zelf heeft aangepast; berekende regels volgen altijd.
        const parsed = parsePositiveDecimal(value);
        if (parsed.ok && (calculated || !kgTouched)) setKg(formatDecimalInput(kgFor(parsed.value)));
        edited();
    };

    const validate = (): Omit<RejectRequest, 'requestId'> | null => {
        const found: FieldErrors = {};
        const parsedQuantity = parsePositiveDecimal(quantity);
        if (!parsedQuantity.ok) addError(found, 'lines[0].quantity', t(decimalErrorKey(parsedQuantity.error)));
        else if (parsedQuantity.value > receipt.rejectableQuantity) addError(found, 'lines[0].quantity', t('po_reject_too_many', { max: formatQuantity(receipt.rejectableQuantity, locale) }));
        const parsedKg = parsePositiveDecimal(kg);
        if (!parsedKg.ok) addError(found, 'lines[0].netWeightKg', t(decimalErrorKey(parsedKg.error)));
        // Berekende regels hebben een vast, niet-bewerkbaar gewicht; de bovengrens geldt voor het zelf ingevoerde gewicht.
        else if (!calculated && parsedKg.value > receipt.rejectableNetWeightKg + 0.0005) addError(found, 'lines[0].netWeightKg', t('po_reject_too_many', { max: formatKg(receipt.rejectableNetWeightKg, locale) }));
        if (reason === '') addError(found, 'reason', t('supply_error_required'));
        const trimmed = explanation.trim();
        if (reason === 'Other' && trimmed === '') addError(found, 'explanation', t('po_reject_explanation_required'));
        if (actor.trim() === '') addError(found, 'actorReference', t('supply_error_required'));
        setErrors(found);
        if (Object.keys(found).length > 0 || !parsedQuantity.ok || !parsedKg.ok) return null;
        return {
            reason,
            explanation: trimmed === '' ? null : trimmed,
            decision,
            actorReference: actor.trim(),
            lines: [{ palletContentLineId: receipt.palletContentLineId, expectedContentLineVersion: receipt.version, quantity: parsedQuantity.value, netWeightKg: parsedKg.value }],
        };
    };

    const submit = async () => {
        if (inFlight.current) return;
        let command = pending;
        if (command === null) {
            const payload = validate();
            if (payload === null) return;
            const requestId = newRequestId();
            command = {
                kind: 'reject-receipt', key: rejectReceiptKey(orderId, receipt.palletContentLineId), orderId, palletContentLineId: receipt.palletContentLineId,
                requestId, payload: { requestId, ...payload }, display: { orderNumber, palletReference: receipt.palletReference, lotNumber: receipt.lotNumber },
                attempts: 0, lastError: null, frozenAt: new Date().toISOString(),
            };
            setPending(command);
        }
        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const result = await rejectReceipt(orderId, command.payload);
            clearPending();
            onDone(result.value);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as RejectReceiptCommand);
            } else {
                clearPending();
                onFailed();
                setError(apiError);
                setErrors(apiError.fieldErrors);
            }
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const e = (key: string) => errors[key];
    const id = `reject-${receipt.palletContentLineId}`;
    return (
        <form noValidate aria-busy={submitting} aria-label={t('po_reject_title')} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="mt-2 space-y-3 rounded-md border border-red-300 bg-red-50/40 p-3">
            <p className="text-sm font-semibold text-red-900">
                {t('po_reject_title')}: {receipt.palletReference} · {receipt.lotNumber} · {t('po_reject_max', { quantity: formatQuantity(receipt.rejectableQuantity, locale), kg: formatKg(receipt.rejectableNetWeightKg, locale) })}
            </p>
            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {error && <ErrorNotice error={error} />}
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <div>
                    <label htmlFor={`${id}-quantity`} className="text-xs font-medium text-gray-700">{t('po_reject_quantity')} *</label>
                    <input {...fieldProps('lines[0].quantity', e('lines[0].quantity'))} id={`${id}-quantity`} inputMode="decimal" value={quantity} disabled={locked}
                        onChange={(event) => changeQuantity(event.target.value)} className={inputClass} />
                    <FieldError id="lines[0].quantity" messages={e('lines[0].quantity')} />
                </div>
                <div>
                    <label htmlFor={`${id}-kg`} className="text-xs font-medium text-gray-700">{t('po_reject_kg')} *</label>
                    <input {...fieldProps('lines[0].netWeightKg', e('lines[0].netWeightKg'))} id={`${id}-kg`} inputMode="decimal" value={kg} disabled={locked} readOnly={calculated}
                        onChange={(event) => { if (locked) return; setKg(event.target.value); setKgTouched(true); edited(); }} className={inputClass} />
                    <FieldError id="lines[0].netWeightKg" messages={e('lines[0].netWeightKg')} />
                </div>
                <div>
                    <label htmlFor={`${id}-reason`} className="text-xs font-medium text-gray-700">{t('po_reject_reason')} *</label>
                    <select {...fieldProps('reason', e('reason'))} id={`${id}-reason`} value={reason} disabled={locked}
                        onChange={(event) => { if (locked) return; setReason(event.target.value); edited(); }} className={inputClass}>
                        <option value="">{t('supply_choose')}</option>
                        {REJECTION_REASONS.map((value) => <option key={value} value={value}>{t(REASON_LABEL_KEYS[value])}</option>)}
                    </select>
                    <FieldError id="reason" messages={e('reason')} />
                </div>
                <div>
                    <label htmlFor={`${id}-actor`} className="text-xs font-medium text-gray-700">{t('supply_actor')} *</label>
                    <input {...fieldProps('actorReference', e('actorReference'))} id={`${id}-actor`} value={actor} maxLength={100} disabled={locked}
                        onChange={(event) => { if (locked) return; setActor(event.target.value); edited(); }} className={inputClass} />
                    <FieldError id="actorReference" messages={e('actorReference')} />
                </div>
            </div>
            <div>
                <label htmlFor={`${id}-explanation`} className="text-xs font-medium text-gray-700">{t('po_reject_explanation')}{reason === 'Other' ? ' *' : ''}</label>
                <input {...fieldProps('explanation', e('explanation'))} id={`${id}-explanation`} value={explanation} maxLength={MAX_EXPLANATION_LENGTH} disabled={locked}
                    onChange={(event) => { if (locked) return; setExplanation(event.target.value); edited(); }} className={inputClass} />
                <FieldError id="explanation" messages={e('explanation')} />
            </div>
            <fieldset className="flex flex-wrap gap-2">
                <legend className="mb-1 text-xs font-medium text-gray-700">{t('po_reject_decision')}</legend>
                {REJECTION_DECISIONS.map((value) => (
                    <label key={value} className="flex cursor-pointer items-center gap-2 rounded border border-gray-300 bg-white px-2 py-1 text-sm has-[:checked]:border-red-500 has-[:checked]:bg-red-50">
                        <input type="radio" name={`${id}-decision`} value={value} checked={decision === value} disabled={locked}
                            onChange={() => { if (locked) return; setDecision(value); edited(); }} />
                        {t(DECISION_LABEL_KEYS[value])}
                    </label>
                ))}
            </fieldset>
            {pending === null && (
                <div className="flex flex-wrap gap-2">
                    <button type="submit" className={primaryButtonClass} disabled={locked}>{submitting ? t('supply_saving') : t('po_reject_save')}</button>
                    <button type="button" className={secondaryButtonClass} disabled={locked} onClick={onCancel}>{t('po_cancel')}</button>
                </div>
            )}
        </form>
    );
}

'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, normalizeActor, normalizeReason } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { releaseReservation } from '../../services/salesOrderService';
import { ReleaseStockReservationRequest, StockReservation } from '../../types/salesOrder';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { ruleErrorsToFieldErrors } from './ruleMessages';

// Vrijgeven met een verplichte reden en de reserveringsversie waarop de beslissing is gebaseerd (optimistische concurrency).
export default function ReleaseReservationForm({ reservation, actor, onSucceeded, onReload, onCancel }: {
    reservation: StockReservation;
    actor: string;
    onSucceeded: (released: StockReservation) => Promise<void>;
    onReload: () => void;
    onCancel: () => void;
}) {
    const { t } = useTranslation();
    const [reason, setReason] = useState('');
    const [pending, setPending] = useState<ReleaseStockReservationRequest | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const unknown = error !== null && outcomeUnknown(error);
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || unknown || conflict;
    const idPrefix = `release-${reservation.id}`;

    const editReason = (value: string) => {
        if (locked) return;
        setReason(value);
        setPending(null);
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current) return;
        let request = pending;
        if (request === null) {
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, true);
            if (!actorResult.ok || !reasonResult.ok || reasonResult.value === null) {
                setFieldErrors(ruleErrorsToFieldErrors([...(actorResult.ok ? [] : actorResult.errors), ...(reasonResult.ok ? [] : reasonResult.errors)], t));
                return;
            }
            setFieldErrors({});
            request = {
                requestId: newRequestId(),
                expectedReservationVersion: reservation.version,
                actorReference: actorResult.value,
                reason: reasonResult.value,
            };
            setPending(request);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const released = await releaseReservation(reservation.id, request);
            await onSucceeded(released);
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            setFieldErrors(apiError.fieldErrors);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    return (
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="mt-2 space-y-2 rounded border border-gray-300 bg-gray-50 p-3">
            <p className="text-xs text-gray-800">{t('sales_release_based_on_version', { version: String(reservation.version) })}</p>
            <div>
                <label htmlFor={`${idPrefix}-reason`} className="text-xs font-medium text-gray-700">{t('sales_release_reason')} *</label>
                <textarea id={`${idPrefix}-reason`} value={reason} rows={2} disabled={locked} maxLength={MAX_REASON_LENGTH}
                    onChange={(event) => editReason(event.target.value)} aria-invalid={fieldErrors.reason !== undefined}
                    aria-describedby={fieldErrors.reason ? `${idPrefix}-reason-error` : `${idPrefix}-reason-count`} className={inputClass} />
                <span id={`${idPrefix}-reason-count`} className="block text-right text-xs text-gray-500">{reason.trim().length}/{MAX_REASON_LENGTH}</span>
                <FieldError id={`${idPrefix}-reason-error`} messages={fieldErrors.reason} />
                <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
                <FieldError id={`${idPrefix}-version-error`} messages={fieldErrors.expectedReservationVersion} />
            </div>

            {error && (
                <div role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1.5 text-xs text-red-900">
                    <p className="font-medium">
                        {conflict ? t('sales_conflict') : unknown ? t('supply_error_outcome_unknown') : t('supply_error_http_400')}
                    </p>
                    {error.kind === 'http' && error.status !== 400 && <p className="mt-1">{error.message}</p>}
                    <div className="mt-2 flex flex-wrap gap-2">
                        {unknown && <button type="submit" className={primaryButtonClass} disabled={submitting}>{t('supply_retry_same')}</button>}
                        {(conflict || unknown) && <button type="button" className={secondaryButtonClass} onClick={onReload}>{t('sales_reload_and_decide')}</button>}
                    </div>
                </div>
            )}

            {!locked && (
                <div className="flex gap-2">
                    <button type="submit" className={primaryButtonClass}>{t('sales_confirm_release')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}

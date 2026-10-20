'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ReleaseCommand, releaseKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, normalizeActor, normalizeReason } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { releaseReservation } from '../../services/salesOrderService';
import { SalesOrderLine, StockReservation } from '../../types/salesOrder';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

// Vrijgeven met een verplichte reden en de reserveringsversie waarop de beslissing is gebaseerd (optimistische concurrency).
// De opdracht wordt bevroren en boven dit formulier bewaard tot de uitkomst bekend is.
export default function ReleaseReservationForm({ orderId, line, reservation, actor, onSucceeded, onReload, onCancel }: {
    orderId: number;
    line: SalesOrderLine;
    reservation: StockReservation;
    actor: string;
    onSucceeded: (released: StockReservation) => Promise<void>;
    onReload: () => void;
    onCancel: () => void;
}) {
    const { t } = useTranslation();
    const [pending, setPending, clearPending] = usePendingCommand<ReleaseCommand>(releaseKey(orderId, line.id, reservation.id));
    const [reason, setReason] = useState(() => pending?.payload.reason ?? '');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict;
    const idPrefix = `release-${reservation.id}`;
    const basedOnVersion = pending?.payload.expectedReservationVersion ?? reservation.version;

    const editReason = (value: string) => {
        if (locked) return;
        setReason(value);
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current) return;
        let command = pending;
        if (command === null) {
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, true);
            if (!actorResult.ok || !reasonResult.ok || reasonResult.value === null) {
                setFieldErrors(ruleErrorsToFieldErrors([...(actorResult.ok ? [] : actorResult.errors), ...(reasonResult.ok ? [] : reasonResult.errors)], t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'release',
                key: releaseKey(orderId, line.id, reservation.id),
                orderId,
                lineId: line.id,
                reservationId: reservation.id,
                requestId,
                payload: { requestId, expectedReservationVersion: reservation.version, actorReference: actorResult.value, reason: reasonResult.value },
                // Een toewijzing heeft geen pallet: dan de leverancier als herkenbare bron in de melding.
                display: { palletReference: reservation.palletReference ?? reservation.vendorName ?? '', lotNumber: reservation.lotNumber ?? '', productLabel: `${line.productName ?? ''} · ${line.productVariantName ?? ''}` },
                attempts: 0,
                lastError: null,
                frozenAt: new Date().toISOString(),
            };
            setPending(command);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const released = await releaseReservation(reservation.id, command.payload);
            clearPending();
            await onSucceeded(released);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as ReleaseCommand);
            } else {
                clearPending();
            }
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
            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            <p className="text-xs text-gray-800">{t('sales_release_based_on_version', { version: String(basedOnVersion) })}</p>
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

            {error && pending === null && (
                <div role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1.5 text-xs text-red-900">
                    <p className="font-medium">{conflict ? t('sales_conflict') : t('supply_error_http_400')}</p>
                    {error.status !== 400 && <TechnicalDetails error={error} />}
                    {conflict && (
                        <div className="mt-2 flex flex-wrap gap-2">
                            <button type="button" className={secondaryButtonClass} onClick={onReload}>{t('sales_reload_and_decide')}</button>
                        </div>
                    )}
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

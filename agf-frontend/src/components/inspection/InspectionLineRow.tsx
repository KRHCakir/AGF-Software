'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import {
    MAX_REASON_LENGTH,
    actionLabelKey,
    allowedQualityTransitions,
    countsAsAvailableNow,
    normalizeReason,
    qualityReasonRequired,
} from '../../lib/qualityTransitions';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { lastInspection } from '../../lib/inspectionList';
import { InspectionPallet, changeQualityStatus, historyFor } from '../../services/inspectionService';
import { QualityStatus } from '../../types/availability';
import { ChangeQualityStatusRequest, PalletDetailLine } from '../../types/inspection';
import QualityBadge from '../availability/QualityBadge';
import GgnBadge from '../shared/GgnBadge';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { WeightBadge } from '../shared/WeightBadge';
import LineHistory from './LineHistory';
import OrderNumberText from '../purchase/OrderNumberText';
import TechnicalDetails from '../supply/TechnicalDetails';
import { KNOWN_INSPECTORS, onRememberedInspectorChange, readRememberedInspector, rememberInspector } from './inspectorMemory';

// Eén inhoudsregel met zijn eigen, losse kwaliteitsacties. Nooit voor de hele pallet en nooit in bulk.
export default function InspectionLineRow({ context, line, readOnly, onSucceeded, onReload }: {
    context: InspectionPallet;
    line: PalletDetailLine;
    readOnly: boolean;
    onSucceeded: (line: PalletDetailLine, toStatus: QualityStatus) => Promise<void>;
    onReload: () => void;
}) {
    const { t, locale } = useTranslation();
    const [action, setAction] = useState<QualityStatus | null>(null);
    const [reason, setReason] = useState('');
    // Wie beoordeelt hoort bij deze keuring: voorgevuld met de laatste keuze op dit apparaat, altijd zichtbaar en aanpasbaar.
    const [inspector, setInspector] = useState('');
    // Bevroren actie: alleen bij een onbekende uitkomst wordt exact deze (zelfde requestId) bewust opnieuw verstuurd.
    const [pending, setPending] = useState<ChangeQualityStatusRequest | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    const [showHistory, setShowHistory] = useState(false);
    const inFlight = useRef(false);

    const commitment = context.commitments[line.supplyCommitmentId];
    // De laatste keuringsbeslissing uit de audit: wie, wanneer. Zichtbaar zonder de historie open te klappen.
    const inspected = lastInspection(historyFor(context, line));
    const actions = readOnly ? [] : allowedQualityTransitions(line.qualityStatus);
    const unknown = error !== null && outcomeUnknown(error);
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || unknown || conflict;
    const reasonRequired = action !== null && qualityReasonRequired(line.qualityStatus, action);
    const idPrefix = `line-${line.id}`;

    // De rondleiding (of een ander tabblad) zet een nieuwe keuze: een open paneel zonder eigen invoer volgt die.
    useEffect(() => onRememberedInspectorChange(() => { if (pending === null) setInspector(readRememberedInspector() ?? ''); }), [pending]);

    const open = (to: QualityStatus) => {
        if (locked) return;
        setAction(to);
        setReason('');
        setInspector(readRememberedInspector() ?? '');
        setPending(null);
        setError(null);
        setFieldErrors({});
    };

    const close = () => {
        if (submitting) return;
        setAction(null);
        setPending(null);
        setError(null);
        setFieldErrors({});
    };

    const editReason = (value: string) => {
        if (locked) return;
        setReason(value);
        // Na een definitieve weigering (bijvoorbeeld 400) hoort een aangepaste reden bij een nieuwe actie.
        setPending(null);
        setError(null);
        setFieldErrors({});
    };

    const editInspector = (value: string) => {
        if (locked) return;
        setInspector(value);
        setPending(null);
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current || action === null) return;
        let request = pending;
        if (request === null) {
            const found: Record<string, string[]> = {};
            const inspectedBy = inspector.trim();
            if (inspectedBy === '') found.inspectedBy = [t('inspection_error_inspector_required')];
            else if (inspectedBy.length > 100) found.inspectedBy = [t('supply_error_too_long', { max: '100' })];
            const normalized = normalizeReason(reason, reasonRequired);
            if (!normalized.ok) {
                found.reason = [normalized.error === 'required' ? t('inspection_error_reason_required') : t('supply_error_too_long', { max: String(MAX_REASON_LENGTH) })];
            }
            setFieldErrors(found);
            if (Object.keys(found).length > 0 || !normalized.ok) {
                // Naar het eerste veld met een fout, nooit naar iets buiten beeld.
                document.getElementById(found.reason ? `${idPrefix}-reason` : `${idPrefix}-inspector`)?.focus();
                return;
            }
            request = {
                requestId: newRequestId(),
                expectedContentLineVersion: line.version,
                toStatus: action,
                inspectedBy,
                reason: normalized.value,
            };
            setPending(request);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            await changeQualityStatus(line.palletId, line.id, request);
            rememberInspector(request.inspectedBy);
            // Eerst echte data herladen; daarna toont de groep de beschikbaarheidsmelding. Deze rij wordt dan opnieuw opgebouwd.
            await onSucceeded(line, request.toStatus);
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
        <tr className="align-top">
            <td className="px-3 py-2">
                <span className="font-medium text-gray-900">{line.productName} · {line.productVariantName}</span>
                <span className="block text-xs text-gray-600">{t('supply_lot')} {line.lotNumber}</span>
                {line.ggn && <span className="mt-0.5 block"><GgnBadge ggn={line.ggn} /></span>}
                {line.origin === 'DispatchCorrection' && (
                    <span className="mt-1 inline-flex rounded border border-amber-300 bg-amber-100 px-1.5 text-xs font-medium text-amber-900">{t('sales_origin_correction')}</span>
                )}
            </td>
            <td className="px-3 py-2 text-right">
                {formatQuantity(line.onHandQuantity, locale)}
                {line.dispatchedQuantity > 0 && <span className="block text-xs text-gray-500">{t('inspection_received', { quantity: formatQuantity(line.quantity, locale) })}</span>}
                {line.depleted && <span className="mt-1 inline-flex rounded border border-gray-400 bg-gray-200 px-1.5 text-xs font-medium text-gray-800">{t('sales_depleted')}</span>}
            </td>
            <td className="px-3 py-2"><UnitBadge unit={line.unit} /></td>
            <td className="px-3 py-2 text-right">
                {formatKg(line.onHandNetWeightKg, locale)}
                {line.dispatchedNetWeightKg > 0 && <span className="block text-xs text-gray-500">{t('inspection_received', { quantity: formatKg(line.netWeightKg, locale) })}</span>}
                <span className="mt-1 block"><WeightBadge method={line.weightMethod} kgPerPackage={line.netKgPerPackage} unit={line.unit} /></span>
            </td>
            <td className="px-3 py-2 text-sm text-gray-800">
                {commitment?.vendorName ?? t('availability_unknown')}
                <span className="block text-xs text-gray-600">{commitment?.origin ?? t('availability_unknown')} · <OrderNumberText id={line.supplyCommitmentId} orderNumber={commitment?.orderNumber} supplierReference={commitment?.supplierReference} /></span>
            </td>
            <td className="px-3 py-2">
                <span data-tour="inspection-status" data-tour-key={`${line.lotNumber}-${line.qualityStatus}`}><QualityBadge status={line.qualityStatus} /></span>
                {line.qualityReason && <span className="mt-1 block text-xs italic text-gray-700">“{line.qualityReason}”</span>}
                {inspected && (
                    <span className="mt-1 block text-xs text-gray-800" data-testid="inspected-by">
                        {inspected.signedInAs !== null
                            ? t('inspection_inspected_by_signed_in', { actor: inspected.actor ?? t('inspection_actor_not_given'), user: inspected.signedInAs, time: formatDateTime(inspected.occurredAt, locale) })
                            : t('inspection_inspected_by', { actor: inspected.actor ?? t('inspection_actor_not_given'), time: formatDateTime(inspected.occurredAt, locale) })}
                    </span>
                )}
                <span className="mt-1 block text-xs text-gray-600">
                    {countsAsAvailableNow(line.qualityStatus) ? t('inspection_counts_now') : t('inspection_not_counts_now')}
                </span>
            </td>
            <td className="w-[28rem] px-3 py-2">
                {actions.length > 0 && action === null && (
                    <div className="flex flex-wrap gap-2">
                        {actions.map((to) => (
                            <button key={to} type="button" data-tour="inspection-action" data-tour-key={`${to}-${line.lotNumber}`} className={secondaryButtonClass} onClick={() => open(to)}
                                aria-label={`${t(actionLabelKey(line.qualityStatus, to))}: ${line.productName ?? ''} · ${line.productVariantName ?? ''} · ${line.lotNumber}`}>
                                {t(actionLabelKey(line.qualityStatus, to))}
                            </button>
                        ))}
                    </div>
                )}

                {action !== null && (
                    <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }}
                        className="space-y-2 rounded border border-gray-300 bg-gray-50 p-3">
                        <p className="flex flex-wrap items-center gap-2 text-sm">
                            <QualityBadge status={line.qualityStatus} /><span aria-hidden="true">→</span><QualityBadge status={action} />
                        </p>
                        <p className="text-xs text-gray-800">
                            {countsAsAvailableNow(action) ? t('inspection_effect_counts') : t('inspection_effect_not_counts')}
                        </p>
                        <div>
                            <label htmlFor={`${idPrefix}-reason`} className="text-xs font-medium text-gray-700">
                                {t('inspection_reason')}{reasonRequired ? ' *' : ` (${t('inspection_optional')})`}
                            </label>
                            <textarea id={`${idPrefix}-reason`} value={reason} rows={2} disabled={locked}
                                onChange={(event) => editReason(event.target.value)}
                                aria-invalid={fieldErrors.reason !== undefined}
                                aria-describedby={fieldErrors.reason ? `${idPrefix}-reason-error` : `${idPrefix}-reason-count`}
                                className={inputClass} />
                            <span id={`${idPrefix}-reason-count`} className="block text-right text-xs text-gray-500">{reason.trim().length}/{MAX_REASON_LENGTH}</span>
                            <FieldError id={`${idPrefix}-reason-error`} messages={fieldErrors.reason} />
                        </div>
                        <div>
                            <label htmlFor={`${idPrefix}-inspector`} className="text-xs font-medium text-gray-700">{t('inspection_actor_label')} *</label>
                            <input id={`${idPrefix}-inspector`} list={`${idPrefix}-inspectors`} value={inspector} maxLength={100} disabled={locked} autoComplete="off"
                                data-tour="inspection-inspector" data-tour-key={line.lotNumber} onChange={(event) => editInspector(event.target.value)}
                                aria-invalid={fieldErrors.inspectedBy !== undefined}
                                aria-describedby={fieldErrors.inspectedBy ? `${idPrefix}-inspector-error` : `${idPrefix}-inspector-hint`}
                                className={inputClass} />
                            <datalist id={`${idPrefix}-inspectors`}>
                                {KNOWN_INSPECTORS.map((name) => <option key={name} value={name} />)}
                            </datalist>
                            <span id={`${idPrefix}-inspector-hint`} className="block text-xs text-gray-500">{t('inspection_inspector_hint')}</span>
                            <FieldError id={`${idPrefix}-inspector-error`} messages={fieldErrors.inspectedBy} />
                        </div>

                        {error && (
                            <div role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1.5 text-xs text-red-900">
                                <p className="font-medium">
                                    {conflict ? t('inspection_conflict') : unknown ? t('supply_error_outcome_unknown') : t('supply_error_http_400')}
                                </p>
                                {error.status !== 400 && <TechnicalDetails error={error} />}
                                <div className="mt-2 flex flex-wrap gap-2">
                                    {unknown && <button type="submit" className={primaryButtonClass} disabled={submitting}>{t('inspection_retry_same')}</button>}
                                    {(conflict || unknown) && <button type="button" className={secondaryButtonClass} onClick={onReload}>{t('inspection_reload')}</button>}
                                </div>
                            </div>
                        )}

                        {!locked && (
                            <div className="flex gap-2">
                                <button type="submit" data-tour="inspection-confirm" data-tour-key={line.lotNumber} className={primaryButtonClass}>{t('inspection_confirm', { action: t(actionLabelKey(line.qualityStatus, action)) })}</button>
                                <button type="button" className={secondaryButtonClass} onClick={close}>{t('inspection_cancel')}</button>
                            </div>
                        )}
                        {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
                    </form>
                )}

                <button type="button" onClick={() => setShowHistory((value) => !value)} aria-expanded={showHistory}
                    className="mt-2 text-xs font-medium text-blue-800 underline underline-offset-2">
                    {showHistory ? t('inspection_hide_history') : t('inspection_show_history')}
                </button>
                {showHistory && <div className="mt-1"><LineHistory events={historyFor(context, line)} /></div>}
            </td>
        </tr>
    );
}

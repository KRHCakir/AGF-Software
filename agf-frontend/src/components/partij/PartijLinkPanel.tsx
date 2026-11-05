'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_PARTIJ_REASON_LENGTH, partijNumberKey } from '../../lib/partijRules';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { createPartij, getNextPartijNumber, linkPartijToOrder } from '../../services/partijService';
import { type PartijSummary, isPartijExistsConflict } from '../../types/partij';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import PartijField, { NO_PARTIJ, type PartijChoice } from './PartijField';

// Een inkooporder zonder partij alsnog aan een partij koppelen (bij ontvangst of op de inkooporder): een bestaande kiezen of een nieuwe
// aanmaken (voorstel of eigen nummer, met dezelfde dubbelcontrole), daarna de order en al haar regels koppelen. Is er al iets ontvangen,
// dan vraagt de server een reden (de ontvangen palletregels krijgen de partij met een auditregel; hun lotnummer blijft staan).
export default function PartijLinkPanel({ orderId, orderNumber, onLinked, dataTourKey }: {
    orderId: number;
    orderNumber: string;
    onLinked: (partij: PartijSummary) => void;
    dataTourKey?: string | number;
}) {
    const { t } = useTranslation();
    const [open, setOpen] = useState(false);
    const [suggestion, setSuggestion] = useState<string | null>(null);
    const [choice, setChoice] = useState<PartijChoice>(NO_PARTIJ);
    const [exists, setExists] = useState<PartijSummary | null>(null);
    const [reason, setReason] = useState('');
    const [needsReason, setNeedsReason] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    // Eén nieuwe partij per keer openen: een herhaling na een mislukte koppeling maakt geen tweede aan.
    // `wanted` is de keuze waarmee ze is aangemaakt (null = voorstel), zodat een andere keuze haar niet hergebruikt.
    const created = useRef<{ partij: PartijSummary; wanted: string | null } | null>(null);

    useEffect(() => {
        if (!open) return;
        const controller = new AbortController();
        getNextPartijNumber(undefined, controller.signal)
            .then((next) => { setSuggestion(next.suggestion); if (next.suggestion !== null) setChoice((current) => (current.kind === 'none' ? { kind: 'suggested', number: next.suggestion! } : current)); })
            .catch(() => setSuggestion(null));
        return () => controller.abort();
    }, [open]);

    // Bestaande partij: haar id. Nieuw: één keer aanmaken (een herhaling na een mislukte koppeling hergebruikt die partij), bij een
    // nummer dat al bestaat dezelfde vraag als op de inkooporder.
    const partijId = async (): Promise<number | null> => {
        if (choice.kind === 'existing') return choice.partij.id;
        const wanted = choice.kind === 'suggested' ? null : choice.number.trim();
        const previous = created.current;
        if (previous !== null && (previous.wanted === null ? wanted === null : wanted !== null && partijNumberKey(previous.wanted) === partijNumberKey(wanted))) return previous.partij.id;
        try {
            const partij = (await createPartij({ requestId: newRequestId(), number: wanted })).value;
            created.current = { partij, wanted };
            return partij.id;
        } catch (caught) {
            const apiError = toApiError(caught);
            if (!isPartijExistsConflict(apiError.body)) throw caught;
            setExists(apiError.body.partij);
            return null;
        }
    };

    const link = async () => {
        if (busy || choice.kind === 'none') return;
        setBusy(true);
        setError(null);
        try {
            const id = await partijId();
            if (id === null) return;
            onLinked(await linkPartijToOrder(id, { supplyCommitmentId: orderId, reason: reason.trim() === '' ? null : reason.trim() }));
            setOpen(false);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (apiError.fieldErrors.reason) setNeedsReason(true);
            else setError(apiError);
        } finally {
            setBusy(false);
        }
    };

    return (
        <div className="rounded border border-amber-300 bg-amber-50 p-2 text-sm text-amber-950" data-tour="partij-link" data-tour-key={dataTourKey}>
            <p>{t('partij_link_none', { order: orderNumber })}</p>
            {!open ? (
                <button type="button" className={`${secondaryButtonClass} mt-1`} data-tour="partij-link-open" data-tour-key={dataTourKey} onClick={() => setOpen(true)}>{t('partij_link_open')}</button>
            ) : (
                <div className="mt-2 space-y-2 rounded border border-gray-300 bg-white p-2 text-gray-900">
                    {error && <ErrorNotice error={error} />}
                    <PartijField value={choice} suggestion={suggestion} exists={exists} disabled={busy} onChange={(next) => { setChoice(next); setError(null); }} onExists={setExists} />
                    {needsReason && (
                        <div>
                            <label htmlFor="partijLinkReason-input" className="text-xs font-medium text-gray-700">{t('partij_link_reason')} *</label>
                            <input {...fieldProps('partijLinkReason', undefined)} value={reason} maxLength={MAX_PARTIJ_REASON_LENGTH} onChange={(event) => setReason(event.target.value)} className={inputClass} />
                            <span className="block text-xs text-gray-600">{t('partij_link_reason_hint')}</span>
                            <FieldError id="partijLinkReason" messages={reason.trim() === '' ? [t('partij_link_reason_required')] : undefined} />
                        </div>
                    )}
                    <div className="flex flex-wrap gap-2">
                        <button type="button" className={primaryButtonClass} disabled={busy || choice.kind === 'none' || (needsReason && reason.trim() === '')} data-tour="partij-link-save" onClick={() => void link()}>
                            {busy ? t('supply_saving') : t('partij_link_save')}
                        </button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setOpen(false)}>{t('partij_link_cancel')}</button>
                    </div>
                </div>
            )}
        </div>
    );
}

'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { availabilityHref, singleVariantId } from '../../lib/availabilityLinks';
import { parsePositiveDecimal } from '../../lib/decimal';
import { newRequestId } from '../../lib/requestId';
import { isValidSscc } from '../../lib/sscc';
import { ApiError, toApiError } from '../../services/apiClient';
import { getAvailabilityVariant } from '../../services/availabilityService';
import { addPalletContentLine, createReceivedWarehousePallet } from '../../services/palletService';
import { listOpenWarehouseCommitments } from '../../services/supplyService';
import { PackageUnit } from '../../types/availability';
import {
    AddPalletContentLineRequest,
    CreateReceivedWarehousePalletRequest,
    PalletContentLineSummary,
    PalletSummary,
} from '../../types/pallet';
import { SupplyCommitment, SupplyCommitmentLine } from '../../types/supply';
import QualityBadge from '../availability/QualityBadge';
import { UnitBadge } from '../availability/QuantityCell';
import { LoadState, LoadingRows } from '../availability/StateViews';
import ErrorNotice from './ErrorNotice';
import FieldError, { fieldProps, inputClass } from './FieldError';
import { FieldErrors, addError, decimalErrorKey, outcomeUnknown } from './messages';
import SubmitProgress, { ProgressStep, StepState } from './SubmitProgress';
import SuccessPanel, { primaryButtonClass, secondaryButtonClass } from './SuccessPanel';

interface ContentDraft {
    lot: string;
    quantity: string;
    netKg: string;
}

interface SourceEntry {
    commitment: SupplyCommitment;
    line: SupplyCommitmentLine;
}

// Bevroren verzending. De pallet is idempotent via zijn referentie, elke inhoudsregel via zijn eigen requestId.
// Opnieuw proberen verstuurt exact deze payloads, maar alleen binnen deze geopende flow: na een refresh is deze
// toestand weg en is er (nog) geen herstel- of zoekflow.
interface FrozenLine {
    requestId: string;
    payload: AddPalletContentLineRequest;
    done: boolean;
    result: PalletContentLineSummary | null;
    replayed: boolean;
}

interface FrozenSubmission {
    pallet: { payload: CreateReceivedWarehousePalletRequest; done: boolean; result: PalletSummary | null; replayed: boolean };
    lines: Record<number, FrozenLine>;
}

interface Failure {
    step: 'pallet' | number;
    error: ApiError;
}

const PALLET_FIELD_KEYS: Record<string, string> = {
    reference: 'pallet.reference',
    requestId: 'general',
    sscc: 'pallet.sscc',
    warehouseLocation: 'pallet.warehouseLocation',
    actorReference: 'actorReference',
};

const LINE_FIELD_KEYS = ['lotNumber', 'quantity', 'netWeightKg', 'unit', 'supplyCommitmentLineId'];

// Pallet ontvangen in het eigen magazijn: altijd Received + Warehouse, inhoud start altijd in Quarantine.
// Bronnen zijn uitsluitend geplande Warehouse-aanvoerregels met een bekende eenheid.
export default function ReceivePalletForm({ actor, preselected, onPreselectedConsumed }: {
    actor: string;
    preselected: SupplyCommitment | null;
    onPreselectedConsumed: () => void;
}) {
    const { t, locale } = useTranslation();
    const [sources, setSources] = useState<LoadState<SupplyCommitment[]>>({ status: 'loading' });
    const [sourcesToken, setSourcesToken] = useState(0);
    const [search, setSearch] = useState('');
    const [selected, setSelected] = useState<number[]>([]);
    const [drafts, setDrafts] = useState<Record<number, ContentDraft>>({});
    const [pallet, setPallet] = useState({ reference: '', sscc: '', bin: '' });
    const [unregistered, setUnregistered] = useState<Record<number, number | 'loading' | 'unknown'>>({});
    const [frozen, setFrozen] = useState<FrozenSubmission | null>(null);
    const [running, setRunning] = useState(false);
    const [failure, setFailure] = useState<Failure | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    const [abandonedNotice, setAbandonedNotice] = useState<string | null>(null);
    const inFlight = useRef(false);
    const requestedUnregistered = useRef(new Set<number>());
    const mounted = useRef(true);

    useEffect(() => {
        mounted.current = true;
        return () => { mounted.current = false; };
    }, []);

    useEffect(() => {
        const controller = new AbortController();
        setSources({ status: 'loading' });
        listOpenWarehouseCommitments(controller.signal)
            .then((data) => setSources({ status: 'ready', data }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') {
                    setSources({ status: 'error', error: apiError });
                }
            });
        return () => controller.abort();
    }, [sourcesToken]);

    // Een zojuist geplande aanvoer staat altijd in de lijst, ook als de server meer dan 200 open toezeggingen heeft.
    const commitments = useMemo(() => {
        if (sources.status !== 'ready') return [];
        const list = sources.data;
        return preselected && preselected.route === 'Warehouse' && !list.some((c) => c.id === preselected.id) ? [preselected, ...list] : list;
    }, [sources, preselected]);

    const index = useMemo(() => {
        const map = new Map<number, SourceEntry>();
        for (const commitment of commitments) {
            for (const line of commitment.lines) {
                map.set(line.id, { commitment, line });
            }
        }
        return map;
    }, [commitments]);

    // Voorselectie vanuit "Pallet ontvangen voor deze aanvoer": geen dubbele invoer van leverancier/product/eenheid.
    useEffect(() => {
        if (sources.status !== 'ready' || preselected === null) return;
        const receivable = preselected.lines.filter((line) => line.expectedUnit !== null);
        setSelected(receivable.map((line) => line.id));
        setDrafts(Object.fromEntries(receivable.map((line) => [line.id, { lot: line.lotNumber ?? '', quantity: '', netKg: '' }])));
        onPreselectedConsumed();
    }, [sources.status, preselected, onPreselectedConsumed]);

    // B5: per gekozen bronregel tonen wat nog niet op een pallet staat (bestaande, alleen-lezen drill-down).
    useEffect(() => {
        for (const lineId of selected) {
            const entry = index.get(lineId);
            if (!entry || entry.line.expectedUnit === null || requestedUnregistered.current.has(lineId)) continue;
            requestedUnregistered.current.add(lineId);
            setUnregistered((current) => ({ ...current, [lineId]: 'loading' }));
            getAvailabilityVariant(entry.line.productVariantId, { route: 'Warehouse', unit: entry.line.expectedUnit, customerId: null, includeClosed: false })
                .then((drillDown) => {
                    const source = drillDown.sourceLines.find((s) => s.supplyCommitmentLineId === lineId);
                    if (mounted.current) setUnregistered((current) => ({ ...current, [lineId]: source ? source.unregisteredQuantity : 'unknown' }));
                })
                .catch(() => {
                    if (mounted.current) setUnregistered((current) => ({ ...current, [lineId]: 'unknown' }));
                });
        }
    }, [selected, index]);

    const succeeded = frozen !== null && frozen.pallet.done && selected.length > 0 && selected.every((id) => frozen.lines[id]?.done);
    const unknownFailure = failure !== null && outcomeUnknown(failure.error);
    const blocked = running || succeeded || unknownFailure;
    const palletEditable = !blocked && !(frozen?.pallet.done ?? false);
    const lineEditable = (id: number) => !blocked && !(frozen?.lines[id]?.done ?? false);

    const clearFailure = () => {
        setFailure(null);
        setErrors({});
    };

    // Er is nog niets aangemaakt: een wijziging betekent een nieuw verzoek met nieuwe sleutels.
    const editPallet = (field: 'reference' | 'sscc' | 'bin', value: string) => {
        if (!palletEditable) return;
        setPallet((current) => ({ ...current, [field]: value }));
        setFrozen(null);
        clearFailure();
    };

    const toggleSource = (entry: SourceEntry) => {
        if (!palletEditable || entry.line.expectedUnit === null) return;
        const id = entry.line.id;
        setSelected((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));
        setDrafts((current) => (current[id] ? current : { ...current, [id]: { lot: entry.line.lotNumber ?? '', quantity: '', netKg: '' } }));
        setFrozen(null);
        clearFailure();
    };

    // Alleen een regel die nog niet is opgeslagen mag wijzigen; die krijgt bij versturen een nieuwe requestId.
    const editLine = (id: number, field: keyof ContentDraft, value: string) => {
        if (!lineEditable(id)) return;
        setDrafts((current) => ({ ...current, [id]: { ...current[id], [field]: value } }));
        setFrozen((current) => {
            if (current === null || !current.lines[id]) return current;
            const lines = { ...current.lines };
            delete lines[id];
            return { ...current, lines };
        });
        clearFailure();
    };

    const validate = (): FieldErrors => {
        const found: FieldErrors = {};
        // Medewerker en eigen palletnummer zijn optioneel bij ontvangst; leeg blokkeert niets.
        if (actor.trim().length > 100) addError(found, 'actorReference', t('supply_error_too_long', { max: '100' }));
        if (!(frozen?.pallet.done ?? false)) {
            const reference = pallet.reference.trim();
            if (reference.length > 64) addError(found, 'pallet.reference', t('supply_error_too_long', { max: '64' }));
            if (pallet.sscc !== '' && !isValidSscc(pallet.sscc)) addError(found, 'pallet.sscc', t('supply_error_sscc'));
            if (pallet.bin.trim().length > 64) addError(found, 'pallet.warehouseLocation', t('supply_error_too_long', { max: '64' }));
        }
        if (selected.length === 0) addError(found, 'lines', t('supply_error_select_source'));
        for (const id of selected) {
            if (frozen?.lines[id]?.done) continue;
            const draft = drafts[id];
            const lot = draft.lot.trim();
            if (lot === '') addError(found, `line.${id}.lotNumber`, t('supply_error_required'));
            else if (lot.length > 100) addError(found, `line.${id}.lotNumber`, t('supply_error_too_long', { max: '100' }));
            const quantity = parsePositiveDecimal(draft.quantity);
            if (!quantity.ok) addError(found, `line.${id}.quantity`, t(decimalErrorKey(quantity.error)));
            const netKg = parsePositiveDecimal(draft.netKg);
            if (!netKg.ok) addError(found, `line.${id}.netWeightKg`, t(decimalErrorKey(netKg.error)));
        }
        return found;
    };

    const buildLine = (id: number, actorReference: string | null): FrozenLine => {
        const entry = index.get(id);
        const unit = entry?.line.expectedUnit;
        if (!entry || unit === null || unit === undefined) {
            throw new Error('Bronregel zonder eenheid kan niet worden ontvangen.');
        }
        const draft = drafts[id];
        const quantity = parsePositiveDecimal(draft.quantity);
        const netKg = parsePositiveDecimal(draft.netKg);
        const requestId = newRequestId();
        return {
            requestId,
            payload: {
                requestId,
                supplyCommitmentLineId: id,
                lotNumber: draft.lot.trim(),
                quantity: quantity.ok ? quantity.value : 0,
                unit: unit as PackageUnit,
                netWeightKg: netKg.ok ? netKg.value : 0,
                actorReference,
                reason: null,
            },
            done: false,
            result: null,
            replayed: false,
        };
    };

    const mapServerErrors = (step: 'pallet' | number, error: ApiError): FieldErrors => {
        const mapped: FieldErrors = {};
        for (const [key, messages] of Object.entries(error.fieldErrors)) {
            const target = step === 'pallet'
                ? PALLET_FIELD_KEYS[key]
                : LINE_FIELD_KEYS.includes(key) ? `line.${step}.${key}` : key === 'actorReference' ? 'actorReference' : undefined;
            for (const message of messages) addError(mapped, target ?? 'general', message);
        }
        return mapped;
    };

    const run = async (submission: FrozenSubmission, order: number[]) => {
        inFlight.current = true;
        setRunning(true);
        setFailure(null);
        let working = submission;
        let step: 'pallet' | number = 'pallet';
        try {
            if (!working.pallet.done) {
                const saved = await createReceivedWarehousePallet(working.pallet.payload);
                working = { ...working, pallet: { ...working.pallet, done: true, result: saved.value, replayed: saved.replayed } };
                setFrozen(working);
            }
            const palletId = working.pallet.result?.id;
            if (palletId === undefined) {
                throw new ApiError('shape', 'Pallet zonder id.');
            }
            for (const id of order) {
                const line = working.lines[id];
                if (line.done) continue;
                step = id;
                const saved = await addPalletContentLine(palletId, line.payload);
                working = { ...working, lines: { ...working.lines, [id]: { ...line, done: true, result: saved.value, replayed: saved.replayed } } };
                setFrozen(working);
            }
        } catch (caught) {
            const apiError = toApiError(caught);
            if (mounted.current) {
                setFailure({ step, error: apiError });
                setErrors(mapServerErrors(step, apiError));
            }
        } finally {
            inFlight.current = false;
            if (mounted.current) setRunning(false);
        }
    };

    const submit = () => {
        if (inFlight.current || succeeded) return;
        const found = validate();
        setErrors(found);
        if (Object.keys(found).length > 0) return;

        const actorReference = actor.trim() === '' ? null : actor.trim();
        const next: FrozenSubmission = {
            pallet: frozen?.pallet ?? {
                payload: {
                    requestId: newRequestId(),
                    reference: pallet.reference.trim() === '' ? null : pallet.reference.trim(),
                    sscc: pallet.sscc === '' ? null : pallet.sscc,
                    logisticsStatus: 'Received',
                    locationContext: 'Warehouse',
                    warehouseLocation: pallet.bin.trim() === '' ? null : pallet.bin.trim(),
                    actorReference,
                    reason: null,
                },
                done: false,
                result: null,
                replayed: false,
            },
            lines: Object.fromEntries(selected.map((id) => [id, frozen?.lines[id] ?? buildLine(id, actorReference)])),
        };
        setFrozen(next);
        void run(next, selected);
    };

    const reset = useCallback((notice: string | null) => {
        setSelected([]);
        setDrafts({});
        setPallet({ reference: '', sscc: '', bin: '' });
        setFrozen(null);
        setFailure(null);
        setErrors({});
        setAbandonedNotice(notice);
        requestedUnregistered.current = new Set();
        setUnregistered({});
        setSourcesToken((n) => n + 1);
    }, []);

    const abandon = () => {
        if (frozen?.pallet.done) {
            const doneCount = selected.filter((id) => frozen.lines[id]?.done).length;
            reset(t('supply_abandoned_notice', {
                reference: palletLabel(frozen),
                done: String(doneCount),
                total: String(selected.length),
            }));
        } else {
            reset(null);
        }
    };

    const e = (key: string) => errors[key];

    // Het servergegenereerde nummer zodra het bekend is; anders het eigen nummer; anders "nummer volgt".
    const palletLabel = (submission: FrozenSubmission) =>
        submission.pallet.result?.reference ?? submission.pallet.payload.reference ?? t('supply_pallet_number_pending');

    const stepState = (step: 'pallet' | number, done: boolean): StepState => {
        if (done) return 'done';
        if (failure?.step === step) return 'failed';
        if (running && !done) {
            const firstOpen = !frozen?.pallet.done ? 'pallet' : selected.find((id) => !frozen?.lines[id]?.done);
            return firstOpen === step ? 'running' : 'waiting';
        }
        return 'waiting';
    };

    const progress: ProgressStep[] = frozen === null ? [] : [
        { key: 'pallet', label: t('supply_step_pallet', { reference: palletLabel(frozen) }), state: stepState('pallet', frozen.pallet.done) },
        ...selected.map((id) => {
            const entry = index.get(id);
            return {
                key: String(id),
                label: t('supply_step_line', { name: entry ? `${entry.line.productName ?? ''} · ${entry.line.productVariantName ?? ''}` : String(id), lot: drafts[id]?.lot ?? '' }),
                state: stepState(id, frozen.lines[id]?.done ?? false),
            };
        }),
    ];

    if (succeeded && frozen !== null) {
        const savedLines = selected.map((id) => frozen.lines[id]);
        const replayed = frozen.pallet.replayed || savedLines.some((line) => line.replayed);
        const href = availabilityHref({
            route: 'Warehouse',
            focus: 'quarantine',
            productVariantId: singleVariantId(savedLines.map((line) => line.result?.productVariantId ?? 0).filter((id) => id > 0)),
        });
        return (
            <SuccessPanel
                title={t('supply_receive_success', { reference: palletLabel(frozen) })}
                note={replayed ? t('supply_saved_before') : undefined}
                actions={(
                    <>
                        <Link href={href} className={primaryButtonClass}>{t('supply_view_availability')}</Link>
                        {frozen.pallet.result && (
                            <Link href={`/inspection?palletId=${frozen.pallet.result.id}`} className={secondaryButtonClass}>{t('supply_to_inspection')}</Link>
                        )}
                        <button type="button" className={secondaryButtonClass} onClick={() => reset(null)}>{t('supply_another_pallet')}</button>
                    </>
                )}
            >
                <p>{t('supply_receive_success_where', { bin: frozen.pallet.result?.warehouseLocation ?? t('supply_no_bin') })}</p>
                <ul className="mt-2 space-y-1">
                    {savedLines.map((line) => {
                        const entry = index.get(line.payload.supplyCommitmentLineId);
                        return (
                            <li key={line.requestId} className="flex flex-wrap items-center gap-2">
                                <span>{entry?.line.productName} · {entry?.line.productVariantName}</span>
                                <span>· {t('supply_lot')} {line.payload.lotNumber}</span>
                                <span>· {formatQuantity(line.payload.quantity, locale)}</span>
                                <UnitBadge unit={line.payload.unit} />
                                <span>· {formatKg(line.payload.netWeightKg, locale)}</span>
                                <QualityBadge status={line.result?.qualityStatus ?? 'Quarantine'} />
                            </li>
                        );
                    })}
                </ul>
            </SuccessPanel>
        );
    }

    const term = search.trim().toLowerCase();
    const visibleCommitments = commitments
        .map((commitment) => ({
            commitment,
            lines: commitment.lines.filter((line) => term === ''
                || `${commitment.id} ${commitment.vendorName ?? ''} ${commitment.origin} ${line.productName ?? ''} ${line.productVariantName ?? ''} ${line.lotNumber ?? ''}`
                    .toLowerCase().includes(term)),
        }))
        .filter((group) => group.lines.length > 0);

    return (
        <form noValidate aria-busy={running} onSubmit={(event) => { event.preventDefault(); submit(); }}
            className="space-y-5 rounded-md border border-gray-300 bg-white p-4">
            {abandonedNotice && (
                <p role="status" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">{abandonedNotice}</p>
            )}

            {failure && (
                <ErrorNotice error={failure.error} actions={(
                    <>
                        {unknownFailure && <button type="submit" className={primaryButtonClass} disabled={running}>{t('supply_retry_same')}</button>}
                        <button type="button" className={secondaryButtonClass} onClick={abandon}>{t('supply_abandon')}</button>
                    </>
                )} />
            )}
            {e('general') && <FieldError id="general" messages={e('general')} />}
            <FieldError id="actorReference" messages={e('actorReference')} />

            <section aria-labelledby="receive-sources" className="space-y-2">
                <div className="flex items-end justify-between gap-3">
                    <div>
                        <h3 id="receive-sources" className="font-semibold text-gray-900">{t('supply_receive_sources_title')}</h3>
                        <p className="text-sm text-gray-600">{t('supply_receive_sources_hint')}</p>
                    </div>
                    <label className="text-xs font-medium text-gray-700">
                        {t('supply_receive_search')}
                        <input type="search" value={search} onChange={(event) => setSearch(event.target.value)}
                            placeholder={t('supply_receive_search_placeholder')} className={`${inputClass} w-72`} />
                    </label>
                </div>
                <FieldError id="lines" messages={e('lines')} />
                {sources.status === 'loading' && <LoadingRows rows={4} label={t('supply_loading_sources')} />}
                {sources.status === 'error' && (
                    <ErrorNotice error={sources.error} actions={
                        <button type="button" className={secondaryButtonClass} onClick={() => setSourcesToken((n) => n + 1)}>{t('availability_retry')}</button>
                    } />
                )}
                {sources.status === 'ready' && visibleCommitments.length === 0 && (
                    <p className="rounded border border-dashed border-gray-300 px-3 py-4 text-center text-sm text-gray-700">
                        {commitments.length === 0 ? t('supply_receive_no_sources') : t('supply_receive_no_matches')}
                    </p>
                )}
                {sources.status === 'ready' && visibleCommitments.length > 0 && (
                    <div className="max-h-80 overflow-y-auto rounded border border-gray-200">
                        <table className="min-w-full text-sm">
                            <thead className="sticky top-0 bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                                <tr>
                                    <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('supply_select')}</span></th>
                                    <th scope="col" className="px-2 py-1.5">{t('supply_variant')}</th>
                                    <th scope="col" className="px-2 py-1.5">{t('supply_lot_provisional')}</th>
                                    <th scope="col" className="px-2 py-1.5 text-right">{t('supply_expected')}</th>
                                    <th scope="col" className="px-2 py-1.5">{t('supply_unit')}</th>
                                    <th scope="col" className="px-2 py-1.5 text-right">{t('supply_declared_kg')}</th>
                                </tr>
                            </thead>
                            {visibleCommitments.map(({ commitment, lines }) => (
                                <tbody key={commitment.id} className="divide-y divide-gray-100 border-t border-gray-200">
                                    <tr className="bg-gray-50">
                                        <th scope="rowgroup" colSpan={6} className="px-2 py-1 text-left text-xs font-semibold text-gray-700">
                                            #{commitment.id} · {commitment.vendorName ?? '–'} · {commitment.origin}
                                            {commitment.expectedAt && <> · {t('supply_expected_on', { date: formatDateTime(commitment.expectedAt, locale) })}</>}
                                        </th>
                                    </tr>
                                    {lines.map((line) => {
                                        const checked = selected.includes(line.id);
                                        const missingUnit = line.expectedUnit === null;
                                        const inputId = `source-${line.id}`;
                                        return (
                                            <tr key={line.id} className={missingUnit ? 'bg-red-50/40' : checked ? 'bg-blue-50' : undefined}>
                                                <td className="px-2 py-1.5 align-top">
                                                    <input id={inputId} type="checkbox" checked={checked} disabled={missingUnit || !palletEditable}
                                                        aria-describedby={missingUnit ? `${inputId}-missing-unit` : undefined}
                                                        onChange={() => toggleSource({ commitment, line })} />
                                                </td>
                                                <td className="px-2 py-1.5 align-top">
                                                    <label htmlFor={inputId} className={missingUnit ? 'text-gray-600' : 'cursor-pointer'}>
                                                        {line.productName} · {line.productVariantName}
                                                    </label>
                                                    {missingUnit && (
                                                        <p id={`${inputId}-missing-unit`} className="mt-1 text-xs font-medium text-red-800">
                                                            <span aria-hidden="true">⚠ </span>{t('supply_error_source_unit_missing')}
                                                        </p>
                                                    )}
                                                </td>
                                                <td className="px-2 py-1.5 align-top">{line.lotNumber ?? '–'}</td>
                                                <td className="px-2 py-1.5 text-right align-top">{formatQuantity(line.expectedQuantity, locale)}</td>
                                                <td className="px-2 py-1.5 align-top"><UnitBadge unit={line.expectedUnit} /></td>
                                                <td className="px-2 py-1.5 text-right align-top">
                                                    {line.expectedNetWeightKg === null ? '–' : formatKg(line.expectedNetWeightKg, locale)}
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            ))}
                        </table>
                    </div>
                )}
            </section>

            <fieldset className="space-y-2">
                <legend className="font-semibold text-gray-900">{t('supply_pallet_title')}</legend>
                <p className="text-sm text-gray-700">
                    {t('supply_pallet_fixed_state')}{' '}
                    <span className="inline-flex rounded border border-gray-300 bg-gray-50 px-2 py-0.5 text-xs text-gray-800">{t('availability_logistics_received')} · {t('availability_location_warehouse')}</span>
                </p>
                <div className="grid grid-cols-4 gap-3">
                    <div>
                        <label htmlFor="pallet.reference-input" className="text-xs font-medium text-gray-700">{t('supply_pallet_reference')}</label>
                        <input {...fieldProps('pallet.reference', e('pallet.reference'))} value={pallet.reference} maxLength={64} disabled={!palletEditable}
                            onChange={(event) => editPallet('reference', event.target.value)} className={inputClass} aria-describedby="pallet.reference-hint" />
                        <span id="pallet.reference-hint" className="block text-xs text-gray-500">{t('supply_pallet_reference_hint')}</span>
                        <FieldError id="pallet.reference" messages={e('pallet.reference')} />
                    </div>
                    <div>
                        <label htmlFor="pallet.sscc-input" className="text-xs font-medium text-gray-700">{t('supply_pallet_sscc')}</label>
                        <input {...fieldProps('pallet.sscc', e('pallet.sscc'))} value={pallet.sscc} inputMode="numeric" maxLength={18} disabled={!palletEditable}
                            onChange={(event) => editPallet('sscc', event.target.value)} className={`${inputClass} font-mono`} />
                        <span className="block text-xs text-gray-500">{t('supply_pallet_sscc_hint')}</span>
                        <FieldError id="pallet.sscc" messages={e('pallet.sscc')} />
                    </div>
                    <div>
                        <label htmlFor="pallet.warehouseLocation-input" className="text-xs font-medium text-gray-700">{t('supply_pallet_bin')}</label>
                        <input {...fieldProps('pallet.warehouseLocation', e('pallet.warehouseLocation'))} value={pallet.bin} maxLength={64} disabled={!palletEditable}
                            onChange={(event) => editPallet('bin', event.target.value)} className={inputClass} />
                        <FieldError id="pallet.warehouseLocation" messages={e('pallet.warehouseLocation')} />
                    </div>
                </div>
            </fieldset>

            <section aria-labelledby="receive-content" className="space-y-3">
                <h3 id="receive-content" className="font-semibold text-gray-900">{t('supply_content_title')}</h3>
                {selected.length === 0 && <p className="text-sm text-gray-600">{t('supply_content_empty')}</p>}
                {selected.map((id) => {
                    const entry = index.get(id);
                    const draft = drafts[id];
                    if (!entry || !draft) return null;
                    const open = unregistered[id];
                    const editable = lineEditable(id);
                    const key = (field: string) => `line.${id}.${field}`;
                    return (
                        <fieldset key={id} className="rounded border border-gray-200 p-3">
                            <legend className="px-1 text-sm font-semibold text-gray-900">
                                {entry.line.productName} · {entry.line.productVariantName}
                            </legend>
                            <p className="mb-2 text-xs text-gray-700">
                                #{entry.commitment.id} · {entry.commitment.vendorName ?? '–'} · {t('supply_expected')} {formatQuantity(entry.line.expectedQuantity, locale)} {entry.line.expectedUnit}
                                {' · '}
                                {open === 'loading' || open === undefined
                                    ? t('supply_open_loading')
                                    : open === 'unknown'
                                        ? t('supply_open_unknown')
                                        : t('supply_open_quantity', { quantity: formatQuantity(open, locale) })}
                            </p>
                            <div className="grid grid-cols-6 gap-3">
                                <div className="col-span-2">
                                    <label htmlFor={`${key('lotNumber')}-input`} className="text-xs font-medium text-gray-700">{t('supply_lot')} *</label>
                                    <input {...fieldProps(key('lotNumber'), e(key('lotNumber')))} value={draft.lot} maxLength={100} disabled={!editable}
                                        onChange={(event) => editLine(id, 'lot', event.target.value)} className={inputClass} />
                                    <FieldError id={key('lotNumber')} messages={e(key('lotNumber'))} />
                                </div>
                                <div>
                                    <label htmlFor={`${key('quantity')}-input`} className="text-xs font-medium text-gray-700">{t('supply_quantity')} *</label>
                                    <input {...fieldProps(key('quantity'), e(key('quantity')))} value={draft.quantity} inputMode="decimal" disabled={!editable}
                                        onChange={(event) => editLine(id, 'quantity', event.target.value)} className={inputClass} />
                                    <FieldError id={key('quantity')} messages={e(key('quantity'))} />
                                </div>
                                <div>
                                    <span className="text-xs font-medium text-gray-700">{t('supply_unit')}</span>
                                    <div className="mt-1.5"><UnitBadge unit={entry.line.expectedUnit} /></div>
                                    <span className="block text-xs text-gray-500">{t('supply_unit_from_source')}</span>
                                    <FieldError id={key('unit')} messages={e(key('unit'))} />
                                </div>
                                <div>
                                    <label htmlFor={`${key('netWeightKg')}-input`} className="text-xs font-medium text-gray-700">{t('supply_actual_net_kg')} *</label>
                                    <input {...fieldProps(key('netWeightKg'), e(key('netWeightKg')))} value={draft.netKg} inputMode="decimal" disabled={!editable}
                                        onChange={(event) => editLine(id, 'netKg', event.target.value)} className={inputClass} />
                                    <span className="block text-xs text-gray-500">{t('supply_actual_net_kg_hint')}</span>
                                    <FieldError id={key('netWeightKg')} messages={e(key('netWeightKg'))} />
                                </div>
                                <div>
                                    <span className="text-xs font-medium text-gray-700">{t('supply_quality')}</span>
                                    <div className="mt-1.5"><QualityBadge status="Quarantine" /></div>
                                    <span className="block text-xs text-gray-500">{t('supply_quality_start_hint')}</span>
                                </div>
                            </div>
                            <FieldError id={key('supplyCommitmentLineId')} messages={e(key('supplyCommitmentLineId'))} />
                        </fieldset>
                    );
                })}
            </section>

            {progress.length > 0 && <SubmitProgress steps={progress} />}

            <div className="flex items-center gap-3 border-t border-gray-200 pt-3">
                <button type="submit" className={primaryButtonClass} disabled={blocked || sources.status !== 'ready'}>
                    {running ? t('supply_saving') : frozen?.pallet.done ? t('supply_continue_registration') : t('supply_register_pallet')}
                </button>
                <span className="text-xs text-gray-600">{t('supply_receive_footer')}</span>
            </div>
        </form>
    );
}

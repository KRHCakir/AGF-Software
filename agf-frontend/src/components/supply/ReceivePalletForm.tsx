'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { formatDateOnly, todayInAmsterdam } from '../../lib/calendarDate';
import { unitLabelKey } from '../../lib/units';
import { availabilityHref, singleVariantId } from '../../lib/availabilityLinks';
import { parsePositiveDecimal } from '../../lib/decimal';
import { normalizeGgn, resolveGgn } from '../../lib/ggn';
import { DEVIATION_CODES, deviationConflict } from '../../lib/receiptDeviation';
import { enteredFrom, similarVariants, withArticle } from '../../lib/articleFromEntry';
import { EnteredAttributes, articleVariants, emptyEntered, matchVariant } from '../../lib/receiptBlindEntry';
import { newRequestId } from '../../lib/requestId';
import { WEIGHT_METHODS, WeightMethod, calculatedTotalKg } from '../../lib/weight';
import { isValidSscc } from '../../lib/sscc';
import { ApiError, toApiError } from '../../services/apiClient';
import { createArticleFromEntry, existingArticle } from '../../services/articleService';
import { getAvailabilityVariant } from '../../services/availabilityService';
import { addPalletContentLine, createReceivedWarehousePallet } from '../../services/palletService';
import { getCountries } from '../../services/countryService';
import { getProductsWithVariants } from '../../services/referenceDataService';
import { getDeviationPreview } from '../../services/receiptDeviationService';
import { listOpenWarehouseCommitments } from '../../services/supplyService';
import { PackageUnit } from '../../types/availability';
import {
    AddPalletContentLineRequest,
    CreateReceivedWarehousePalletRequest,
    PalletContentLineSummary,
    PalletSummary,
} from '../../types/pallet';
import type { CountryReference } from '../../types/country';
import { DeviationPreview, isDeviationPreview } from '../../types/receiptDeviation';
import type { ProductOption } from '../../types/referenceData';
import { SupplyCommitment, SupplyCommitmentLine, VendorGgnOption } from '../../types/supply';
import QualityBadge from '../availability/QualityBadge';
import { UnitBadge } from '../availability/QuantityCell';
import { LoadState, LoadingRows } from '../availability/StateViews';
import GgnBadge from '../shared/GgnBadge';
import NewArticlePrompt from '../shared/NewArticlePrompt';
import NumberEcho from '../shared/NumberEcho';
import { palletBreakdownLabel } from '../shared/palletBreakdownLabel';
import DeviationDialog, { DeviationChoice } from './DeviationDialog';
import ErrorNotice from './ErrorNotice';
import FieldError, { fieldProps, inputClass } from './FieldError';
import { FieldErrors, addError, decimalErrorKey, outcomeUnknown } from './messages';
import ReceivedAttributes from './ReceivedAttributes';
import SubmitProgress, { ProgressStep, StepState } from './SubmitProgress';
import SuccessPanel, { primaryButtonClass, secondaryButtonClass } from './SuccessPanel';

interface ContentDraft {
    lot: string;
    quantity: string;
    // Gewogen: netKg is het gewogen totaal. Berekend: kgPerPackage (etiket) × aantal; netKg blijft dan leeg.
    method: WeightMethod;
    kgPerPackage: string;
    netKg: string;
    // GGN van deze partij: alleen bij precies één actieve leveranciers-GGN vooraf ingevuld; bij meer kiest de gebruiker; altijd aanpasbaar of leeg.
    ggn: string;
    // Blinde invoer: wat er werkelijk is aangekomen (leeg tot de gebruiker het invult), de variant die daar bij registreren bij
    // bleek te horen, of de invoer al gecontroleerd is (exact, zonder venster) en de bevestiging uit het venster.
    entered: EnteredAttributes;
    matchedVariantId: number | null;
    verified: boolean;
    deviation: DeviationChoice | null;
}

// Startwaarden uit de variant (methode, standaard kg per verpakking) en de leverancier (GGN), per regel aanpasbaar.
const draftFor = (line: SupplyCommitmentLine, vendorGgns: readonly VendorGgnOption[]): ContentDraft => ({
    lot: line.lotNumber ?? '',
    quantity: '',
    method: line.weightMethod,
    // Blind: ook het etiketgewicht vult de gebruiker zelf in (de bestelling wordt niet vooraf getoond).
    kgPerPackage: '',
    netKg: '',
    ggn: proposedGgn(vendorGgns) ?? '',
    entered: emptyEntered(),
    matchedVariantId: null,
    verified: false,
    deviation: null,
});

// Voorstel alleen als er precies één toepasselijk nummer is; bij meerdere nummers kiest de gebruiker zelf.
export function proposedGgn(vendorGgns: readonly VendorGgnOption[]): string | null {
    return vendorGgns.length === 1 ? vendorGgns[0].ggn : null;
}

// Herkenbare toelichting bij de GGN-invoer ten opzichte van de leveranciers-GGN's (voorstel, gekozen, aangepast, leeggemaakt, kiezen).
export type GgnStateKey = 'supply_ggn_from_vendor' | 'supply_ggn_chosen' | 'supply_ggn_adjusted' | 'supply_ggn_cleared' | 'supply_ggn_choose' | 'supply_ggn_none_in_vendor';
export function ggnStateKey(draftGgn: string, vendorGgns: readonly VendorGgnOption[]): GgnStateKey | null {
    const normalized = normalizeGgn(draftGgn);
    if (vendorGgns.length === 0) return normalized === '' ? 'supply_ggn_none_in_vendor' : null;
    if (normalized === '') return vendorGgns.length === 1 ? 'supply_ggn_cleared' : 'supply_ggn_choose';
    if (vendorGgns.length === 1) return normalized === vendorGgns[0].ggn ? 'supply_ggn_from_vendor' : 'supply_ggn_adjusted';
    return vendorGgns.some((option) => option.ggn === normalized) ? 'supply_ggn_chosen' : 'supply_ggn_adjusted';
}

// Certificaatwaarschuwing voor de gekozen leveranciers-GGN (nooit blokkerend): certificaat verlopen (geldig tot vóór vandaag) of
// nooit gecontroleerd. Een zelf ingetypte GGN die niet bij de leverancier staat, krijgt geen waarschuwing (daar is niets over bekend).
export type GgnCertificateWarning = 'supply_ggn_certificate_expired' | 'supply_ggn_never_checked';
export function ggnCertificateWarning(draftGgn: string, vendorGgns: readonly VendorGgnOption[], today: string): GgnCertificateWarning | null {
    const option = vendorGgns.find((candidate) => candidate.ggn === normalizeGgn(draftGgn));
    if (option === undefined) return null;
    if (option.certificateValidUntil !== null && option.certificateValidUntil < today) return 'supply_ggn_certificate_expired';
    if (option.checkedOn === null) return 'supply_ggn_never_checked';
    return null;
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

const LINE_FIELD_KEYS = ['lotNumber', 'quantity', 'netWeightKg', 'netKgPerPackage', 'weightMethod', 'unit', 'supplyCommitmentLineId', 'ggn'];

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
    // Venster "Afwijking van de bestelling"; afterRun = de pallet bestaat al (409 als vangnet), Ja stuurt dan dezelfde aanvraag opnieuw.
    const [dialog, setDialog] = useState<{ lineId: number; preview: DeviationPreview; afterRun: boolean } | null>(null);
    const [checking, setChecking] = useState(false);
    // Varianten per product en de landenlijst: suggesties en het zoeken van de ontvangen variant bij registreren.
    const [products, setProducts] = useState<ProductOption[] | null>(null);
    const [countries, setCountries] = useState<CountryReference | null>(null);
    const [combinations, setCombinations] = useState<Record<number, 'missing' | 'ambiguous'>>({});
    // "Artikel aanmaken en doorgaan" per regel: bezig/foutmelding, en per invoer één vaste requestId (een herhaalde klik of een
    // verloren antwoord maakt nooit een tweede artikel; een gewijzigde invoer is een nieuw verzoek).
    const [creation, setCreation] = useState<Record<number, { busy: boolean; error: string | null }>>({});
    const creationRequests = useRef(new Map<number, { key: string; requestId: string }>());
    const resumeAfterCreate = useRef(false);
    const inFlight = useRef(false);
    const requestedUnregistered = useRef(new Set<number>());
    const mounted = useRef(true);

    useEffect(() => {
        mounted.current = true;
        return () => { mounted.current = false; };
    }, []);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([getProductsWithVariants(controller.signal), getCountries(controller.signal)])
            .then(([productList, countryList]) => { setProducts(productList); setCountries(countryList); })
            .catch(() => { /* zonder lijsten blijft registreren geblokkeerd met een melding */ });
        return () => controller.abort();
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
        setDrafts(Object.fromEntries(receivable.map((line) => [line.id, draftFor(line, preselected.vendorGgns)])));
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
    // Tijdens de controle van een afwijking ligt de invoer ook vast: Ja en een exacte match boeken precies wat gecontroleerd is.
    const blocked = running || checking || succeeded || unknownFailure;
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
        setDrafts((current) => (current[id] ? current : { ...current, [id]: draftFor(entry.line, entry.commitment.vendorGgns) }));
        setFrozen(null);
        clearFailure();
    };

    // Alleen een regel die nog niet is opgeslagen mag wijzigen; die krijgt bij versturen een nieuwe requestId. Een bevestigde
    // afwijking geldt voor precies deze invoer: elke wijziging vraagt opnieuw "Klopt dit?".
    const editLine = <K extends keyof ContentDraft>(id: number, field: K, value: ContentDraft[K]) => {
        if (!lineEditable(id)) return;
        setDrafts((current) => ({ ...current, [id]: { ...current[id], [field]: value, matchedVariantId: null, verified: false, deviation: null } }));
        setCombinations((current) => { const next = { ...current }; delete next[id]; return next; });
        setFrozen((current) => {
            if (current === null || !current.lines[id]) return current;
            const lines = { ...current.lines };
            delete lines[id];
            return { ...current, lines };
        });
        clearFailure();
    };

    const validate = (source: Record<number, ContentDraft>): FieldErrors => {
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
            const draft = source[id];
            const lot = draft.lot.trim();
            if (lot === '') addError(found, `line.${id}.lotNumber`, t('supply_error_required'));
            else if (lot.length > 100) addError(found, `line.${id}.lotNumber`, t('supply_error_too_long', { max: '100' }));
            const quantity = parsePositiveDecimal(draft.quantity);
            if (!quantity.ok) addError(found, `line.${id}.quantity`, t(decimalErrorKey(quantity.error)));
            if (draft.method === 'Calculated') {
                const perPackage = parsePositiveDecimal(draft.kgPerPackage);
                if (!perPackage.ok) addError(found, `line.${id}.netKgPerPackage`, t(decimalErrorKey(perPackage.error)));
            } else {
                const netKg = parsePositiveDecimal(draft.netKg);
                if (!netKg.ok) addError(found, `line.${id}.netWeightKg`, t(decimalErrorKey(netKg.error)));
            }
            if (!resolveGgn(draft.ggn).ok) addError(found, `line.${id}.ggn`, t('supply_error_ggn'));
            const article = articleOf(id);
            if (article === null) {
                addError(found, 'general', t('receipt_lists_unavailable'));
                continue;
            }
            // Alleen herkomst is verplicht; maat, verpakking, klasse en merk mogen leeg (= bewust "geen").
            if (draft.entered.origin === null) addError(found, `line.${id}.origin`, t('supply_error_required'));
        }
        return found;
    };

    // Varianten van hetzelfde artikel als de bronregel; null zolang de lijsten niet geladen zijn.
    const articleOf = (id: number): { variants: ReturnType<typeof articleVariants> } | null => {
        const entry = index.get(id);
        if (!entry || products === null || countries === null) return null;
        return { variants: articleVariants(products, entry.line.productVariantId) };
    };
    const countryNames = useMemo(() => new Map((countries?.countries ?? []).map((country) => [country.code, country.nameNl])), [countries]);

    // Nieuw artikel aanmaken met wat er is ingevuld, onder het bestelde artikel (zelfde productsoort en ras), en dan gewoon verder
    // registreren: de lijst kent het artikel nu, dus de controle vindt het en vraagt zo nodig "Klopt dit?". Bestaat het al (409), dan
    // dat artikel; is dat gearchiveerd, dan de melding.
    const createArticle = async (id: number) => {
        const draft = drafts[id];
        const entry = index.get(id);
        if (!draft || !entry || draft.entered.origin === null || products === null) return;
        const snapshot = JSON.stringify(draft.entered);
        let request = creationRequests.current.get(id);
        if (!request || request.key !== snapshot) {
            request = { key: snapshot, requestId: newRequestId() };
            creationRequests.current.set(id, request);
        }
        const perPackage = draft.method === 'Calculated' ? parsePositiveDecimal(draft.kgPerPackage) : null;
        const text = (value: string) => (value.trim() === '' ? null : value.trim());
        setCreation((current) => ({ ...current, [id]: { busy: true, error: null } }));
        let article = null;
        try {
            article = await createArticleFromEntry({
                requestId: request.requestId, source: 'Receipt', templateVariantId: entry.line.productVariantId, originCountryCode: draft.entered.origin,
                sizeCategory: text(draft.entered.size), packaging: text(draft.entered.packaging), tradeClass: text(draft.entered.tradeClass), brand: text(draft.entered.brand),
                netKgPerPackage: perPackage?.ok ? perPackage.value : null, actorReference: actor.trim() === '' ? null : actor.trim(),
            });
        } catch (caught) {
            const existing = existingArticle(caught);
            if (existing === null || existing.isArchived) {
                if (mounted.current) setCreation((current) => ({ ...current, [id]: { busy: false, error: toApiError(caught).message } }));
                return;
            }
            article = existing;
        }
        if (!mounted.current) return;
        setCreation((current) => ({ ...current, [id]: { busy: false, error: null } }));
        setCombinations((current) => { const next = { ...current }; delete next[id]; return next; });
        resumeAfterCreate.current = true;
        setProducts((current) => withArticle(current ?? [], article));
    };

    // Tijdens "Artikel aanmaken en doorgaan" liggen de kenmerken en het kg per colli vast: het artikel komt overeen met wat er staat.
    const creating = (id: number) => creation[id]?.busy ?? false;

    // Kies een bestaande variant uit "Bedoel je dit?": de invoer wordt die variant, daarna gewoon registreren.
    const chooseSimilar = (id: number, entered: EnteredAttributes) => {
        editLine(id, 'entered', entered);
        resumeAfterCreate.current = true;
    };

    // Netto kg zoals het verstuurd wordt: berekend = aantal × kg per verpakking (zelfde afronding als de server), anders gewogen.
    const netKgOf = (draft: ContentDraft): number | null => {
        const quantity = parsePositiveDecimal(draft.quantity);
        if (draft.method === 'Calculated') {
            const perPackage = parsePositiveDecimal(draft.kgPerPackage);
            return quantity.ok && perPackage.ok ? calculatedTotalKg(quantity.value, perPackage.value) : null;
        }
        const netKg = parsePositiveDecimal(draft.netKg);
        return netKg.ok ? netKg.value : null;
    };

    const buildLine = (id: number, actorReference: string | null, source: Record<number, ContentDraft>): FrozenLine => {
        const entry = index.get(id);
        const unit = entry?.line.expectedUnit;
        if (!entry || unit === null || unit === undefined) {
            throw new Error('Bronregel zonder eenheid kan niet worden ontvangen.');
        }
        const draft = source[id];
        const quantity = parsePositiveDecimal(draft.quantity);
        const perPackage = parsePositiveDecimal(draft.kgPerPackage);
        // Berekend: het totaal volgt uit aantal × kg per verpakking (zelfde afronding als de server); nooit als gewogen gestuurd.
        const netKg = draft.method === 'Calculated'
            ? (quantity.ok && perPackage.ok ? { ok: true as const, value: calculatedTotalKg(quantity.value, perPackage.value) } : { ok: false as const })
            : parsePositiveDecimal(draft.netKg);
        const requestId = newRequestId();
        // GGN zoals op de regel gekozen (voorstel overgenomen, aangepast of leeggemaakt); de server bewaart precies dit.
        const ggn = resolveGgn(draft.ggn);
        return {
            requestId,
            payload: {
                requestId,
                supplyCommitmentLineId: id,
                lotNumber: draft.lot.trim(),
                quantity: quantity.ok ? quantity.value : 0,
                unit: unit as PackageUnit,
                weightMethod: draft.method,
                netKgPerPackage: draft.method === 'Calculated' && perPackage.ok ? perPackage.value : null,
                netWeightKg: netKg.ok ? netKg.value : 0,
                ggn: ggn.ok ? ggn.value : null,
                actorReference,
                reason: null,
                // De variant die bij de ingevulde kenmerken hoort; bevestigen alleen na Ja in het venster, nooit bij een exacte controle.
                receivedProductVariantId: draft.matchedVariantId,
                confirmDeviation: draft.deviation !== null ? true : null,
                deviationReason: draft.deviation?.reason ?? null,
                deviationReasonNote: draft.deviation?.note ?? null,
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
                const mapped = mapServerErrors(step, apiError);
                // Vangnet: de server eist een bevestiging (of weigert de afwijking) terwijl het venster vooraf niet getoond is.
                const conflict = step !== 'pallet' && apiError.status === 409 ? deviationConflict(apiError.body) : null;
                if (conflict !== null && step !== 'pallet') {
                    if (conflict.code === DEVIATION_CODES.confirmationRequired && isDeviationPreview(conflict.deviation)) {
                        setDialog({ lineId: step, preview: conflict.deviation, afterRun: true });
                    } else {
                        addError(mapped, `line.${step}.supplyCommitmentLineId`, t(conflict.code === DEVIATION_CODES.otherArticle ? 'deviation_other_article' : 'deviation_blocked_assigned'));
                    }
                }
                setErrors(mapped);
            }
        } finally {
            inFlight.current = false;
            if (mounted.current) setRunning(false);
        }
    };

    // Elke regel eerst controleren (alleen lezen), vóórdat er iets wordt aangemaakt, zodat Nee echt niets achterlaat: de variant die bij
    // de invoer hoort en het gewicht. Exact = zonder venster verder; anders het venster "Afwijking van de bestelling".
    const checkLine = async (id: number, source: Record<number, ContentDraft>) => {
        const draft = source[id];
        const quantity = parsePositiveDecimal(draft.quantity);
        const article = articleOf(id);
        const entry = index.get(id);
        if (!quantity.ok || article === null || entry === undefined) return;
        const match = matchVariant(article.variants, draft.entered, entry.line.productVariantId, countryNames);
        if (match.kind !== 'match') {
            setCombinations((current) => ({ ...current, [id]: match.kind === 'none' ? 'missing' : 'ambiguous' }));
            return;
        }
        inFlight.current = true;
        setChecking(true);
        let next: Record<number, ContentDraft> | null = null;
        try {
            const preview = await getDeviationPreview(id, match.variantId, quantity.value, netKgOf(draft));
            const withVariant = { ...draft, matchedVariantId: match.variantId };
            if (preview.status === 'Deviation') {
                setDrafts((current) => ({ ...current, [id]: withVariant }));
                setDialog({ lineId: id, preview, afterRun: false });
            } else if (preview.status === 'Exact') next = { ...source, [id]: { ...withVariant, verified: true } };
            else setErrors({ [`line.${id}.supplyCommitmentLineId`]: [t(preview.status === 'OtherArticle' ? 'deviation_other_article' : 'deviation_blocked_assigned')] });
        } catch {
            if (mounted.current) setErrors({ general: [t('deviation_check_failed')] });
        } finally {
            inFlight.current = false;
            if (mounted.current) setChecking(false);
        }
        if (next !== null) {
            setDrafts(next);
            submit(next);
        }
    };

    // Na "Artikel aanmaken en doorgaan" of "Kies dit": opnieuw registreren zodra de lijst of de invoer is bijgewerkt.
    useEffect(() => {
        if (!resumeAfterCreate.current) return;
        resumeAfterCreate.current = false;
        submit();
    });

    const submit = (source: Record<number, ContentDraft> = drafts) => {
        if (inFlight.current || succeeded) return;
        const found = validate(source);
        setErrors(found);
        if (Object.keys(found).length > 0) return;
        const unchecked = selected.find((id) => !(frozen?.lines[id]?.done ?? false) && !source[id].verified && source[id].deviation === null);
        if (unchecked !== undefined) {
            void checkLine(unchecked, source);
            return;
        }

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
            lines: Object.fromEntries(selected.map((id) => [id, frozen?.lines[id] ?? buildLine(id, actorReference, source)])),
        };
        setFrozen(next);
        void run(next, selected);
    };

    // Ja, klopt: vóór het boeken gewoon verder registreren; na een 409 dezelfde aanvraag (zelfde requestId) met bevestiging opnieuw.
    const confirmDeviation = (choice: DeviationChoice) => {
        if (dialog === null) return;
        const { lineId, afterRun } = dialog;
        setDialog(null);
        const next = { ...drafts, [lineId]: { ...drafts[lineId], deviation: choice } };
        setDrafts(next);
        const line = frozen?.lines[lineId];
        if (afterRun && frozen !== null && line !== undefined) {
            const updated: FrozenSubmission = { ...frozen, lines: { ...frozen.lines, [lineId]: { ...line, payload: {
                ...line.payload, confirmDeviation: true, deviationReason: choice.reason, deviationReasonNote: choice.note,
            } } } };
            setFrozen(updated);
            setErrors({});
            void run(updated, selected);
        } else {
            submit(next);
        }
    };

    const reset = useCallback((notice: string | null) => {
        setDialog(null);
        setCombinations({});
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
                            <Link href={`/inspection?palletId=${frozen.pallet.result.id}`} data-tour="receive-to-inspection" className={secondaryButtonClass}>{t('supply_to_inspection')}</Link>
                        )}
                        <button type="button" className={secondaryButtonClass} onClick={() => reset(null)}>{t('supply_another_pallet')}</button>
                    </>
                )}
            >
                <p>{t('supply_receive_success_where', { bin: frozen.pallet.result?.warehouseLocation ?? t('supply_no_bin') })}</p>
                <ul className="mt-2 space-y-1">
                    {savedLines.map((line) => {
                        const entry = index.get(line.payload.supplyCommitmentLineId);
                        const deviated = drafts[line.payload.supplyCommitmentLineId]?.deviation != null;
                        return (
                            <li key={line.requestId} className="flex flex-wrap items-center gap-2">
                                <span>{entry?.line.productName} · {entry?.line.productVariantName}{deviated && ` (${t('deviation_recorded')})`}</span>
                                <span>· {t('supply_lot')} {line.payload.lotNumber}</span>
                                <span>· {formatQuantity(line.payload.quantity, locale)}</span>
                                <UnitBadge unit={line.payload.unit} />
                                <span>· {formatKg(line.payload.netWeightKg, locale)}</span>
                                <GgnBadge ggn={line.result?.ggn ?? line.payload.ggn} />
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
                || `${commitment.id} ${commitment.vendorName ?? ''} ${line.productName ?? ''} ${line.productVariantName ?? ''} ${line.lotNumber ?? ''}`
                    .toLowerCase().includes(term)),
        }))
        .filter((group) => group.lines.length > 0);

    return (
        <form noValidate aria-busy={running || checking} onSubmit={(event) => { event.preventDefault(); submit(); }}
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
                        <input type="search" data-tour="receive-search" value={search} onChange={(event) => setSearch(event.target.value)}
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
                                </tr>
                            </thead>
                            {visibleCommitments.map(({ commitment, lines }) => (
                                <tbody key={commitment.id} className="divide-y divide-gray-100 border-t border-gray-200">
                                    <tr className="bg-gray-50">
                                        <th scope="rowgroup" colSpan={5} className="px-2 py-1 text-left text-xs font-semibold text-gray-700">
                                            #{commitment.id} · {commitment.vendorName ?? '–'}
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
                                                    <input id={inputId} type="checkbox" checked={checked} disabled={missingUnit || !palletEditable} data-tour="receive-line" data-tour-key={`${commitment.id}-${line.id}`}
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
                                                <td className="px-2 py-1.5 text-right align-top">
                                                    {formatQuantity(line.expectedQuantity, locale)}
                                                    {palletBreakdownLabel(line, line.expectedUnit, t, locale) !== null && <span className="block text-xs text-gray-600">{palletBreakdownLabel(line, line.expectedUnit, t, locale)}</span>}
                                                </td>
                                                <td className="px-2 py-1.5 align-top"><UnitBadge unit={line.expectedUnit} /></td>
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
                {/* Alleen in deze Warehouse-flow; DirectTrade heeft geen palletontvangst in het eigen magazijn. */}
                <p className="text-sm text-gray-700">{t('supply_pallet_fixed_state')}</p>
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
                                #{entry.commitment.id} · {entry.commitment.vendorName ?? '–'} · {t('supply_expected')} {formatQuantity(entry.line.expectedQuantity, locale)} {entry.line.expectedUnit === null ? t('availability_unit_unknown') : t(unitLabelKey(entry.line.expectedUnit))}
                                {' · '}
                                {open === 'loading' || open === undefined
                                    ? t('supply_open_loading')
                                    : open === 'unknown'
                                        ? t('supply_open_unknown')
                                        : t('supply_open_quantity', { quantity: formatQuantity(open, locale) })}
                            </p>
                            <div className="grid grid-cols-2 gap-3 md:grid-cols-7">
                                {/* Eerst wat er werkelijk is aangekomen (blind, nooit vooraf ingevuld); Tab loopt herkomst, maat, verpakking, klasse, merk, partij, aantal, kg. */}
                                {(() => {
                                    const article = articleOf(id);
                                    return article === null || countries === null
                                        ? <p className="col-span-2 text-xs text-gray-600 md:col-span-7">{products === null ? t('supply_open_loading') : t('receipt_lists_unavailable')}</p>
                                        : <ReceivedAttributes lineId={id} variants={article.variants} countries={countries} value={draft.entered}
                                            onChange={(patch) => editLine(id, 'entered', { ...draft.entered, ...patch })} disabled={!editable || creating(id)} errors={e}
                                            combination={combinations[id] ?? null}
                                            missingPrompt={combinations[id] === 'missing' && draft.entered.origin !== null ? (
                                                <NewArticlePrompt articleName={`${entry.line.productName} ${entry.line.productVariantName}`}
                                                    countryName={countryNames.get(draft.entered.origin) ?? null} entered={draft.entered}
                                                    similar={similarVariants(article.variants, draft.entered, countryNames)}
                                                    onChoose={(variant) => chooseSimilar(id, enteredFrom(variant, countryNames))} onCreate={() => void createArticle(id)}
                                                    busy={creation[id]?.busy ?? false} error={creation[id]?.error ?? null} dataTourKey={id} />
                                            ) : undefined} />;
                                })()}
                                <div className="col-span-2">
                                    <label htmlFor={`${key('lotNumber')}-input`} className="text-xs font-medium text-gray-700">{t('supply_lot')} *</label>
                                    <input {...fieldProps(key('lotNumber'), e(key('lotNumber')))} data-tour="receive-lot" data-tour-key={id} value={draft.lot} maxLength={100} disabled={!editable}
                                        onChange={(event) => editLine(id, 'lot', event.target.value)} className={inputClass} />
                                    <FieldError id={key('lotNumber')} messages={e(key('lotNumber'))} />
                                </div>
                                <div>
                                    <label htmlFor={`${key('quantity')}-input`} className="text-xs font-medium text-gray-700">{t('supply_quantity')} *</label>
                                    <input {...fieldProps(key('quantity'), e(key('quantity')))} data-tour="receive-quantity" data-tour-key={id} value={draft.quantity} inputMode="decimal" disabled={!editable}
                                        onChange={(event) => editLine(id, 'quantity', event.target.value)} className={`${inputClass} min-w-[7rem] text-right`} />
                                    {/* Alleen het getal terug ("= 1.560"); geen rekenregel met het artikelgewicht, zodat de bestelling niet vooraf zichtbaar is. */}
                                    <NumberEcho value={draft.quantity} unit={entry.line.expectedUnit === null ? '' : t(unitLabelKey(entry.line.expectedUnit)).toLowerCase()} testId={`${key('quantity')}-echo`} />
                                    <FieldError id={key('quantity')} messages={e(key('quantity'))} />
                                </div>
                                <div>
                                    <span className="text-xs font-medium text-gray-700">{t('supply_unit')}</span>
                                    <div className="mt-1.5"><UnitBadge unit={entry.line.expectedUnit} /></div>
                                    <FieldError id={key('unit')} messages={e(key('unit'))} />
                                </div>
                                <div className="col-span-2" data-tour="receive-weight" data-tour-key={id}>
                                    <fieldset>
                                        <legend className="text-xs font-medium text-gray-700">{t('supply_weight_method')} *</legend>
                                        <div className="mt-1 flex flex-wrap gap-3 text-xs text-gray-800">
                                            {WEIGHT_METHODS.map((method) => (
                                                <label key={method} className="flex items-center gap-1">
                                                    <input type="radio" name={key('weightMethod')} value={method} checked={draft.method === method} disabled={!editable}
                                                        onChange={() => editLine(id, 'method', method)} />
                                                    {t(method === 'Weighed' ? 'supply_weight_method_weighed' : 'supply_weight_method_calculated')}
                                                </label>
                                            ))}
                                        </div>
                                        <FieldError id={key('weightMethod')} messages={e(key('weightMethod'))} />
                                    </fieldset>
                                    {draft.method === 'Calculated' ? (
                                        <div className="mt-1">
                                            <label htmlFor={`${key('netKgPerPackage')}-input`} className="text-xs font-medium text-gray-700">{t('supply_kg_per_package')} *</label>
                                            <input {...fieldProps(key('netKgPerPackage'), e(key('netKgPerPackage')))} data-tour="receive-kg-per-package" data-tour-key={id} value={draft.kgPerPackage} inputMode="decimal" disabled={!editable || creating(id)}
                                                onChange={(event) => editLine(id, 'kgPerPackage', event.target.value)} className={inputClass} />
                                            <span className="block text-xs text-gray-500">{t('supply_kg_per_package_hint')}</span>
                                            <span className="block text-xs font-medium text-amber-900" data-testid={`calculated-total-${id}`}>
                                                {(() => {
                                                    const quantity = parsePositiveDecimal(draft.quantity);
                                                    const perPackage = parsePositiveDecimal(draft.kgPerPackage);
                                                    return quantity.ok && perPackage.ok
                                                        ? t('supply_calculated_total', { quantity: formatQuantity(quantity.value, locale), kg: formatKg(perPackage.value, locale), total: formatKg(calculatedTotalKg(quantity.value, perPackage.value), locale) })
                                                        : t('supply_calculated_total_pending');
                                                })()}
                                            </span>
                                            <FieldError id={key('netKgPerPackage')} messages={e(key('netKgPerPackage'))} />
                                            <FieldError id={key('netWeightKg')} messages={e(key('netWeightKg'))} />
                                        </div>
                                    ) : (
                                        <div className="mt-1">
                                            <label htmlFor={`${key('netWeightKg')}-input`} className="text-xs font-medium text-gray-700">{t('supply_actual_net_kg')} *</label>
                                            <input {...fieldProps(key('netWeightKg'), e(key('netWeightKg')))} data-tour="receive-net-kg" data-tour-key={id} value={draft.netKg} inputMode="decimal" disabled={!editable}
                                                onChange={(event) => editLine(id, 'netKg', event.target.value)} className={inputClass} />
                                            <NumberEcho value={draft.netKg} unit="kg" testId={`${key('netWeightKg')}-echo`} />
                                            <span className="block text-xs text-gray-500">{t('supply_actual_net_kg_hint')}</span>
                                            <FieldError id={key('netWeightKg')} messages={e(key('netWeightKg'))} />
                                        </div>
                                    )}
                                </div>
                                <div>
                                    <span className="text-xs font-medium text-gray-700">{t('supply_quality')}</span>
                                    <div className="mt-1.5"><QualityBadge status="Quarantine" /></div>
                                </div>
                                {/* GGN: leveranciers-GGN's als invoerhulp (precies één = voorstel, meer = kiezen), per partij aanpasbaar of bewust leeg; de gekozen waarde staat op de ontvangst. */}
                                <div className="col-span-2 md:col-span-3">
                                    <label htmlFor={`${key('ggn')}-input`} className="text-xs font-medium text-gray-700">{t('supply_ggn')}</label>
                                    <div className="flex flex-wrap gap-2">
                                        <input {...fieldProps(key('ggn'), e(key('ggn')))} value={draft.ggn} inputMode="numeric" maxLength={20} disabled={!editable}
                                            onChange={(event) => editLine(id, 'ggn', event.target.value)} className={`${inputClass} min-w-48 flex-1 font-mono`} />
                                        {entry.commitment.vendorGgns.length > 1 && (
                                            <select aria-label={t('supply_ggn_choose_label')} disabled={!editable} value={entry.commitment.vendorGgns.some((option) => option.ggn === normalizeGgn(draft.ggn)) ? normalizeGgn(draft.ggn) : ''}
                                                onChange={(event) => editLine(id, 'ggn', event.target.value)} className={`${inputClass} w-auto`} data-testid={`ggn-choice-${id}`}>
                                                <option value="">{t('supply_ggn_choose_option')}</option>
                                                {entry.commitment.vendorGgns.map((option) => <option key={option.ggn} value={option.ggn}>{option.ggn}{option.producerName ? ` · ${option.producerName}` : ''}</option>)}
                                            </select>
                                        )}
                                    </div>
                                    <span className="block text-xs text-gray-500" data-testid={`ggn-state-${id}`}>
                                        {(() => { const state = ggnStateKey(draft.ggn, entry.commitment.vendorGgns); return state === null ? '' : t(state, { ggn: entry.commitment.vendorGgns[0]?.ggn ?? '', count: String(entry.commitment.vendorGgns.length) }); })()}
                                    </span>
                                    {(() => {
                                        const warning = ggnCertificateWarning(draft.ggn, entry.commitment.vendorGgns, todayInAmsterdam());
                                        const option = entry.commitment.vendorGgns.find((candidate) => candidate.ggn === normalizeGgn(draft.ggn));
                                        return warning === null ? null : (
                                            <p role="status" className="mt-1 rounded border border-amber-400 bg-amber-50 px-2 py-1 text-xs font-medium text-amber-950" data-testid={`ggn-certificate-warning-${id}`}>
                                                ⚠ {t(warning, { ggn: option?.ggn ?? '', date: option?.certificateValidUntil ? formatDateOnly(option.certificateValidUntil, locale) : '' })}
                                            </p>
                                        );
                                    })()}
                                    <FieldError id={key('ggn')} messages={e(key('ggn'))} />
                                </div>
                            </div>
                            <FieldError id={key('supplyCommitmentLineId')} messages={e(key('supplyCommitmentLineId'))} />
                        </fieldset>
                    );
                })}
            </section>

            {progress.length > 0 && <SubmitProgress steps={progress} />}

            <div className="flex items-center gap-3 border-t border-gray-200 pt-3">
                <button type="submit" data-tour="receive-save" className={primaryButtonClass} disabled={blocked || checking || dialog !== null || sources.status !== 'ready'}>
                    {running || checking ? t('supply_saving') : frozen?.pallet.done ? t('supply_continue_registration') : t('supply_register_pallet')}
                </button>
            </div>
            {dialog !== null && <DeviationDialog preview={dialog.preview} onNo={() => setDialog(null)} onYes={confirmDeviation} />}
        </form>
    );
}

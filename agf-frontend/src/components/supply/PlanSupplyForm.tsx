'use client';

import Link from 'next/link';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { availabilityHref, singleVariantId } from '../../lib/availabilityLinks';
import { formatDateTime, formatQuantity } from '../../lib/availability';
import { type PurchaseLineDraft, emptyPurchaseLine, purchaseLinePayloads, purchaseLineRow } from '../../lib/purchaseOrderLines';
import { withArticle } from '../../lib/articleFromEntry';
import { isDateOnly, parseDateInput, toLocalMidnightIso, todayInAmsterdam } from '../../lib/calendarDate';
import { MAX_SUPPLIER_REFERENCE_LENGTH } from '../../lib/purchaseOrderRules';
import { MAX_PARTIJ_NUMBER_LENGTH, MAX_SUPPLIER_LOT_REFERENCE_LENGTH } from '../../lib/partijRules';
import { newRequestId } from '../../lib/requestId';
import { isMacPlatform, shortcutLabel } from '../../lib/shortcuts';
import { ApiError, toApiError } from '../../services/apiClient';
import OrderNumberText from '../purchase/OrderNumberText';
import SupplierReferenceNotice from '../purchase/SupplierReferenceNotice';
import { SaveResult, createSupplyCommitment } from '../../services/supplyService';
import CertificateCheckDialog from '../certificates/CertificateCheckDialog';
import { type CertificateCheck, certificateChecksOf } from '../../types/certificate';
import { getGgnProposals, updatePurchaseOrder } from '../../services/purchaseOrderService';
import { applyGgnProposals } from '../../lib/productGgn';
import type { GgnProposal } from '../../types/productGgn';
import { type OrderEdit, lineLimits, orderEditOf, updateLinePayload } from '../../lib/purchaseOrderEdit';
import type { PurchaseOrderDetail, UpdatePurchaseOrderRequest } from '../../types/purchaseOrder';
import { MAX_NOTE_LENGTH } from '../../lib/purchaseOrderRules';
import { getNextPartijNumber } from '../../services/partijService';
import { getCountries } from '../../services/countryService';
import type { CountryReference } from '../../types/country';
import CountryPicker from '../shared/CountryPicker';
import { type PartijSummary, isPartijExistsConflict } from '../../types/partij';
import { SupplyRoute } from '../../types/availability';
import { CustomerOption, ProductOption, VendorOption } from '../../types/referenceData';
import { CreateSupplyCommitmentRequest, SupplyCommitment } from '../../types/supply';
import { UnitBadge } from '../availability/QuantityCell';
import DateField from '../shared/DateField';
import VendorPicker from '../purchase/VendorPicker';
import PartijField, { NO_PARTIJ, type PartijChoice, partijPayload } from '../partij/PartijField';
import CustomerPicker from '../sales/CustomerPicker';
import NewSupplyArticle from './NewSupplyArticle';
import PurchaseOrderLines from './PurchaseOrderLines';
import { lineKgLabel } from '../shared/lineKgLabel';
import { palletBreakdownLabel } from '../shared/palletBreakdownLabel';
import ErrorNotice from './ErrorNotice';
import FieldError, { fieldProps, inputClass } from './FieldError';
import { MissingMasterDataNotice } from '../shared/FirstStep';
import { missingMasterData } from '../../lib/masterDataStatus';
import { FieldErrors, addError, decimalErrorKey, outcomeUnknown } from './messages';
import SuccessPanel, { primaryButtonClass, secondaryButtonClass } from './SuccessPanel';
import SubmitErrors from '../shared/SubmitErrors';
import { useFormLeaveGuard } from '../shared/LeaveGuard';
import DocumentCurrencyField from '../purchase/DocumentCurrencyField';
import { type CurrencyDraft, currencyPayload, draftFromDocument, effectiveRate, euroDraft } from '../../lib/documentCurrency';
import { EURO, isEuro } from '../../lib/exchangeRate';
import { getPurchaseCurrencies, type PurchaseCurrency } from '../../services/purchaseCurrencyService';

export interface ReferenceData {
    vendors: VendorOption[];
    customers: CustomerOption[];
    products: ProductOption[];
}

interface HeaderDraft {
    vendorId: string;
    customerId: string;
    // Herkomst van de order: een land uit de vaste lijst (feedback 22 november); legacyOrigin = oude tekst zonder land (alleen tonen).
    originCountryCode: string | null;
    legacyOrigin: string | null;
    destination: string;
    expectedDate: string;
    // Inkooporder: referentie van de leverancier en besteldatum (standaard vandaag).
    supplierReference: string;
    orderedOn: string;
    // Het eigen lotnummer van de leverancier (vrije tekst, optioneel).
    supplierLotReference: string;
    // Notitie bij de order (intern, optioneel).
    note: string;
}

// Bevroren verzending: exact deze payload en requestId worden bij "opnieuw proberen" verstuurd (aanmaken of aanpassen).
interface PendingSubmission {
    requestId: string;
    payload: CreateSupplyCommitmentRequest | UpdatePurchaseOrderRequest;
}

const MAX_LINES = 200;

// Aanpassen: een 409 met code (iemand anders was eerder, intussen definitief, of niet aanpasbaar) in gewone taal.
const EDIT_CONFLICTS: Readonly<Record<string, string>> = { stale: 'po_edit_stale', definitive: 'po_edit_definitive_now', notEditable: 'po_edit_not_editable' };
function editConflict(error: ApiError): string | null {
    const body = error.body as { code?: unknown } | null | undefined;
    return typeof body?.code === 'string' ? EDIT_CONFLICTS[body.code] ?? null : null;
}
const emptyLine = (): PurchaseLineDraft => emptyPurchaseLine(newRequestId());
const emptyHeader = (): HeaderDraft => ({ vendorId: '', customerId: '', originCountryCode: null, legacyOrigin: null, destination: '', expectedDate: '', supplierReference: '', orderedOn: todayInAmsterdam(), supplierLotReference: '', note: '' });

// De partij van een order die wordt aangepast, als "bestaande partij" in het partijveld (alleen id en nummer zijn nodig).
const orderPartij = (order: PurchaseOrderDetail): PartijChoice => order.partijId && order.partijNumber
    ? { kind: 'existing', number: order.partijNumber, partij: { id: order.partijId, number: order.partijNumber, kind: 'Purchase', salesOrderId: null, status: 'Open', note: null, createdAt: '', createdBy: '', linked: true } }
    : NO_PARTIJ;

// Aanpassen (stap 4a): dezelfde invoer als aanmaken, gevuld met de opgeslagen order; opslaan via PUT /api/purchase-orders/{id}.
export interface PurchaseOrderEditing {
    order: PurchaseOrderDetail;
    onSaved: (order: PurchaseOrderDetail) => void;
}

// Aanvoer plannen voor Warehouse of DirectTrade via POST /api/supply-commitments (idempotent via requestId). Met `editing` hetzelfde
// formulier voor een opgeslagen inkooporder (alle sneltoetsen en de nl-notatie gelden ook hier), met de grenzen na ontvangst.
export default function PlanSupplyForm({ route, reference, onReceive, editing = null }: {
    route: SupplyRoute;
    reference: ReferenceData;
    onReceive: (commitment: SupplyCommitment) => void;
    editing?: PurchaseOrderEditing | null;
}) {
    const { t, locale } = useTranslation();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    const isDirect = route === 'DirectTrade';
    // Aanpassen: het formulier opent met de opgeslagen order; per regel wat vastligt (één keer bepaald bij openen).
    const [opened] = useState(() => (editing === null ? null : orderEditOf(editing.order)));
    const edit: OrderEdit | null = opened?.edit ?? null;
    const [header, setHeader] = useState<HeaderDraft>(() => (opened === null ? emptyHeader() : { ...emptyHeader(), ...opened.header }));
    const [lines, setLines] = useState<PurchaseLineDraft[]>(() => opened?.lines ?? [emptyLine()]);
    const [pending, setPending] = useState<PendingSubmission | null>(null);
    // Certificaten deel 1: de vraag "Klopt dit?" (product niet in het GGN-certificaat van de leverancier, of verlopen); Ja stuurt dezelfde
    // opdracht opnieuw met de bevestigde producten.
    const [certificateCheck, setCertificateCheck] = useState<CertificateCheck[] | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    // Fixes 5b: weggaan met een fout of niet-opgeslagen wijzigingen vraagt eerst (LeaveGuard).
    const leave = useFormLeaveGuard(Object.keys(errors).length > 0);
    const [result, setResult] = useState<SaveResult<SupplyCommitment> | null>(null);
    const inFlight = useRef(false);
    // Artikelen van dit formulier: de referentielijst plus wat hier met "Nieuw artikel" is aangemaakt; per regel het open blok.
    const [products, setProducts] = useState(reference.products);
    const [newArticleFor, setNewArticleFor] = useState<string | null>(null);
    // Partij (kop): het voorstel volgens de bedrijfsinstelling (null in modus Eigen of als het niet te laden is), de keuze en de open
    // vraag "Partij … bestaat al, wil je die gebruiken?".
    const [suggestion, setSuggestion] = useState<string | null>(null);
    const [partij, setPartij] = useState<PartijChoice>(() => (editing === null ? NO_PARTIJ : orderPartij(editing.order)));
    const [partijExists, setPartijExists] = useState<PartijSummary | null>(null);
    const [suggestionToken, setSuggestionToken] = useState(0);
    // Bij aanpassen nooit een voorstel over de partij van de order heen.
    const partijTouched = useRef(editing !== null);
    // Valuta-ronde: valuta en koers van de order (euro standaard; bij aanpassen de vastgelegde) en de lijst om uit te kiezen.
    const [money, setMoney] = useState<CurrencyDraft>(() => (editing === null ? euroDraft()
        : draftFromDocument(editing.order.currency, editing.order.exchangeRate, editing.order.exchangeRateSource, editing.order.exchangeRateDate, locale)));
    const [currencies, setCurrencies] = useState<PurchaseCurrency[]>([{ code: EURO, hasEcbRate: false }]);
    // De landenlijst voor de herkomst van de order; null zolang ze laadt.
    const [countries, setCountries] = useState<CountryReference | null>(null);
    useEffect(() => {
        const controller = new AbortController();
        getCountries(controller.signal).then(setCountries).catch(() => setCountries({ countries: [], aliases: [] }));
        return () => controller.abort();
    }, []);
    // Lukt het laden van de valutalijst niet, dan blijven euro en de valuta van de order; het scherm zegt dat en kan het opnieuw proberen.
    const [currenciesFailed, setCurrenciesFailed] = useState(false);
    const [currenciesAttempt, setCurrenciesAttempt] = useState(0);
    useEffect(() => {
        const controller = new AbortController();
        getPurchaseCurrencies(controller.signal)
            .then((list) => { setCurrencies(list); setCurrenciesFailed(false); })
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setCurrenciesFailed(true); });
        return () => controller.abort();
    }, [currenciesAttempt]);

    useEffect(() => {
        const controller = new AbortController();
        getNextPartijNumber(undefined, controller.signal)
            .then((next) => {
                setSuggestion(next.suggestion);
                if (!partijTouched.current && next.suggestion !== null) setPartij({ kind: 'suggested', number: next.suggestion });
            })
            .catch(() => setSuggestion(null));
        return () => controller.abort();
    }, [suggestionToken]);

    // Na een onbekende uitkomst of na succes is het formulier vergrendeld (geen dubbele aanvoer door een gewijzigde retry).
    const locked = submitting || result !== null || (error !== null && outcomeUnknown(error));

    // Het artikel van een regel: eenheid, netto en bruto per colli, colli per pallet, merk, land, maat en inhoud.
    const variants = useMemo(() => new Map(products.flatMap((product) => product.variants.map((variant) => [variant.id, variant] as const))), [products]);

    // Certificaten deel 2: het voorgestelde GGN van het product per regel, uit de geldige GGN-certificaten van de leverancier op de verwachte
    // leverdatum. Opnieuw opgehaald bij een andere leverancier, andere producten of een andere datum; lukt het niet, dan blijft het veld
    // gewoon in te vullen (geen voorstel, geen fout).
    const productOfVariant = useMemo(() => new Map(products.flatMap((product) => product.variants.map((variant) => [variant.id, product.id] as const))), [products]);
    const productIdsKey = [...new Set(lines.flatMap((line) => {
        const product = line.productVariantId === '' ? undefined : productOfVariant.get(Number(line.productVariantId));
        return product === undefined ? [] : [product];
    }))].sort((a, b) => a - b).join(',');
    const proposalDate = useMemo(() => { const parsed = parseDateInput(header.expectedDate); return parsed !== null && isDateOnly(parsed) ? parsed : null; }, [header.expectedDate]);
    const [ggnProposals, setGgnProposals] = useState<ReadonlyMap<number, GgnProposal>>(() => new Map());
    // De voorstellen gelden voor één leverancier op één datum: een andere leverancier of datum wist ze meteen. Een ander of extra product
    // houdt de bestaande voorstellen (de voorgevulde GGN's van de andere regels blijven staan) tot het nieuwe antwoord er is.
    const proposalScope = `${header.vendorId}|${proposalDate ?? ''}`;
    const scopeRef = useRef(proposalScope);
    useEffect(() => {
        if (scopeRef.current !== proposalScope) {
            scopeRef.current = proposalScope;
            setGgnProposals(new Map());
        }
        if (header.vendorId === '' || productIdsKey === '') return;
        const controller = new AbortController();
        getGgnProposals(Number(header.vendorId), productIdsKey.split(',').map(Number), proposalDate, controller.signal)
            .then((result) => setGgnProposals(new Map(result.proposals.map((proposal) => [proposal.productId, proposal]))))
            .catch(() => { /* geen voorstel: het GGN blijft gewoon in te vullen */ });
        return () => controller.abort();
    }, [header.vendorId, productIdsKey, proposalDate, proposalScope]);
    const ggnProposalOf = (line: PurchaseLineDraft): GgnProposal | undefined => {
        const product = line.productVariantId === '' ? undefined : productOfVariant.get(Number(line.productVariantId));
        return product === undefined ? undefined : ggnProposals.get(product);
    };
    // Regels die de gebruiker niet zelf aanraakte, volgen het voorstel (één treffer = voorgevuld, anders leeg).
    // Niet tijdens of na een opslag (vergrendeld): dan blijft het formulier precies zoals het verstuurd is.
    useEffect(() => {
        if (locked) return;
        setLines((current) => applyGgnProposals(current, ggnProposalOf) as PurchaseLineDraft[]);
        // ggnProposalOf hangt alleen af van de voorstellen en de artikelen.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [ggnProposals, productOfVariant, lines, locked]);

    const edited = () => {
        // Na een definitieve weigering (of niets verstuurd) hoort een gewijzigde invoer bij een nieuw verzoek.
        setPending(null);
        setError(null);
        setErrors({});
        setCertificateCheck(null);
    };

    const updateHeader = (field: Exclude<keyof HeaderDraft, 'originCountryCode' | 'legacyOrigin'>, value: string) => {
        if (locked) return;
        setHeader((current) => ({ ...current, [field]: value }));
        edited();
    };
    const updateOrigin = (code: string | null) => {
        if (locked) return;
        setHeader((current) => ({ ...current, originCountryCode: code }));
        edited();
    };

    const updateMoney = (next: CurrencyDraft) => {
        if (locked) return;
        setMoney(next);
        edited();
    };

    // Een andere leverancier stelt haar standaardvaluta voor, zolang de gebruiker de valuta niet zelf koos (en nooit bij aanpassen).
    const chooseVendor = (vendor: VendorOption | null) => {
        updateHeader('vendorId', vendor === null ? '' : String(vendor.id));
        // Zolang de gebruiker de valuta niet zelf koos, volgt ze de leverancier; een leverancier zonder standaardvaluta (of geen
        // leverancier) is euro, zodat de valuta van een eerder gekozen leverancier niet blijft staan.
        const proposal = vendor?.defaultCurrency?.trim().toUpperCase();
        const wanted = proposal && /^[A-Z]{3}$/.test(proposal) ? proposal : EURO;
        if (editing === null && !money.touched && wanted !== money.currency.trim().toUpperCase()) {
            setMoney(isEuro(wanted) ? euroDraft() : { currency: wanted, rate: '', source: 'ECB', rateDate: null, touched: false });
        }
    };

    const updatePartij = (choice: PartijChoice) => {
        if (locked) return;
        partijTouched.current = true;
        setPartij(choice);
        edited();
    };

    const updateLine = (key: string, patch: Partial<Omit<PurchaseLineDraft, 'key'>>) => {
        if (locked) return;
        setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));
        edited();
    };

    // Per regel wat uit het artikel en de invoer volgt (kg, totaal, pallets); ook voor de totalen onder de regels.
    const lineRows = lines.map((line) => purchaseLineRow(line, line.productVariantId === '' ? undefined : variants.get(Number(line.productVariantId))));

    const addLine = () => {
        if (locked || lines.length >= MAX_LINES) return;
        setLines((current) => [...current, emptyLine()]);
        edited();
    };

    const limitsOf = (key: string) => lineLimits(edit?.lines.get(key));

    const removeLine = (key: string) => {
        if (locked || lines.length <= 1 || !limitsOf(key).removable) return;
        setLines((current) => current.filter((line) => line.key !== key));
        edited();
    };

    const validate = (): { payload: Omit<CreateSupplyCommitmentRequest, 'requestId'> | null; errors: FieldErrors; editLines?: UpdatePurchaseOrderRequest['lines'] } => {
        const found: FieldErrors = {};
        const vendorId = Number(header.vendorId);
        if (!header.vendorId) addError(found, 'vendorId', t('supply_error_required'));
        const currency = currencyPayload(money);
        if (!currency.ok) addError(found, currency.field, t(currency.field === 'currency' ? 'supply_error_required' : money.source === 'ECB' ? 'currency_error_rate_pending' : 'currency_error_rate'));

        const customerId = Number(header.customerId);
        if (isDirect && !header.customerId) addError(found, 'customerId', t('supply_error_required_customer'));

        // Herkomst van de order is optioneel (de herkomst staat per regel bij het artikel) en een land uit de lijst.
        const originCountryCode = header.originCountryCode;

        const destination = header.destination.trim();
        if (isDirect && destination === '') addError(found, 'destination', t('supply_error_required_destination'));
        else if (destination.length > 200) addError(found, 'destination', t('supply_error_too_long', { max: '200' }));

        // Verwacht op: het datumveld geeft de ISO-datum, of de getypte tekst als dat (nog) geen datum is. Vandaag en eerder zijn
        // geldig (een aanvoer kan achteraf worden vastgelegd); naar de server als lokale middernacht.
        let expectedAt = '';
        const expectedDate = parseDateInput(header.expectedDate);
        const expectedIso = expectedDate === null ? null : toLocalMidnightIso(expectedDate);
        if (expectedIso === null) addError(found, 'expectedAt', t(header.expectedDate.trim() === '' ? 'supply_error_required_date' : 'supply_error_incomplete_date'));
        else expectedAt = expectedIso;

        const supplierReference = header.supplierReference.trim();
        if (supplierReference.length > MAX_SUPPLIER_REFERENCE_LENGTH) addError(found, 'supplierReference', t('supply_error_too_long', { max: String(MAX_SUPPLIER_REFERENCE_LENGTH) }));
        if (header.orderedOn !== '' && !isDateOnly(header.orderedOn)) addError(found, 'orderedOn', t('supply_error_required_date'));
        const supplierLotReference = header.supplierLotReference.trim();
        if (supplierLotReference.length > MAX_SUPPLIER_LOT_REFERENCE_LENGTH) addError(found, 'supplierLotReference', t('supply_error_too_long', { max: String(MAX_SUPPLIER_LOT_REFERENCE_LENGTH) }));
        if (partij.kind === 'own' && partij.number.trim().length > MAX_PARTIJ_NUMBER_LENGTH) addError(found, 'partij', t('supply_error_too_long', { max: String(MAX_PARTIJ_NUMBER_LENGTH) }));
        // Een partij gaat nooit van een order af (regel 23): kies eventueel een andere.
        if (edit !== null && edit.partijId !== null && partij.kind === 'none') addError(found, 'partij', t('po_edit_partij_kept'));
        const note = header.note.trim();
        if (note.length > MAX_NOTE_LENGTH) addError(found, 'note', t('supply_error_too_long', { max: String(MAX_NOTE_LENGTH) }));

        if (lines.length === 0) addError(found, 'lines', t('supply_error_lines_required'));
        // Alleen invullen wat per regel anders is: product, aantal, prijs (en eventueel de bruto); de rest komt uit het artikel.
        // Een lege regel (zoals de nieuwe na Tab op het laatste veld) telt niet mee zolang er een gevulde regel is.
        const built = purchaseLinePayloads(lines, (variantId) => variants.get(variantId));
        for (const { index, error } of built.errors) {
            const message = error.code === 'required'
                ? t(error.field === 'expectedUnit' ? 'po_line_unit_missing' : 'supply_error_required')
                : error.code === 'gross_below_net' ? t('line_kg_gross_below_net') : error.code === 'ggn_13_digits' ? t('supply_error_ggn') : t(decimalErrorKey(error.code));
            addError(found, `lines[${index}].${error.field}`, message);
        }
        const seen = new Set<string>();
        for (const { index, payload } of built.lines) {
            if (payload.lotNumber !== null && payload.lotNumber.length > 100) addError(found, `lines[${index}].lotNumber`, t('supply_error_too_long', { max: '100' }));
            if (payload.lotNumber === null || !payload.productVariantId) continue;
            const duplicateKey = `${payload.productVariantId}|${payload.lotNumber}`;
            if (seen.has(duplicateKey)) addError(found, `lines[${index}]`, t('supply_error_duplicate_line'));
            seen.add(duplicateKey);
        }
        // Aanpassen: nooit onder wat al ontvangen is (de uitleg staat ook al bij het veld).
        for (const { index, payload } of built.lines) {
            const minimum = limitsOf(lines[index].key).minimumQuantity;
            if (minimum !== null && payload.expectedQuantity < minimum) addError(found, `lines[${index}].expectedQuantity`, t('po_edit_min_quantity', { received: formatQuantity(minimum, locale) }));
        }
        const payloadLines = built.lines.map((line) => line.payload);

        if (Object.keys(found).length > 0) {
            return { payload: null, errors: found };
        }
        return {
            payload: {
                vendorId,
                customerId: isDirect ? customerId : null,
                route,
                ...(originCountryCode === null ? {} : { originCountryCode }),
                destination: destination === '' ? null : destination,
                expectedAt,
                ...(supplierReference === '' ? {} : { supplierReference }),
                ...(header.orderedOn === '' ? {} : { orderedOn: header.orderedOn }),
                ...partijPayload(partij),
                ...(supplierLotReference === '' ? {} : { supplierLotReference }),
                ...(note === '' ? {} : { note }),
                ...(currency.ok ? currency.value : {}),
                lines: payloadLines,
            },
            errors: {},
            // Aanpassen: per regel haar id; een niet aangeraakte regel gaat exact terug zoals ze was opgeslagen.
            editLines: edit === null ? [] : built.lines.map(({ index, payload }) => updateLinePayload(lines[index], payload, edit.lines.get(lines[index].key))),
        };
    };

    const submit = async (confirmed: PendingSubmission | null = null) => {
        if (inFlight.current || result !== null) return;
        let submission = confirmed ?? pending;
        if (submission === null) {
            const { payload, errors: found, editLines } = validate();
            setErrors(found);
            if (payload === null) return;
            const requestId = newRequestId();
            submission = edit === null ? { requestId, payload: { requestId, ...payload } } : {
                requestId,
                payload: {
                    requestId, expectedVersion: edit.version, vendorId: payload.vendorId, originCountryCode: payload.originCountryCode ?? null, expectedAt: payload.expectedAt,
                    ...(payload.supplierReference === undefined ? {} : { supplierReference: payload.supplierReference }),
                    ...(payload.orderedOn === undefined ? {} : { orderedOn: payload.orderedOn }), ...(payload.note === undefined ? {} : { note: payload.note }),
                    ...partijPayload(partij), ...(payload.supplierLotReference === undefined ? {} : { supplierLotReference: payload.supplierLotReference }),
                    currency: payload.currency, exchangeRate: payload.exchangeRate, exchangeRateSource: payload.exchangeRateSource,
                    lines: editLines ?? [],
                },
            };
            setPending(submission);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            if (editing !== null && edit !== null) {
                const updated = await updatePurchaseOrder(edit.orderId, submission.payload as UpdatePurchaseOrderRequest);
                leave.saved();
                editing.onSaved(updated);
                return;
            }
            const created = await createSupplyCommitment(submission.payload as CreateSupplyCommitmentRequest);
            leave.saved();
            setResult(created);
        } catch (caught) {
            const apiError = toApiError(caught);
            // "Klopt dit?": niets opgeslagen; dezelfde opdracht (zelfde requestId) wacht op Ja of Nee.
            const checks = apiError.status === 409 ? certificateChecksOf(apiError.body) : null;
            if (checks !== null) {
                setCertificateCheck(checks);
                return;
            }
            // Het getypte partijnummer bestaat al (ook als een ander het net aanmaakte): dezelfde vraag als bij het verlaten van het veld.
            if (isPartijExistsConflict(apiError.body)) {
                setPartijExists(apiError.body.partij);
                setPending(null);
                return;
            }
            setError(apiError);
            setErrors(apiError.fieldErrors);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    // Ja, klopt: dezelfde opdracht met de bevestigde producten erbij. Nee: terug naar het formulier om aan te passen (een nieuwe opdracht).
    const confirmCertificates = () => {
        if (certificateCheck === null || pending === null) return;
        const already = (pending.payload as { confirmedCertificateProductIds?: number[] }).confirmedCertificateProductIds ?? [];
        const next = { ...pending, payload: { ...pending.payload, confirmedCertificateProductIds: [...new Set([...already, ...certificateCheck.map((c) => c.productId)])] } } as PendingSubmission;
        setCertificateCheck(null);
        setPending(next);
        void submit(next);
    };
    const declineCertificates = () => {
        setCertificateCheck(null);
        setPending(null);
    };

    const startOver = () => {
        // Aanpassen: terug naar de order zoals ze opende.
        setHeader(opened === null ? emptyHeader() : { ...emptyHeader(), ...opened.header });
        setLines(opened?.lines ?? [emptyLine()]);
        setPending(null);
        setError(null);
        setErrors({});
        setResult(null);
        partijTouched.current = editing !== null;
        setPartij(editing === null ? NO_PARTIJ : orderPartij(editing.order));
        setPartijExists(null);
        setSuggestionToken((token) => token + 1);
    };

    if (result !== null) {
        const commitment = result.value;
        const href = availabilityHref({
            route,
            focus: 'expected',
            productVariantId: singleVariantId(commitment.lines.map((line) => line.productVariantId)),
            customerId: commitment.customerId ?? undefined,
        });
        return (
            <SuccessPanel
                title={isDirect ? t('supply_plan_success_direct', { id: String(commitment.id) }) : t('supply_plan_success_warehouse', { id: String(commitment.id) })}
                note={result.replayed ? t('supply_saved_before') : undefined}
                actions={(
                    <>
                        <Link href={href} className={primaryButtonClass}>{t('supply_view_availability')}</Link>
                        <Link href={`/purchase-orders?id=${commitment.id}`} className={secondaryButtonClass}>{t('po_to_order')}</Link>
                        {!isDirect && (
                            <button type="button" data-tour="supply-receive" className={secondaryButtonClass} onClick={() => onReceive(commitment)}>
                                {t('supply_receive_for_this')}
                            </button>
                        )}
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('supply_new_plan')}</button>
                    </>
                )}
            >
                {commitment.orderNumber && (
                    <p className="mb-2" data-testid="supply-order-number" data-tour="supply-order-number">
                        <span className="block text-xs font-medium uppercase tracking-wide text-green-900">{t(commitment.supplierReference ? 'po_supplier_reference' : 'po_own_order_number')}</span>
                        <OrderNumberText orderNumber={commitment.orderNumber} supplierReference={commitment.supplierReference} stacked mainClassName="block text-3xl font-bold text-gray-900" />
                    </p>
                )}
                <p className="mb-1" data-tour="supply-saved-partij">
                    {commitment.partijNumber
                        ? <>{t('partij_field')} <span className="font-mono font-semibold" data-tour="supply-saved-partij-number">{commitment.partijNumber}</span>{commitment.supplierLotReference && <> · {t('partij_supplier_lot')} <span className="font-mono">{commitment.supplierLotReference}</span></>}</>
                        : t('partij_saved_without')}
                </p>
                <p>
                    {commitment.vendorName ?? '–'}{(commitment.linesOrigin || commitment.origin) && <> · {commitment.linesOrigin || commitment.origin}</>}
                    {commitment.customerName && <> · 🔒 {commitment.customerName}</>}
                    {commitment.destination && <> → {commitment.destination}</>}
                    {commitment.expectedAt && <> · {formatDateTime(commitment.expectedAt, locale)}</>}
                </p>
                <ul className="mt-2 list-disc pl-5">
                    {commitment.lines.map((line) => (
                        <li key={line.id}>
                            {line.productName} · {line.productVariantName}: {formatQuantity(line.expectedQuantity, locale)} <UnitBadge unit={line.expectedUnit} />
                            {palletBreakdownLabel(line, line.expectedUnit, t, locale) !== null && <> ({palletBreakdownLabel(line, line.expectedUnit, t, locale)})</>}
                            {line.lotNumber && <> · {t('supply_lot')} {line.lotNumber}</>}
                            {lineKgLabel({ netWeightKg: line.expectedNetWeightKg, grossWeightKg: line.expectedGrossWeightKg, weightSource: line.weightSource }, t, locale) !== null
                                && <> · {lineKgLabel({ netWeightKg: line.expectedNetWeightKg, grossWeightKg: line.expectedGrossWeightKg, weightSource: line.weightSource }, t, locale, true)}</>}
                        </li>
                    ))}
                </ul>
            </SuccessPanel>
        );
    }

    const e = (key: string) => errors[key];

    return (
        <form
            ref={leave.ref}
            noValidate
            aria-busy={submitting}
            onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className={`space-y-4 rounded-md border bg-white p-4 ${isDirect ? 'border-2 border-violet-300' : 'border-gray-300'}`}
        >
            {/* Schone start: zonder leverancier (en bij klantgebonden aanvoer klant) zegt het formulier wat eerst moet. Een artikel kan ook hier via "+ Nieuw artikel". */}
            <MissingMasterDataNotice missing={missingMasterData({ vendors: reference.vendors, customers: reference.customers, products }, isDirect ? ['vendor', 'customer', 'article'] : ['vendor', 'article'])} />
            {isDirect && (
                <p className="flex items-center gap-2 rounded border border-violet-300 bg-violet-50 px-3 py-2 text-sm font-medium text-violet-900">
                    <span aria-hidden="true">🔒</span>{t('supply_direct_banner')}
                </p>
            )}

            {error && editConflict(error) !== null ? (
                <div role="alert" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900" data-testid="po-edit-conflict">
                    <p>{t(editConflict(error)!)}</p>
                    <button type="button" className={`${secondaryButtonClass} mt-2`} onClick={() => window.location.reload()}>{t('po_edit_reload')}</button>
                </div>
            ) : error && (
                <ErrorNotice error={error} actions={outcomeUnknown(error) ? (
                    <>
                        <button type="submit" className={primaryButtonClass} disabled={submitting}>{t('supply_retry_same')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('supply_discard_and_restart')}</button>
                    </>
                ) : undefined} />
            )}

            {/* Leverancier en haar ordernummer eerst (Tab: Leverancier, Ordernummer leverancier, Besteldatum, Verwacht, Herkomst); de partij daarna. */}
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <div className="col-span-2">
                    <label htmlFor="vendorId-input" className="text-xs font-medium text-gray-700">{t('supply_vendor')} *</label>
                    <VendorPicker id="vendorId" dataTour="supply-vendor" ariaLabel={t('supply_vendor')} vendors={reference.vendors} value={header.vendorId} disabled={locked || edit?.vendorFixed === true}
                        invalid={!!e('vendorId')} describedBy={e('vendorId') ? 'vendorId' : undefined}
                        onChange={chooseVendor} />
                    <FieldError id="vendorId" messages={e('vendorId')} />
                    {edit?.vendorFixed && <span className="block text-xs text-gray-700" data-testid="po-edit-vendor-fixed">{t('po_edit_vendor_fixed')}</span>}
                </div>
            </div>

            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <div className="col-span-2">
                    <label htmlFor="supplierReference-input" className="text-xs font-medium text-gray-700">{t('po_supplier_reference')}</label>
                    <input {...fieldProps('supplierReference', e('supplierReference'))} data-tour="supply-reference" value={header.supplierReference} disabled={locked} maxLength={MAX_SUPPLIER_REFERENCE_LENGTH}
                        onChange={(event) => updateHeader('supplierReference', event.target.value)} className={`${inputClass} font-mono`} aria-describedby="supplierReference-hint" />
                    <span id="supplierReference-hint" className="block text-xs text-gray-500">{t('po_supplier_reference_hint')}</span>
                    <FieldError id="supplierReference" messages={e('supplierReference')} />
                    <SupplierReferenceNotice supplierReference={header.supplierReference} vendorId={header.vendorId === '' ? null : Number(header.vendorId)} excludeOrderId={edit?.orderId ?? null} />
                </div>
                <div>
                    <label htmlFor="orderedOn-input" className="text-xs font-medium text-gray-700">{t('po_ordered_on')}</label>
                    <DateField {...fieldProps('orderedOn', e('orderedOn'))} dataTour="supply-ordered-on" value={header.orderedOn} disabled={locked}
                        onChange={(value) => updateHeader('orderedOn', value)} />
                    <FieldError id="orderedOn" messages={e('orderedOn')} />
                </div>
                <div>
                    <label htmlFor="expectedAt-input" className="text-xs font-medium text-gray-700">{t('supply_expected_date')} *</label>
                    <DateField {...fieldProps('expectedAt', e('expectedAt'))} dataTour="supply-expected" value={header.expectedDate} disabled={locked}
                        onChange={(value) => updateHeader('expectedDate', value)} />
                    <FieldError id="expectedAt" messages={e('expectedAt')} />
                </div>
            </div>

            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <div>
                    <label htmlFor="originCountryCode-input" className="text-xs font-medium text-gray-700">{t('supply_origin')}</label>
                    {countries === null
                        ? <p className="mt-1 text-xs text-gray-600">{t('supply_open_loading')}</p>
                        : <CountryPicker id="originCountryCode" dataTour="supply-origin" countries={countries} value={header.originCountryCode} onChange={updateOrigin}
                            disabled={locked} invalid={(e('originCountryCode') ?? e('origin')) !== undefined} describedBy="origin-hint" />}
                    <span id="origin-hint" className="block text-xs text-gray-500">{t('supply_origin_hint')}</span>
                    {header.legacyOrigin !== null && header.originCountryCode === null && (
                        <span className="block text-xs text-amber-800" data-testid="supply-origin-legacy">{t('supply_origin_legacy', { text: header.legacyOrigin })}</span>
                    )}
                    <FieldError id="originCountryCode" messages={[...(e('originCountryCode') ?? []), ...(e('origin') ?? [])]} />
                </div>
                {/* Valuta-ronde: valuta en koers in de kop (na de vaste volgorde Leverancier, Ordernummer, Besteldatum, Verwacht, Herkomst). */}
                <div className="col-span-2 md:col-span-3">
                    <DocumentCurrencyField value={money} onChange={updateMoney} documentDate={header.orderedOn} currencies={currencies} disabled={locked}
                        fetchOnOpen={editing === null} errors={{ currency: e('currency') ?? [], exchangeRate: e('exchangeRate') ?? [] }} />
                    {currenciesFailed && (
                        <p role="alert" className="mt-1 text-xs text-amber-900" data-testid="currency-list-failed">
                            {t('currency_list_failed')}{' '}
                            <button type="button" className="underline underline-offset-2" onClick={() => setCurrenciesAttempt((n) => n + 1)}>{t('currency_list_retry')}</button>
                        </p>
                    )}
                </div>
            </div>

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4">
                <div className="sm:col-span-1 md:col-span-2">
                    <PartijField value={partij} suggestion={suggestion} exists={partijExists} disabled={locked || edit?.partijFixed === true} errors={e('partij') ?? e('newPartij.number') ?? e('partijId')}
                        onChange={updatePartij} onExists={setPartijExists} />
                    {edit?.partijFixed && <span className="block text-xs text-gray-700" data-testid="po-edit-partij-fixed">{t('po_edit_partij_fixed')}</span>}
                </div>
                <div>
                    <label htmlFor="supplierLotReference-input" className="text-xs font-medium text-gray-700">{t('partij_supplier_lot')}</label>
                    <input {...fieldProps('supplierLotReference', e('supplierLotReference'))} data-tour="supply-supplier-lot" value={header.supplierLotReference} disabled={locked}
                        maxLength={MAX_SUPPLIER_LOT_REFERENCE_LENGTH} onChange={(event) => updateHeader('supplierLotReference', event.target.value)} className={inputClass}
                        aria-describedby="supplierLotReference-hint" />
                    <span id="supplierLotReference-hint" className="block text-xs text-gray-500">{t('partij_supplier_lot_hint')}</span>
                    <FieldError id="supplierLotReference" messages={e('supplierLotReference')} />
                </div>
                {!isDirect && (
                    <div>
                        <label htmlFor="note-input" className="text-xs font-medium text-gray-700">{t('po_note')}</label>
                        <input {...fieldProps('note', e('note'))} data-tour="supply-note" value={header.note} disabled={locked} maxLength={MAX_NOTE_LENGTH}
                            onChange={(event) => updateHeader('note', event.target.value)} className={inputClass} aria-describedby="note-hint" />
                        <span id="note-hint" className="block text-xs text-gray-500">{t('po_note_hint')}</span>
                        <FieldError id="note" messages={e('note')} />
                    </div>
                )}
            </div>

            {isDirect && (
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                    <div>
                        <label htmlFor="customerId-input" className="text-xs font-medium text-violet-900">{t('supply_customer')} *</label>
                        <CustomerPicker id="customerId" customers={reference.customers} value={header.customerId} disabled={locked}
                            invalid={!!e('customerId')} describedBy={e('customerId') ? 'customerId' : undefined}
                            onChange={(customer) => updateHeader('customerId', customer === null ? '' : String(customer.id))} />
                        <FieldError id="customerId" messages={e('customerId')} />
                    </div>
                    <div className="col-span-2">
                        <label htmlFor="destination-input" className="text-xs font-medium text-violet-900">{t('supply_destination')} *</label>
                        <input {...fieldProps('destination', e('destination'))} value={header.destination} disabled={locked} maxLength={200}
                            placeholder={t('supply_destination_placeholder')}
                            onChange={(event) => updateHeader('destination', event.target.value)} className={inputClass} />
                        <FieldError id="destination" messages={e('destination')} />
                    </div>
                </div>
            )}

            <fieldset className="space-y-2">
                <legend className="text-sm font-semibold text-gray-900">{t('supply_lines')}</legend>
                <FieldError id="lines" messages={e('lines')} />
                <PurchaseOrderLines currency={money.currency} rate={effectiveRate(money)} lines={lines} rows={lineRows} products={products} variantOf={(variantId) => variants.get(variantId)} locked={locked} isDirect={isDirect}
                    errors={e} onChange={updateLine} onAdd={addLine} onRemove={removeLine} onNewArticle={setNewArticleFor} limits={limitsOf} ggnProposalOf={ggnProposalOf}
                    productExtra={(line, index) => (newArticleFor === line.key ? (
                        <NewSupplyArticle index={index} products={products} currentVariantId={line.productVariantId} onClose={() => setNewArticleFor(null)}
                            onUse={(variantId, created) => {
                                if (created !== null) setProducts((current) => withArticle(current, created));
                                updateLine(line.key, { productVariantId: String(variantId) });
                                setNewArticleFor(null);
                            }} />
                    ) : !locked && !limitsOf(line.key).articleFixed && (
                        <button type="button" tabIndex={-1} className="mt-1 text-xs font-medium text-blue-800 underline underline-offset-2" onClick={() => setNewArticleFor(line.key)}
                            title={`${t('supply_new_article_open')} (${shortcutLabel('newArticle', isMac)})`} data-tour="supply-line-new-article" data-tour-key={index}>+ {t('supply_new_article_open')}</button>
                    ))} />
                <button type="button" tabIndex={-1} onClick={addLine} disabled={locked || lines.length >= MAX_LINES} className={secondaryButtonClass}>
                    + {t('supply_add_line')}
                </button>
            </fieldset>

            <div className="flex flex-wrap items-center gap-3 border-t border-gray-200 pt-3">
                <button type="submit" data-tour="supply-save" className={primaryButtonClass} disabled={locked}>
                    {submitting ? t('supply_saving') : editing !== null ? t('po_edit_save') : isDirect ? t('supply_save_plan_direct') : t('supply_save_plan_warehouse')}
                </button>
                <SubmitErrors errors={errors} />
                {/* Zacht: zonder partij kan de order gewoon worden opgeslagen; een partij kiezen kan later op de order of bij ontvangst. */}
                {partij.kind === 'none' && !locked && (
                    <p role="status" className="text-sm text-amber-900" data-tour="supply-partij-none">{t('partij_none_warning')}</p>
                )}
            </div>
            {certificateCheck !== null && <CertificateCheckDialog checks={certificateCheck} onNo={declineCertificates} onYes={confirmCertificates} />}
        </form>
    );
}

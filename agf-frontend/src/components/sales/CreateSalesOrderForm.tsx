'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate, isoToDateInput } from '../../lib/calendarDate';
import { formatAmountInput, formatDecimalInput, formatKgInput, parsePositiveDecimal } from '../../lib/decimal';
import { CREATE_CUSTOMER_KEY, CREATE_ORDER_KEY, CreateCustomerCommand, CreateOrderCommand, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { unitLabelKey } from '../../lib/units';
import { MAX_ORDER_LINES, MAX_SALES_PARTIJ_NUMBER_LENGTH, OrderDraft, OrderLineDraft, buildOrderPayload, emptyOrderLine, isBlankOrderLine, remapPayloadLineKeys, salesPartijPayload } from '../../lib/salesOrderRules';
import { purchaseLineRow } from '../../lib/purchaseOrderLines';
import { partijNumberKey } from '../../lib/partijRules';
import { findPartijByNumber, getNextPartijNumber } from '../../services/partijService';
import { isPartijExistsConflict } from '../../types/partij';
import { type SellPrefill, orderLabel, singleSource } from '../../lib/salesSupply';
import type { SupplySource } from '../../types/salesSupply';
import LineSourceChoice from './LineSourceChoice';
import { useSalesBalances } from './useSalesBalances';
import { ApiError, toApiError } from '../../services/apiClient';
import { createSalesOrder } from '../../services/salesOrderService';
import { listPackagingArticles } from '../../services/costArticleService';
import { packagingDraftFrom, palletProposal } from '../../lib/packaging';
import type { CostArticle } from '../../types/costArticle';
import { PackagingLinesInput, PackagingLinesView } from './PackagingLines';
import { SaveResult } from '../../services/supplyService';
import { CreatedCustomer, CustomerOption, ProductOption } from '../../types/referenceData';
import { SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import DateField from '../shared/DateField';
import { lineKgLabel } from '../shared/lineKgLabel';
import ArticleAddButton from '../shared/ArticleAddButton';
import { isMacPlatform, shortcutLabel } from '../../lib/shortcuts';
import NewArticlePanel from '../articles/NewArticlePanel';
import { withArticle } from '../../lib/articleFromEntry';
import type { Article } from '../../types/article';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import PurchaseOrderLines, { SALES_LINE_FIELDS } from '../supply/PurchaseOrderLines';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import SuccessPanel, { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import CustomerPicker from './CustomerPicker';
import NewCustomerPanel from './NewCustomerPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import SubmitErrors from '../shared/SubmitErrors';
import { useFormLeaveGuard } from '../shared/LeaveGuard';

// Een regel is pas leeg als geen enkel inhoudelijk veld is ingevuld (ook prijs, korting, bruto en een gekozen bron).
const isBlankLine = isBlankOrderLine;

const emptyLine = (): OrderLineDraft => emptyOrderLine(newRequestId());
const emptyDraft = (): OrderDraft => ({ customerId: '', customerReference: '', internalNote: '', requestedDeliveryDate: '', lines: [emptyLine()], packagingLines: [] });
// Voorinvulling vanuit "Verkopen" in Beschikbaarheid: één regel met dit artikel, deze eenheid en dit aantal; de rest kiest de gebruiker.
const prefilledDraft = (prefill: SellPrefill): OrderDraft => ({
    ...emptyDraft(),
    lines: [{ ...emptyLine(), productVariantId: String(prefill.productVariantId), unit: prefill.unit, quantity: formatDecimalInput(prefill.quantity) }],
});

// Een onafgerond commando wordt als (vergrendeld) concept getoond; verstuurd wordt altijd de bevroren payload zelf.
const draftFromCommand = (command: CreateOrderCommand): OrderDraft => ({
    customerId: String(command.payload.customerId),
    customerReference: command.payload.customerReference ?? '',
    internalNote: command.payload.internalNote ?? '',
    requestedDeliveryDate: isoToDateInput(command.payload.requestedDeliveryDate) ?? '',
    lines: command.payload.lines.map((line): OrderLineDraft => ({
        ...emptyLine(), productVariantId: String(line.productVariantId), quantity: formatDecimalInput(line.quantity), unit: line.unit,
        supplySource: line.supplySource ?? '', sourceTouched: line.supplySource !== undefined,
        unitPrice: line.unitPrice == null ? '' : formatAmountInput(line.unitPrice),
        discount: line.discountPercentage == null ? '' : formatDecimalInput(line.discountPercentage, 2),
        vat: line.vatPercentage == null ? '' : String(line.vatPercentage), vatTouched: true,
        // Ingevulde netto en aangepaste bruto als ingevuld terugzetten (ook bij opdrachten van vóór de nieuwe regel); berekende kg rekent het
        // (vergrendelde) concept zelf opnieuw uit.
        ...(line.weightSource === 'Entered' && line.netWeightKg != null ? { netKg: formatKgInput(line.netWeightKg), netKgEntered: true } : {}),
        ...(line.weightSource === 'Entered' && line.grossWeightKg != null ? { grossKg: formatKgInput(line.grossWeightKg), grossKgEntered: true } : {}),
    })).concat(command.payload.lines.length === 0 ? [emptyLine()] : []),
    packagingLines: (command.payload.packagingLines ?? []).map((line) => packagingDraftFrom(line, newRequestId())),
});

// Nieuwe verkooporder via POST /api/sales-orders. Bestaande klant, leverdatum en de regels in de indeling van de inkooporder (feedback
// 22 november): artikel, aantal, eenheid en kg uit het artikel, prijs, korting, btw en bedrag, totalen en btw per tarief eronder; per regel
// daarbij de stand en de bron (uit voorraad of uit verwachte aanvoer). De payload en requestId worden bij versturen bevroren en boven dit
// formulier bewaard: bij een onbekende uitkomst is alleen exact dezelfde herhaling mogelijk, ook na weg- en terugnavigeren.
export default function CreateSalesOrderForm({ customers: knownCustomers, products: givenProducts, prefill = null, onSaved, onOpen, onCancel, onCustomerCreated }: {
    prefill?: SellPrefill | null;
    customers: CustomerOption[];
    products: ProductOption[];
    onSaved: (order: SalesOrder) => void;
    onOpen: (orderId: number) => void;
    onCancel: () => void;
    // Na een nieuwe klant: de pagina ververst de klantenlijst op de achtergrond zonder dit formulier te hermonteren.
    onCustomerCreated?: (customer: CreatedCustomer) => void;
}) {
    const { t, locale } = useTranslation();
    // Artikelen die in het zijpaneel "Nieuw artikel" zijn aangemaakt (feedback 22 november), bij de lijst van de pagina opgeteld.
    const [addedArticles, setAddedArticles] = useState<Article[]>([]);
    const products = useMemo(() => addedArticles.reduce<ProductOption[]>((current, article) => withArticle(current, article), givenProducts), [givenProducts, addedArticles]);
    const [newArticleFor, setNewArticleFor] = useState<string | null | undefined>(undefined);
    const [pending, setPending, clearPending] = usePendingCommand<CreateOrderCommand>(CREATE_ORDER_KEY);
    const { commands } = usePendingCommands();
    // Nieuwe klant: paneel naast de klantkeuze. Een onafgeronde klantopdracht opent het paneel altijd (ook na sluiten,
    // navigeren of verversen) en kan niet door een tweede worden vervangen. Zojuist aangemaakte klanten staan direct in de keuze.
    const pendingCustomer = (commands[CREATE_CUSTOMER_KEY] as CreateCustomerCommand | undefined) ?? null;
    const [customerPanelOpen, setCustomerPanelOpen] = useState(false);
    // Getypte naam uit de klantkeuze bij "Nieuwe klant" zonder treffer (alleen voorinvulling).
    const [newCustomerName, setNewCustomerName] = useState('');
    const [createdCustomers, setCreatedCustomers] = useState<CustomerOption[]>([]);
    const [customerNotice, setCustomerNotice] = useState<string | null>(null);
    const showCustomerPanel = customerPanelOpen || pendingCustomer !== null;
    const customers = useMemo(() => {
        const extra = createdCustomers.filter((created) => !knownCustomers.some((known) => known.id === created.id));
        return extra.length === 0 ? knownCustomers : [...knownCustomers, ...extra].sort((a, b) => a.name.localeCompare(b.name));
    }, [knownCustomers, createdCustomers]);
    const [draft, setDraft] = useState<OrderDraft>(() => (pending !== null ? draftFromCommand(pending) : prefill !== null ? prefilledDraft(prefill) : emptyDraft()));
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    // Fixes 5b: weggaan met een fout of niet-opgeslagen wijzigingen vraagt eerst (LeaveGuard).
    const leave = useFormLeaveGuard(Object.keys(errors).length > 0);
    const [result, setResult] = useState<SaveResult<SalesOrder> | null>(null);
    const inFlight = useRef(false);
    // Verkoop-partij: het voorstel van de server (null in modus Eigen of zonder antwoord) en wat de gebruiker typt (null = niet
    // aangeraakt, het voorstel staat in het veld). Een getypt nummer dat al bestaat, staat als melding bij het veld.
    const [partijNext, setPartijNext] = useState<{ suggestion: string | null; mode: 'Auto' | 'Own' } | null>(null);
    const [partijText, setPartijText] = useState<string | null>(() => pending?.payload.salesPartijNumber ?? null);
    const [partijExists, setPartijExists] = useState<string | null>(null);
    const [partijToken, setPartijToken] = useState(0);
    // Volgnummer van de laatste wijziging/controle: een antwoord van een oudere controle wordt genegeerd.
    const partijCheckSeq = useRef(0);

    // Vergrendeld zolang er een onafgerond commando is: nooit een gewijzigde retry, nooit een tweede order ernaast.
    const locked = submitting || result !== null || pending !== null;

    // Emballage-artikelen, ook inactieve: een bevroren regel met een intussen inactief artikel toont dat artikel; kiezen kan alleen uit actieve.
    const [packagingArticles, setPackagingArticles] = useState<CostArticle[]>([]);
    const [packagingFailed, setPackagingFailed] = useState(false);
    const [packagingToken, setPackagingToken] = useState(0);
    useEffect(() => {
        const controller = new AbortController();
        setPackagingFailed(false);
        listPackagingArticles(true, controller.signal).then(setPackagingArticles).catch(() => { if (!controller.signal.aborted) setPackagingFailed(true); });
        return () => controller.abort();
    }, [packagingToken]);

    useEffect(() => {
        const controller = new AbortController();
        getNextPartijNumber({ kind: 'Sales' }, controller.signal)
            .then((next) => setPartijNext({ suggestion: next.suggestion, mode: next.mode }))
            .catch(() => { if (!controller.signal.aborted) setPartijNext(null); });
        return () => controller.abort();
    }, [partijToken]);
    const partijSuggestion = partijNext?.suggestion ?? null;
    const partijValue = partijText ?? partijSuggestion ?? '';

    // Dubbelcontrole bij het verlaten van het veld (alleen een eigen nummer; het voorstel is vrij en de server kiest bij een botsing het
    // volgende). De server controleert bij opslaan opnieuw.
    const checkPartij = async () => {
        const typed = (partijText ?? '').trim();
        if (typed === '' || typed === partijSuggestion || partijNumberKey(typed) === null) return;
        const seq = ++partijCheckSeq.current;
        try {
            const existing = await findPartijByNumber(typed, undefined, 'Sales');
            if (seq === partijCheckSeq.current && existing !== null) setPartijExists(existing.number);
        } catch {
            // Geen antwoord: bij opslaan volgt dezelfde controle.
        }
    };

    // Het artikel per variant: eenheid, kg per verpakking, colli per pallet en btw komen daaruit (zoals bij de inkooporder).
    const variants = useMemo(() => new Map(products.flatMap((product) => product.variants.map((variant) => [variant.id, variant] as const))), [products]);
    const variantOf = (variantId: number) => variants.get(variantId);

    const edited = () => {
        setError(null);
        setErrors({});
    };

    const updateHeader = (field: 'customerId' | 'customerReference' | 'internalNote' | 'requestedDeliveryDate', value: string) => {
        if (locked) return;
        setDraft((current) => ({ ...current, [field]: value }));
        edited();
    };

    const updateLine = (key: string, patch: Partial<OrderLineDraft>) => {
        if (locked) return;
        setDraft((current) => ({ ...current, lines: current.lines.map((line) => (line.key === key ? { ...line, ...patch } : line)) }));
        edited();
    };

    // Per regel wat de inkoopregel ook rekent: aantal, eenheid (uit het artikel), kg, pallets, prijs, korting, btw en bedrag.
    const rows = draft.lines.map((line) => purchaseLineRow(line, /^[1-9]\d*$/.test(line.productVariantId) ? variantOf(Number(line.productVariantId)) : undefined));
    // "Pallet toevoegen": de pallets van de goederenregels samen, naar boven afgerond (alleen een voorstel).
    const proposal = palletProposal(rows.map((row) => ({ quantity: row.quantity, packagesPerPallet: row.perPallet })));

    // Stand per gekozen variant + eenheid (Voorraad · Verwacht · Vrij) en de bron per regel. Zolang de gebruiker niet zelf kiest, volgt de
    // bron het voorstel: uit voorraad als het past, uit verwachte aanvoer als er niets vrij is; past maar een deel, dan het splitsvoorstel.
    const unitOf = (index: number) => rows[index]?.unit ?? '';
    const balancePairs = useMemo(() => draft.lines.map((line) => ({ line, unit: purchaseLineRow(line, variants.get(Number(line.productVariantId))).unit }))
        .filter(({ line, unit }) => /^[1-9]\d*$/.test(line.productVariantId) && unit !== '')
        .map(({ line, unit }) => ({ productVariantId: Number(line.productVariantId), unit: unit as Exclude<OrderLineDraft['unit'], ''> })), [draft.lines, variants]);
    const balanceOf = useSalesBalances(balancePairs);
    const stateOf = (line: OrderLineDraft, index: number) => {
        const unit = unitOf(index);
        return /^[1-9]\d*$/.test(line.productVariantId) && unit !== '' ? balanceOf(Number(line.productVariantId), unit) : null;
    };
    // null: geen eigen keuze en de stand is (nog) niet bekend, of het past maar deels in de vrije voorraad (dan staat het splitsvoorstel
    // erbij). Er gaat dan geen bron mee en de server kiest: uit voorraad als het past, anders uit verwachte aanvoer (nooit een 409).
    const effectiveSource = (line: OrderLineDraft, index: number): SupplySource | null => {
        const quantity = rows[index]?.quantity ?? null;
        if (line.sourceTouched && line.supplySource) return line.supplySource;
        const state = stateOf(line, index);
        if (state?.status !== 'ready' || quantity === null) return null;
        return singleSource(quantity, Math.max(0, state.balance.freeStock));
    };

    const chooseSource = (key: string, source: SupplySource) => {
        if (locked) return;
        setDraft((current) => ({ ...current, lines: current.lines.map((line) => (line.key === key ? { ...line, supplySource: source, sourceTouched: true } : line)) }));
        edited();
    };

    // Splitsen: een aangepaste bruto wordt gewist (opnieuw berekend uit het nieuwe aantal; een getypt totaal klopt na het splitsen niet meer).
    // Een aangepaste netto wordt naar verhouding van het aantal over beide regels verdeeld.
    // Deze regel wordt het deel uit voorraad, direct eronder een regel met hetzelfde artikel, dezelfde eenheid, prijs, korting en btw voor
    // de rest uit verwachte aanvoer.
    const splitLine = (key: string, stock: number, expected: number) => {
        if (locked || draft.lines.length >= MAX_ORDER_LINES) return;
        setDraft((current) => ({
            ...current,
            lines: current.lines.flatMap((line, index) => {
                if (line.key !== key) return [line];
                // Een zelf ingevoerd netto gewicht (artikel zonder standaard) gaat niet verloren: naar verhouding over beide regels.
                let stockNet: string | null = null;
                let expectedNet: string | null = null;
                const enteredNet = line.netKgEntered === true ? parsePositiveDecimal(line.netKg ?? '') : null;
                if (enteredNet?.ok && stock + expected > 0) {
                    const stockPart = Math.round((enteredNet.value * stock / (stock + expected)) * 1000) / 1000;
                    const expectedPart = Math.round((enteredNet.value - stockPart) * 1000) / 1000;
                    if (stockPart > 0 && expectedPart > 0) {
                        stockNet = formatKgInput(stockPart);
                        expectedNet = formatKgInput(expectedPart);
                    }
                }
                return [
                    { ...line, quantity: formatDecimalInput(stock), grossKg: '', grossKgEntered: false, netKg: stockNet ?? '', netKgEntered: stockNet !== null, supplySource: 'Stock' as const, sourceTouched: true },
                    { ...emptyLine(), productVariantId: line.productVariantId, unit: unitOf(index) as OrderLineDraft['unit'], quantity: formatDecimalInput(expected), unitPrice: line.unitPrice,
                        discount: line.discount, vat: line.vat, vatTouched: line.vatTouched, supplySource: 'Expected' as const, sourceTouched: true,
                        ...(expectedNet !== null ? { netKg: expectedNet, netKgEntered: true } : {}) },
                ];
            }),
        }));
        edited();
    };

    const addLine = () => {
        if (locked || draft.lines.length >= MAX_ORDER_LINES) return;
        setDraft((current) => ({ ...current, lines: [...current.lines, emptyLine()] }));
        edited();
    };

    // Een artikel uit de lijst "Artikel toevoegen" of uit het zijpaneel: op de regel waar de gebruiker was, anders op een lege laatste
    // regel, anders op een nieuwe; de eenheid van het artikel vult een lege eenheid voor. Daarna de cursor naar Aantal.
    const focusQuantityOf = useRef<string | null>(null);
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    // Review #314: op de limiet kan de lijst nog een lege laatste regel vullen.
    const lastLine = draft.lines[draft.lines.length - 1];
    const lastLineBlank = lastLine !== undefined && isBlankLine(lastLine);
    const putArticle = (variantId: number, lineKey: string | null) => {
        if (locked) return;
        const last = draft.lines[draft.lines.length - 1];
        const target = lineKey !== null && draft.lines.some((line) => line.key === lineKey) ? lineKey : last !== undefined && isBlankLine(last) ? last.key : null;
        if (target === null && draft.lines.length >= MAX_ORDER_LINES) return;
        const fresh = target === null ? emptyLine() : null;
        // Eenheid en btw komen uit het (nieuwe) artikel, zoals bij een keuze op de regel.
        const withVariant = (line: OrderLineDraft): OrderLineDraft => ({ ...line, productVariantId: String(variantId), grossKg: '', grossKgEntered: false, netKg: '', netKgEntered: false, unit: '', vat: '', vatTouched: false });
        setDraft((current) => ({ ...current, lines: fresh !== null ? [...current.lines, withVariant(fresh)] : current.lines.map((line) => (line.key === target ? withVariant(line) : line)) }));
        focusQuantityOf.current = fresh?.key ?? target;
        edited();
    };
    useEffect(() => {
        if (focusQuantityOf.current === null) return;
        const index = draft.lines.findIndex((line) => line.key === focusQuantityOf.current);
        focusQuantityOf.current = null;
        if (index >= 0) setTimeout(() => document.getElementById(`lines[${index}].quantity-input`)?.focus(), 0);
    }, [draft.lines]);

    const removeLine = (key: string) => {
        if (locked || draft.lines.length <= 1) return;
        setDraft((current) => ({ ...current, lines: current.lines.filter((line) => line.key !== key) }));
        edited();
    };

    const submit = async () => {
        if (inFlight.current || result !== null) return;
        let command = pending;
        if (command === null) {
            const withSources: OrderDraft = { ...draft, lines: draft.lines.map((line, index): OrderLineDraft => ({ ...line, supplySource: effectiveSource(line, index) ?? '' })) };
            const built = buildOrderPayload(withSources, variantOf);
            const partij = salesPartijPayload(partijText, partijSuggestion, partijNext?.mode ?? 'Auto');
            if (!built.ok || !partij.ok) {
                setErrors(ruleErrorsToFieldErrors([...(built.ok ? [] : built.errors), ...(partij.ok ? [] : partij.errors)], t));
                return;
            }
            if (partijExists !== null) return;
            setErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'create-order',
                key: CREATE_ORDER_KEY,
                requestId,
                payload: { requestId, ...built.value, ...(partij.value === null ? {} : { salesPartijNumber: partij.value }) },
                display: { customerName: customers.find((customer) => customer.id === built.value.customerId)?.name ?? String(built.value.customerId) },
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
            const saved = await createSalesOrder(command.payload);
            clearPending();
            leave.saved();
            setResult(saved);
            onSaved(saved.value);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                // Uitkomst onbekend: het commando blijft bewaard; alleen exact dezelfde herhaling is mogelijk.
                setPending(withAttempt(command, apiError.message) as CreateOrderCommand);
            } else {
                // Definitief antwoord van de server (400/404/409): er is niets opgeslagen; de invoer mag weer wijzigen.
                clearPending();
            }
            // Het getypte verkoop-partijnummer bestaat al (409 PARTIJ_EXISTS): melding bij het veld in plaats van de servertekst.
            if (isPartijExistsConflict(apiError.body)) {
                setPartijExists(apiError.body.partij.number);
            } else {
                setError(apiError);
            }
            setErrors(remapPayloadLineKeys(apiError.fieldErrors, draft));
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const onCustomerSaved = (customer: CreatedCustomer, replayed: boolean) => {
        setCreatedCustomers((current) => (current.some((c) => c.id === customer.id) ? current : [...current, { id: customer.id, name: customer.name, searchCode: null, isActive: true }]));
        // Alleen de klantkeuze verandert; regels, datum en referentie van het concept blijven staan.
        setDraft((current) => ({ ...current, customerId: String(customer.id) }));
        setCustomerPanelOpen(false);
        setCustomerNotice(`${t('sales_customer_created', { name: customer.name })}${replayed ? ` ${t('supply_saved_before')}` : ''}`);
        edited();
        onCustomerCreated?.(customer);
    };

    const chooseExisting = (customerId: number) => {
        setDraft((current) => ({ ...current, customerId: String(customerId) }));
        setCustomerPanelOpen(false);
        setCustomerNotice(null);
        edited();
    };

    const startOver = () => {
        setDraft(emptyDraft());
        partijCheckSeq.current++;
        setPartijText(null);
        setPartijExists(null);
        setPartijToken((n) => n + 1);
        setError(null);
        setErrors({});
        setResult(null);
    };

    const e = (key: string) => errors[key];

    const euro = (value: number) => new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { style: 'currency', currency: 'EUR' }).format(value);

    if (result !== null) {
        const order = result.value;
        return (
            <SuccessPanel
                title={t('sales_create_success', { id: orderLabel(order), customer: order.customerName ?? '' })}
                note={[order.salesPartijNumber ? t('sales_created_partij', { number: order.salesPartijNumber }) : null, result.replayed ? t('supply_saved_before') : null]
                    .filter((part) => part !== null).join(' ') || undefined}
                actions={(
                    <>
                        <button type="button" data-tour="sales-open-saved" className={primaryButtonClass} onClick={() => onOpen(order.id)}>{t('sales_reserve_for_this_order')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('sales_another_order')}</button>
                    </>
                )}
            >
                <p>
                    <span className="font-mono font-semibold" data-tour="sales-saved-number">{orderLabel(order)}</span> ·{' '}
                    {t('sales_delivery_on', { date: formatCalendarDate(order.requestedDeliveryDate, locale) })}
                    {order.customerReference && <> · {t('sales_reference_short')} {order.customerReference}</>}
                </p>
                {/* Meer verkocht dan voorraad + verwachte aanvoer: toegestaan, met deze korte waarschuwing per artikel; de order is vastgelegd. */}
                {(order.shortageWarnings ?? []).map((warning) => (
                    <p key={`${warning.productVariantId}:${warning.unit}`} role="alert" className="mt-2 rounded border border-red-300 bg-red-50 px-3 py-2 font-medium text-red-900" data-testid="sales-shortage-warning" data-tour="sales-shortage-warning">
                        ⚠ {t('sales_shortage_saved', { quantity: formatQuantity(warning.shortageQuantity, locale), unit: t(unitLabelKey(warning.unit)).toLowerCase(), name: `${warning.productName ?? ''} · ${warning.variantName ?? ''}` })}
                    </p>
                ))}
                {/* Kredietlimiet overschreden: waarschuwing met limiet, openstaand en verschil; de order is gewoon vastgelegd. */}
                {order.creditWarning && (
                    <div role="alert" className="mt-2 rounded border border-amber-400 bg-amber-50 px-3 py-2 text-amber-950" data-testid="sales-credit-warning">
                        <p className="font-medium">⚠ {t('sales_credit_warning_title')}</p>
                        <p className="mt-1">{t('sales_credit_warning_amounts', { limit: euro(order.creditWarning.creditLimit), outstanding: euro(order.creditWarning.outstanding), difference: euro(order.creditWarning.difference) })}</p>
                        {!order.creditWarning.isComplete && order.creditWarning.unpricedSalesOrdersCount > 0 && (
                            <p className="mt-1 text-xs">{t(order.creditWarning.unpricedSalesOrdersCount === 1 ? 'relations_credit_unpriced_one' : 'relations_credit_unpriced', { count: String(order.creditWarning.unpricedSalesOrdersCount) })}</p>
                        )}
                        <p className="mt-1 text-xs">{t('sales_credit_warning_hint')}</p>
                    </div>
                )}
                <ul className="mt-2 list-disc pl-5">
                    {order.lines.map((line) => (
                        <li key={line.id}>{line.productName} · {line.productVariantName}: {formatQuantity(line.quantity, locale)} <UnitBadge unit={line.unit} />{lineKgLabel(line, t, locale) !== null && <> · {lineKgLabel(line, t, locale, true)}</>}</li>
                    ))}
                </ul>
                <PackagingLinesView lines={order.packagingLines ?? []} />
            </SuccessPanel>
        );
    }

    return (
        <form ref={leave.ref} noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-4 rounded-md border border-gray-300 bg-white p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h2 className="text-lg font-semibold text-gray-900">{t('sales_new_order')}</h2>
                    <p className="text-xs text-gray-600">{t('sales_new_order_hint')}</p>
                </div>
                <button type="button" className={secondaryButtonClass} onClick={onCancel} disabled={submitting}>{t('sales_close_form')}</button>
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {error && pending === null && <ErrorNotice error={error} />}

            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <div className="col-span-2">
                    <div className="flex items-center justify-between gap-2">
                        <label htmlFor="customerId-input" className="text-xs font-medium text-gray-700">{t('sales_customer')} *</label>
                        <button type="button" className={`${secondaryButtonClass} px-2 py-1 text-xs`} disabled={locked && pendingCustomer === null}
                            onClick={() => { setCustomerNotice(null); setNewCustomerName(''); setCustomerPanelOpen(true); }} aria-expanded={showCustomerPanel}
                            title={locked && pendingCustomer === null ? t('sales_customer_blocked_by_order') : undefined}>
                            + {t('sales_new_customer')}
                        </button>
                    </div>
                    <CustomerPicker id="customerId" dataTour="sales-customer" customers={customers} value={draft.customerId} disabled={locked}
                        invalid={(e('customerId') ?? []).length > 0} describedBy={(e('customerId') ?? []).length > 0 ? 'customerId' : undefined}
                        onChange={(customer) => updateHeader('customerId', customer === null ? '' : String(customer.id))}
                        newCustomerDisabled={locked && pendingCustomer === null}
                        onNewCustomer={(name) => { setCustomerNotice(null); setNewCustomerName(name); setCustomerPanelOpen(true); }} />
                    <FieldError id="customerId" messages={e('customerId')} />
                    {customerNotice && <p role="status" className="mt-1 rounded border border-green-300 bg-green-50 px-2 py-1 text-xs text-green-900">{customerNotice}</p>}
                    {showCustomerPanel && (
                        <div className="mt-2">
                            <NewCustomerPanel key={newCustomerName} initialName={newCustomerName} customers={customers} onCreated={onCustomerSaved} onChooseExisting={chooseExisting} onClose={() => setCustomerPanelOpen(false)} />
                        </div>
                    )}
                </div>
                <div className="col-span-2">
                    <label htmlFor="requestedDeliveryDate-input" className="text-xs font-medium text-gray-700">{t('sales_delivery_date')} *</label>
                    <DateField {...fieldProps('requestedDeliveryDate', e('requestedDeliveryDate'))} dataTour="sales-delivery" value={draft.requestedDeliveryDate} disabled={locked}
                        onChange={(value) => updateHeader('requestedDeliveryDate', value)} />
                    <FieldError id="requestedDeliveryDate" messages={e('requestedDeliveryDate')} />
                </div>
                <div>
                    <label htmlFor="customerReference-input" className="text-xs font-medium text-gray-700">{t('sales_customer_reference')}</label>
                    <input {...fieldProps('customerReference', e('customerReference'))} data-tour="sales-reference" value={draft.customerReference} disabled={locked} maxLength={100}
                        placeholder={t('sales_customer_reference_placeholder')}
                        onChange={(event) => updateHeader('customerReference', event.target.value)} className={inputClass} />
                    <FieldError id="customerReference" messages={e('customerReference')} />
                </div>
                <div data-tour="sales-partij-block">
                    <label htmlFor="salesPartijNumber-input" className="text-xs font-medium text-gray-700">{t('sales_partij_field')}{partijNext?.mode === 'Own' ? ' *' : ''}</label>
                    <input {...fieldProps('salesPartijNumber', partijExists !== null ? [t('sales_partij_exists', { number: partijExists })] : e('salesPartijNumber'))}
                        data-tour="sales-partij" value={partijValue} disabled={locked} maxLength={MAX_SALES_PARTIJ_NUMBER_LENGTH} aria-describedby="salesPartijNumber-hint"
                        onChange={(event) => { if (locked) return; partijCheckSeq.current++; setPartijText(event.target.value); setPartijExists(null); edited(); }}
                        onBlur={() => { void checkPartij(); }} className={`${inputClass} font-mono`} />
                    <span id="salesPartijNumber-hint" className="block text-xs text-gray-500">{t(partijNext?.mode === 'Own' ? 'sales_partij_hint_own' : 'sales_partij_hint')}</span>
                    <FieldError id="salesPartijNumber" messages={partijExists !== null ? [t('sales_partij_exists', { number: partijExists })] : e('salesPartijNumber')} />
                </div>
                <div className="col-span-2 md:col-span-4">
                    <label htmlFor="internalNote-input" className="text-xs font-medium text-gray-700">{t('internal_note_label')}</label>
                    <input {...fieldProps('internalNote', e('internalNote'))} value={draft.internalNote} disabled={locked} maxLength={1000} placeholder={t('internal_note_placeholder')}
                        onChange={(event) => updateHeader('internalNote', event.target.value)} className={inputClass} />
                    <span className="block text-xs text-gray-500">{t('internal_note_hint')}</span>
                    <FieldError id="internalNote" messages={e('internalNote')} />
                </div>
            </div>

            <fieldset className="space-y-2">
                <legend className="text-sm font-semibold text-gray-900">{t('sales_lines')}</legend>
                <FieldError id="lines" messages={e('lines')} />
                {/* Feedback 22 november: dezelfde regelcomponent als de inkooporder; onder het artikel de stand en de bron van de regel. */}
                <PurchaseOrderLines<OrderLineDraft> lines={draft.lines} rows={rows} products={products} variantOf={variantOf} locked={locked} isDirect={false} withGgn={false}
                    tourPrefix="sales-line" testId="sales-lines" errors={e} fields={SALES_LINE_FIELDS} currency="EUR"
                    onChange={updateLine} onAdd={addLine} onRemove={removeLine} onNewArticle={(key) => setNewArticleFor(key)}
                    productExtra={(line, index) => {
                        const unit = unitOf(index);
                        const unitText = unit === '' ? t('supply_unit').toLowerCase() : t(unitLabelKey(unit)).toLowerCase();
                        // Een bevroren opdracht van vóór de nieuwe regel kan palletinvoer hebben; die staat dan zoals verstuurd bij de regel.
                        const frozen = pending?.payload.lines[index];
                        const frozenPallets = frozen?.palletCount != null && frozen.packagesPerPallet != null ? frozen : null;
                        return (
                            <>
                                {frozenPallets !== null && (
                                    <span className="mt-1 block text-xs text-gray-700" data-testid={`lines[${index}].frozen-pallets`}>
                                        {t('sales_line_frozen_pallets', {
                                            pallets: formatDecimalInput(frozenPallets.palletCount ?? 0), perPallet: formatDecimalInput(frozenPallets.packagesPerPallet ?? 0),
                                            loose: frozenPallets.loosePackages ? t('sales_line_frozen_loose', { loose: formatDecimalInput(frozenPallets.loosePackages) }) : '',
                                        })}
                                    </span>
                                )}
                                {!locked && (
                                    <button type="button" tabIndex={-1} className="mt-1 text-xs font-medium text-blue-800 underline underline-offset-2" onClick={() => setNewArticleFor(line.key)}
                                        title={`${t('supply_new_article_open')} (${shortcutLabel('newArticle', isMac)})`} data-tour="sales-line-new-article" data-tour-key={index}>+ {t('supply_new_article_open')}</button>
                                )}
                                <LineSourceChoice name={`lines[${index}]`} quantity={rows[index]?.quantity ?? null} unitText={unitText} state={stateOf(line, index)}
                                    source={effectiveSource(line, index)} packagesPerPallet={rows[index]?.perPallet ?? null}
                                    locked={locked} canSplit={draft.lines.length < MAX_ORDER_LINES} onSource={(source) => chooseSource(line.key, source)} onSplit={(stock, expected) => splitLine(line.key, stock, expected)} />
                                <FieldError id={`lines[${index}].supplySource`} messages={e(`lines[${index}].supplySource`)} />
                            </>
                        );
                    }} />
                <div className="flex flex-wrap items-start gap-2">
                    <ArticleAddButton products={products} disabled={locked || (draft.lines.length >= MAX_ORDER_LINES && !lastLineBlank)} dataTour="sales-article-add"
                        onPick={(variantId) => putArticle(variantId, null)} onNewArticle={() => setNewArticleFor(null)} />
                    <button type="button" onClick={addLine} disabled={locked || draft.lines.length >= MAX_ORDER_LINES} className={secondaryButtonClass}>
                        + {t('supply_add_line')}
                    </button>
                </div>
                {newArticleFor !== undefined && (
                    <NewArticlePanel onClose={() => setNewArticleFor(undefined)} onSaved={(article) => {
                        setAddedArticles((current) => [...current, article]);
                        putArticle(article.id, newArticleFor);
                        setNewArticleFor(undefined);
                    }} />
                )}
            </fieldset>

            {packagingFailed && (
                <div role="alert" className="flex items-center gap-3 text-sm text-red-700">
                    <span>{t('packaging_load_failed')}</span>
                    <button type="button" onClick={() => setPackagingToken((token) => token + 1)} className={secondaryButtonClass}>{t('packaging_reload')}</button>
                </div>
            )}
            <PackagingLinesInput lines={draft.packagingLines ?? []} articles={packagingArticles} proposal={proposal} locked={locked} errors={e}
                onChange={(packagingLines) => { if (locked) return; setDraft((current) => ({ ...current, packagingLines })); edited(); }} />

            {pending === null && (
                <div className="flex items-center gap-3 border-t border-gray-200 pt-3">
                    <button type="submit" data-tour="sales-save" className={primaryButtonClass} disabled={locked}>
                        {submitting ? t('supply_saving') : t('sales_save_order')}
                    </button>
                    <SubmitErrors errors={errors} />
                </div>
            )}
        </form>
    );
}

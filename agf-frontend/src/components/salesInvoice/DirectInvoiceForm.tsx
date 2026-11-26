'use client';

import { useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { localToday } from '../../lib/calendarDate';
import { formatDecimalInput } from '../../lib/decimal';
import { buildDirectLines, emptyService, formKeyOf, isBlankService, isShownFormKey, serviceDraftAmounts, type ServiceDraft } from '../../lib/directInvoice';
import { purchaseLineRow, purchaseLineTotals } from '../../lib/purchaseOrderLines';
import { newRequestId } from '../../lib/requestId';
import { emptySalesLine, type SalesLineDraft } from '../../lib/salesLine';
import { VAT_CHOICES } from '../../lib/salesInvoiceEdit';
import { toApiError, type ApiError } from '../../services/apiClient';
import { createDirectInvoice } from '../../services/salesInvoiceService';
import type { CostArticle } from '../../types/costArticle';
import type { CustomerOption, ProductOption } from '../../types/referenceData';
import type { RevenueType, SalesInvoice } from '../../types/salesInvoice';
import { OrderAmountsSummary } from '../purchase/PurchaseLineTable';
import CustomerPicker from '../sales/CustomerPicker';
import LotPicker from '../sales/LotPicker';
import DateField from '../shared/DateField';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { inputClass } from '../supply/FieldError';
import PurchaseOrderLines, { SALES_LINE_FIELDS } from '../supply/PurchaseOrderLines';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

const MAX_LINES = 200;

// Verkoopfactuur 1: een directe factuur (zonder order), voor wie alleen direct factureert of andere kosten doorberekent. Productregels met
// dezelfde regelcomponent als de verkooporder (artikel kiezen met Enter, aantal, prijs, korting, btw, bedrag) en een verplichte partij
// (I-nummer, met wat er vrij is); vrije of dienstregels met omzetsoort en zonder voorraad. Opslaan maakt een concept met een eigen V-nummer;
// de voorraad van de productregels is dan gereserveerd. Herhalen na een netwerkfout gebruikt dezelfde sleutel.
export default function DirectInvoiceForm({ customers, products, serviceArticles, revenueTypes, onCreated, onCancel }: {
    customers: CustomerOption[];
    products: ProductOption[];
    serviceArticles: CostArticle[];
    revenueTypes: RevenueType[];
    onCreated: (invoice: SalesInvoice) => void;
    onCancel: () => void;
}) {
    const { t } = useTranslation();
    const keySeq = useRef(1);
    const nextKey = () => `l${keySeq.current++}`;
    const defaultRevenue = String(revenueTypes.find((r) => r.isDefault)?.id ?? '');
    const [customerId, setCustomerId] = useState('');
    const [deliveryDate, setDeliveryDate] = useState(localToday());
    const [reference, setReference] = useState('');
    const [lines, setLines] = useState<SalesLineDraft[]>(() => [emptySalesLine('p0')]);
    const [services, setServices] = useState<ServiceDraft[]>([]);
    const [lotPending, setLotPending] = useState<Set<string>>(new Set());
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [failure, setFailure] = useState<ApiError | null>(null);
    // Na een onzekere uitkomst (netwerk, 5xx) blijft de sleutel staan. Weigert de server die sleutel daarna met andere gegevens (409), dan
    // is de eerdere poging al opgeslagen: dat zeggen we dan duidelijk, in plaats van een tweede factuur te maken.
    const uncertain = useRef(false);
    const [earlierSaved, setEarlierSaved] = useState(false);
    const [busy, setBusy] = useState(false);
    const requestId = useRef<string | null>(null);

    const variants = useMemo(() => new Map(products.flatMap((product) => product.variants.map((variant) => [variant.id, variant] as const))), [products]);
    const variantOf = (id: number) => variants.get(id);
    const rows = lines.map((line) => purchaseLineRow(line, /^[1-9]\d*$/.test(line.productVariantId) ? variantOf(Number(line.productVariantId)) : undefined));
    // De sleutel blijft behouden bij wijzigingen: na een netwerkfout is de uitkomst onzeker en moet opnieuw opslaan dezelfde aanvraag zijn.
    // Alleen een definitieve afwijzing of succes wist de sleutel (zie save).
    const edited = () => { if (busy) return; setFailure(null); };

    const setPending = (key: string, on: boolean) => setLotPending((current) => { const next = new Set(current); if (on) next.add(key); else next.delete(key); return next; });

    const updateLine = (key: string, patch: Partial<SalesLineDraft>) => {
        // Een ander artikel: de gekozen partij hoort bij het oude artikel en vervalt, en een openstaande partijtekst ook.
        if (patch.productVariantId !== undefined && lines.some((line) => line.key === key && line.productVariantId !== patch.productVariantId)) setPending(key, false);
        setLines((current) => current.map((line) => (line.key === key
            ? { ...line, ...patch, ...(patch.productVariantId !== undefined && patch.productVariantId !== line.productVariantId ? { partijId: null, partijNumber: null } : {}) }
            : line)));
        edited();
    };
    const addLine = () => { if (lines.length + services.length < MAX_LINES) { setLines((current) => [...current, emptySalesLine(nextKey())]); edited(); } };
    const removeLine = (key: string) => { if (lines.length > 1) setPending(key, false); setLines((current) => (current.length > 1 ? current.filter((line) => line.key !== key) : current)); edited(); };
    const changeService = (index: number, patch: Partial<ServiceDraft>) => { setServices((current) => current.map((line, i) => (i === index ? { ...line, ...patch } : line))); edited(); };
    const chooseServiceArticle = (index: number, value: string) => {
        const article = serviceArticles.find((a) => String(a.id) === value);
        changeService(index, { costArticleId: value, ...(article ? { description: article.name, vat: String(article.vatPercentage ?? 21), unitPrice: article.defaultUnitPrice == null ? '' : formatDecimalInput(article.defaultUnitPrice, 2, undefined, 2) } : {}) });
    };
    const save = async () => {
        const nextErrors: Record<string, string> = {};
        if (customerId === '') nextErrors.customerId = 'si_direct_customer_required';
        const built = buildDirectLines(lines, services, variantOf);
        if (!built.ok) Object.assign(nextErrors, built.errors);
        setErrors(nextErrors);
        if (Object.keys(nextErrors).length > 0 || !built.ok) return;
        requestId.current ??= newRequestId();
        setBusy(true);
        setFailure(null);
        try {
            const result = await createDirectInvoice({
                requestId: requestId.current, customerId: Number(customerId), deliveryDate: deliveryDate === '' ? null : deliveryDate,
                referenceNumber: reference.trim() === '' ? null : reference.trim(), lotNumber: null, lines: built.value,
            });
            requestId.current = null;
            uncertain.current = false;
            onCreated(result.value);
        } catch (caught) {
            const apiError = toApiError(caught);
            setEarlierSaved(uncertain.current && apiError.status === 409);
            if (apiError.isDefinitiveRejection) requestId.current = null;
            uncertain.current = !apiError.isDefinitiveRejection;
            const productIndexes = lines.map((line, index) => ({ line, index })).filter(({ line }) => !(line.productVariantId === '' && line.quantity.trim() === '')).map(({ index }) => index);
            const serviceIndexes = services.map((line, index) => ({ line, index })).filter(({ line }) => !isBlankService(line)).map(({ index }) => index);
            const fields = Object.fromEntries(Object.entries(apiError.fieldErrors).map(([key, messages]) => [formKeyOf(key, productIndexes, serviceIndexes), messages.join(' ')]));
            setErrors(fields);
            setFailure(apiError);
        } finally {
            setBusy(false);
        }
    };
    // Serverteksten staan er letterlijk; eigen sleutels worden vertaald.
    const message = (key: string) => (errors[key] ? (errors[key].startsWith('si_') || errors[key].startsWith('supply_') ? t(errors[key]) : errors[key]) : null);
    const lineErrors = (key: string) => (message(key) ? [message(key)!] : undefined);
    // Serverfouten zonder veld in het formulier (bijvoorbeeld korting of prijs van een productregel) tonen we hier, zodat opslaan nooit stil mislukt.
    const unshownErrors = failure ? Object.keys(errors).filter((key) => !isShownFormKey(key)).map((key) => message(key)!) : [];
    const amounts = purchaseLineTotals(rows.filter((row, index) => lines[index].productVariantId !== ''), serviceDraftAmounts(services)).amounts;

    return (
        <form className="space-y-4 rounded-md border border-gray-200 bg-white p-3" data-testid="si-direct-form" onSubmit={(event) => { event.preventDefault(); void save(); }}>
            <h2 className="text-lg font-semibold text-gray-900">{t('si_new_direct')}</h2>
            <p className="text-sm text-gray-700">{t('si_new_direct_text')}</p>
            <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                <div>
                    <label htmlFor="customerId-input" className="text-xs font-medium text-gray-700">{t('si_col_customer')}</label>
                    <CustomerPicker id="customerId" dataTour="si-direct-customer" customers={customers} value={customerId} newCustomerDisabled disabled={busy} invalid={!!errors.customerId}
                        describedBy={errors.customerId ? 'customerId' : undefined} onChange={(customer) => { setCustomerId(customer === null ? '' : String(customer.id)); edited(); }} />
                    <FieldError id="customerId" messages={lineErrors('customerId')} />
                </div>
                <div>
                    <label htmlFor="deliveryDate" className="text-xs font-medium text-gray-700">{t('si_col_delivery_date')}</label>
                    <DateField id="deliveryDate" value={deliveryDate} onChange={(value) => { setDeliveryDate(value); edited(); }} dataTour="si-direct-delivery-date" disabled={busy} />
                </div>
                <div>
                    <label htmlFor="si-direct-reference" className="text-xs font-medium text-gray-700">{t('si_reference')}</label>
                    <input id="si-direct-reference" className={inputClass} value={reference} maxLength={100} disabled={busy} onChange={(event) => { setReference(event.target.value); edited(); }} />
                </div>
            </div>

            <fieldset className="space-y-2">
                <legend className="text-sm font-semibold text-gray-900">{t('si_direct_products')}</legend>
                <FieldError id="lines" messages={lineErrors('lines')} />
                <PurchaseOrderLines<SalesLineDraft> lines={lines} rows={rows} products={products} variantOf={variantOf} locked={busy} isDirect={false} withGgn={false}
                    tourPrefix="si-line" testId="si-direct-lines" errors={(key) => lineErrors(key)} fields={SALES_LINE_FIELDS} currency="EUR" showAmounts={false} extraIsLast vatRequired
                    onChange={updateLine} onAdd={addLine} onRemove={removeLine} productExtra={() => null}
                    lineExtra={(line, index, onLastKey) => {
                        const variantId = /^[1-9]\d*$/.test(line.productVariantId) ? Number(line.productVariantId) : null;
                        if (variantId === null) return null;
                        const unit = rows[index]?.unit ?? '';
                        const key = `lines[${index}].partijId`;
                        return (
                            <div className="mt-1 max-w-md">
                                <LotPicker id={`${key}-input`} productVariantId={variantId} unit={unit} disabled={busy} dataTour="si-line-lot" required
                                    label={t('si_direct_lot')} hint={t('si_direct_lot_hint')} describedBy={errors[key] ? key : undefined} onTabKey={onLastKey}
                                    value={line.partijId ? { partijId: line.partijId, number: line.partijNumber ?? `#${line.partijId}` } : null}
                                    onPendingChange={(on) => setPending(line.key, on)}
                                    onChange={(lot) => updateLine(line.key, { partijId: lot?.partijId ?? null, partijNumber: lot?.number ?? null })} />
                                <FieldError id={key} messages={lineErrors(key)} />
                            </div>
                        );
                    }} />
                <button type="button" onClick={addLine} disabled={busy} className={secondaryButtonClass}>+ {t('supply_add_line')}</button>
            </fieldset>

            <fieldset className="space-y-2" data-testid="si-direct-services" disabled={busy}>
                <legend className="text-sm font-semibold text-gray-900">{t('si_direct_services')}</legend>
                <p className="text-xs text-gray-700">{t('si_direct_services_text')}</p>
                {services.map((line, index) => {
                    const key = `services[${index}]`;
                    return (
                        <div key={line.key} className="grid grid-cols-2 gap-2 rounded border border-gray-200 p-2 md:grid-cols-7" data-testid="si-direct-service">
                            <div className="col-span-2 md:col-span-2">
                                <label htmlFor={`${key}.description`} className="text-xs font-medium text-gray-700">{t('si_line_description')}</label>
                                <input id={`${key}.description`} className={inputClass} value={line.description} maxLength={500} data-tour="si-service-description" data-tour-key={String(index)}
                                    aria-invalid={!!errors[`${key}.description`]} onChange={(event) => changeService(index, { description: event.target.value })} />
                                <FieldError id={`${key}.description`} messages={lineErrors(`${key}.description`)} />
                            </div>
                            <div>
                                <label htmlFor={`${key}.costArticleId`} className="text-xs font-medium text-gray-700">{t('si_direct_service_article')}</label>
                                <select id={`${key}.costArticleId`} className={inputClass} value={line.costArticleId} onChange={(event) => chooseServiceArticle(index, event.target.value)}>
                                    <option value="">{t('si_direct_service_free')}</option>
                                    {serviceArticles.filter((a) => a.isActive).map((a) => <option key={a.id} value={String(a.id)}>{a.name}</option>)}
                                </select>
                                <FieldError id={`${key}.costArticleId`} messages={lineErrors(`${key}.costArticleId`)} />
                            </div>
                            <div>
                                <label htmlFor={`${key}.quantity`} className="text-xs font-medium text-gray-700">{t('si_line_quantity')}</label>
                                <input id={`${key}.quantity`} inputMode="decimal" className={inputClass} value={line.quantity} onChange={(event) => changeService(index, { quantity: event.target.value })} />
                                <FieldError id={`${key}.quantity`} messages={lineErrors(`${key}.quantity`)} />
                            </div>
                            <div>
                                <label htmlFor={`${key}.unitPrice`} className="text-xs font-medium text-gray-700">{t('si_line_price')}</label>
                                <input id={`${key}.unitPrice`} inputMode="decimal" className={inputClass} value={line.unitPrice} data-tour="si-service-price" data-tour-key={String(index)}
                                    onChange={(event) => changeService(index, { unitPrice: event.target.value })} />
                                <FieldError id={`${key}.unitPrice`} messages={lineErrors(`${key}.unitPrice`)} />
                            </div>
                            <div>
                                <label htmlFor={`${key}.vat`} className="text-xs font-medium text-gray-700">{t('si_line_vat')}</label>
                                <select id={`${key}.vat`} className={inputClass} value={line.vat} onChange={(event) => changeService(index, { vat: event.target.value })}>
                                    {VAT_CHOICES.map((rate) => <option key={rate} value={rate}>{rate}%</option>)}
                                </select>
                                <FieldError id={`${key}.vat`} messages={lineErrors(`${key}.vat`)} />
                            </div>
                            <div>
                                <label htmlFor={`${key}.revenueTypeId`} className="text-xs font-medium text-gray-700">{t('si_revenue_type')}</label>
                                <select id={`${key}.revenueTypeId`} className={inputClass} value={line.revenueTypeId} aria-invalid={!!errors[`${key}.revenueTypeId`]} onChange={(event) => changeService(index, { revenueTypeId: event.target.value })}>
                                    {line.revenueTypeId === '' && <option value="">—</option>}
                                    {revenueTypes.filter((r) => r.isActive).map((r) => <option key={r.id} value={String(r.id)}>{r.name}</option>)}
                                </select>
                                <FieldError id={`${key}.revenueTypeId`} messages={lineErrors(`${key}.revenueTypeId`)} />
                                <button type="button" className="mt-1 text-xs text-blue-800 underline underline-offset-2" onClick={() => { setServices((current) => current.filter((_, i) => i !== index)); edited(); }}>{t('si_remove')}</button>
                            </div>
                        </div>
                    );
                })}
                <button type="button" className={secondaryButtonClass} data-tour="si-add-service" disabled={busy || lines.length + services.length >= MAX_LINES}
                    onClick={() => { setServices((current) => [...current, emptyService(nextKey(), defaultRevenue)]); edited(); }}>+ {t('si_direct_add_service')}</button>
            </fieldset>

            <OrderAmountsSummary amounts={amounts} currency="EUR" />
            {failure && earlierSaved && <p role="alert" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900" data-testid="si-direct-earlier-saved">{t('si_direct_earlier_saved')}</p>}
            {failure && !earlierSaved && Object.keys(failure.fieldErrors).length === 0 && <ErrorNotice error={failure} />}
            {failure && !earlierSaved && unshownErrors.length > 0 && (
                <ul role="alert" className="list-disc rounded border border-red-300 bg-red-50 py-2 pl-7 pr-3 text-sm text-red-900" data-testid="si-direct-unshown-errors">
                    {unshownErrors.map((text) => <li key={text}>{text}</li>)}
                </ul>
            )}
            <div className="flex flex-wrap items-center gap-2 border-t border-gray-200 pt-3">
                <button type="submit" className={primaryButtonClass} disabled={busy || lotPending.size > 0} data-tour="si-direct-save">{t('si_direct_save')}</button>
                <button type="button" className={secondaryButtonClass} onClick={onCancel} disabled={busy}>{t('si_cancel')}</button>
            </div>
        </form>
    );
}

'use client';

import { focusQuantityAfterPick } from '../../lib/productSearch';
import { Fragment, type KeyboardEvent, type ReactNode, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatMoney, formatQuantity } from '../../lib/availability';
import { formatAmountInput, formatKgInput } from '../../lib/decimal';
import { parseUnitPrice } from '../../lib/packaging';
import { formatPallets, palletWordKey } from '../../lib/purchaseLine';
import { isMacPlatform, matchShortcut, shortcutLabel } from '../../lib/shortcuts';
import { type PurchaseArticle, type PurchaseLineDraft, type PurchaseLineRow, isBlankPurchaseLine, purchaseLineTotals } from '../../lib/purchaseOrderLines';
import { type AmountLine, VAT_RATES, formatPercent, parseDiscount } from '../../lib/orderLineMath';
import { formatDecimalInput } from '../../lib/decimal';
import { type LineLimits, lineLimits } from '../../lib/purchaseOrderEdit';
import { ggnHint, ggnProblem } from '../../lib/productGgn';
import { normalizeGgn } from '../../lib/ggn';
import type { GgnProposal } from '../../types/productGgn';
import { unitLabelKey } from '../../lib/units';
import type { ProductOption, VariantOption } from '../../types/referenceData';
import { OrderAmountsSummary, PURCHASE_LINE_COLUMNS, PurchaseLineHead, PurchaseLineTotalsRow, lineCell, purchaseLineBodyClass, purchaseLineRowClass, purchaseLineTableClass, usePurchaseLineLabels } from '../purchase/PurchaseLineTable';
import ProductVariantPicker from '../shared/ProductVariantPicker';
import FieldError, { fieldProps, inputClass } from './FieldError';
import { secondaryButtonClass } from './SuccessPanel';

export interface LineFieldNames { quantity: string; unit: string; gross: string; net?: string }
export const PURCHASE_LINE_FIELDS: LineFieldNames = { quantity: 'expectedQuantity', unit: 'expectedUnit', gross: 'expectedGrossWeightKg' };
// De verkooporder (POST /api/sales-orders): ook een fout op de netto kg staat bij de bruto.
export const SALES_LINE_FIELDS: LineFieldNames = { quantity: 'quantity', unit: 'unit', gross: 'grossWeightKg', net: 'netWeightKg' };

const linkButton = 'whitespace-nowrap text-xs font-medium text-blue-800 underline underline-offset-2 disabled:text-gray-500';

// De regels van Inkooporder aanmaken in de nieuwe indeling (zie PurchaseLineTable). Tab loopt per regel door Product, Aantal, Prijs en GGN
// (bij klantgebonden aanvoer daarna de partij); alles wat uit het artikel komt is tekst. Tab op het laatste veld van de laatste regel
// maakt een nieuwe regel en zet de cursor in haar productveld; een lege regel telt bij opslaan niet mee. De knoppen (Bruto aanpassen,
// Bruto van het artikel, ✕) staan buiten de Tab-volgorde, zoals de eigenaar wil en net als ⧉ en ✕ bij de inkoopfactuur; met het
// toetsenbord bereik je ze vanuit elk veld van de regel met Alt+B (Bruto aanpassen), Alt+A (Nieuw artikel) en Alt+R (regel verwijderen), wat onder de tabel
// staat. Een aangepaste bruto leegmaken zet de regel terug op de bruto van het artikel. Bij aanpassen (stap 4a) zegt `limits` per regel
// wat vastligt na ontvangst: dan staat de uitleg bij het veld (artikel vast, nooit onder ontvangen, regel blijft staan).
// Certificaten deel 2: na Prijs het GGN van het product (Tab: Product, Aantal, Prijs, GGN). Het voorstel uit de GGN-certificaten van de
// leverancier staat eronder (voorgesteld, kiezen bij meer, of geen); de gebruiker mag altijd een eigen nummer typen of het leegmaken.
// Feedback 22 november: na Prijs de korting (leeg = geen) en het btw-tarief (voorstel uit het artikel), dus Tab: Product, Aantal, Prijs,
// Korting, Btw, GGN; onder de tabel de btw per tarief. Dezelfde component voor de verkooporder: zonder GGN-kolom (`withGgn` onwaar) is
// Btw het laatste veld, `extraAmounts` telt emballage mee in de btw, en `tourPrefix` geeft de rondleiding haar eigen namen.
export default function PurchaseOrderLines<D extends PurchaseLineDraft = PurchaseLineDraft>({ lines, rows, products, variantOf, locked, isDirect, errors, onChange, onAdd, onRemove, onNewArticle, productExtra, limits = () => lineLimits(undefined),
    currency = null, rate = null, ggnProposalOf = () => undefined, withGgn = true, extraAmounts = [], tourPrefix = 'supply-line', testId = 'purchase-lines', showAmounts = true, fields = PURCHASE_LINE_FIELDS, lineExtra = () => null }: {
    lines: D[];
    // De veldnamen van de server in ids en foutsleutels (lines[i].expectedQuantity bij inkoop, lines[i].quantity bij verkoop).
    fields?: LineFieldNames;
    withGgn?: boolean;
    // Onwaar: de btw-samenvatting staat elders (de verkooporder zet haar onder de emballage).
    showAmounts?: boolean;
    extraAmounts?: readonly AmountLine[];
    tourPrefix?: string;
    testId?: string;
    // Valuta-ronde: valuta van de order (prijzen en totalen staan erin) en de koers voor het eurobedrag onder het totaal.
    currency?: string | null;
    rate?: number | null;
    rows: PurchaseLineRow[];
    products: ProductOption[];
    variantOf: (variantId: number) => (VariantOption & PurchaseArticle) | undefined;
    locked: boolean;
    isDirect: boolean;
    errors: (key: string) => string[] | undefined;
    onChange: (key: string, patch: Partial<D>) => void;
    onAdd: () => void;
    onRemove: (key: string) => void;
    onNewArticle?: (key: string) => void;
    productExtra: (line: D, index: number) => ReactNode;
    // Extra inhoud onder de regel, over de hele breedte (de verkooporder: stand en bron). Staat in de Tab-volgorde ná de regel, zodat Tab
    // vanuit het artikel naar Aantal gaat.
    lineExtra?: (line: D, index: number) => ReactNode;
    limits?: (key: string) => LineLimits;
    ggnProposalOf?: (line: D) => GgnProposal | undefined;
}) {
    const { t, locale } = useTranslation();
    const labels = usePurchaseLineLabels();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    // Regels waar "Bruto aanpassen" open staat (ook zonder getypte waarde nog).
    const [grossOpen, setGrossOpen] = useState<ReadonlySet<string>>(new Set());
    const focusIndex = useRef<number | null>(null);
    useEffect(() => {
        if (focusIndex.current === null) return;
        document.getElementById(`lines[${focusIndex.current}].productVariantId-input`)?.focus();
        focusIndex.current = null;
    }, [lines.length]);

    const kg = (value: number | null | undefined) => (value == null ? '–' : formatKg(value, locale));
    const text = (value: string | null | undefined) => (value && value.trim() !== '' ? value : '–');

    // Tab op het laatste veld van de laatste (gevulde) regel: een nieuwe regel, cursor in haar product.
    const onLastField = (event: KeyboardEvent<HTMLElement>, index: number) => {
        if (event.key !== 'Tab' || event.shiftKey || event.altKey || event.ctrlKey || event.metaKey || locked) return;
        if (index !== lines.length - 1 || isBlankPurchaseLine(lines[index])) return;
        event.preventDefault();
        focusIndex.current = lines.length;
        onAdd();
    };

    const openGross = (line: D, row: PurchaseLineRow) => {
        setGrossOpen((current) => new Set(current).add(line.key));
        if (!line.grossKgEntered) onChange(line.key, { grossKg: row.weight?.grossWeightKg == null ? '' : formatKgInput(row.weight.grossWeightKg) } as Partial<D>);
        setTimeout(() => document.getElementById(`lines[${lines.indexOf(line)}].${fields.gross}-input`)?.focus(), 0);
    };
    // Alt+B, Alt+A en Alt+R in een veld van de regel: dezelfde acties als de knoppen.
    const onRowKey = (event: KeyboardEvent<HTMLTableRowElement>, line: D, row: PurchaseLineRow, index: number) => {
        if (locked) return;
        const action = matchShortcut(event, isMac);
        if (action === 'adjustGross' && line.productVariantId !== '') {
            event.preventDefault();
            openGross(line, row);
        } else if (action === 'newArticle' && onNewArticle && !limits(line.key).articleFixed) {
            event.preventDefault();
            onNewArticle(line.key);
        } else if (action === 'removeLine' && lines.length > 1 && limits(line.key).removable) {
            event.preventDefault();
            onRemove(line.key);
            const next = Math.min(index, lines.length - 2);
            setTimeout(() => document.getElementById(`lines[${next}].productVariantId-input`)?.focus(), 0);
        }
    };
    const resetGross = (line: D) => {
        setGrossOpen((current) => { const next = new Set(current); next.delete(line.key); return next; });
        onChange(line.key, { grossKg: '', grossKgEntered: false } as Partial<D>);
    };
    const percent = (value: number) => formatPercent(value, locale);
    const filled = rows.filter((_, index) => !isBlankPurchaseLine(lines[index]));
    const totals = purchaseLineTotals(filled, extraAmounts);

    return (
        <div className="rounded border border-gray-200 max-md:border-0">
            <table className={purchaseLineTableClass} data-testid={testId}>
                <PurchaseLineHead trailing={<>
                    {withGgn && <th scope="col" className="px-2 py-1.5">{t('po_line_col_ggn')}</th>}
                    {isDirect && <th scope="col" className="px-2 py-1.5">{t('supply_lot_optional')}</th>}
                    <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('supply_remove_line')}</span></th>
                </>} />
                <tbody className={purchaseLineBodyClass}>
                    {lines.map((line, index) => {
                        const prefix = `lines[${index}]`;
                        const row = rows[index];
                        const variant = line.productVariantId === '' ? undefined : variantOf(Number(line.productVariantId));
                        const editingGross = line.grossKgEntered === true || grossOpen.has(line.key);
                        const last = isDirect ? 'lot' : withGgn ? 'ggn' : 'vat';
                        const proposal = line.productVariantId === '' ? undefined : ggnProposalOf(line);
                        const hint = ggnHint(line, proposal);
                        const problem = ggnProblem(line.ggn ?? '');
                        // De keuzelijst staat er bij meer treffers, zolang er geen eigen nummer (buiten de lijst) is ingevuld; dan is zij het laatste veld.
                        const choosing = proposal?.kind === 'Many' && (normalizeGgn(line.ggn ?? '') === '' || proposal.options.some((o) => o.ggn === normalizeGgn(line.ggn ?? '')));
                        const limit = limits(line.key);
                        const extra = lineExtra(line, index);
                        return (
                            <Fragment key={line.key}>
                            <tr className={purchaseLineRowClass(index)} data-tour={tourPrefix} data-tour-key={index} onKeyDown={(event) => onRowKey(event, line, row, index)}>
                                <td {...lineCell('product', labels.product, 'w-64 max-md:w-auto', true)}>
                                    <label htmlFor={`${prefix}.productVariantId-input`} className="sr-only">{t('supply_variant')} {index + 1}</label>
                                    <ProductVariantPicker id={`${prefix}.productVariantId`} dataTour={`${tourPrefix}-product-${index}`} ariaLabel={`${t('supply_variant')} ${index + 1}`} products={products}
                                        value={line.productVariantId} disabled={locked || limit.articleFixed} invalid={(errors(`${prefix}.productVariantId`) ?? []).length > 0}
                                        describedBy={(errors(`${prefix}.productVariantId`) ?? []).length > 0 ? `${prefix}.productVariantId` : undefined}
                                        onChange={(variantId) => {
                                            onChange(line.key, { productVariantId: variantId, unit: '', ...(line.vatTouched && !limit.articleFixed ? {} : { vat: '', vatTouched: false }) } as Partial<D>);
                                            // Feedback 22 november: een gekozen artikel staat direct op de regel en de cursor springt naar Aantal.
                                            if (variantId !== '') focusQuantityAfterPick(`${prefix}.productVariantId-input`, `${prefix}.${fields.quantity}-input`);
                                        }} />
                                    <FieldError id={`${prefix}.productVariantId`} messages={errors(`${prefix}.productVariantId`)} />
                                    <FieldError id={prefix} messages={errors(prefix)} />
                                    {limit.articleFixed && (
                                        <span className="mt-1 block text-xs text-gray-700" data-testid={`${prefix}.article-fixed`}>{t('po_edit_article_fixed')} {t('po_edit_not_removable')}</span>
                                    )}
                                    {productExtra(line, index)}
                                </td>
                                <td {...lineCell('brand', labels.brand)} data-testid={`${prefix}.brand`}>{text(variant?.brand)}</td>
                                <td {...lineCell('country', labels.country)} data-testid={`${prefix}.country`}>{text(variant?.origin)}</td>
                                <td {...lineCell('size', labels.size)} data-testid={`${prefix}.size`}>{text(variant?.sizeCategory)}</td>
                                <td {...lineCell('gross', labels.gross, 'min-w-[7rem]')}>
                                    <div className="space-y-1">
                                        {editingGross ? (
                                            <>
                                                <label htmlFor={`${prefix}.${fields.gross}-input`} className="sr-only">{labels.gross} {index + 1}</label>
                                                <input {...fieldProps(`${prefix}.${fields.gross}`, errors(`${prefix}.${fields.gross}`))} inputMode="decimal" data-decimals={2} value={line.grossKg ?? ''} disabled={locked}
                                                    data-tour={`${tourPrefix}-gross`} data-tour-key={index} onChange={(event) => onChange(line.key, { grossKg: event.target.value, grossKgEntered: true } as Partial<D>)}
                                                    className={`${inputClass} !mt-0 w-24 text-right`} />
                                                {line.grossKgEntered && <span className="block text-xs font-medium text-amber-900" data-testid={`${prefix}.gross-deviates`}>{t('po_line_gross_deviates')}</span>}
                                                <button type="button" tabIndex={-1} disabled={locked} className={linkButton} onClick={() => resetGross(line)}>↺ {t('po_line_gross_reset')}</button>
                                            </>
                                        ) : (
                                            <>
                                                <span data-testid={`${prefix}.gross`}>{kg(row?.weight?.grossWeightKg)}</span>
                                                {variant && !locked && (
                                                    <button type="button" tabIndex={-1} title={`${t('po_line_gross_adjust')} (${shortcutLabel('adjustGross', isMac)})`} className={`${linkButton} block`} data-tour={`${tourPrefix}-gross-adjust`} data-tour-key={index}
                                                        onClick={() => openGross(line, row)}>{t('po_line_gross_adjust')}</button>
                                                )}
                                            </>
                                        )}
                                        <FieldError id={`${prefix}.${fields.gross}`} messages={errors(`${prefix}.${fields.gross}`)} />
                                        {fields.net && <FieldError id={`${prefix}.${fields.net}`} messages={errors(`${prefix}.${fields.net}`)} />}
                                    </div>
                                </td>
                                <td {...lineCell('net', labels.net)} data-testid={`${prefix}.net`}>{kg(row?.weight?.netWeightKg)}</td>
                                <td {...lineCell('quantity', labels.quantity, 'min-w-[8rem]')}>
                                    <div className="space-y-1">
                                        <div className="flex items-center justify-end gap-1.5">
                                            <label htmlFor={`${prefix}.${fields.quantity}-input`} className="sr-only">{labels.quantity} {index + 1}</label>
                                            <input {...fieldProps(`${prefix}.${fields.quantity}`, errors(`${prefix}.${fields.quantity}`))} inputMode="decimal" value={line.quantity} disabled={locked}
                                                data-tour={`${tourPrefix}-quantity`} data-tour-key={index} onChange={(event) => onChange(line.key, { quantity: event.target.value } as Partial<D>)}
                                                className={`${inputClass} !mt-0 w-20 text-right`} />
                                            {row?.unit && <span className="text-xs text-gray-700" data-testid={`${prefix}.unit`}>{t(unitLabelKey(row.unit)).toLowerCase()}</span>}
                                        </div>
                                        {variant && row?.quantity != null && (
                                            <span className="block text-xs text-gray-600" data-testid={`${prefix}.pallets`}>
                                                {row.pallets === null ? t('po_line_no_packages_per_pallet') : t(palletWordKey(row.pallets), { pallets: formatPallets(row.pallets, locale) })}
                                            </span>
                                        )}
                                        {limit.minimumQuantity !== null && (
                                            <span className="block text-xs text-gray-700" data-testid={`${prefix}.minimum`}>{t('po_edit_min_quantity', { received: formatQuantity(limit.minimumQuantity, locale) })}</span>
                                        )}
                                        <FieldError id={`${prefix}.${fields.quantity}`} messages={errors(`${prefix}.${fields.quantity}`)} />
                                        <FieldError id={`${prefix}.${fields.unit}`} messages={errors(`${prefix}.${fields.unit}`)} />
                                    </div>
                                </td>
                                <td {...lineCell('packaging', labels.packaging)} data-testid={`${prefix}.packaging`}>{text(variant?.packaging)}</td>
                                <td {...lineCell('price', labels.price, 'min-w-[7rem]')}>
                                    <label htmlFor={`${prefix}.unitPrice-input`} className="sr-only">{labels.price} {index + 1}</label>
                                    <input {...fieldProps(`${prefix}.unitPrice`, errors(`${prefix}.unitPrice`))} inputMode="decimal" data-decimals={2} value={line.unitPrice} disabled={locked}
                                        data-tour={`${tourPrefix}-price`} data-tour-key={index} onChange={(event) => onChange(line.key, { unitPrice: event.target.value } as Partial<D>)}
                                        onBlur={() => { const price = parseUnitPrice(line.unitPrice); if (price.ok && price.value !== null) onChange(line.key, { unitPrice: formatAmountInput(price.value) } as Partial<D>); }}
                                        className={`${inputClass} !mt-0 w-24 text-right`} />
                                    <FieldError id={`${prefix}.unitPrice`} messages={errors(`${prefix}.unitPrice`)} />
                                </td>
                                <td {...lineCell('discount', labels.discount, 'min-w-[5rem]')}>
                                    <label htmlFor={`${prefix}.discountPercentage-input`} className="sr-only">{labels.discount} {index + 1}</label>
                                    <input {...fieldProps(`${prefix}.discountPercentage`, errors(`${prefix}.discountPercentage`))} inputMode="decimal" value={line.discount ?? ''} disabled={locked}
                                        title={t('po_line_discount_hint')} data-tour={`${tourPrefix}-discount`} data-tour-key={index}
                                        onChange={(event) => onChange(line.key, { discount: event.target.value } as Partial<D>)}
                                        onBlur={() => { const discount = parseDiscount(line.discount ?? ''); if (discount.ok && discount.value !== null) onChange(line.key, { discount: formatDecimalInput(discount.value, 2) } as Partial<D>); }}
                                        className={`${inputClass} !mt-0 w-16 text-right`} />
                                    <FieldError id={`${prefix}.discountPercentage`} messages={errors(`${prefix}.discountPercentage`)} />
                                </td>
                                <td {...lineCell('vat', labels.vat, 'min-w-[5.5rem]')}>
                                    <label htmlFor={`${prefix}.vatPercentage-input`} className="sr-only">{labels.vat} {index + 1}</label>
                                    <select {...fieldProps(`${prefix}.vatPercentage`, errors(`${prefix}.vatPercentage`))} value={row?.vat == null ? '' : String(row.vat)} disabled={locked}
                                        data-tour={`${tourPrefix}-vat`} data-tour-key={index}
                                        onChange={(event) => onChange(line.key, { vat: event.target.value, vatTouched: true } as Partial<D>)}
                                        onKeyDown={last === 'vat' ? (event) => onLastField(event, index) : undefined}
                                        className={`${inputClass} !mt-0 w-20 text-right`}>
                                        {row?.vat == null && <option value="">{t('po_line_vat_none')}</option>}
                                        {VAT_RATES.map((rate) => <option key={rate} value={String(rate)}>{percent(rate)}%</option>)}
                                    </select>
                                    <FieldError id={`${prefix}.vatPercentage`} messages={errors(`${prefix}.vatPercentage`)} />
                                </td>
                                <td {...lineCell('total', labels.total)} data-testid={`${prefix}.total`}>{row?.total == null ? '–' : formatMoney(row.total, currency, locale)}</td>
                                {withGgn && <td {...lineCell('extra', t('po_line_col_ggn'), 'min-w-[10rem]')}>
                                    <div className="space-y-1 max-md:text-right">
                                        <label htmlFor={`${prefix}.ggn-input`} className="sr-only">{t('po_line_col_ggn')} {index + 1}</label>
                                        <input {...fieldProps(`${prefix}.ggn`, errors(`${prefix}.ggn`))} value={line.ggn ?? ''} disabled={locked} inputMode="numeric" maxLength={20}
                                            data-tour={`${tourPrefix}-ggn`} data-tour-key={index} onChange={(event) => onChange(line.key, { ggn: event.target.value, ggnTouched: true } as Partial<D>)}
                                            onKeyDown={last === 'ggn' && !choosing ? (event) => onLastField(event, index) : undefined}
                                            className={`${inputClass} !mt-0 w-40 font-mono`} />
                                        {/* Kiezen bij meer treffers; met een eigen nummer (niet in de lijst) is er niets te kiezen. */}
                                        {proposal?.kind === 'Many' && choosing && (
                                            <select aria-label={t('po_line_ggn_choose_label', { n: String(index + 1) })} disabled={locked} className={`${inputClass} !mt-0 w-40 text-xs`}
                                                onKeyDown={last === 'ggn' ? (event) => onLastField(event, index) : undefined}
                                                value={proposal.options.some((o) => o.ggn === normalizeGgn(line.ggn ?? '')) ? normalizeGgn(line.ggn ?? '') : ''} data-testid={`${prefix}.ggn-choice`}
                                                onChange={(event) => onChange(line.key, { ggn: event.target.value, ggnTouched: true } as Partial<D>)}>
                                                <option value="">{t('po_line_ggn_choose')}</option>
                                                {proposal.options.map((o) => <option key={o.ggn} value={o.ggn}>{o.ggn}{o.producerName ? ` · ${o.producerName}` : ''}</option>)}
                                            </select>
                                        )}
                                        {hint && <span className="block text-xs text-gray-600" data-testid={`${prefix}.ggn-hint`}>{t(`po_line_ggn_hint_${hint}`, { count: String(proposal?.options.length ?? 0) })}</span>}
                                        {problem === 'ggn_check_digit' && <span className="block text-xs font-medium text-amber-900" data-testid={`${prefix}.ggn-check-digit`}>{t('cert_warning_ggn_check_digit')}</span>}
                                        <FieldError id={`${prefix}.ggn`} messages={errors(`${prefix}.ggn`)} />
                                    </div>
                                </td>}
                                {isDirect && (
                                    <td {...lineCell('extra', t('supply_lot_optional'))}>
                                        <label htmlFor={`${prefix}.lotNumber-input`} className="sr-only">{t('supply_lot_optional')} {index + 1}</label>
                                        <input {...fieldProps(`${prefix}.lotNumber`, errors(`${prefix}.lotNumber`))} value={line.lotNumber} disabled={locked} maxLength={100}
                                            onChange={(event) => onChange(line.key, { lotNumber: event.target.value } as Partial<D>)} onKeyDown={(event) => onLastField(event, index)} className={`${inputClass} !mt-0`} />
                                        <FieldError id={`${prefix}.lotNumber`} messages={errors(`${prefix}.lotNumber`)} />
                                    </td>
                                )}
                                <td className="px-2 py-1.5 max-md:block max-md:px-0 max-md:pt-1 max-md:text-right">
                                    <button type="button" tabIndex={-1} title={limit.removable ? `${t('supply_remove_line_n', { n: String(index + 1) })} (${shortcutLabel('removeLine', isMac)})` : t('po_edit_not_removable')}
                                        onClick={() => onRemove(line.key)} disabled={locked || lines.length <= 1 || !limit.removable}
                                        className={secondaryButtonClass} aria-label={t('supply_remove_line_n', { n: String(index + 1) })}>✕</button>
                                </td>
                            </tr>
                            {extra !== null && (
                                <tr className={`${purchaseLineRowClass(index)} max-md:-mt-2 max-md:rounded-t-none max-md:border-t-0`} data-testid={`${prefix}.extra`}>
                                    <td colSpan={PURCHASE_LINE_COLUMNS.length + (withGgn ? 1 : 0) + (isDirect ? 1 : 0) + 1} className="px-2 pb-2 max-md:block max-md:px-0">{extra}</td>
                                </tr>
                            )}
                            </Fragment>
                        );
                    })}
                </tbody>
                <PurchaseLineTotalsRow totals={totals} trailingCells={(isDirect ? 2 : 1) + (withGgn ? 1 : 0)} currency={currency} rate={rate} />
            </table>
            {showAmounts && <div className="px-2 pb-2"><OrderAmountsSummary amounts={totals.amounts} currency={currency} rate={rate} /></div>}
            {!locked && <p className="px-2 py-1 text-xs text-gray-600" data-testid="purchase-lines-keys">{t('po_line_keys_hint', { gross: shortcutLabel('adjustGross', isMac), article: shortcutLabel('newArticle', isMac), remove: shortcutLabel('removeLine', isMac) })}</p>}
        </div>
    );
}

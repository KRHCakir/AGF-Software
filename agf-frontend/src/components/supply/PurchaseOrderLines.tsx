'use client';

import { type KeyboardEvent, type ReactNode, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatMoney, formatQuantity } from '../../lib/availability';
import { formatAmountInput } from '../../lib/decimal';
import { parseUnitPrice } from '../../lib/packaging';
import { formatPallets } from '../../lib/purchaseLine';
import { type PurchaseArticle, type PurchaseLineDraft, type PurchaseLineRow, isBlankPurchaseLine, purchaseLineTotals } from '../../lib/purchaseOrderLines';
import { unitLabelKey } from '../../lib/units';
import type { ProductOption, VariantOption } from '../../types/referenceData';
import { PurchaseLineHead, PurchaseLineTotalsRow, lineCell, purchaseLineBodyClass, purchaseLineRowClass, purchaseLineTableClass, usePurchaseLineLabels } from '../purchase/PurchaseLineTable';
import ProductVariantPicker from '../shared/ProductVariantPicker';
import UnitSelect from '../shared/UnitSelect';
import FieldError, { fieldProps, inputClass } from './FieldError';
import { secondaryButtonClass } from './SuccessPanel';

const linkButton = 'whitespace-nowrap text-xs font-medium text-blue-800 underline underline-offset-2 disabled:text-gray-500';

// De regels van Inkooporder aanmaken in de nieuwe indeling (zie PurchaseLineTable). Tab loopt per regel door Product, Aantal en Prijs
// (bij klantgebonden aanvoer daarna de partij); alles wat uit het artikel komt is tekst. Tab op het laatste veld van de laatste regel
// maakt een nieuwe regel en zet de cursor in haar productveld; een lege regel telt bij opslaan niet mee. De knoppen (Bruto aanpassen,
// ✕) staan buiten de Tab-volgorde, net als bij de inkoopfactuur.
export default function PurchaseOrderLines({ lines, rows, products, variantOf, locked, isDirect, errors, onChange, onAdd, onRemove, productExtra }: {
    lines: PurchaseLineDraft[];
    rows: PurchaseLineRow[];
    products: ProductOption[];
    variantOf: (variantId: number) => (VariantOption & PurchaseArticle) | undefined;
    locked: boolean;
    isDirect: boolean;
    errors: (key: string) => string[] | undefined;
    onChange: (key: string, patch: Partial<PurchaseLineDraft>) => void;
    onAdd: () => void;
    onRemove: (key: string) => void;
    productExtra: (line: PurchaseLineDraft, index: number) => ReactNode;
}) {
    const { t, locale } = useTranslation();
    const labels = usePurchaseLineLabels();
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
    const onLastField = (event: KeyboardEvent<HTMLInputElement>, index: number) => {
        if (event.key !== 'Tab' || event.shiftKey || event.altKey || event.ctrlKey || event.metaKey || locked) return;
        if (index !== lines.length - 1 || isBlankPurchaseLine(lines[index])) return;
        event.preventDefault();
        focusIndex.current = lines.length;
        onAdd();
    };

    const openGross = (line: PurchaseLineDraft, row: PurchaseLineRow) => {
        setGrossOpen((current) => new Set(current).add(line.key));
        if (!line.grossKgEntered) onChange(line.key, { grossKg: row.weight?.grossWeightKg == null ? '' : formatQuantity(row.weight.grossWeightKg, locale) });
        setTimeout(() => document.getElementById(`lines[${lines.indexOf(line)}].expectedGrossWeightKg-input`)?.focus(), 0);
    };
    const resetGross = (line: PurchaseLineDraft) => {
        setGrossOpen((current) => { const next = new Set(current); next.delete(line.key); return next; });
        onChange(line.key, { grossKg: '', grossKgEntered: false });
    };

    return (
        <div className="rounded border border-gray-200 max-md:border-0">
            <table className={purchaseLineTableClass} data-testid="purchase-lines">
                <PurchaseLineHead trailing={<>
                    {isDirect && <th scope="col" className="px-2 py-1.5">{t('supply_lot_optional')}</th>}
                    <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('supply_remove_line')}</span></th>
                </>} />
                <tbody className={purchaseLineBodyClass}>
                    {lines.map((line, index) => {
                        const prefix = `lines[${index}]`;
                        const row = rows[index];
                        const variant = line.productVariantId === '' ? undefined : variantOf(Number(line.productVariantId));
                        const editingGross = line.grossKgEntered === true || grossOpen.has(line.key);
                        const last = isDirect ? 'lot' : 'price';
                        return (
                            <tr key={line.key} className={purchaseLineRowClass(index)} data-tour="supply-line" data-tour-key={index}>
                                <td {...lineCell('product', labels.product, 'w-64 max-md:block max-md:w-auto max-md:text-left max-md:before:hidden')}>
                                    <label htmlFor={`${prefix}.productVariantId-input`} className="sr-only">{t('supply_variant')} {index + 1}</label>
                                    <ProductVariantPicker id={`${prefix}.productVariantId`} dataTour={`supply-line-product-${index}`} ariaLabel={`${t('supply_variant')} ${index + 1}`} products={products}
                                        value={line.productVariantId} disabled={locked} invalid={(errors(`${prefix}.productVariantId`) ?? []).length > 0}
                                        describedBy={(errors(`${prefix}.productVariantId`) ?? []).length > 0 ? `${prefix}.productVariantId` : undefined}
                                        onChange={(variantId) => onChange(line.key, { productVariantId: variantId })} />
                                    <FieldError id={`${prefix}.productVariantId`} messages={errors(`${prefix}.productVariantId`)} />
                                    <FieldError id={prefix} messages={errors(prefix)} />
                                    {productExtra(line, index)}
                                </td>
                                <td {...lineCell('brand', labels.brand)} data-testid={`${prefix}.brand`}>{text(variant?.brand)}</td>
                                <td {...lineCell('country', labels.country)} data-testid={`${prefix}.country`}>{text(variant?.origin)}</td>
                                <td {...lineCell('size', labels.size)} data-testid={`${prefix}.size`}>{text(variant?.sizeCategory)}</td>
                                <td {...lineCell('gross', labels.gross, 'min-w-[7rem]')}>
                                    <div className="space-y-1">
                                        {editingGross ? (
                                            <>
                                                <label htmlFor={`${prefix}.expectedGrossWeightKg-input`} className="sr-only">{labels.gross} {index + 1}</label>
                                                <input {...fieldProps(`${prefix}.expectedGrossWeightKg`, errors(`${prefix}.expectedGrossWeightKg`))} inputMode="decimal" value={line.grossKg ?? ''} disabled={locked}
                                                    data-tour="supply-line-gross" data-tour-key={index} onChange={(event) => onChange(line.key, { grossKg: event.target.value, grossKgEntered: true })}
                                                    className={`${inputClass} !mt-0 w-24 text-right`} />
                                                {line.grossKgEntered && <span className="block text-xs font-medium text-amber-900" data-testid={`${prefix}.gross-deviates`}>{t('po_line_gross_deviates')}</span>}
                                                <button type="button" tabIndex={-1} disabled={locked} className={linkButton} onClick={() => resetGross(line)}>↺ {t('po_line_gross_reset')}</button>
                                            </>
                                        ) : (
                                            <>
                                                <span data-testid={`${prefix}.gross`}>{kg(row?.weight?.grossWeightKg)}</span>
                                                {variant && !locked && (
                                                    <button type="button" tabIndex={-1} className={`${linkButton} block`} data-tour="supply-line-gross-adjust" data-tour-key={index}
                                                        onClick={() => openGross(line, row)}>{t('po_line_gross_adjust')}</button>
                                                )}
                                            </>
                                        )}
                                        <FieldError id={`${prefix}.expectedGrossWeightKg`} messages={errors(`${prefix}.expectedGrossWeightKg`)} />
                                    </div>
                                </td>
                                <td {...lineCell('net', labels.net)} data-testid={`${prefix}.net`}>{kg(row?.weight?.netWeightKg)}</td>
                                <td {...lineCell('quantity', labels.quantity, 'min-w-[8rem]')}>
                                    <div className="space-y-1">
                                        <div className="flex items-center justify-end gap-1.5">
                                            <label htmlFor={`${prefix}.expectedQuantity-input`} className="sr-only">{labels.quantity} {index + 1}</label>
                                            <input {...fieldProps(`${prefix}.expectedQuantity`, errors(`${prefix}.expectedQuantity`))} inputMode="decimal" value={line.quantity} disabled={locked}
                                                data-tour="supply-line-quantity" data-tour-key={index} onChange={(event) => onChange(line.key, { quantity: event.target.value })}
                                                className={`${inputClass} !mt-0 w-20 text-right`} />
                                            {row?.unitFromArticle !== false && row?.unit
                                                ? <span className="text-xs text-gray-700" data-testid={`${prefix}.unit`}>{t(unitLabelKey(row.unit)).toLowerCase()}</span>
                                                : variant && <UnitSelect fieldKey={`${prefix}.expectedUnit`} index={index} value={line.unit} errors={errors(`${prefix}.expectedUnit`)} disabled={locked}
                                                    dataTour="supply-line-unit" onChange={(unit) => onChange(line.key, { unit })} />}
                                        </div>
                                        {variant && row?.quantity != null && (
                                            <span className="block text-xs text-gray-600" data-testid={`${prefix}.pallets`}>
                                                {row.pallets === null ? t('po_line_no_packages_per_pallet') : t('po_line_pallets', { pallets: formatPallets(row.pallets, locale) })}
                                            </span>
                                        )}
                                        <FieldError id={`${prefix}.expectedQuantity`} messages={errors(`${prefix}.expectedQuantity`)} />
                                        <FieldError id={`${prefix}.expectedUnit`} messages={errors(`${prefix}.expectedUnit`)} />
                                    </div>
                                </td>
                                <td {...lineCell('packaging', labels.packaging)} data-testid={`${prefix}.packaging`}>{text(variant?.packaging)}</td>
                                <td {...lineCell('price', labels.price, 'min-w-[7rem]')}>
                                    <label htmlFor={`${prefix}.unitPrice-input`} className="sr-only">{labels.price} {index + 1}</label>
                                    <input {...fieldProps(`${prefix}.unitPrice`, errors(`${prefix}.unitPrice`))} inputMode="decimal" value={line.unitPrice} disabled={locked}
                                        data-tour="supply-line-price" data-tour-key={index} onChange={(event) => onChange(line.key, { unitPrice: event.target.value })}
                                        onBlur={() => { const price = parseUnitPrice(line.unitPrice); if (price.ok && price.value !== null) onChange(line.key, { unitPrice: formatAmountInput(price.value) }); }}
                                        onKeyDown={last === 'price' ? (event) => onLastField(event, index) : undefined}
                                        className={`${inputClass} !mt-0 w-24 text-right`} />
                                    <FieldError id={`${prefix}.unitPrice`} messages={errors(`${prefix}.unitPrice`)} />
                                </td>
                                <td {...lineCell('total', labels.total)} data-testid={`${prefix}.total`}>{row?.total == null ? '–' : formatMoney(row.total, null, locale)}</td>
                                {isDirect && (
                                    <td {...lineCell('extra', t('supply_lot_optional'))}>
                                        <label htmlFor={`${prefix}.lotNumber-input`} className="sr-only">{t('supply_lot_optional')} {index + 1}</label>
                                        <input {...fieldProps(`${prefix}.lotNumber`, errors(`${prefix}.lotNumber`))} value={line.lotNumber} disabled={locked} maxLength={100}
                                            onChange={(event) => onChange(line.key, { lotNumber: event.target.value })} onKeyDown={(event) => onLastField(event, index)} className={`${inputClass} !mt-0`} />
                                        <FieldError id={`${prefix}.lotNumber`} messages={errors(`${prefix}.lotNumber`)} />
                                    </td>
                                )}
                                <td className="px-2 py-1.5 max-md:block max-md:px-0 max-md:pt-1 max-md:text-right">
                                    <button type="button" tabIndex={-1} onClick={() => onRemove(line.key)} disabled={locked || lines.length <= 1}
                                        className={secondaryButtonClass} aria-label={t('supply_remove_line_n', { n: String(index + 1) })}>✕</button>
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
                <PurchaseLineTotalsRow totals={purchaseLineTotals(rows.filter((_, index) => !isBlankPurchaseLine(lines[index])))} trailingCells={isDirect ? 2 : 1} />
            </table>
        </div>
    );
}

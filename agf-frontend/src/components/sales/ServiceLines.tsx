'use client';

import { useEffect, useRef } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney, formatQuantity } from '../../lib/availability';
import { VAT_RATES, formatPercent } from '../../lib/orderLineMath';
import { type ServiceLineDraft, emptyServiceLine, serviceLineRow } from '../../lib/serviceLines';
import { newRequestId } from '../../lib/requestId';
import type { CostArticle } from '../../types/costArticle';
import type { SalesOrderServiceLine } from '../../types/salesOrder';
import FieldError, { fieldProps, inputClass, lineTableClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Diensten onder de goederen en de emballage (feedback 22 november): handling of transport die aan de klant wordt doorberekend, met een
// Dienst-artikel, aantal, prijs, korting en het btw-tarief van het artikel (standaard 21%, per regel aan te passen). Geen voorraad, geen
// pallets of kg, en niet op de laadbon of de transportopdracht; wel in de totalen en straks op de factuur.
export function ServiceLinesInput({ lines, articles, locked, errors, onChange }: {
    lines: ServiceLineDraft[];
    articles: CostArticle[];
    locked: boolean;
    errors: (key: string) => string[] | undefined;
    onChange: (lines: ServiceLineDraft[]) => void;
}) {
    const { t, locale } = useTranslation();
    const focusId = useRef<string | null>(null);
    const choosable = articles.filter((article) => article.isActive);
    useEffect(() => {
        if (focusId.current === null) return;
        const element = document.getElementById(focusId.current);
        focusId.current = null;
        element?.focus();
    }, [lines]);
    const update = (key: string, patch: Partial<ServiceLineDraft>) => onChange(lines.map((line) => (line.key === key ? { ...line, ...patch } : line)));

    return (
        <fieldset className="space-y-2" data-tour="sales-services">
            <legend className="text-sm font-semibold text-gray-900">{t('service_lines')}</legend>
            <p className="text-xs text-gray-600">{t('service_lines_hint')}</p>
            <FieldError id="serviceLines" messages={errors('serviceLines')} />
            {lines.length > 0 && (
                <div className="overflow-x-auto rounded border border-gray-200">
                    <table className={lineTableClass} data-testid="service-lines">
                        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-2 py-1.5">{t('service_article')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('po_line_col_quantity')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('po_line_col_price')}</th>
                                <th scope="col" className="px-2 py-1.5">{t('po_line_col_discount')}</th>
                                <th scope="col" className="px-2 py-1.5">{t('po_line_col_vat')}</th>
                                <th scope="col" className="px-2 py-1.5 text-right">{t('po_line_col_total')}</th>
                                <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('supply_remove_line')}</span></th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {lines.map((line, index) => {
                                const prefix = `serviceLines[${index}]`;
                                const article = articles.find((candidate) => String(candidate.id) === line.costArticleId);
                                const row = serviceLineRow(line, article?.vatPercentage);
                                const cell = (label: string) => ({ 'data-label': label, className: 'px-2 py-1.5 max-md:flex max-md:items-center max-md:justify-between max-md:gap-3 max-md:before:text-xs max-md:before:font-medium max-md:before:text-gray-600 max-md:before:content-[attr(data-label)]' });
                                return (
                                    <tr key={line.key} className="align-top" data-tour="sales-service-line" data-tour-key={index}>
                                        <td className="px-2 py-1.5 md:w-64">
                                            <label htmlFor={`${prefix}.costArticleId-input`} className="sr-only">{t('service_article')} {index + 1}</label>
                                            <select {...fieldProps(`${prefix}.costArticleId`, errors(`${prefix}.costArticleId`))} value={line.costArticleId} disabled={locked} className={inputClass}
                                                onChange={(event) => update(line.key, { costArticleId: event.target.value, vat: '' })}>
                                                <option value="">{t('service_choose')}</option>
                                                {(article && !article.isActive ? [article, ...choosable] : choosable).map((option) => <option key={option.id} value={option.id}>{option.name}</option>)}
                                            </select>
                                            <FieldError id={`${prefix}.costArticleId`} messages={errors(`${prefix}.costArticleId`)} />
                                        </td>
                                        <td {...cell(t('po_line_col_quantity'))}>
                                            <input {...fieldProps(`${prefix}.quantity`, errors(`${prefix}.quantity`))} value={line.quantity} disabled={locked} inputMode="decimal" aria-label={`${t('po_line_col_quantity')} ${index + 1}`}
                                                className={`${inputClass} !mt-0 w-20 text-right`} onChange={(event) => update(line.key, { quantity: event.target.value })} />
                                            <FieldError id={`${prefix}.quantity`} messages={errors(`${prefix}.quantity`)} />
                                        </td>
                                        <td {...cell(t('po_line_col_price'))}>
                                            <input {...fieldProps(`${prefix}.unitPrice`, errors(`${prefix}.unitPrice`))} value={line.unitPrice} disabled={locked} inputMode="decimal" data-decimals={2} placeholder="0,00"
                                                aria-label={`${t('po_line_col_price')} ${index + 1}`} className={`${inputClass} !mt-0 w-24 text-right`} onChange={(event) => update(line.key, { unitPrice: event.target.value })} />
                                            <FieldError id={`${prefix}.unitPrice`} messages={errors(`${prefix}.unitPrice`)} />
                                        </td>
                                        <td {...cell(t('po_line_col_discount'))}>
                                            <input {...fieldProps(`${prefix}.discountPercentage`, errors(`${prefix}.discountPercentage`))} value={line.discount} disabled={locked} inputMode="decimal"
                                                aria-label={`${t('po_line_col_discount')} ${index + 1}`} className={`${inputClass} !mt-0 w-16 text-right`} onChange={(event) => update(line.key, { discount: event.target.value })} />
                                            <FieldError id={`${prefix}.discountPercentage`} messages={errors(`${prefix}.discountPercentage`)} />
                                        </td>
                                        <td {...cell(t('po_line_col_vat'))}>
                                            <select {...fieldProps(`${prefix}.vatPercentage`, errors(`${prefix}.vatPercentage`))} value={String(row.vat)} disabled={locked} aria-label={`${t('po_line_col_vat')} ${index + 1}`}
                                                className={`${inputClass} !mt-0 w-20 text-right`} onChange={(event) => update(line.key, { vat: event.target.value })}>
                                                {VAT_RATES.map((rate) => <option key={rate} value={String(rate)}>{formatPercent(rate, locale)}%</option>)}
                                            </select>
                                        </td>
                                        <td {...cell(t('po_line_col_total'))}>
                                            <span className="block text-right font-mono" data-testid={`${prefix}.amount`}>{row.amount === null ? '–' : formatMoney(row.amount, 'EUR', locale)}</span>
                                        </td>
                                        <td className="px-2 py-1.5">
                                            <button type="button" onClick={() => onChange(lines.filter((candidate) => candidate.key !== line.key))} disabled={locked}
                                                className={secondaryButtonClass} aria-label={t('service_remove_n', { n: String(index + 1) })}>✕</button>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}
            <button type="button" className={secondaryButtonClass} disabled={locked} data-tour="sales-add-service"
                onClick={() => { if (locked) return; focusId.current = `serviceLines[${lines.length}].costArticleId-input`; onChange([...lines, emptyServiceLine(newRequestId())]); }}>
                + {t('service_add')}
            </button>
        </fieldset>
    );
}

// Diensten van een opgeslagen order (alleen lezen).
export function ServiceLinesView({ lines }: { lines: SalesOrderServiceLine[] }) {
    const { t, locale } = useTranslation();
    if (lines.length === 0) return null;
    const money = (value: number | null) => (value === null ? t('packaging_no_price') : formatMoney(value, 'EUR', locale));
    return (
        <section className="rounded border border-gray-200 bg-white p-2" data-testid="order-services" data-tour="sales-services-saved">
            <h3 className="text-xs font-semibold uppercase tracking-wide text-gray-600">{t('service_lines')}</h3>
            <ul className="mt-1 divide-y divide-gray-100 text-sm">
                {lines.map((line) => (
                    <li key={line.id} className="flex flex-wrap items-baseline justify-between gap-2 py-1">
                        <span className="text-gray-900">
                            {line.name} · {formatQuantity(line.quantity, locale)} × {money(line.unitPrice)}
                            {line.discountPercentage != null && <> · {t('service_discount', { discount: formatPercent(line.discountPercentage, locale) })}</>}
                        </span>
                        <span className="font-mono text-gray-900">{line.lineTotal === null ? '–' : money(line.lineTotal)}
                            <span className="ml-1 font-sans text-xs text-gray-600">{t('packaging_vat', { vat: formatPercent(line.vatPercentage, locale) })}</span></span>
                    </li>
                ))}
            </ul>
            <p className="mt-1 text-xs text-gray-600">{t('service_saved_hint')}</p>
        </section>
    );
}

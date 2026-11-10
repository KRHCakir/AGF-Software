'use client';

import { useEffect, useRef } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney } from '../../lib/availability';
import { type PackagingLineDraft, defaultPriceText, packagingLineTotal, palletArticle } from '../../lib/packaging';
import { newRequestId } from '../../lib/requestId';
import type { CostArticle } from '../../types/costArticle';
import type { SalesOrderPackagingLine } from '../../types/salesOrder';
import FieldError, { fieldProps, inputClass, lineTableClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

const emptyPackagingLine = (): PackagingLineDraft => ({ key: newRequestId(), costArticleId: '', quantity: '', unitPrice: '' });

// Emballage (pallets, kratten) in het orderformulier, onder de goederen: artikel, heel aantal en prijs per stuk (voorgevuld met de
// standaardprijs, aanpasbaar; 0,00 bij een gratis ruil). Tab gaat per regel artikel → aantal → prijs en dan naar de volgende regel.
// "Pallet toevoegen" zet een regel Pallet wegwerp neer met het palletvoorstel uit de goederenregels; alleen na die klik, altijd aanpasbaar.
export function PackagingLinesInput({ lines, articles, proposal, locked, errors, onChange }: {
    lines: PackagingLineDraft[];
    articles: CostArticle[];
    proposal: number;
    locked: boolean;
    errors: (key: string) => string[] | undefined;
    onChange: (lines: PackagingLineDraft[]) => void;
}) {
    const { t, locale } = useTranslation();
    const focusId = useRef<string | null>(null);
    const pallet = palletArticle(articles);
    const choosable = articles.filter((article) => article.isActive);

    useEffect(() => {
        if (focusId.current === null) return;
        const element = document.getElementById(focusId.current);
        focusId.current = null;
        element?.focus();
    }, [lines]);

    const update = (key: string, patch: Partial<PackagingLineDraft>) => onChange(lines.map((line) => (line.key === key ? { ...line, ...patch } : line)));
    const add = (line: PackagingLineDraft, focus: 'costArticleId' | 'quantity') => {
        if (locked) return;
        focusId.current = `packagingLines[${lines.length}].${focus}-input`;
        onChange([...lines, line]);
    };

    return (
        <fieldset className="space-y-2" data-tour="sales-packaging">
            <legend className="text-sm font-semibold text-gray-900">{t('packaging_lines')}</legend>
            <p className="text-xs text-gray-600">{t('packaging_lines_hint')}</p>
            <FieldError id="packagingLines" messages={errors('packagingLines')} />
            {lines.length > 0 && (
                <div className="overflow-x-auto rounded border border-gray-200">
                    <table className={lineTableClass}>
                        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-2 py-1.5">{t('packaging_article')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('packaging_quantity')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('packaging_unit_price')}</th>
                                <th scope="col" className="px-2 py-1.5 text-right">{t('packaging_amount')}</th>
                                <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('supply_remove_line')}</span></th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {lines.map((line, index) => {
                                const prefix = `packagingLines[${index}]`;
                                const article = articles.find((candidate) => String(candidate.id) === line.costArticleId);
                                const total = packagingLineTotal(line);
                                return (
                                    <tr key={line.key} className="align-top" data-tour="sales-packaging-line" data-tour-key={index}>
                                        <td className="px-2 py-1.5 md:w-72">
                                            <label htmlFor={`${prefix}.costArticleId-input`} className="sr-only">{t('packaging_article')} {index + 1}</label>
                                            <select {...fieldProps(`${prefix}.costArticleId`, errors(`${prefix}.costArticleId`))} value={line.costArticleId} disabled={locked} className={inputClass}
                                                onChange={(event) => {
                                                    const picked = articles.find((candidate) => String(candidate.id) === event.target.value);
                                                    update(line.key, { costArticleId: event.target.value, unitPrice: defaultPriceText(picked) });
                                                }}>
                                                <option value="">{t('packaging_choose')}</option>
                                                {(article && !article.isActive ? [article, ...choosable] : choosable).map((option) => <option key={option.id} value={option.id}>{option.name}</option>)}
                                            </select>
                                            <FieldError id={`${prefix}.costArticleId`} messages={errors(`${prefix}.costArticleId`)} />
                                        </td>
                                        <td className="px-2 py-1.5 md:w-28">
                                            <label htmlFor={`${prefix}.quantity-input`} className="sr-only">{t('packaging_quantity')} {index + 1}</label>
                                            <input {...fieldProps(`${prefix}.quantity`, errors(`${prefix}.quantity`))} value={line.quantity} disabled={locked} inputMode="numeric" className={inputClass}
                                                onChange={(event) => update(line.key, { quantity: event.target.value })} />
                                            <FieldError id={`${prefix}.quantity`} messages={errors(`${prefix}.quantity`)} />
                                        </td>
                                        <td className="px-2 py-1.5 md:w-40">
                                            <label htmlFor={`${prefix}.unitPrice-input`} className="sr-only">{t('packaging_unit_price')} {index + 1}</label>
                                            <input {...fieldProps(`${prefix}.unitPrice`, errors(`${prefix}.unitPrice`))} value={line.unitPrice} disabled={locked} inputMode="decimal" placeholder="0,00" className={inputClass}
                                                onChange={(event) => update(line.key, { unitPrice: event.target.value })} />
                                            <span className="block text-xs text-gray-500">{article?.vatPercentage != null ? t('packaging_vat', { vat: String(article.vatPercentage).replace('.', locale === 'nl' ? ',' : '.') }) : ''}</span>
                                            <FieldError id={`${prefix}.unitPrice`} messages={errors(`${prefix}.unitPrice`)} />
                                        </td>
                                        <td className="px-2 py-1.5 text-right font-mono" data-testid={`${prefix}.amount`}>{total === null ? '–' : formatMoney(total, null, locale)}</td>
                                        <td className="px-2 py-1.5">
                                            <button type="button" tabIndex={-1} onClick={() => onChange(lines.filter((candidate) => candidate.key !== line.key))} disabled={locked}
                                                className={secondaryButtonClass} aria-label={t('packaging_remove_n', { n: String(index + 1) })}>✕</button>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}
            <div className="flex flex-wrap items-center gap-2">
                <button type="button" className={secondaryButtonClass} disabled={locked} data-tour="sales-add-packaging" onClick={() => add(emptyPackagingLine(), 'costArticleId')}>
                    + {t('packaging_add')}
                </button>
                {pallet !== null && (
                    <>
                        <button type="button" className={secondaryButtonClass} disabled={locked} data-tour="sales-add-pallet"
                            onClick={() => add({ ...emptyPackagingLine(), costArticleId: String(pallet.id), quantity: String(proposal), unitPrice: defaultPriceText(pallet) }, 'quantity')}>
                            + {t('packaging_add_pallet')}
                        </button>
                        <span className="text-xs text-gray-600" data-testid="packaging-pallet-proposal">{t('packaging_pallet_proposal', { count: String(proposal), name: pallet.name })}</span>
                    </>
                )}
            </div>
        </fieldset>
    );
}

// Emballage van een opgeslagen order (alleen lezen): staat los van reserveren en verzenden en gaat later mee naar de factuur.
export function PackagingLinesView({ lines }: { lines: SalesOrderPackagingLine[] }) {
    const { t, locale } = useTranslation();
    if (lines.length === 0) return null;
    const money = (value: number | null) => (value === null ? t('packaging_no_price') : formatMoney(value, null, locale));
    return (
        <section className="rounded border border-gray-200 bg-white p-2" data-testid="order-packaging" data-tour="sales-packaging-saved">
            <h3 className="text-xs font-semibold uppercase tracking-wide text-gray-600">{t('packaging_lines')}</h3>
            <ul className="mt-1 divide-y divide-gray-100 text-sm">
                {lines.map((line) => (
                    <li key={line.id} className="flex flex-wrap items-baseline justify-between gap-2 py-1">
                        <span className="text-gray-900">{line.name} · {t('packaging_pieces', { count: String(line.quantity) })} × {money(line.unitPrice)}</span>
                        <span className="font-mono text-gray-900">{line.lineTotal === null ? '–' : money(line.lineTotal)}
                            <span className="ml-1 font-sans text-xs text-gray-600">{t('packaging_vat', { vat: String(line.vatPercentage).replace('.', locale === 'nl' ? ',' : '.') })}</span></span>
                    </li>
                ))}
            </ul>
            <p className="mt-1 text-xs text-gray-600">{t('packaging_saved_hint')}</p>
        </section>
    );
}

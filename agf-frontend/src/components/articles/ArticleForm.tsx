'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ArticleDraft, buildArticlePayload, draftFromArticle, emptyArticleDraft, formatGoodsCode, groupArticleErrors } from '../../lib/articleRules';
import { isMacPlatform, matchShortcut, shortcutLabel } from '../../lib/shortcuts';
import { unitLabelKey } from '../../lib/units';
import { WEIGHT_METHODS, isWeightMethod } from '../../lib/weight';
import { ApiError, toApiError } from '../../services/apiClient';
import { createArticle, updateArticle } from '../../services/articleService';
import { PACKAGE_UNITS } from '../../types/availability';
import type { Article, ArticleReference, GoodsCodeSystem } from '../../types/article';
import type { CountryReference } from '../../types/country';
import CountryPicker from '../shared/CountryPicker';
import { GOODS_CODE_SYSTEMS, isGoodsCodeSystem } from '../../types/article';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import GoodsCodeLookup from './GoodsCodeLookup';

// Eén compact artikelformulier: identificatie en uitvoering direct zichtbaar, verpakking en gewicht ernaast, handels- en
// aanvullende gegevens ingeklapt (open zodra er iets in staat). Alleen de productsoort is verplicht; leeg blijft leeg.
// Inline validatie, invoer blijft staan bij fouten, ⌘S/Ctrl+S slaat op. Artikelgegevens zijn standaarden voor nieuwe
// invoer en raken nooit bestaande documenten of voorraad.

const codeKey = (code: string) => `articles_error_${code}`;

function hasTradeValues(draft: ArticleDraft): boolean {
    return draft.goodsCode !== '' || draft.goodsCodeSystem !== '' || draft.tradeClass !== '' || draft.season !== '' || draft.notes !== '';
}

export default function ArticleForm({ article, reference, countries, onSaved, onClose }: {
    article: Article | null;
    reference: ArticleReference;
    countries: CountryReference;
    onSaved: (article: Article, created: boolean) => void;
    onClose: () => void;
}) {
    const { t } = useTranslation();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    const [draft, setDraft] = useState<ArticleDraft>(() => (article === null ? emptyArticleDraft() : draftFromArticle(article)));
    const [errors, setErrors] = useState<FieldErrors>({});
    const [serverError, setServerError] = useState<ApiError | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [tradeOpen, setTradeOpen] = useState<boolean>(() => article !== null && hasTradeValues(draftFromArticle(article)));
    const [lookupOpen, setLookupOpen] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);
    const inFlight = useRef(false);
    const firstInput = useRef<HTMLInputElement | null>(null);

    useEffect(() => { firstInput.current?.focus(); }, []);

    const update = (patch: Partial<ArticleDraft>) => {
        setDraft((current) => ({ ...current, ...patch }));
        setErrors({});
        setServerError(null);
        setNotice(null);
    };
    const e = (key: string) => errors[key];
    const unit = draft.packagingUnit === '' ? t('articles_unit_generic') : t(unitLabelKey(draft.packagingUnit)).toLowerCase();

    const submit = async () => {
        if (inFlight.current) return;
        const built = buildArticlePayload(draft);
        if (!built.ok) {
            const grouped = groupArticleErrors(built.errors);
            setErrors(Object.fromEntries(Object.entries(grouped).map(([field, codes]) => [field, codes.map((code) => t(codeKey(code)))])));
            if (Object.keys(grouped).some((field) => ['goodsCode', 'goodsCodeSystem', 'tradeClass', 'season', 'notes'].includes(field))) setTradeOpen(true);
            const first = Object.keys(grouped)[0];
            setTimeout(() => document.getElementById(`${first}-input`)?.focus(), 0);
            return;
        }
        inFlight.current = true;
        setSubmitting(true);
        setServerError(null);
        try {
            const saved = article === null ? await createArticle(built.value) : await updateArticle(article.id, built.value);
            setNotice(t(article === null ? 'articles_created' : 'articles_saved', { description: saved.description }));
            setDraft(draftFromArticle(saved));
            onSaved(saved, article === null);
        } catch (caught) {
            const apiError = toApiError(caught);
            setServerError(apiError);
            setErrors(apiError.fieldErrors);
            if (Object.keys(apiError.fieldErrors).some((field) => ['goodsCode', 'goodsCodeSystem', 'tradeClass'].includes(field))) setTradeOpen(true);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    // ⌘S/Ctrl+S slaat op, ⌥E/Alt+E klapt de handelsgegevens open of dicht; de pagina zelf regelt ⌥F en ⌥N.
    const handler = useRef<(event: KeyboardEvent) => void>(() => {});
    handler.current = (event: KeyboardEvent) => {
        const action = matchShortcut(event, isMac);
        if (action === 'save') { event.preventDefault(); void submit(); }
        if (action === 'toggleExtra') { event.preventDefault(); setTradeOpen((open) => !open); }
    };
    useEffect(() => {
        const listener = (event: KeyboardEvent) => handler.current(event);
        document.addEventListener('keydown', listener, true);
        return () => document.removeEventListener('keydown', listener, true);
    }, []);

    // De referentielijst zoekt binnen het gekozen stelsel en levert alleen codes van dat stelsel; overnemen verandert het stelsel dus nooit.
    const pickGoodsCode = (code: string, system: GoodsCodeSystem) => {
        if (system !== draft.goodsCodeSystem) return;
        update({ goodsCode: formatGoodsCode(code) });
        setLookupOpen(false);
        setNotice(t('articles_goods_picked', { code: formatGoodsCode(code), system }));
        setTimeout(() => document.getElementById('goodsCode-input')?.focus(), 0);
    };
    const enterGoodsCodeManually = () => {
        setLookupOpen(false);
        setTimeout(() => document.getElementById('goodsCode-input')?.focus(), 0);
    };

    const textField = (field: keyof ArticleDraft & string, labelKey: string, options: { ref?: boolean; hintKey?: string; list?: string; mono?: boolean; maxLength?: number; span?: string } = {}) => (
        <div className={options.span ?? ''}>
            <label htmlFor={`${field}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{field === 'productName' ? ' *' : ''}</label>
            <input {...fieldProps(field, e(field))} ref={options.ref ? firstInput : undefined} value={draft[field]} list={options.list} maxLength={options.maxLength ?? 100} disabled={submitting}
                onChange={(event) => update({ [field]: event.target.value } as Partial<ArticleDraft>)} className={`${inputClass}${options.mono ? ' font-mono' : ''}`} />
            {options.hintKey && <span className="block text-xs text-gray-500">{t(options.hintKey)}</span>}
            <FieldError id={field} messages={e(field)} />
        </div>
    );

    return (
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }} className="space-y-3 rounded-md border border-gray-300 bg-white p-3" data-testid="article-form">
            <div className="flex flex-wrap items-start justify-between gap-2">
                <h2 className="text-lg font-semibold text-gray-900">{article === null ? t('articles_new') : t('articles_edit', { id: String(article.id) })}</h2>
                <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('articles_cancel')}</button>
            </div>
            {notice && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{notice}</p>}
            {serverError && <ErrorNotice error={serverError} />}

            <fieldset className="grid grid-cols-2 gap-3 md:grid-cols-4" disabled={submitting}>
                <legend className="px-1 text-sm font-semibold text-gray-900">{t('articles_section_identity')}</legend>
                {textField('shortcutCode', 'articles_field_shortcut', { ref: true, hintKey: 'articles_hint_shortcut', mono: true, maxLength: 32 })}
                {textField('productName', 'articles_field_product', { list: 'article-product-names' })}
                <datalist id="article-product-names">{reference.productNames.map((name) => <option key={name} value={name} />)}</datalist>
                {textField('variantName', 'articles_field_variant')}
                <div>
                    <label htmlFor="originCountryCode-input" className="text-xs font-medium text-gray-700">{t('articles_field_origin')} *</label>
                    <CountryPicker id="originCountryCode" dataTour="article-origin" countries={countries} value={draft.originCountryCode === '' ? null : draft.originCountryCode}
                        onChange={(code) => update({ originCountryCode: code ?? '' })} disabled={submitting} invalid={e('originCountryCode') !== undefined} describedBy="originCountryCode-error" />
                    {article !== null && !article.originCountryCode && article.origin && (
                        <span className="block text-xs text-amber-900">{t('articles_origin_old_text', { origin: article.origin })}</span>
                    )}
                    <FieldError id="originCountryCode" messages={e('originCountryCode')} />
                </div>
                {textField('brand', 'articles_field_brand')}
                {textField('sizeCategory', 'articles_field_size')}
                <div>
                    <label htmlFor="packagingUnit-input" className="text-xs font-medium text-gray-700">{t('articles_field_packaging_unit')}</label>
                    <select {...fieldProps('packagingUnit', e('packagingUnit'))} value={draft.packagingUnit} disabled={submitting} className={inputClass}
                        onChange={(event) => update({ packagingUnit: event.target.value as ArticleDraft['packagingUnit'] })}>
                        <option value="">–</option>
                        {PACKAGE_UNITS.map((option) => <option key={option} value={option}>{t(unitLabelKey(option))}</option>)}
                    </select>
                    <FieldError id="packagingUnit" messages={e('packagingUnit')} />
                </div>
                {textField('packaging', 'articles_field_packaging')}
            </fieldset>

            <fieldset className="grid grid-cols-2 gap-3 md:grid-cols-4" disabled={submitting}>
                <legend className="px-1 text-sm font-semibold text-gray-900">{t('articles_section_weight')}</legend>
                <div>
                    <label htmlFor="weightMethod-input" className="text-xs font-medium text-gray-700">{t('articles_field_weight_method')}</label>
                    <select {...fieldProps('weightMethod', e('weightMethod'))} value={draft.weightMethod} disabled={submitting} className={inputClass}
                        onChange={(event) => { if (isWeightMethod(event.target.value)) update({ weightMethod: event.target.value }); }}>
                        {WEIGHT_METHODS.map((method) => <option key={method} value={method}>{t(method === 'Weighed' ? 'products_weight_method_weighed' : 'products_weight_method_calculated')}</option>)}
                    </select>
                    <FieldError id="weightMethod" messages={e('weightMethod')} />
                </div>
                <div>
                    <label htmlFor="defaultNetKgPerPackage-input" className="text-xs font-medium text-gray-700">{t('articles_field_net_kg', { unit })}</label>
                    <input {...fieldProps('defaultNetKgPerPackage', e('defaultNetKgPerPackage'))} inputMode="decimal" value={draft.defaultNetKgPerPackage} disabled={submitting}
                        onChange={(event) => update({ defaultNetKgPerPackage: event.target.value })} className={inputClass} />
                    <FieldError id="defaultNetKgPerPackage" messages={e('defaultNetKgPerPackage')} />
                </div>
                <div>
                    <label htmlFor="defaultGrossKgPerPackage-input" className="text-xs font-medium text-gray-700">{t('articles_field_gross_kg', { unit })}</label>
                    <input {...fieldProps('defaultGrossKgPerPackage', e('defaultGrossKgPerPackage'))} inputMode="decimal" value={draft.defaultGrossKgPerPackage} disabled={submitting}
                        onChange={(event) => update({ defaultGrossKgPerPackage: event.target.value })} className={inputClass} />
                    <FieldError id="defaultGrossKgPerPackage" messages={e('defaultGrossKgPerPackage')} />
                </div>
                <div>
                    <label htmlFor="defaultPackagesPerPallet-input" className="text-xs font-medium text-gray-700">{t('articles_field_cpp', { unit })}</label>
                    <input {...fieldProps('defaultPackagesPerPallet', e('defaultPackagesPerPallet'))} inputMode="decimal" value={draft.defaultPackagesPerPallet} disabled={submitting}
                        onChange={(event) => update({ defaultPackagesPerPallet: event.target.value })} className={inputClass} />
                    <FieldError id="defaultPackagesPerPallet" messages={e('defaultPackagesPerPallet')} />
                </div>
            </fieldset>

            {/* Handelsgegevens en aanvullend: ingeklapt, open zodra er iets in staat of een fout erin zit. */}
            <section className="rounded-md border border-gray-200">
                <button type="button" aria-expanded={tradeOpen} aria-controls="article-trade" onClick={() => setTradeOpen((open) => !open)}
                    className="flex w-full items-center justify-between px-3 py-2 text-left text-sm font-semibold text-gray-900">
                    <span>{tradeOpen ? '▾' : '▸'} {t('articles_section_trade')}{!tradeOpen && hasTradeValues(draft) ? ` · ${[draft.goodsCode && `${draft.goodsCodeSystem} ${formatGoodsCode(draft.goodsCode)}`, draft.tradeClass].filter(Boolean).join(' · ')}` : ''}</span>
                    <kbd className="text-xs font-normal text-gray-500">{shortcutLabel('toggleExtra', isMac)}</kbd>
                </button>
                {tradeOpen && (
                    <div id="article-trade" className="grid grid-cols-2 gap-3 border-t border-gray-200 p-3 md:grid-cols-4">
                        <div>
                            <label htmlFor="goodsCodeSystem-input" className="text-xs font-medium text-gray-700">{t('articles_field_goods_system')}</label>
                            <select {...fieldProps('goodsCodeSystem', e('goodsCodeSystem'))} value={draft.goodsCodeSystem} disabled={submitting} className={inputClass}
                                onChange={(event) => update({ goodsCodeSystem: isGoodsCodeSystem(event.target.value) ? event.target.value : '' })}>
                                <option value="">–</option>
                                {GOODS_CODE_SYSTEMS.map((system) => <option key={system} value={system}>{t(`articles_goods_system_${system}`)}</option>)}
                            </select>
                            <FieldError id="goodsCodeSystem" messages={e('goodsCodeSystem')} />
                        </div>
                        <div>
                            <label htmlFor="goodsCode-input" className="text-xs font-medium text-gray-700">{t('articles_field_goods_code')}</label>
                            <div className="flex gap-1">
                                <input {...fieldProps('goodsCode', e('goodsCode'))} inputMode="numeric" value={draft.goodsCode} maxLength={14} disabled={submitting}
                                    onChange={(event) => update({ goodsCode: event.target.value })} className={`${inputClass} font-mono`} />
                                <button type="button" tabIndex={-1} className={`${secondaryButtonClass} mt-1 shrink-0 px-2`} aria-expanded={lookupOpen} onClick={() => setLookupOpen((open) => !open)} disabled={submitting}>🔍</button>
                            </div>
                            <span className="block text-xs text-gray-500">{t('articles_hint_goods_code')}</span>
                            <FieldError id="goodsCode" messages={e('goodsCode')} />
                        </div>
                        <div>
                            <label htmlFor="tradeClass-input" className="text-xs font-medium text-gray-700">{t('articles_field_trade_class')}</label>
                            <input {...fieldProps('tradeClass', e('tradeClass'))} value={draft.tradeClass} list="article-trade-classes" maxLength={50} disabled={submitting}
                                onChange={(event) => update({ tradeClass: event.target.value })} className={inputClass} />
                            <datalist id="article-trade-classes">{reference.tradeClassSuggestions.map((option) => <option key={option} value={option} />)}</datalist>
                            <span className="block text-xs text-gray-500">{t('articles_hint_trade_class')}</span>
                            <FieldError id="tradeClass" messages={e('tradeClass')} />
                        </div>
                        {textField('season', 'articles_field_season')}
                        {lookupOpen && (
                            <div className="col-span-2 md:col-span-4">
                                <GoodsCodeLookup system={draft.goodsCodeSystem} onSystemChange={(system) => update({ goodsCodeSystem: system })} onPick={pickGoodsCode} onManual={enterGoodsCodeManually} disabled={submitting} />
                            </div>
                        )}
                        <div className="col-span-2 md:col-span-4">
                            <label htmlFor="notes-input" className="text-xs font-medium text-gray-700">{t('articles_field_notes')}</label>
                            <textarea {...fieldProps('notes', e('notes'))} rows={2} value={draft.notes} maxLength={500} disabled={submitting} onChange={(event) => update({ notes: event.target.value })} className={inputClass} />
                            <FieldError id="notes" messages={e('notes')} />
                        </div>
                    </div>
                )}
            </section>

            <div className="flex flex-wrap items-center gap-2 border-t border-gray-200 pt-3">
                <button type="submit" className={primaryButtonClass} disabled={submitting}>{submitting ? t('supply_saving') : t('articles_save')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('save', isMac)}</kbd></button>
                <span className="text-xs text-gray-600">{t('articles_hint_defaults')}</span>
            </div>
            <details className="text-xs text-gray-600">
                <summary className="cursor-pointer">{t('articles_used_by_title')}</summary>
                <ul className="mt-1 list-disc pl-5">
                    <li>{t('articles_used_by_purchase')}</li>
                    <li>{t('articles_used_by_sales')}</li>
                    <li>{t('articles_used_by_supply')}</li>
                    <li>{t('articles_used_by_none')}</li>
                </ul>
            </details>
        </form>
    );
}

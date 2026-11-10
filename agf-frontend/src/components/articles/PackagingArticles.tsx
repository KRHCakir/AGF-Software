'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { costArticleNameKey, MAX_COST_ARTICLE_NAME_LENGTH } from '../../lib/costArticles';
import { formatAmountInput, formatDecimalInput, parseLocaleDecimal } from '../../lib/decimal';
import { listRowClass } from '../../lib/listRows';
import { parseUnitPrice } from '../../lib/packaging';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { type PackagingArticleInput, createPackagingArticle, listPackagingArticles, setPackagingArticleActive, updatePackagingArticle } from '../../services/costArticleService';
import type { CostArticle } from '../../types/costArticle';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

interface PriceDraft { price: string; vat: string }

const STANDARD_VAT = '9';

// Btw-percentage: 0 tot en met 100 met hoogstens 2 decimalen; null bij ongeldige invoer.
function parseVat(text: string): number | null {
    const parsed = parseLocaleDecimal(text, undefined, { maxDecimals: 2, maxDigits: 3, allowZero: true });
    return parsed.ok && parsed.value <= 100 ? parsed.value : null;
}

// Emballage (fust) bij Artikelen: Pallet wegwerp, Euro-pallet en Krat staan er al; eigen soorten (Fust CBL, Doos) voeg je hier toe.
// Per artikel een standaardprijs per stuk (excl. btw, leeg = geen) en btw; de orderregel neemt die over en blijft aanpasbaar. Geen
// voorraad, maat, klasse of herkomst. Nooit verwijderen: orderregels en factuurregels verwijzen ernaar. Inactief = niet meer te kiezen.
export default function PackagingArticles() {
    const { t } = useTranslation();
    const [articles, setArticles] = useState<CostArticle[] | null>(null);
    const [drafts, setDrafts] = useState<Record<number, PriceDraft>>({});
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    const [notice, setNotice] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [reloadToken, setReloadToken] = useState(0);
    const [added, setAdded] = useState<{ name: string } & PriceDraft>({ name: '', price: '', vat: STANDARD_VAT });
    const [requestId, setRequestId] = useState(newRequestId);

    useEffect(() => {
        const controller = new AbortController();
        listPackagingArticles(true, controller.signal)
            .then((list) => { setArticles(list); setLoadError(null); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, [reloadToken]);

    const draftOf = (article: CostArticle): PriceDraft => drafts[article.id]
        ?? { price: article.defaultUnitPrice == null ? '' : formatAmountInput(article.defaultUnitPrice), vat: formatDecimalInput(article.vatPercentage ?? 9, 2) };
    const changed = (article: CostArticle, patch: Partial<PriceDraft>) => { setDrafts((current) => ({ ...current, [article.id]: { ...draftOf(article), ...patch } })); setNotice(null); };

    // Prijs en btw controleren zoals de server; fouten onder het veld met deze prefix.
    const prices = (draft: PriceDraft, prefix: string, found: FieldErrors): PackagingArticleInput | null => {
        const price = parseUnitPrice(draft.price);
        const vat = parseVat(draft.vat);
        if (!price.ok) found[`${prefix}.defaultUnitPrice`] = [t('packaging_error_price')];
        if (vat === null) found[`${prefix}.vatPercentage`] = [t('packaging_error_vat')];
        return price.ok && vat !== null ? { defaultUnitPrice: price.value, vatPercentage: vat } : null;
    };

    const run = async (action: () => Promise<void>) => {
        if (busy) return;
        setBusy(true);
        setError(null);
        try {
            await action();
            setReloadToken((n) => n + 1);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    const save = (article: CostArticle) => {
        const found: FieldErrors = {};
        const input = prices(draftOf(article), `packaging-${article.id}`, found);
        setErrors(found);
        if (input === null) return;
        void run(async () => {
            await updatePackagingArticle(article.id, input);
            setDrafts((current) => { const next = { ...current }; delete next[article.id]; return next; });
            setNotice(t('packaging_article_saved', { name: article.name }));
        });
    };

    const add = (event: FormEvent) => {
        event.preventDefault();
        const found: FieldErrors = {};
        const key = costArticleNameKey(added.name);
        const existing = articles?.find((article) => costArticleNameKey(article.name) === key);
        if (key === null) found['packaging-new.name'] = [t('supply_error_required')];
        else if (existing) found['packaging-new.name'] = [t('cost_article_exists', { name: existing.name })];
        const input = prices(added, 'packaging-new', found);
        setErrors(found);
        if (input === null || Object.keys(found).length > 0) return;
        void run(async () => {
            try {
                await createPackagingArticle({ requestId, name: added.name.trim(), ...input });
            } catch (caught) {
                if (toApiError(caught).status !== 409) throw caught;
                setErrors({ 'packaging-new.name': [t('cost_article_exists', { name: added.name.trim() })] });
                return;
            }
            setNotice(t('packaging_article_saved', { name: added.name.trim() }));
            setAdded({ name: '', price: '', vat: STANDARD_VAT });
            setRequestId(newRequestId());
        });
    };

    const e = (key: string) => errors[key];
    const priceFields = (prefix: string, draft: PriceDraft, onChange: (patch: Partial<PriceDraft>) => void, label: boolean) => (
        <>
            <div className="w-32">
                <label htmlFor={`${prefix}.defaultUnitPrice-input`} className={label ? 'text-xs font-medium text-gray-700' : 'sr-only'}>{t('packaging_default_price')}</label>
                <input {...fieldProps(`${prefix}.defaultUnitPrice`, e(`${prefix}.defaultUnitPrice`))} value={draft.price} inputMode="decimal" placeholder={t('packaging_no_price')}
                    onChange={(event) => onChange({ price: event.target.value })} className={`${inputClass} bg-white`} />
                <FieldError id={`${prefix}.defaultUnitPrice`} messages={e(`${prefix}.defaultUnitPrice`)} />
            </div>
            <div className="w-20">
                <label htmlFor={`${prefix}.vatPercentage-input`} className={label ? 'text-xs font-medium text-gray-700' : 'sr-only'}>{t('packaging_vat_label')}</label>
                <input {...fieldProps(`${prefix}.vatPercentage`, e(`${prefix}.vatPercentage`))} value={draft.vat} inputMode="decimal"
                    onChange={(event) => onChange({ vat: event.target.value })} className={`${inputClass} bg-white`} />
                <FieldError id={`${prefix}.vatPercentage`} messages={e(`${prefix}.vatPercentage`)} />
            </div>
        </>
    );

    return (
        <section className="space-y-3 rounded-md border border-gray-300 bg-white p-3" data-testid="packaging-articles" data-tour="packaging-articles">
            <div>
                <h2 className="text-lg font-semibold text-gray-900">{t('packaging_articles_title')}</h2>
                <p className="text-sm text-gray-600">{t('packaging_articles_hint')}</p>
            </div>
            {loadError && <ErrorNotice error={loadError} />}
            {error && <ErrorNotice error={error} />}
            {notice && <p role="status" className="rounded border border-green-300 bg-green-50 px-2 py-1 text-sm text-green-900">{notice}</p>}
            {articles !== null && (
                <ul className="divide-y divide-gray-100 rounded border border-gray-200 text-sm">
                    {articles.map((article, index) => (
                        <li key={article.id} className={`flex flex-wrap items-start gap-2 px-2 py-1.5 ${listRowClass(index)}`} data-tour="packaging-article" data-tour-key={article.name}>
                            <span className={`min-w-[10rem] flex-1 pt-2 ${article.isActive ? 'text-gray-900' : 'text-gray-500 line-through'}`}>
                                {article.name}
                                {article.isStandard && <span className="ml-2 rounded bg-gray-100 px-1 text-xs text-gray-700">{t('cost_article_standard')}</span>}
                                {article.lineCount > 0 && <span className="ml-2 text-xs text-gray-600">{t('packaging_used', { count: String(article.lineCount) })}</span>}
                            </span>
                            {priceFields(`packaging-${article.id}`, draftOf(article), (patch) => changed(article, patch), false)}
                            <button type="button" className={`${primaryButtonClass} mt-1`} disabled={busy || drafts[article.id] === undefined} onClick={() => save(article)}>{t('packaging_save')}</button>
                            <button type="button" className={`${secondaryButtonClass} mt-1`} disabled={busy} onClick={() => void run(async () => { await setPackagingArticleActive(article.id, !article.isActive); })}>
                                {t(article.isActive ? 'cost_article_deactivate' : 'cost_article_activate')}
                            </button>
                        </li>
                    ))}
                </ul>
            )}
            <form noValidate onSubmit={add} className="flex flex-wrap items-start gap-2">
                <div className="min-w-[12rem] flex-1">
                    <label htmlFor="packaging-new.name-input" className="text-xs font-medium text-gray-700">{t('packaging_new')}</label>
                    <input {...fieldProps('packaging-new.name', e('packaging-new.name'))} value={added.name} maxLength={MAX_COST_ARTICLE_NAME_LENGTH} placeholder={t('packaging_new_placeholder')}
                        onChange={(event) => setAdded((current) => ({ ...current, name: event.target.value }))} className={inputClass} />
                    <FieldError id="packaging-new.name" messages={e('packaging-new.name')} />
                </div>
                {priceFields('packaging-new', added, (patch) => setAdded((current) => ({ ...current, ...patch })), true)}
                <button type="submit" className={`${primaryButtonClass} mt-5`} disabled={busy} data-tour="packaging-add">{t('cost_article_add')}</button>
            </form>
        </section>
    );
}

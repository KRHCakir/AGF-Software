'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_COST_ARTICLE_NAME_LENGTH, costArticleNameKey } from '../../lib/costArticles';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { createCostArticle, listCostArticles, setCostArticleActive } from '../../services/costArticleService';
import type { CostArticle } from '../../types/costArticle';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Kostenartikelen (type Dienst) beheren: de standaardsoorten (Handling, Transport, QC, Zeevracht, Afval, Overig) staan er al; eigen soorten
// (Elektra, Autohuur) voeg je hier toe. Nooit verwijderen: factuurregels en partijkosten verwijzen ernaar. Inactief = niet meer te kiezen.
export default function CostArticlesAdmin() {
    const { t } = useTranslation();
    const [articles, setArticles] = useState<CostArticle[] | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [name, setName] = useState('');
    const [requestId, setRequestId] = useState(newRequestId);
    const [errors, setErrors] = useState<string[] | undefined>(undefined);
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);
    const [reloadToken, setReloadToken] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        listCostArticles(true, controller.signal)
            .then((list) => { setArticles(list); setLoadError(null); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, [reloadToken]);

    const add = async (event: FormEvent) => {
        event.preventDefault();
        if (busy) return;
        const key = costArticleNameKey(name);
        if (key === null) { setErrors([t('supply_error_required')]); return; }
        const existing = articles?.find((article) => costArticleNameKey(article.name) === key);
        if (existing) { setErrors([t('cost_article_exists', { name: existing.name })]); return; }
        setBusy(true);
        setError(null);
        try {
            await createCostArticle({ requestId, name: name.trim() });
            setName('');
            setRequestId(newRequestId());
            setReloadToken((n) => n + 1);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (apiError.status === 409) setErrors([t('cost_article_exists', { name: name.trim() })]);
            else { setError(apiError); setErrors(apiError.fieldErrors.name); }
        } finally {
            setBusy(false);
        }
    };

    const toggle = async (article: CostArticle) => {
        if (busy) return;
        setBusy(true);
        setError(null);
        try {
            await setCostArticleActive(article.id, !article.isActive);
            setReloadToken((n) => n + 1);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    return (
        <section id="kostenartikelen" className="space-y-3 rounded-md border border-gray-300 bg-white p-3" data-testid="cost-articles-admin" data-tour="settings-cost-articles">
            <div>
                <h2 className="text-lg font-semibold text-gray-900">{t('cost_articles_title')}</h2>
                <p className="text-sm text-gray-600">{t('cost_articles_hint')}</p>
            </div>
            {loadError && <ErrorNotice error={loadError} />}
            {error && <ErrorNotice error={error} />}
            {articles !== null && (
                <ul className="divide-y divide-gray-100 text-sm">
                    {articles.map((article) => (
                        <li key={article.id} className="flex flex-wrap items-center justify-between gap-2 py-1.5" data-tour="cost-article" data-tour-key={article.name}>
                            <span className={article.isActive ? 'text-gray-900' : 'text-gray-500 line-through'}>
                                {article.name}
                                {article.isStandard && <span className="ml-2 rounded bg-gray-100 px-1 text-xs text-gray-700">{t('cost_article_standard')}</span>}
                                {article.lineCount > 0 && <span className="ml-2 text-xs text-gray-600">{t('cost_article_used', { count: String(article.lineCount) })}</span>}
                            </span>
                            <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void toggle(article)}>
                                {t(article.isActive ? 'cost_article_deactivate' : 'cost_article_activate')}
                            </button>
                        </li>
                    ))}
                </ul>
            )}
            <form noValidate onSubmit={add} className="flex flex-wrap items-end gap-2">
                <div className="min-w-[12rem] flex-1">
                    <label htmlFor="costArticleName-input" className="text-xs font-medium text-gray-700">{t('cost_article_new')}</label>
                    <input {...fieldProps('costArticleName', errors)} value={name} maxLength={MAX_COST_ARTICLE_NAME_LENGTH} placeholder={t('cost_article_new_placeholder')}
                        onChange={(event) => { setName(event.target.value); setErrors(undefined); }} className={inputClass} />
                    <FieldError id="costArticleName" messages={errors} />
                </div>
                <button type="submit" className={primaryButtonClass} disabled={busy} data-tour="cost-article-add">{t('cost_article_add')}</button>
            </form>
        </section>
    );
}

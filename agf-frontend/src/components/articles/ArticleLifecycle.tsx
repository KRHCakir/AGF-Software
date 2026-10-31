'use client';

import { KeyboardEvent, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { deleteBlockedReason, fullNameRows } from '../../lib/articleLifecycle';
import { toApiError } from '../../services/apiClient';
import { deleteArticle, getArticleUsage, setArticleArchived } from '../../services/articleService';
import type { Article, ArticleUsage } from '../../types/article';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { useActorReference } from '../supply/useActorReference';

const dangerButtonClass = 'rounded-md bg-red-700 px-3 py-1.5 text-sm font-semibold text-white hover:bg-red-800 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-1 disabled:cursor-not-allowed disabled:bg-gray-300 disabled:text-gray-600';

// Beheer van één artikel onder het formulier: verwijderen (alleen als er nergens iets aan hangt; anders uitgeschakeld met dezelfde reden
// als de server), archiveren of terugzetten. Verwijderen vraagt eerst een bevestiging met de volledige naam; Annuleren heeft de focus.
export default function ArticleLifecycle({ article, onChanged, onDeleted }: {
    article: Article;
    onChanged: (saved: Article) => void;
    onDeleted: (id: number) => void;
}) {
    const { t, locale } = useTranslation();
    const [actor] = useActorReference();
    const [usage, setUsage] = useState<ArticleUsage | null>(null);
    const [usageFailed, setUsageFailed] = useState(false);
    const [busy, setBusy] = useState(false);
    const [confirming, setConfirming] = useState(false);
    const [message, setMessage] = useState<string | null>(null);
    const [reloadToken, setReloadToken] = useState(0);
    const reasonId = useId();

    useEffect(() => {
        const controller = new AbortController();
        setUsage(null);
        setUsageFailed(false);
        getArticleUsage(article.id, controller.signal)
            .then(setUsage)
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setUsageFailed(true); });
        return () => controller.abort();
    }, [article.id, reloadToken]);

    const actorReference = actor.trim() === '' ? null : actor.trim();
    const blocked = deleteBlockedReason(t, usage);

    const toggleArchived = async () => {
        setBusy(true);
        setMessage(null);
        try {
            onChanged(await setArticleArchived(article.id, !article.isArchived, actorReference));
        } catch (caught) {
            setMessage(toApiError(caught).message);
        } finally {
            setBusy(false);
        }
    };

    const remove = async () => {
        setBusy(true);
        setMessage(null);
        try {
            await deleteArticle(article.id, actorReference);
            setConfirming(false);
            onDeleted(article.id);
        } catch (caught) {
            // 409: intussen in gebruik genomen. De reden van de server tonen en het gebruik opnieuw ophalen (de knop gaat dan uit).
            setConfirming(false);
            setMessage(toApiError(caught).message);
            setReloadToken((n) => n + 1);
        } finally {
            setBusy(false);
        }
    };

    return (
        <section className="rounded-md border border-gray-200 bg-white p-3 text-sm" aria-label={t('articles_manage_title')} data-testid="article-lifecycle">
            <div className="flex flex-wrap items-center gap-2">
                <h3 className="mr-auto font-semibold text-gray-900">{t('articles_manage_title')}</h3>
                {article.isArchived
                    ? <button type="button" className={primaryButtonClass} disabled={busy} onClick={toggleArchived}>{t('articles_restore')}</button>
                    : <button type="button" className={secondaryButtonClass} disabled={busy} onClick={toggleArchived}>{t('articles_archive')}</button>}
                <button type="button" className={dangerButtonClass} disabled={busy || usage === null || blocked !== null}
                    aria-describedby={blocked !== null ? reasonId : undefined} onClick={() => setConfirming(true)}>{t('articles_delete')}</button>
            </div>
            {article.isArchived && (
                <p className="mt-1 text-xs text-gray-700" data-testid="article-archived-note">
                    {t('articles_archived_note', { when: article.archivedAt ? formatDateTime(article.archivedAt, locale) : '–', who: article.archivedBy ?? '–' })}
                </p>
            )}
            {usage === null && !usageFailed && <p className="mt-1 text-xs text-gray-600">{t('articles_usage_loading')}</p>}
            {usageFailed && <p className="mt-1 text-xs text-red-800">{t('articles_usage_failed')}</p>}
            {blocked !== null && <p id={reasonId} className="mt-1 text-xs text-gray-700" data-testid="article-delete-reason">{blocked}</p>}
            {message !== null && <p role="alert" className="mt-1 text-xs font-medium text-red-800">{message}</p>}
            {confirming && <ConfirmDelete article={article} busy={busy} onCancel={() => setConfirming(false)} onConfirm={remove} />}
        </section>
    );
}

function ConfirmDelete({ article, busy, onCancel, onConfirm }: { article: Article; busy: boolean; onCancel: () => void; onConfirm: () => void }) {
    const { t } = useTranslation();
    const titleId = useId();
    const cancelRef = useRef<HTMLButtonElement | null>(null);
    useEffect(() => { cancelRef.current?.focus(); }, []);

    const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            onCancel();
        }
    };

    return createPortal(
        <div className="fixed inset-0 z-50 flex items-end justify-center bg-gray-900/40 p-0 sm:items-center sm:p-4" onKeyDown={onKeyDown}>
            <div role="alertdialog" aria-modal="true" aria-labelledby={titleId} data-testid="article-delete-dialog"
                className="max-h-full w-full overflow-y-auto rounded-t-lg bg-white p-4 shadow-xl sm:max-w-md sm:rounded-lg">
                <h2 id={titleId} className="text-base font-semibold text-gray-900">{t('articles_delete_title')}</h2>
                <p className="mt-1 text-sm text-gray-700">{t('articles_delete_question')}</p>
                <dl className="mt-3 divide-y divide-gray-100 rounded border border-gray-200 text-sm">
                    {fullNameRows(article).map((row) => (
                        <div key={row.labelKey} className="grid grid-cols-3 gap-2 px-2 py-1">
                            <dt className="text-gray-600">{t(row.labelKey)}</dt>
                            <dd className="col-span-2 break-words text-gray-900">{row.value ?? <span className="text-gray-500">{t('articles_value_none')}</span>}</dd>
                        </div>
                    ))}
                </dl>
                <div className="mt-4 flex flex-col gap-2 sm:flex-row sm:justify-end">
                    <button ref={cancelRef} type="button" className={secondaryButtonClass} onClick={onCancel}>{t('articles_delete_cancel')}</button>
                    <button type="button" className={dangerButtonClass} disabled={busy} onClick={onConfirm}>{t('articles_delete_confirm')}</button>
                </div>
            </div>
        </div>,
        document.body,
    );
}

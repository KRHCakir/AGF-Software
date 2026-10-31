'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { SimilarAttribute, SimilarVariant, enteredFrom, newArticleText } from '../../lib/articleFromEntry';
import type { EnteredAttributes } from '../../lib/receiptBlindEntry';
import type { VariantOption } from '../../types/referenceData';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

const DIFFERS_KEYS: Record<SimilarAttribute, string> = {
    origin: 'new_article_differs_origin', size: 'new_article_differs_size', packaging: 'new_article_differs_packaging',
    tradeClass: 'new_article_differs_trade_class', brand: 'new_article_differs_brand',
};

// De ingetypte combinatie bestaat nog niet: precies wat er wordt aangemaakt ("Nieuw artikel: Avocado Hass · Zuid-Afrika · maat 12 …"),
// eerst de bestaande varianten die er sterk op lijken ("Bedoel je dit?", met een knop om die te kiezen) en dan één knop
// "Artikel aanmaken en doorgaan". Daarna loopt het gewone pad verder (bij ontvangst de controle "Klopt dit?").
export default function NewArticlePrompt({ articleName, countryName, entered, similar, onChoose, onCreate, busy, error, dataTourKey }: {
    articleName: string;
    countryName: string | null;
    entered: EnteredAttributes;
    similar: readonly SimilarVariant[];
    onChoose: (variant: VariantOption) => void;
    onCreate: () => void;
    busy: boolean;
    error: string | null;
    dataTourKey: string | number;
}) {
    const { t } = useTranslation();
    return (
        <div className="rounded border border-amber-300 bg-amber-50 px-2 py-2 text-xs text-gray-900" data-tour="new-article" data-tour-key={dataTourKey}>
            <p role="status">
                <span className="font-semibold">{t('new_article_title')}:</span>{' '}
                <span data-testid="new-article-text">{newArticleText(t, articleName, countryName, entered)}</span>
            </p>
            {similar.length > 0 && (
                <div className="mt-2">
                    <p className="font-semibold">{t('new_article_did_you_mean')}</p>
                    <ul className="mt-1 space-y-1">
                        {similar.map(({ variant, differs }) => (
                            <li key={variant.id} className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between" data-testid="new-article-similar">
                                <span className="break-words">
                                    {newArticleText(t, articleName, variant.origin, enteredFrom(variant))}
                                    <span className="ml-1 text-gray-600">({t(differs === null ? 'new_article_same_spelling' : DIFFERS_KEYS[differs])})</span>
                                </span>
                                <button type="button" className={`${secondaryButtonClass} shrink-0 px-2 py-0.5 text-xs`} disabled={busy}
                                    onClick={() => onChoose(variant)} data-tour="new-article-choose" data-tour-key={`${dataTourKey}-${variant.id}`}>{t('new_article_choose')}</button>
                            </li>
                        ))}
                    </ul>
                </div>
            )}
            <button type="button" className={`${primaryButtonClass} mt-2`} disabled={busy} onClick={onCreate} data-tour="new-article-create" data-tour-key={dataTourKey}>
                {t('new_article_create')}
            </button>
            {error !== null && <p role="alert" className="mt-1 font-medium text-red-800">{error}</p>}
        </div>
    );
}

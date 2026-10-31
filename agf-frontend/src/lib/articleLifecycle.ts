import type { Article, ArticleUsage } from '../types/article';

// Artikel verwijderen of archiveren: de reden in gewone taal uit de tellingen van de server ("Dit artikel is gebruikt in 3 ontvangsten
// en 1 inkooporderregel …") en de volledige naam voor het bevestigingsvenster (leeg kenmerk = "geen", nooit lege tekst).
// Geen imports buiten types: met node:test te toetsen.

type Translate = (key: string, params?: Record<string, string>) => string;

// "3 ontvangsten, 1 inkooporderregel en 2 afvalregistraties"; enkelvoud en meervoud per soort (articles_usage_<soort>_one/_many).
export function usageList(t: Translate, usages: ArticleUsage['usages']): string {
    const parts = usages.map((usage) => `${usage.count} ${t(`articles_usage_${usage.kind}_${usage.count === 1 ? 'one' : 'many'}`)}`);
    if (parts.length <= 1) return parts.join('');
    return `${parts.slice(0, -1).join(', ')} ${t('articles_usage_and')} ${parts[parts.length - 1]}`;
}

// Waarom verwijderen niet kan; null als het wel kan.
export function deleteBlockedReason(t: Translate, usage: ArticleUsage | null): string | null {
    if (usage === null || usage.canDelete) return null;
    return t('articles_delete_blocked', { list: usageList(t, usage.usages) });
}

export interface NameRow { labelKey: string; value: string | null }

// Product (met ras), herkomst, maat, verpakking, klasse en merk; null = leeg ("geen" in het venster).
export function fullNameRows(article: Pick<Article, 'productName' | 'variantName' | 'origin' | 'sizeCategory' | 'packaging' | 'tradeClass' | 'brand'>): NameRow[] {
    const value = (text: string | null | undefined) => (text === null || text === undefined || text.trim() === '' ? null : text.trim());
    return [
        { labelKey: 'articles_name_product', value: value([article.productName, article.variantName].filter((part) => part && part.trim() !== '').join(' ')) },
        { labelKey: 'articles_name_origin', value: value(article.origin) },
        { labelKey: 'articles_name_size', value: value(article.sizeCategory) },
        { labelKey: 'articles_name_packaging', value: value(article.packaging) },
        { labelKey: 'articles_name_trade_class', value: value(article.tradeClass) },
        { labelKey: 'articles_name_brand', value: value(article.brand) },
    ];
}

// Lijst in Artikelen: gearchiveerde artikelen alleen als de gebruiker ze wil zien.
export function visibleArticles<T extends Pick<Article, 'isArchived'>>(articles: readonly T[], showArchived: boolean): T[] {
    return showArchived ? [...articles] : articles.filter((article) => !article.isArchived);
}

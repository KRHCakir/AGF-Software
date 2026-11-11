// Kostenartikelen (type Dienst) in de artikelkeuze van een inkoopfactuurregel (spiegel van AgfAdmin/Services/CostArticleRules.cs). Pure
// module zonder React of i18n, zodat ze met node:test te toetsen is. In de keuzelijst staan ze als een eigen groep met een negatieve
// sleutel (-id), zodat ze nooit met een artikel (ProductVariant) te verwarren zijn; de regel bewaart het kostenartikel apart.
import type { CostArticle } from '../types/costArticle';
import type { ProductOption } from '../types/referenceData';

export const STANDARD_COST_ARTICLES = ['Handling', 'Transport', 'QC', 'Zeevracht', 'Afval', 'Overig'] as const;
export const MAX_COST_ARTICLE_NAME_LENGTH = 100;

// Eén groep in de artikelkeuze ("Kosten", of "Emballage" met groupId -2), alleen met actieve kostenartikelen; null als er geen zijn.
export function costArticleGroup(articles: CostArticle[], groupName: string, groupId = -1): ProductOption | null {
    const active = articles.filter((article) => article.isActive);
    if (active.length === 0) return null;
    return {
        id: groupId,
        name: groupName,
        variants: active.map((article) => ({
            id: -article.id, name: article.name, origin: null, packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: null,
        })),
    };
}

// Waarde uit de artikelkeuze: een kostenartikel ("-3" = kostenartikel 3), een artikel ("12") of niets.
export function pickedItem(value: string): { kind: 'cost'; costArticleId: string } | { kind: 'article'; productVariantId: string } | { kind: 'none' } {
    if (/^-[1-9]\d*$/.test(value)) return { kind: 'cost', costArticleId: value.slice(1) };
    if (/^[1-9]\d*$/.test(value)) return { kind: 'article', productVariantId: value };
    return { kind: 'none' };
}

// De waarde voor de artikelkeuze bij een regel: het kostenartikel als negatieve sleutel, anders het artikel.
export function itemValue(line: { productVariantId: string; costArticleId?: string }): string {
    return line.costArticleId ? `-${line.costArticleId}` : line.productVariantId;
}

// Zelfde sleutel als CostArticleRules.NameKey: getrimd, kleine letters, spaties samengevoegd.
export function costArticleNameKey(name: string): string | null {
    const key = name.trim().split(/\s+/).filter((part) => part !== '').join(' ').toLowerCase();
    return key === '' ? null : key;
}

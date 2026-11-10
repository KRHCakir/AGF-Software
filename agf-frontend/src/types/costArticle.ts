import { isNumber, isString } from './guards';

// Kostenartikel (type Dienst), api/cost-articles: zie AgfAdmin/DTOs/CostArticleDtos.cs. Soort Packaging = emballage (pallet, krat),
// ook via api/packaging-articles, met een standaardprijs per stuk (excl. btw) en btw.
export type CostArticleKind = 'Cost' | 'Packaging';

export interface CostArticle {
    id: number;
    name: string;
    kind?: CostArticleKind;
    defaultUnitPrice?: number | null;
    vatPercentage?: number | null;
    isStandard: boolean;
    isActive: boolean;
    sortOrder: number;
    lineCount: number;
}

export function isCostArticle(value: unknown): value is CostArticle {
    if (typeof value !== 'object' || value === null) return false;
    const record = value as Record<string, unknown>;
    return isNumber(record.id) && isString(record.name) && typeof record.isActive === 'boolean' && typeof record.isStandard === 'boolean';
}

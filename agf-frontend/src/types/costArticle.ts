import { isNumber, isString } from './guards';

// Kostenartikel (type Dienst), api/cost-articles: zie AgfAdmin/DTOs/CostArticleDtos.cs.
export interface CostArticle {
    id: number;
    name: string;
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

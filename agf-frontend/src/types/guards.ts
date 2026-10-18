// Kleine, gedeelde type-guards voor API-antwoorden die als `unknown` binnenkomen.

export type JsonObject = Record<string, unknown>;

export function isObject(value: unknown): value is JsonObject {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value);
}

export function isNullableNumber(value: unknown): value is number | null {
    return value === null || isNumber(value);
}

export function isString(value: unknown): value is string {
    return typeof value === 'string';
}

export function isNullableString(value: unknown): value is string | null {
    return value === null || typeof value === 'string';
}

export function isOptionalNullableString(value: unknown): value is string | null | undefined {
    return value === undefined || isNullableString(value);
}

export function isArrayOf<T>(value: unknown, guard: (item: unknown) => item is T): value is T[] {
    return Array.isArray(value) && value.every(guard);
}

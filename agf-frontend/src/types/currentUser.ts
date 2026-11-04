// De ingelogde gebruiker (GET /api/me). Wie iets keurt, verkoopt of reserveert, volgt hieruit aan de serverkant; schermen tonen
// alleen de naam ("Ingelogd als …") en sturen nooit zelf een medewerker mee.
export interface CurrentUser {
    userId: string;
    displayName: string;
    roles: string[];
    permissions: string[];
}

const isStringList = (value: unknown): value is string[] => Array.isArray(value) && value.every((item) => typeof item === 'string');

export function isCurrentUser(value: unknown): value is CurrentUser {
    if (typeof value !== 'object' || value === null) return false;
    const user = value as Record<string, unknown>;
    return typeof user.userId === 'string' && typeof user.displayName === 'string' && isStringList(user.roles) && isStringList(user.permissions);
}

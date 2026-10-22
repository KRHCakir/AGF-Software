// GLOBALG.A.P.-nummer (GGN): 13 cijfers, als tekst zodat voorloopnullen blijven. Los van HS/GN/TARIC.
// Alleen de schrijfwijze wordt gecontroleerd; een ingevuld nummer bewijst geen geldige certificering
// ("zoals opgegeven; certificeringsstatus niet gecontroleerd"). Spiegel van ArticleRules.NormalizeGgn/IsGgn.

export const GGN_LENGTH = 13;

// Alleen spaties vallen weg als scheidingsteken; alle andere tekens blijven staan zodat de validatie ze weigert.
export function normalizeGgn(raw: string): string {
    return raw.trim().replace(/ /g, '');
}

export function isGgn(normalized: string): boolean {
    return /^[0-9]{13}$/.test(normalized);
}

// Leeg = niet vastgelegd (null); anders genormaliseerd en geldig, of een fout.
export function resolveGgn(raw: string): { ok: true; value: string | null } | { ok: false } {
    const normalized = normalizeGgn(raw);
    if (normalized === '') return { ok: true, value: null };
    return isGgn(normalized) ? { ok: true, value: normalized } : { ok: false };
}

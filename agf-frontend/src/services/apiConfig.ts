// De enige plek waar de website het adres van de API bepaalt. Standaard het relatieve pad /api: de website stuurt dat zelf door naar
// de backend (next.config.ts, API_INTERNAL_URL). De browser praat dan alleen met de eigen website, ook online achter één adres en
// zonder CORS. NEXT_PUBLIC_API_URL mag een ander pad (beginnend met /) of een volledig http(s)-adres zijn, zoals lokaal vroeger
// (bijvoorbeeld de e2e-tests met een eigen API). Een ongeldige waarde blijft een zichtbare configuratiefout.

export const DEFAULT_API_BASE_URL = '/api';

export class ApiConfigError extends Error {
    constructor(message: string) {
        super(message);
        this.name = 'ApiConfigError';
    }
}

export function getApiBaseUrl(): string {
    // Letterlijke verwijzing, zodat Next.js de waarde bij de build kan invullen.
    const raw = process.env.NEXT_PUBLIC_API_URL;
    const trimmed = (raw ?? '').trim().replace(/\/+$/, '');

    if (trimmed === '') {
        return DEFAULT_API_BASE_URL;
    }

    // Een pad op de eigen website; "//host" is geen pad maar een ander adres zonder protocol.
    if (trimmed.startsWith('/')) {
        if (trimmed.startsWith('//')) {
            throw new ApiConfigError('NEXT_PUBLIC_API_URL moet een pad (/api) of een volledig http(s)-adres zijn.');
        }
        return trimmed;
    }

    let url: URL;
    try {
        url = new URL(trimmed);
    } catch {
        throw new ApiConfigError('NEXT_PUBLIC_API_URL is geen geldige URL.');
    }

    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
        throw new ApiConfigError('NEXT_PUBLIC_API_URL moet met http:// of https:// beginnen.');
    }

    return trimmed;
}

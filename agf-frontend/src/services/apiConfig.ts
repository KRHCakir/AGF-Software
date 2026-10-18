// Centrale API-basis-URL uit NEXT_PUBLIC_API_URL (bijvoorbeeld in .env.local; zie .env.example).
// Bewust zonder terugvaladres: een ontbrekende of ongeldige waarde is een zichtbare configuratiefout.

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
        throw new ApiConfigError('NEXT_PUBLIC_API_URL is niet ingesteld.');
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

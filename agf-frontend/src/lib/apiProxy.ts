// Regels voor het doorsturen van /api door de website (src/middleware.ts). De grens is dezelfde als die van de upload in de API
// (AiInvoiceReadingController: 32 MB per verzoek, ruim boven de documentgrens van 10 MB), zodat een te groot document de nette
// melding van de API krijgt en alleen echt te grote verzoeken al bij de website stoppen.
export const MAX_API_REQUEST_BYTES = 32 * 1024 * 1024;

export const TOO_LARGE_MESSAGE = 'Het bestand is te groot om te versturen (maximaal 32 MB per keer).';

// Content-Length uit de kop; zonder (of onleesbaar) beslist de API zelf met dezelfde grens.
export function requestTooLarge(contentLength: string | null, limit = MAX_API_REQUEST_BYTES): boolean {
    if (contentLength === null || !/^\d+$/.test(contentLength.trim())) return false;
    return Number(contentLength.trim()) > limit;
}

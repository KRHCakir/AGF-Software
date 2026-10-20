// Hoogte waarmee een blok vanaf zijn bovenkant precies tot de onderkant van het venster reikt (min. een bruikbare hoogte).
// `topInDocument` is de afstand van de bovenkant van het document tot het blok, `bottomGap` de ruimte die eronder moet blijven.
export const MIN_FILL_HEIGHT = 320;

export function fillHeight(viewportHeight: number, topInDocument: number, bottomGap: number, minimum = MIN_FILL_HEIGHT): number {
    return Math.max(minimum, Math.floor(viewportHeight - topInDocument - bottomGap));
}

// Breedte vanaf waar Verkoop naast elkaar (lijst | detail) toont, gelijk aan Tailwind `lg`.
export const SPLIT_LAYOUT_QUERY = '(min-width: 1024px)';

// Zonder matchMedia (bijvoorbeeld een testomgeving) geldt de gestapelde weergave.
export function splitLayoutMedia(): MediaQueryList | null {
    return typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(SPLIT_LAYOUT_QUERY) : null;
}

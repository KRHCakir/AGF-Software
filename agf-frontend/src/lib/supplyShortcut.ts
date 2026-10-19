// Snelkoppelingen naar de werkplek Aanvoer: "Aanvoer plannen" en "Pallet ontvangen" (altijd voor het eigen magazijn).
// Bij aankomst vanaf een andere pagina leest de pagina de hash; staat de pagina al open, dan komt de wens als gebeurtenis
// binnen (een hash-navigatie op dezelfde pagina hermonteert niets). Handmatige hashwijzigingen tellen ook.

export type SupplyShortcutMode = 'plan' | 'receive';

export const SUPPLY_MODE_EVENT = 'agf:supply-mode';

export function supplyShortcutHref(mode: SupplyShortcutMode): string {
    return `/supply#${mode}`;
}

export function modeFromHash(hash: string): SupplyShortcutMode | null {
    const value = hash.replace(/^#/, '').toLowerCase();
    return value === 'plan' || value === 'receive' ? value : null;
}

export function isSupplyShortcutMode(value: unknown): value is SupplyShortcutMode {
    return value === 'plan' || value === 'receive';
}

// Door de zijbalk aangeroepen bij elke klik; onschadelijk als de pagina nog niet open is (dan geldt de hash bij aankomst).
export function requestSupplyMode(mode: SupplyShortcutMode): void {
    if (typeof window === 'undefined') return;
    // window.CustomEvent: in de browser hetzelfde als CustomEvent; in de testomgeving is alleen de jsdom-variant geldig voor window.
    window.dispatchEvent(new window.CustomEvent<SupplyShortcutMode>(SUPPLY_MODE_EVENT, { detail: mode }));
}

export function subscribeSupplyMode(handler: (mode: SupplyShortcutMode) => void): () => void {
    if (typeof window === 'undefined') return () => {};
    const onEvent = (event: Event) => {
        const detail = (event as CustomEvent<unknown>).detail;
        if (isSupplyShortcutMode(detail)) handler(detail);
    };
    const onHash = () => {
        const mode = modeFromHash(window.location.hash);
        if (mode !== null) handler(mode);
    };
    window.addEventListener(SUPPLY_MODE_EVENT, onEvent);
    window.addEventListener('hashchange', onHash);
    return () => {
        window.removeEventListener(SUPPLY_MODE_EVENT, onEvent);
        window.removeEventListener('hashchange', onHash);
    };
}

// Sneltoetsen van de invoerwerkplek. F-toetsen zijn op macOS standaard systeemfuncties (helderheid, Mission Control) en
// F3/F7 hebben browserfuncties; daarom modifier-combinaties die in Chrome, Safari en Firefox op macOS en Windows vrij zijn
// of met preventDefault betrouwbaar zijn te onderscheppen. Alt/Option-combinaties worden op `code` herkend, omdat Option
// op macOS het `key`-teken verandert (⌥N geeft een dode toets). Pure module, zodat node:test de toewijzing kan toetsen.

export type ShortcutAction = 'save' | 'saveAndNew' | 'newDocument' | 'focusSearch' | 'toggleExtra' | 'duplicateLine' | 'removeLine';

export interface ShortcutKeys {
    key?: string;
    code?: string;
    primary?: boolean; // ⌘ op macOS, Ctrl elders
    alt?: boolean;
    shift?: boolean;
}

export const SHORTCUTS: Readonly<Record<ShortcutAction, ShortcutKeys>> = {
    save: { key: 's', primary: true },
    saveAndNew: { key: 'Enter', primary: true },
    newDocument: { code: 'KeyN', alt: true },
    focusSearch: { code: 'KeyF', alt: true },
    toggleExtra: { code: 'KeyE', alt: true },
    duplicateLine: { code: 'KeyD', alt: true },
    removeLine: { code: 'KeyR', alt: true },
};

export interface KeyLike {
    key: string;
    code: string;
    metaKey: boolean;
    ctrlKey: boolean;
    altKey: boolean;
    shiftKey: boolean;
}

export function isMacPlatform(userAgent: string): boolean {
    return /Mac|iPhone|iPad/.test(userAgent);
}

export function matchShortcut(event: KeyLike, isMac: boolean): ShortcutAction | null {
    for (const action of Object.keys(SHORTCUTS) as ShortcutAction[]) {
        const keys = SHORTCUTS[action];
        const primaryDown = isMac ? event.metaKey : event.ctrlKey;
        const otherModifier = isMac ? event.ctrlKey : event.metaKey;
        if ((keys.primary ?? false) !== primaryDown) continue;
        if ((keys.alt ?? false) !== event.altKey) continue;
        if ((keys.shift ?? false) !== event.shiftKey) continue;
        if (otherModifier) continue;
        if (keys.key !== undefined && event.key.toLowerCase() !== keys.key.toLowerCase()) continue;
        if (keys.code !== undefined && event.code !== keys.code) continue;
        return action;
    }
    return null;
}

// Zichtbaar label bij de knop, in de notatie van het platform.
export function shortcutLabel(action: ShortcutAction, isMac: boolean): string {
    const keys = SHORTCUTS[action];
    const parts: string[] = [];
    if (keys.primary) parts.push(isMac ? '⌘' : 'Ctrl');
    if (keys.alt) parts.push(isMac ? '⌥' : 'Alt');
    if (keys.shift) parts.push(isMac ? '⇧' : 'Shift');
    const main = keys.key === 'Enter' ? (isMac ? '↩' : 'Enter') : keys.key !== undefined ? keys.key.toUpperCase() : (keys.code ?? '').replace(/^Key/, '');
    parts.push(main);
    return isMac ? parts.join('') : parts.join('+');
}

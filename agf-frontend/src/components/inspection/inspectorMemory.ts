// Wie beoordeelt: per keuring gekozen, want in de loods keurt niet altijd de ingelogde gebruiker (gedeelde tablet of pc). Dit apparaat
// onthoudt de laatste keuze als voorstel; het veld toont die altijd zichtbaar en aanpasbaar, nooit stil op de achtergrond.
export const INSPECTOR_STORAGE_KEY = 'agf_inspection_inspector';
// Medewerkers die als suggestie verschijnen (typen met suggesties; een andere naam mag ook).
export const KNOWN_INSPECTORS: readonly string[] = ['Jan', 'Steve'];
const INSPECTOR_CHANGED_EVENT = 'agf-inspector-changed';

export function readRememberedInspector(): string | null {
    try {
        return localStorage.getItem(INSPECTOR_STORAGE_KEY);
    } catch {
        return null;
    }
}

export function rememberInspector(value: string | null) {
    try {
        if (value === null) localStorage.removeItem(INSPECTOR_STORAGE_KEY);
        else localStorage.setItem(INSPECTOR_STORAGE_KEY, value);
    } catch {
        // Niet onthouden is geen fout.
    }
    window.dispatchEvent(new Event(INSPECTOR_CHANGED_EVENT));
}

// Een open keuringspaneel volgt een nieuwe keuze (bijvoorbeeld de rondleiding die even "Demo-assistent" zet).
export function onRememberedInspectorChange(listener: () => void): () => void {
    window.addEventListener(INSPECTOR_CHANGED_EVENT, listener);
    return () => window.removeEventListener(INSPECTOR_CHANGED_EVENT, listener);
}

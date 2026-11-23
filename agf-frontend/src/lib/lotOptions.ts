// Partijkeuze op de verkooporderregel (feedback 22 november). Pure module, te toetsen met node:test.
import type { LotOption } from '../types/salesOrder';

// Vergelijkingssleutel zoals de server (PartijRules.NumberKey): hoofdletters, zonder spaties en streepjes. "261140" vindt zo I-261140 en
// V-261140; "I 2611-40" vindt alleen I-261140.
export function lotKey(text: string): string {
    return text.replace(/[\s-]/g, '').toUpperCase();
}

// Alleen kiesbare partijen (met een partij) die bij de getypte tekst passen, in de volgorde van de server (oudste ontvangst eerst).
export function matchingLots(options: readonly LotOption[], text: string): LotOption[] {
    const key = lotKey(text);
    return options.filter((option) => option.partijId !== null && option.number !== null && (key === '' || lotKey(option.number).includes(key)));
}

// De partij waar de getypte tekst precies naar wijst: dezelfde sleutel, of dezelfde sleutel zonder de letter vooraan (2611002 = I-2611002).
// Past de tekst zonder letter op meer partijen, dan is er geen exacte keuze.
export function exactLot(options: readonly LotOption[], text: string): LotOption | null {
    const key = lotKey(text);
    if (key === '') return null;
    const lots = options.filter((option) => option.partijId !== null && option.number !== null);
    const same = lots.find((option) => lotKey(option.number!) === key);
    if (same) return same;
    const withoutLetter = lots.filter((option) => lotKey(option.number!).replace(/^[A-Z]/, '') === key);
    return withoutLetter.length === 1 ? withoutLetter[0] : null;
}

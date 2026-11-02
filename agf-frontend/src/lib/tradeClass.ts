// Handelsklasse: een vaste lijst met de drie EU-klassen voor groenten en fruit. Dezelfde regels als de backend (ArticleRules.NormalizeTradeClass
// en de migratie NormalizeTradeClasses): hoofdletters, spaties en voorvoegsels tellen niet, dus "1", "i", "klasse 1", "class I" en "cat. 1"
// zijn I; "2", "ii" en "klasse 2" zijn II; "extra", "0", "e" en "klasse extra" zijn Extra. Wat niet eenduidig één klasse is, wordt nooit
// stil geaccepteerd: de gebruiker kiest uit de lijst. Geen imports: met node:test te toetsen.

export const TRADE_CLASSES = ['Extra', 'I', 'II'] as const;
export type TradeClass = typeof TRADE_CLASSES[number];

const PREFIX = /^(handelsklasse|klasse|klas|kl|class|categorie|category|cat)[\s.:]*/;
const SEPARATORS = /[\s.:_-]/g;
const KEYS: Record<string, TradeClass> = { extra: 'Extra', e: 'Extra', 0: 'Extra', i: 'I', 1: 'I', ii: 'II', 2: 'II' };

export function normalizeTradeClass(value: string | null | undefined): TradeClass | null {
    if (value === null || value === undefined || value.trim() === '') return null;
    const key = value.trim().toLowerCase().replace(PREFIX, '').replace(SEPARATORS, '');
    return Object.prototype.hasOwnProperty.call(KEYS, key) ? KEYS[key] : null;
}

export const isTradeClass = (value: string | null | undefined): value is TradeClass => (TRADE_CLASSES as readonly string[]).includes(value ?? '');

// Vergelijksleutel: de vaste klasse als die herkend wordt ("1" en "I" zijn hetzelfde), anders de tekst zelf zonder hoofdletters en dubbele
// spaties (een oude, niet herkende waarde is alleen gelijk aan zichzelf); leeg = "".
export function tradeClassKey(value: string | null | undefined): string {
    return normalizeTradeClass(value) ?? (value ?? '').trim().replace(/\s+/g, ' ').toLowerCase();
}

// Suggesties bij typen in de klassekiezer: de herkende klasse bovenaan (exact, Tab neemt hem), daarna klassen waarvan de naam met de
// getypte tekst begint ("ex" -> Extra). Leeg = de hele lijst. Niets herkend = geen suggesties (dan kiezen uit de lijst).
export function searchTradeClasses(query: string): { tradeClass: TradeClass; exact: boolean }[] {
    const term = query.trim().toLowerCase();
    if (term === '') return TRADE_CLASSES.map((tradeClass) => ({ tradeClass, exact: false }));
    const recognised = normalizeTradeClass(query);
    const starts = TRADE_CLASSES.filter((tradeClass) => tradeClass !== recognised && tradeClass.toLowerCase().startsWith(term));
    return [...(recognised === null ? [] : [{ tradeClass: recognised, exact: true }]), ...starts.map((tradeClass) => ({ tradeClass, exact: false }))];
}

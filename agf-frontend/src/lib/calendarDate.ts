// Kalenderdatums zonder tijd (gevraagde leverdatum). De gebruiker kiest een datum; die gaat als lokale middernacht
// naar de server, die naar UTC normaliseert. Terugtonen gebeurt in de lokale tijdzone, zodat de kalenderdag gelijk blijft.
// Geen imports: dit bestand is met node:test te toetsen in een vaste tijdzone.

const DATE_INPUT = /^(\d{4})-(\d{2})-(\d{2})$/;

export function toLocalMidnightIso(dateText: string): string | null {
    const match = DATE_INPUT.exec(dateText);
    if (match === null) return null;
    const [, year, month, day] = match.map(Number);
    const local = new Date(year, month - 1, day);
    if (local.getFullYear() !== year || local.getMonth() !== month - 1 || local.getDate() !== day) return null;
    return local.toISOString();
}

// Een ingetypte kalenderdatum naar de ISO-vorm (2026-10-31): de ISO-vorm zelf of de Nederlandse volgorde dag-maand-jaar, altijd in
// die volgorde, ongeacht de browsertaal: dd-mm-jjjj, dd/mm/jjjj, d.m.jjjj, ddmmjjjj en d-m (dan het jaar van `today`, de lokale
// kalenderdag). Een jaar van twee cijfers is 20jj (04-11-26, 4/11/26 en 041126 zijn 4 november 2026): 00 t/m 99 is 2000 t/m 2099,
// want hier staan alleen recente en komende datums. Een jaar van één of drie cijfers is nooit een datum. null bij lege, onvolledige
// of niet-bestaande datums (31-02 schuift nooit stil door naar maart). Vandaag en datums in het verleden zijn gewoon geldig: dit is
// alleen de notatie.
const DUTCH_DATE = /^(\d{1,2})[-/.](\d{1,2})[-/.](\d{4}|\d{2})$/;
const DUTCH_DAY_MONTH = /^(\d{1,2})[-/.](\d{1,2})$/;
const DUTCH_DIGITS = /^(\d{2})(\d{2})(\d{4}|\d{2})$/;
const fullYear = (year: string) => Number(year.length === 2 ? `20${year}` : year);
export function parseDateInput(text: string, today: string = localToday()): string | null {
    const trimmed = text.trim();
    const iso = DATE_INPUT.exec(trimmed);
    const dutch = iso === null ? DUTCH_DATE.exec(trimmed) ?? DUTCH_DIGITS.exec(trimmed) : null;
    const short = iso === null && dutch === null ? DUTCH_DAY_MONTH.exec(trimmed) : null;
    let parts: number[];
    if (iso !== null) parts = [iso[1], iso[2], iso[3]].map(Number);
    else if (dutch !== null) parts = [fullYear(dutch[3]), Number(dutch[2]), Number(dutch[1])];
    else if (short !== null && isDateOnly(today)) parts = [Number(today.slice(0, 4)), Number(short[2]), Number(short[1])];
    else return null;
    const [year, month, day] = parts;
    const pad = (value: number, length = 2) => String(value).padStart(length, '0');
    const value = `${pad(year, 4)}-${pad(month)}-${pad(day)}`;
    return year >= 1900 && isDateOnly(value) ? value : null;
}

// Een ISO-kalenderdatum zoals het datumveld hem toont: dd-mm-jjjj, in elke taal en tijdzone; anders de tekst ongewijzigd.
export function formatDateInput(value: string): string {
    return isDateOnly(value) ? `${value.slice(8, 10)}-${value.slice(5, 7)}-${value.slice(0, 4)}` : value;
}

// Vandaag als kalenderdatum in de lokale tijdzone van de browser (niet UTC: om 00:30 is het hier al de nieuwe dag).
export function localToday(now: Date = new Date()): string {
    const pad = (value: number) => String(value).padStart(2, '0');
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

// Terug naar de waarde van een <input type="date"> (lokale kalenderdag); null bij een ongeldige datum.
export function isoToDateInput(iso: string): string | null {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return null;
    const pad = (value: number) => String(value).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function dateLocale(locale: string): string {
    return locale === 'nl' ? 'nl-NL' : 'en-GB';
}

export function formatCalendarDate(iso: string, locale: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
        ? iso
        : new Intl.DateTimeFormat(dateLocale(locale), { dateStyle: 'medium' }).format(date);
}

// Een kalenderdatum zonder tijdstip (yyyy-MM-dd, bijvoorbeeld ShippedOn) letterlijk als die dag tonen, in elke tijdzone.
export function formatDateOnly(dateText: string, locale: string): string {
    const match = DATE_INPUT.exec(dateText);
    if (match === null) return dateText;
    const [, year, month, day] = match.map(Number);
    return new Intl.DateTimeFormat(dateLocale(locale), { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, day)));
}

// "Vandaag" als kalenderdatum in Europe/Amsterdam (zelfde definitie als de backend); `now` is injecteerbaar voor tests.
export function todayInAmsterdam(now: Date = new Date()): string {
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Amsterdam', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
    const get = (type: string) => parts.find((part) => part.type === type)?.value ?? '';
    return `${get('year')}-${get('month')}-${get('day')}`;
}

export function isDateOnly(value: string): boolean {
    const match = DATE_INPUT.exec(value);
    if (match === null) return false;
    const [, year, month, day] = match.map(Number);
    const date = new Date(Date.UTC(year, month - 1, day));
    return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

// De kalenderdatum in Europe/Amsterdam van een UTC-tijdstip (zelfde definitie als de backend, bijvoorbeeld voor DispatchedAt).
export function amsterdamDateOf(iso: string): string | null {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return null;
    return todayInAmsterdam(date);
}


// Een kalenderdatum `days` dagen verder (of terug bij een negatief getal); in UTC gerekend, dus zonder last van zomer- en wintertijd.
export function addDays(iso: string, days: number): string | null {
    if (!isDateOnly(iso)) return null;
    const [year, month, day] = iso.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}

// Sneltoetsen van het datumveld: een dag ten opzichte van vandaag. Eén letter: V(andaag), G(isteren), M(orgen), E(ergisteren),
// O(vermorgen), en T(oday) en Y(esterday) voor het Engelse scherm. Het woord voluit werkt ook (vandaag, morgen, overmorgen, today,
// tomorrow, ...), zodat wie "morgen" typt ook morgen krijgt. Hoofdletters en spaties doen er niet toe; andere tekst is geen sneltoets.
export const DATE_SHORTCUTS: Readonly<Record<string, number>> = {
    v: 0, t: 0, g: -1, y: -1, m: 1, e: -2, o: 2,
    vandaag: 0, gisteren: -1, morgen: 1, eergisteren: -2, overmorgen: 2, today: 0, yesterday: -1, tomorrow: 1,
};

export function dateShortcut(text: string, today: string = localToday()): string | null {
    const key = text.trim().toLowerCase();
    const days = Object.hasOwn(DATE_SHORTCUTS, key) ? DATE_SHORTCUTS[key] : undefined;
    return days === undefined ? null : addDays(today, days);
}

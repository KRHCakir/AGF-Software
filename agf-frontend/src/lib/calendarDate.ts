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


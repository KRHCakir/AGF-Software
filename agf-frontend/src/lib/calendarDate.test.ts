import assert from 'node:assert/strict';
import { before, describe, test } from 'node:test';
import { formatCalendarDate, isoToDateInput, toLocalMidnightIso } from './calendarDate.ts';

// Rondreis van de gevraagde leverdatum in Europe/Amsterdam, zomer- en wintertijd: de kalenderdag die de gebruiker koos
// moet na UTC-normalisatie door de server exact zo worden teruggetoond. node:test draait elk bestand in een eigen proces.

const calendarDayInAmsterdam = (iso: string) =>
    new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Amsterdam', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(iso));

describe('requested delivery date in Europe/Amsterdam', () => {
    before(() => {
        process.env.TZ = 'Europe/Amsterdam';
        assert.equal(new Date(2026, 6, 15).getTimezoneOffset(), -120, 'test process must run in Europe/Amsterdam');
    });

    test('summer time: local midnight is 22:00 UTC the day before, and the calendar day survives the round trip', () => {
        const iso = toLocalMidnightIso('2026-07-15');
        assert.equal(iso, '2026-07-14T22:00:00.000Z');
        assert.equal(calendarDayInAmsterdam(iso!), '2026-07-15');
        assert.equal(isoToDateInput(iso!), '2026-07-15');
        assert.equal(formatCalendarDate(iso!, 'nl'), '15 jul 2026');
        assert.equal(formatCalendarDate(iso!, 'en'), '15 Jul 2026');
    });

    test('winter time: local midnight is 23:00 UTC the day before, and the calendar day survives the round trip', () => {
        const iso = toLocalMidnightIso('2026-01-15');
        assert.equal(iso, '2026-01-14T23:00:00.000Z');
        assert.equal(calendarDayInAmsterdam(iso!), '2026-01-15');
        assert.equal(isoToDateInput(iso!), '2026-01-15');
        assert.equal(formatCalendarDate(iso!, 'nl'), '15 jan 2026');
        assert.equal(formatCalendarDate(iso!, 'en'), '15 Jan 2026');
    });

    test('the server answer (UTC, microseconds) for the smoke order displays as the chosen day', () => {
        // Zoals de API het teruggeeft: 2026-10-23T06:00:00.123456Z is 08:00 lokale tijd op 23 oktober (zomertijd).
        assert.equal(isoToDateInput('2026-10-23T06:00:00.123456Z'), '2026-10-23');
        assert.equal(formatCalendarDate('2026-10-23T06:00:00.123456Z', 'nl'), '23 okt 2026');
        // Rond de overgang naar wintertijd (25 oktober 2026) blijft de dag kloppen.
        assert.equal(isoToDateInput(toLocalMidnightIso('2026-10-25')!), '2026-10-25');
        assert.equal(isoToDateInput(toLocalMidnightIso('2026-10-26')!), '2026-10-26');
        assert.equal(toLocalMidnightIso('2026-10-26'), '2026-10-25T23:00:00.000Z');
    });

    test('invalid input never produces a date', () => {
        assert.equal(toLocalMidnightIso('2026-02-30'), null);
        assert.equal(toLocalMidnightIso(''), null);
        assert.equal(isoToDateInput('not-a-date'), null);
        assert.equal(formatCalendarDate('not-a-date', 'nl'), 'not-a-date');
    });
});

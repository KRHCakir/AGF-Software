import assert from 'node:assert/strict';
import { before, describe, test } from 'node:test';
import { addDays, amsterdamDateOf, dateShortcut, formatCalendarDate, formatDateInput, formatDateOnly, isDateOnly, isoToDateInput, localToday, parseDateInput, toLocalMidnightIso, todayInAmsterdam } from './calendarDate.ts';

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

describe('DirectTrade shipping date helpers', () => {
    test('today in Europe/Amsterdam follows local midnight, not UTC, including DST changes', () => {
        assert.equal(todayInAmsterdam(new Date('2026-10-20T21:59:59Z')), '2026-10-20');
        assert.equal(todayInAmsterdam(new Date('2026-10-20T22:00:00Z')), '2026-10-21');
        assert.equal(todayInAmsterdam(new Date('2026-12-31T22:59:59Z')), '2026-12-31');
        assert.equal(todayInAmsterdam(new Date('2026-12-31T23:00:00Z')), '2027-01-01');
        assert.equal(todayInAmsterdam(new Date('2026-10-25T22:59:59Z')), '2026-10-25');
        assert.equal(todayInAmsterdam(new Date('2026-10-25T23:00:00Z')), '2026-10-26');
    });

    test('a date-only value is shown as exactly that calendar day and validated strictly', () => {
        assert.match(formatDateOnly('2026-10-19', 'nl'), /19 okt/);
        assert.match(formatDateOnly('2026-12-31', 'en'), /31 Dec/);
        assert.equal(isDateOnly('2026-02-29'), false);
        assert.equal(isDateOnly('2028-02-29'), true);
        assert.equal(isDateOnly('2026-10-19T00:00:00Z'), false);
    });
});

describe('shipping date of a UTC instant in Europe/Amsterdam', () => {
    test('a dispatch at 00:30 Amsterdam time on 21 October is shipped on 21 October, also in winter time', () => {
        assert.equal(amsterdamDateOf('2026-10-20T21:30:00Z'), '2026-10-20');
        assert.equal(amsterdamDateOf('2026-10-20T22:30:00Z'), '2026-10-21');
        assert.equal(amsterdamDateOf('2026-12-31T22:59:59Z'), '2026-12-31');
        assert.equal(amsterdamDateOf('2026-12-31T23:00:00Z'), '2027-01-01');
        assert.equal(amsterdamDateOf('not a date'), null);
    });

    test('typed dates: the ISO value of a date field and the Dutch notation both work; incomplete or impossible dates never do', () => {
        assert.equal(parseDateInput('2026-10-31'), '2026-10-31');
        assert.equal(parseDateInput('31-10-2026'), '2026-10-31');
        assert.equal(parseDateInput(' 31/10/2026 '), '2026-10-31');
        assert.equal(parseDateInput('1.9.2026'), '2026-09-01');
        assert.equal(parseDateInput('31-02-2026'), null, 'never rolls over to March');
        assert.equal(parseDateInput('2026-02-31'), null);
        assert.equal(parseDateInput('0026-10-31'), null, 'a half-typed year');
        assert.equal(parseDateInput('2026-10-3'), null);
        assert.equal(parseDateInput(''), null);
        assert.equal(parseDateInput('morgen'), null);
    });

    test('day-month order always, in every notation the date field accepts; d-m takes the year of today', () => {
        const today = '2026-11-02';
        for (const text of ['02-11-2026', '2-11-2026', '02/11/2026', '2/11/2026', '2.11.2026', '02112026', '2026-11-02', '2-11', '02/11', '2.11']) {
            assert.equal(parseDateInput(text, today), '2026-11-02', text);
        }
        assert.equal(parseDateInput('31-12', today), '2026-12-31', 'd-m is always this year, also later in the year');
        assert.equal(parseDateInput('29-2', '2028-01-10'), '2028-02-29');
        assert.equal(parseDateInput('29-2', today), null, 'no leap day in 2026');
        assert.equal(parseDateInput('31-02', today), null);
        assert.equal(parseDateInput('11/02/2026', today), '2026-02-11', 'never the American month/day order');
        assert.equal(parseDateInput('13-13', today), null);
        assert.equal(parseDateInput('2-11-', today), null);
        assert.equal(parseDateInput('211', today), null);
        assert.equal(parseDateInput('0211202', today), null);
    });

    test('a two-digit year is 20jj in every Dutch notation; one or three digits never make a date', () => {
        const today = '2026-11-04';
        for (const text of ['04-11-26', '4/11/26', '4-11-26', '4.11.26', '041126', '04-11-2026']) {
            assert.equal(parseDateInput(text, today), '2026-11-04', text);
        }
        assert.equal(parseDateInput('31-12-99', today), '2099-12-31', '99 is 2099, never 1999');
        assert.equal(parseDateInput('1-1-00', today), '2000-01-01', '00 is 2000');
        assert.equal(parseDateInput('29-02-28', today), '2028-02-29', 'leap day in 2028');
        assert.equal(parseDateInput('29-02-26', today), null, 'no leap day in 2026');
        assert.equal(parseDateInput('31-02-26', today), null, 'never rolls over to March');
        assert.equal(parseDateInput('04-11-6', today), null, 'a one-digit year');
        assert.equal(parseDateInput('04-11-026', today), null, 'a three-digit year');
        assert.equal(parseDateInput('0411026', today), null);
        assert.equal(formatDateInput(parseDateInput('4/11/26', today)!), '04-11-2026', 'the field rewrites the short form');
    });

    test('the field shows dd-mm-jjjj for every ISO date and leaves other text alone', () => {
        assert.equal(formatDateInput('2026-11-02'), '02-11-2026');
        assert.equal(formatDateInput('2026-02-31'), '2026-02-31');
        assert.equal(formatDateInput('31-02'), '31-02');
        assert.equal(formatDateInput(''), '');
    });
});

describe('today in the local time zone', () => {
    before(() => { process.env.TZ = 'Europe/Amsterdam'; });
    test('00:30 in Amsterdam is already the new day, while UTC is still on the old one', () => {
        const now = new Date(Date.UTC(2026, 10, 1, 23, 30));
        assert.equal(now.toISOString().slice(0, 10), '2026-11-01');
        assert.equal(localToday(now), '2026-11-02');
        assert.equal(localToday(new Date(Date.UTC(2026, 10, 2, 22, 59))), '2026-11-02');
        assert.equal(localToday(new Date(Date.UTC(2026, 10, 2, 23, 0))), '2026-11-03');
    });
});

describe('date shortcuts', () => {
    test('addDays crosses months, years and the switch to winter time, and rejects what is not a date', () => {
        assert.equal(addDays('2026-10-31', 1), '2026-11-01');
        assert.equal(addDays('2026-10-25', -1), '2026-10-24');
        assert.equal(addDays('2026-01-01', -2), '2025-12-30');
        assert.equal(addDays('2028-02-28', 1), '2028-02-29');
        assert.equal(addDays('2026-02-30', 1), null);
    });

    test('one letter is a day relative to today: V/T today, G/Y yesterday, M tomorrow, E two days back, O two days ahead', () => {
        const today = '2026-11-08';
        assert.deepEqual(['v', 'T', 'g', 'Y', 'm', 'E', 'o', ' v '].map((letter) => dateShortcut(letter, today)),
            ['2026-11-08', '2026-11-08', '2026-11-07', '2026-11-07', '2026-11-09', '2026-11-06', '2026-11-10', '2026-11-08']);
        for (const text of ['', 'x', 'vv', 'morgen', '1', 'v1']) assert.equal(dateShortcut(text, today), null, text);
    });
});

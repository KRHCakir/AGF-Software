import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, before, beforeEach, describe, test } from 'node:test';
import { isoToDateInput, localToday, parseDateInput, toLocalMidnightIso } from '../../lib/calendarDate.ts';
import { Mounted, setValue } from '../../test/reactHarness.ts';
import DateField from './DateField';

// Hetzelfde datumveld in een andere tijdzone (America/New_York, vlak voor middernacht): "vandaag" is de lokale dag, niet de
// UTC-dag, en een gekozen kalenderdag komt na de reis naar de server (lokale middernacht in UTC) als precies die dag terug.

const NOW = Date.UTC(2026, 10, 3, 4, 30); // 2 november 23:30 in New York, 3 november 04:30 UTC
const RealDate = Date;
class FixedDate extends RealDate {
    constructor(...args: ConstructorParameters<typeof Date> | []) {
        if (args.length === 0) super(NOW);
        else super(...(args as ConstructorParameters<typeof Date>));
    }
    static now() { return NOW; }
}

let mounted: Mounted;
let values: string[];

describe('DateField in another time zone', () => {
    before(() => {
        process.env.TZ = 'America/New_York';
        assert.equal(new RealDate(2026, 0, 15).getTimezoneOffset(), 300, 'test process must run in America/New_York');
    });
    beforeEach(() => {
        globalThis.Date = FixedDate as DateConstructor;
        values = [];
        mounted = new Mounted();
    });
    afterEach(async () => {
        await mounted.unmount();
        globalThis.Date = RealDate;
    });

    test('today is the local day (2 November), while UTC is already on 3 November', async () => {
        assert.equal(new Date().toISOString().slice(0, 10), '2026-11-03');
        assert.equal(localToday(), '2026-11-02');
        await mounted.render(<DateField id="when-input" value="" onChange={(next) => values.push(next)} />);
        const today = mounted.buttons().find((button) => button.textContent === 'date_field_today')!;
        await act(async () => { today.click(); });
        assert.deepEqual(values, ['2026-11-02']);
    });

    test('d-m takes the local year and the day survives the round trip to the server', async () => {
        await mounted.render(<DateField id="when-input" value="" onChange={(next) => values.push(next)} />);
        const input = mounted.container.querySelector<HTMLInputElement>('#when-input')!;
        await act(async () => { input.focus(); setValue(input, '2-11'); });
        assert.equal(values.at(-1), '2026-11-02');
        const sent = toLocalMidnightIso(values.at(-1)!);
        assert.equal(sent, '2026-11-02T05:00:00.000Z');
        assert.equal(isoToDateInput(sent!), '2026-11-02');
    });

    test('31 December just before midnight: d-m is still this year', () => {
        assert.equal(parseDateInput('1-1', localToday(new Date(Date.UTC(2027, 0, 1, 4, 30)))), '2026-01-01');
    });
});

import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, before, beforeEach, describe, test } from 'node:test';
import { Mounted, setValue } from '../../test/reactHarness.ts';
import DateField from './DateField';

// Het gedeelde datumveld met een vaste klok: 2 november 2026 00:30 in Europe/Amsterdam (1 november 23:30 UTC). Wat de gebruiker
// typt is altijd dag-maand-jaar; het formulier krijgt de ISO-datum, of de getypte tekst zolang dat geen datum is.

const NOW = Date.UTC(2026, 10, 1, 23, 30);
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
let setOutside: (value: string) => void;

function Harness({ initial }: { initial: string }) {
    const [value, setValueState] = useState(initial);
    setOutside = setValueState;
    return <DateField id="when-input" value={value} onChange={(next) => { values.push(next); setValueState(next); }} />;
}

const input = () => mounted.container.querySelector<HTMLInputElement>('#when-input')!;
const type = (text: string) => act(async () => { input().focus(); setValue(input(), text); });
const blur = () => act(async () => { input().dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); input().blur(); });
const last = () => values[values.length - 1];

describe('DateField', () => {
    before(() => {
        process.env.TZ = 'Europe/Amsterdam';
        assert.equal(new RealDate(2026, 0, 15).getTimezoneOffset(), -60, 'test process must run in Europe/Amsterdam');
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

    test('a plain text field: never a native date field whose order follows the browser language', async () => {
        await mounted.render(<Harness initial="" />);
        assert.equal(input().type, 'text');
        assert.equal(input().placeholder, 'date_field_placeholder');
    });

    test('an ISO value from outside shows as dd-mm-jjjj', async () => {
        await mounted.render(<Harness initial="2026-11-02" />);
        assert.equal(input().value, '02-11-2026');
        await act(async () => setOutside('2026-12-24'));
        assert.equal(input().value, '24-12-2026');
        await act(async () => setOutside(''));
        assert.equal(input().value, '');
    });

    for (const [text, iso] of [
        ['02-11-2026', '2026-11-02'], ['2-11-2026', '2026-11-02'], ['02/11/2026', '2026-11-02'], ['02112026', '2026-11-02'],
        ['2026-11-02', '2026-11-02'], ['2-11', '2026-11-02'], ['1-11', '2026-11-01'], ['3-11-2026', '2026-11-03'],
    ] as const) {
        test(`typing "${text}" gives ${iso}, and leaving the field shows dd-mm-jjjj`, async () => {
            await mounted.render(<Harness initial="" />);
            await type(text);
            assert.equal(last(), iso);
            await blur();
            assert.equal(input().value, `${iso.slice(8, 10)}-${iso.slice(5, 7)}-${iso.slice(0, 4)}`);
        });
    }

    test('what is not a date reaches the form as the typed text, never as a date or as empty', async () => {
        await mounted.render(<Harness initial="" />);
        for (const text of ['31-02-2026', '31-02', '11/31/2026', '2-11-20', 'morgen']) {
            await type(text);
            assert.equal(last(), text, text);
            await blur();
            assert.equal(input().value, text, 'the typed text stays visible so it can be corrected');
        }
        await type('');
        assert.equal(last(), '');
    });

    test('"Today" fills in the local calendar day: 2 November at 00:30 in Amsterdam, while UTC is still on 1 November', async () => {
        await mounted.render(<Harness initial="" />);
        const today = mounted.buttons().find((button) => button.textContent === 'date_field_today');
        assert.ok(today);
        await act(async () => { today!.click(); });
        assert.equal(last(), '2026-11-02');
        assert.equal(input().value, '02-11-2026');
    });

    test('typing over a filled-in date replaces it; half-typed text never keeps the old date', async () => {
        await mounted.render(<Harness initial="2026-11-02" />);
        await type('3-11');
        assert.equal(last(), '2026-11-03');
        await type('3-11-');
        assert.equal(last(), '3-11-');
    });

    test('disabled: no typing and no "Today"', async () => {
        await mounted.render(<DateField id="when-input" value="2026-11-02" disabled onChange={() => assert.fail('no change while disabled')} />);
        assert.equal(input().disabled, true);
        assert.equal(mounted.buttons().every((button) => button.disabled), true);
    });
});

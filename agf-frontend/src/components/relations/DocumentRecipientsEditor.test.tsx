import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, setValue } from '../../test/reactHarness.ts';
import { type RecipientRow, emptyRow } from '../../lib/documentRecipients';
import DocumentRecipientsEditor from './DocumentRecipientsEditor';

// Het blok met e-mailadressen voor documenten: Tab op het laatste veld van de laatste regel geeft een nieuwe regel (met focus op de naam),
// een bekende tikfout een hint met overnemen, en bij afleveradressen de keuze voor welk adres het geldt.

let mounted: Mounted;
let latest: RecipientRow[] = [];
function Harness({ initial, addresses }: { initial: RecipientRow[]; addresses: { id: number; purpose: string; text: string }[] | null }) {
    const [rows, setRows] = useState(initial);
    latest = rows;
    return <DocumentRecipientsEditor idPrefix="r" rows={rows} onChange={setRows} addresses={addresses} defaultKinds={['LoadingSlip']} dataTour="rec" />;
}
const tab = async (element: HTMLElement) => {
    const event = new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    await act(async () => { element.dispatchEvent(event); });
    return event.defaultPrevented;
};

describe('DocumentRecipientsEditor', () => {
    beforeEach(() => { mounted = new Mounted(); latest = []; });
    afterEach(async () => { await mounted.unmount(); });

    test('Tab on the last field of the last filled row adds a row and moves to its name; a blank last row adds nothing', async () => {
        await mounted.render(<Harness initial={[{ ...emptyRow(['TransportOrder']), email: 'planning@zeeland.nl' }]} addresses={null} />);
        const lastKind = mounted.container.querySelector<HTMLInputElement>('[data-tour="rec-kind-Other"]')!;
        assert.equal(await tab(lastKind), true);
        assert.equal(latest.length, 2);
        assert.deepEqual(latest[1].kinds, ['LoadingSlip'], 'a new row starts with the default documents');
        assert.equal(window.document.activeElement, mounted.container.querySelector('#r-1-name'));
        const secondLast = mounted.container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
        assert.equal(await tab(secondLast[secondLast.length - 1]), false, 'the new row is still blank');
        assert.equal(latest.length, 2);
    });

    test('a known typo gives a hint that can be taken over; with delivery addresses the scope is the last field', async () => {
        await mounted.render(<Harness initial={[emptyRow(['LoadingSlip'])]} addresses={[{ id: 9, purpose: 'Delivery', text: 'Winkel Centrum, Markt 1' }]} />);
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('#r-0-email')!, 'winkel@gmial.com'); });
        assert.ok(mounted.hasText('recipients_typo'));
        await act(async () => { mounted.button('recipients_typo_use')!.click(); });
        assert.equal(latest[0].email, 'winkel@gmail.com');
        const scope = mounted.container.querySelector<HTMLSelectElement>('#r-0-scope')!;
        assert.deepEqual(Array.from(scope.options).map((o) => o.value), ['', '9']);
        assert.equal(await tab(scope), true);
        assert.equal(latest.length, 2);
    });
});

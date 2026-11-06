import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import PartijNumberingSetting from './PartijNumberingSetting';

// Instelscherm partijnummering door de echte componentlevenscyclus: laden, live voorbeeld van de server, een ongeldig formaat direct
// geweigerd (zonder voorbeeld en zonder opslaan) en opslaan met PUT.

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];

const format = () => mounted.container.querySelector<HTMLInputElement>('[id="partijFormat-input"]')!;
const example = () => mounted.container.querySelector('[data-testid="partij-numbering-example"]')!.textContent ?? '';

describe('PartijNumberingSetting', () => {
    beforeEach(() => {
        calls = [];
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(input), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'PUT') return json(200, call.body);
            if (call.url.includes('/partijen/next-number')) {
                const params = new URL(call.url).searchParams;
                // Verkoop-partijen: eigen formaat en aantal cijfers (2 geeft 261140).
                if (params.get('kind') === 'Sales') return json(200, { kind: 'Sales', mode: 'Auto', format: params.get('format'), sequenceReset: params.get('reset'), suggestion: params.get('digits') === '2' ? '261140' : '2611140' });
                return json(200, { mode: 'Auto', format: params.get('format'), sequenceReset: params.get('reset'), suggestion: params.get('format') === '{JJJJ}-{volgnr}' ? '2026-007' : params.get('format') === '5{JJ}{MM}{volgnr}' ? '52611002' : '2611252' });
            }
            return json(200, { mode: 'Auto', format: '{JJ}{MM}{volgnr}', sequenceReset: 'Yearly' });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('shows the next number live and follows an edited format', async () => {
        await mounted.render(<PartijNumberingSetting previewDelayMs={0} />);
        await mounted.waitFor(() => example().includes('2611252'), 'example');
        assert.ok(example().startsWith('settings_partij_example'));
        await act(async () => { setValue(format(), '{JJJJ}-{volgnr}'); });
        await mounted.waitFor(() => example().includes('2026-007'), 'example for the new format');
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="partijReset-input"]')!, 'Monthly'); });
        await mounted.waitFor(() => calls.some((call) => call.url.includes('reset=Monthly')), 'preview with the new reset');
    });

    test('an invalid format is refused right away: no example and nothing saved', async () => {
        await mounted.render(<PartijNumberingSetting previewDelayMs={0} />);
        await mounted.waitFor(() => example().includes('2611252'), 'example');
        await act(async () => { setValue(format(), '{JJ}{MM}'); });
        assert.ok(mounted.hasText('settings_partij_format_error_sequence_once'));
        assert.ok(example().includes('–'));
        await act(async () => { mounted.button('settings_partij_save')!.click(); });
        assert.equal(calls.filter((call) => call.method === 'PUT').length, 0);
    });

    test('own number mode shows no example and fetches no suggestion', async () => {
        await mounted.render(<PartijNumberingSetting previewDelayMs={0} />);
        await mounted.waitFor(() => example().includes('2611252'), 'example');
        const before = calls.filter((call) => call.url.includes('/partijen/next-number')).length;
        await act(async () => { mounted.container.querySelector<HTMLInputElement>('input[value="Own"]')!.click(); });
        assert.ok(!example().includes('2611252'));
        assert.ok(example().includes('–'));
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
        assert.equal(calls.filter((call) => call.url.includes('/partijen/next-number')).length, before);
    });

    test('saving puts mode, format and reset', async () => {
        await mounted.render(<PartijNumberingSetting previewDelayMs={0} />);
        await mounted.waitFor(() => example().includes('2611252'), 'example');
        await act(async () => { mounted.container.querySelector<HTMLInputElement>('input[value="Own"]')!.click(); });
        await act(async () => { mounted.button('settings_partij_save')!.click(); });
        await mounted.waitFor(() => mounted.hasText('settings_partij_saved'), 'saved');
        assert.deepEqual(calls.find((call) => call.method === 'PUT')!.body,
            { mode: 'Own', format: '{JJ}{MM}{volgnr}', sequenceReset: 'Yearly', sequenceDigits: 3, salesFormat: '{JJ}{MM}{volgnr}', salesSequenceDigits: 3 });
    });

    test('fixed text in the purchase format and a second series for sales lots with its own digits', async () => {
        const salesExample = () => mounted.container.querySelector('[data-testid="partij-numbering-sales-example"]')!.textContent ?? '';
        await mounted.render(<PartijNumberingSetting previewDelayMs={0} />);
        await mounted.waitFor(() => example().includes('2611252') && salesExample().includes('2611140'), 'both examples');
        await act(async () => { setValue(format(), '5{JJ}{MM}{volgnr}'); });
        await mounted.waitFor(() => example().includes('52611002'), 'purchase example with a fixed 5');
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="salesSequenceDigits-input"]')!, '2'); });
        await mounted.waitFor(() => salesExample().includes('261140'), 'sales example with two digits');
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="sequenceDigits-input"]')!, '0'); });
        await act(async () => { mounted.button('settings_partij_save')!.click(); });
        assert.ok(mounted.hasText('settings_partij_digits_error'));
        assert.equal(calls.filter((call) => call.method === 'PUT').length, 0);
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="sequenceDigits-input"]')!, '3'); });
        await act(async () => { mounted.button('settings_partij_save')!.click(); });
        await mounted.waitFor(() => mounted.hasText('settings_partij_saved'), 'saved');
        assert.deepEqual(calls.find((call) => call.method === 'PUT')!.body,
            { mode: 'Auto', format: '5{JJ}{MM}{volgnr}', sequenceReset: 'Yearly', sequenceDigits: 3, salesFormat: '{JJ}{MM}{volgnr}', salesSequenceDigits: 2 });
    });
});

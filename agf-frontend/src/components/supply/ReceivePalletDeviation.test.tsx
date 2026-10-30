import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { DeviationCandidate, DeviationPreview, DeviationVariant } from '../../types/receiptDeviation';
import type { SupplyCommitment } from '../../types/supply';
import ReceivePalletForm from './ReceivePalletForm';

// Afwijkende ontvangst in het formulier: alleen een andere variant van hetzelfde artikel is te kiezen, "Klopt dit?" komt vóór
// er iets (ook de pallet) wordt aangemaakt, Nee boekt niets, Ja stuurt de bevestiging mee, en een 409 van de server toont
// hetzelfde venster en stuurt daarna dezelfde aanvraag (zelfde requestId) opnieuw.

const hass = (overrides: Partial<DeviationVariant> = {}): DeviationVariant => ({
    productVariantId: 10, productId: 1, productName: 'Avocado', variantName: 'Hass', origin: 'Peru', size: '18', packaging: 'Doos',
    packagingUnit: 'colli', tradeClass: 'I', brand: 'Verita', ...overrides,
});
const southAfrica = hass({ productVariantId: 12, origin: 'Zuid-Afrika' });
const candidates: DeviationCandidate[] = [
    { variant: southAfrica, shortcutCode: 'AvoHassZA', otherArticle: false, kinds: ['Origin'] },
    { variant: hass({ productVariantId: 14, variantName: 'Fuerte' }), shortcutCode: 'AvoFuerte', otherArticle: true, kinds: ['Other'] },
];
const preview = (overrides: Partial<DeviationPreview> = {}): DeviationPreview => ({
    status: 'Deviation', supplyCommitmentId: 46, orderNumber: 'IO-2026-0046', originalLineId: 501, expected: hass(), received: southAfrica, kinds: ['Origin'],
    quantity: 10, unit: 'colli', originalExpectedQuantity: 100, originalReceivedQuantity: 0, reducedQuantity: 10, originalExpectedQuantityAfter: 90,
    excessQuantity: 0, assignedQuantity: null, ...overrides,
});

const commitment: SupplyCommitment = {
    id: 46, requestId: 'r-46', vendorId: 1, vendorName: '4 Fruit Supplier', customerId: null, customerName: null, route: 'Warehouse', status: 'Expected',
    origin: 'Peru', destination: null, expectedAt: null, vendorGgns: [],
    lines: [{ id: 501, productVariantId: 10, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 100, expectedUnit: 'colli',
        expectedNetWeightKg: null, weightMethod: 'Weighed', defaultNetKgPerPackage: null, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null }],
};

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let previewAnswer: () => Response | Promise<Response>;
let contentAnswers: (() => Response)[];

const doc = () => window.document;
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const type = async (element: HTMLInputElement, value: string) => { await act(async () => { setValue(element, value); }); };
const key = async (element: HTMLElement, name: string) => { await act(async () => { element.dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); }); };
const submit = async () => { await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); }); };
const dialog = () => doc().querySelector<HTMLElement>('[data-tour="deviation-dialog"]');
const posts = () => calls.filter((call) => call.method === 'POST');
const contentPosts = () => posts().filter((call) => call.url.endsWith('/content-lines')).map((call) => call.body!);

async function arrange() {
    await mounted.render(<ReceivePalletForm actor="Kees" preselected={null} onPreselectedConsumed={() => {}} />);
    await mounted.waitFor(() => mounted.container.querySelector('#source-501') !== null, 'sources loaded');
    await act(async () => { mounted.container.querySelector<HTMLInputElement>('#source-501')!.click(); });
    await type(input('line.501.lotNumber'), 'LOT-ZA');
    await type(input('line.501.quantity'), '10');
    await type(input('line.501.netWeightKg'), '40');
}

async function chooseReceived(query: string) {
    await click(mounted.container.querySelector<HTMLButtonElement>('[data-tour="receive-deviation-toggle"]')!);
    const search = await mounted.waitFor(() => { const element = input('line.501.receivedVariant'); return element && !element.disabled ? element : null; }, 'candidates loaded');
    await type(search, query);
    await key(search, 'Enter');
}

describe('ReceivePalletForm deviation', () => {
    beforeEach(() => {
        calls = [];
        contentAnswers = [];
        previewAnswer = () => json(200, preview());
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const url = String(inputUrl);
            const call: Call = { method: init?.method ?? 'GET', url, body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'GET' && url.includes('/supply-commitments')) return json(200, [commitment]);
            if (call.method === 'GET' && url.includes('/deviation-candidates')) return json(200, candidates);
            if (call.method === 'GET' && url.includes('/deviation-preview')) return previewAnswer();
            if (call.method === 'GET') return json(500, {});
            if (url.endsWith('/pallets')) return json(201, { id: 77, reference: 'P-000077', logisticsStatus: 'Received', locationContext: 'Warehouse', warehouseLocation: null });
            if (url.endsWith('/content-lines')) {
                const answer = contentAnswers.shift();
                if (answer) return answer();
                const body = call.body!;
                return json(201, { id: 900, palletId: 77, supplyCommitmentLineId: 502, productVariantId: body.receivedProductVariantId ?? 10, lotNumber: body.lotNumber, quantity: body.quantity, unit: 'colli', netWeightKg: body.netWeightKg, weightMethod: 'Weighed', netKgPerPackage: null, ggn: null, qualityStatus: 'Quarantine' });
            }
            return json(404, {});
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('exact match books without a question', async () => {
        await arrange();
        await submit();
        await mounted.waitFor(() => contentPosts().length === 1, 'content posted');
        assert.equal(dialog(), null);
        assert.equal(calls.some((call) => call.url.includes('/deviation-preview')), false);
        assert.equal(contentPosts()[0].receivedProductVariantId, null);
        assert.equal(contentPosts()[0].confirmDeviation, null);
    });

    test('another article (other variety) is never chosen', async () => {
        await arrange();
        await chooseReceived('fuerte');
        assert.ok(mounted.hasText('deviation_other_article'));
        assert.equal(mounted.hasText('deviation_will_ask'), false, 'nothing chosen');
        await submit();
        await mounted.waitFor(() => contentPosts().length === 1, 'books the ordered article');
        assert.equal(contentPosts()[0].receivedProductVariantId, null);
    });

    test('a different variant asks first; No (Escape) books nothing, not even a pallet; Yes sends the confirmation', async () => {
        await arrange();
        await chooseReceived('za');
        assert.ok(mounted.hasText('deviation_will_ask'));
        await submit();
        const shown = await mounted.waitFor(dialog, 'dialog shown');
        assert.equal(posts().length, 0, 'nothing created before the answer');
        assert.equal(doc().activeElement?.textContent, 'deviation_no', 'No has the focus, so Enter never confirms Yes');
        const red = Array.from(shown.querySelectorAll('[data-differs="true"]')).map((row) => row.textContent);
        assert.equal(red.length, 1);
        assert.ok(red[0]!.includes('Peru') && red[0]!.includes('Zuid-Afrika'));
        assert.ok(shown.querySelector('[data-testid="deviation-consequence"]')!.textContent!.includes('deviation_consequence'));

        await key(shown, 'Escape');
        assert.equal(dialog(), null);
        assert.equal(posts().length, 0, 'No changes nothing');

        await submit();
        const again = await mounted.waitFor(dialog, 'dialog shown again');
        await act(async () => { setSelectValue(again.querySelector<HTMLSelectElement>('[data-tour="deviation-reason"]')!, 'Other'); });
        await click(again.querySelector<HTMLButtonElement>('[data-tour="deviation-yes"]')!);
        assert.ok(dialog()!.textContent!.includes('deviation_reason_note_required'), 'Other needs an explanation');
        assert.equal(posts().length, 0);
        await act(async () => { setSelectValue(dialog()!.querySelector<HTMLSelectElement>('[data-tour="deviation-reason"]')!, 'SupplierDeliveredDifferently'); });
        await click(dialog()!.querySelector<HTMLButtonElement>('[data-tour="deviation-yes"]')!);

        await mounted.waitFor(() => contentPosts().length === 1, 'content posted');
        assert.deepEqual(
            [contentPosts()[0].receivedProductVariantId, contentPosts()[0].confirmDeviation, contentPosts()[0].deviationReason, contentPosts()[0].deviationReasonNote],
            [12, true, 'SupplierDeliveredDifferently', null]);
        await mounted.waitFor(() => mounted.hasText('supply_receive_success'), 'success');
        assert.ok(mounted.hasText('deviation_recorded'));
    });

    test('while the deviation is being checked the input is locked, so what is booked is what was checked', async () => {
        await arrange();
        await chooseReceived('za');
        let release: (response: Response) => void = () => {};
        previewAnswer = () => new Promise<Response>((resolve) => { release = resolve; });
        await submit();
        await mounted.waitFor(() => calls.some((call) => call.url.includes('/deviation-preview')), 'preview requested');
        assert.equal(input('line.501.quantity').disabled, true);
        assert.equal(input('line.501.lotNumber').disabled, true);
        await act(async () => { release(json(200, preview())); });
        await mounted.waitFor(dialog, 'dialog shown');
        await key(dialog()!, 'Escape');
        assert.equal(input('line.501.quantity').disabled, false, 'editable again after No');
    });

    test('preview refusals are shown in plain language and nothing is booked', async () => {
        await arrange();
        await chooseReceived('za');
        previewAnswer = () => json(200, preview({ status: 'BlockedAssigned', assignedQuantity: 95 }));
        await submit();
        await mounted.waitFor(() => mounted.hasText('deviation_blocked_assigned'), 'blocked message');
        assert.equal(dialog(), null);
        assert.equal(posts().length, 0);
    });

    test('a 409 from the server shows the same dialog and Yes resends the same request', async () => {
        await arrange();
        contentAnswers.push(() => json(409, { message: 'confirm', code: 'DEVIATION_CONFIRMATION_REQUIRED', deviation: preview() }));
        await submit();
        await mounted.waitFor(dialog, 'dialog from the 409');
        await click(dialog()!.querySelector<HTMLButtonElement>('[data-tour="deviation-yes"]')!);
        await mounted.waitFor(() => contentPosts().length === 2, 'resent');
        const [first, second] = contentPosts();
        assert.equal(first.requestId, second.requestId);
        assert.equal(first.confirmDeviation, null);
        assert.equal(second.confirmDeviation, true);
        assert.equal(posts().filter((call) => call.url.endsWith('/pallets')).length, 1, 'one pallet');
    });
});

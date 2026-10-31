import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { DeviationPreview, DeviationVariant } from '../../types/receiptDeviation';
import type { SupplyCommitment } from '../../types/supply';
import ReceivePalletForm from './ReceivePalletForm';

// Blinde invoer bij ontvangst: lege velden, de bestelling (herkomst, maat, verpakking, kg) wordt niet vooraf getoond; de gebruiker typt
// wat er staat (snelkoppeling "PE" kiest Peru), alleen bestaande combinaties passen. Bij registreren: exact = boeken zonder venster;
// een andere variant of een ander gewicht = het venster vóór er iets wordt aangemaakt (Nee boekt niets); een combinatie die niet
// bestaat = melding met link naar Artikelen, nooit een nieuw artikel. Een 409 van de server toont hetzelfde venster.

const variant = (id: number, origin: string, code: string, size: string) =>
    ({ id, name: 'Hass', origin, originCountryCode: code, packaging: '4 kg doos', sizeCategory: size, shortcutCode: null, packagingUnit: 'colli', tradeClass: 'I', brand: null, defaultNetKgPerPackage: 4 });
const products = [{ id: 1, name: 'Avocado', variants: [variant(10, 'Peru', 'PE', '18'), variant(11, 'Peru', 'PE', '20'), variant(12, 'Zuid-Afrika', 'ZA', '18')] }];
const countries = { countries: [{ code: 'PE', nameNl: 'Peru', nameEn: 'Peru' }, { code: 'ZA', nameNl: 'Zuid-Afrika', nameEn: 'South Africa' }, { code: 'PY', nameNl: 'Paraguay', nameEn: 'Paraguay' }], aliases: [] };

const dto = (id: number, origin: string, size: string): DeviationVariant => ({
    productVariantId: id, productId: 1, productName: 'Avocado', variantName: 'Hass', origin, size, packaging: '4 kg doos', packagingUnit: 'colli', tradeClass: 'I', brand: null,
});
const preview = (status: DeviationPreview['status'], received: DeviationVariant, kinds: string[], overrides: Partial<DeviationPreview> = {}): DeviationPreview => ({
    status, supplyCommitmentId: 46, orderNumber: 'IO-2026-0046', originalLineId: 501, expected: dto(10, 'Peru', '18'), received, kinds,
    quantity: 10, unit: 'colli', originalExpectedQuantity: 10, originalReceivedQuantity: 0, reducedQuantity: kinds.length === 1 && kinds[0] === 'Weight' ? 0 : 10,
    originalExpectedQuantityAfter: 0, excessQuantity: 0, assignedQuantity: null, expectedKgPerPackage: 4, receivedKgPerPackage: 4, expectedNetKg: 40, receivedNetKg: 40, ...overrides,
});

const commitment: SupplyCommitment = {
    id: 46, requestId: 'r-46', vendorId: 1, vendorName: "Nature's Pride", customerId: null, customerName: null, route: 'Warehouse', status: 'Expected',
    origin: 'Peru', destination: null, expectedAt: null, vendorGgns: [],
    lines: [{ id: 501, productVariantId: 10, productName: 'Avocado', productVariantName: 'Hass', lotNumber: 'TEST-001', expectedQuantity: 10, expectedUnit: 'colli',
        expectedNetWeightKg: 40, weightMethod: 'Calculated', defaultNetKgPerPackage: 4, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null }],
};

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let contentAnswers: (() => Response)[];

const doc = () => window.document;
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const type = async (element: HTMLInputElement, value: string) => { await act(async () => { setValue(element, value); }); };
const key = async (element: HTMLElement, name: string) => { await act(async () => { element.dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); }); };
// Zoals in de browser: Tab kiest en daarna verlaat de focus het veld (blur).
const pick = async (id: string, value: string) => { await type(input(id), value); await key(input(id), 'Tab'); await act(async () => { input(id).dispatchEvent(new window.FocusEvent('blur')); input(id).dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); }); };
const submit = async () => { await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); }); };
const dialog = () => doc().querySelector<HTMLElement>('[data-tour="deviation-dialog"]');
const posts = () => calls.filter((call) => call.method === 'POST');
const previews = () => calls.filter((call) => call.url.includes('/deviation-preview')).map((call) => new URL(call.url).searchParams);
const contentPosts = () => posts().filter((call) => call.url.endsWith('/content-lines')).map((call) => call.body!);

// Server-antwoord op de controle: andere variant = herkomst/maat; ander kg per colli dan 4 (buiten 2%) = gewicht.
function answer(url: string): DeviationPreview {
    const params = new URL(url).searchParams;
    const id = Number(params.get('receivedProductVariantId'));
    const perPackage = Number(params.get('netWeightKg')) / Number(params.get('quantity'));
    const weight = Math.abs(perPackage - 4) / 4 > 0.02;
    const received = id === 12 ? dto(12, 'Zuid-Afrika', '18') : id === 11 ? dto(11, 'Peru', '20') : dto(10, 'Peru', '18');
    const kinds = [...(id === 12 ? ['Origin'] : id === 11 ? ['Size'] : []), ...(weight ? ['Weight'] : [])];
    return preview(kinds.length === 0 ? 'Exact' : 'Deviation', received, kinds, { receivedKgPerPackage: perPackage, receivedNetKg: perPackage * 10 });
}

async function arrange() {
    await mounted.render(<ReceivePalletForm actor="Kees" preselected={null} onPreselectedConsumed={() => {}} />);
    await mounted.waitFor(() => mounted.container.querySelector('#source-501') !== null, 'sources loaded');
    await act(async () => { mounted.container.querySelector<HTMLInputElement>('#source-501')!.click(); });
    await mounted.waitFor(() => input('line.501.origin'), 'attribute fields');
}

async function fill(origin: string, size: string, kgPerColli = '4') {
    await pick('line.501.origin', origin);
    await pick('line.501.size', size);
    await pick('line.501.packaging', '4');
    await type(input('line.501.lotNumber'), 'TEST-001');
    await type(input('line.501.quantity'), '10');
    await type(input('line.501.netKgPerPackage'), kgPerColli);
}

describe('ReceivePalletForm blind entry', () => {
    beforeEach(() => {
        calls = [];
        contentAnswers = [];
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const url = String(inputUrl);
            const call: Call = { method: init?.method ?? 'GET', url, body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'GET' && url.includes('/supply-commitments')) return json(200, [commitment]);
            if (call.method === 'GET' && url.includes('/products')) return json(200, products);
            if (call.method === 'GET' && url.includes('/countries')) return json(200, countries);
            if (call.method === 'GET' && url.includes('/deviation-preview')) return json(200, answer(url));
            if (call.method === 'GET') return json(500, {});
            if (url.endsWith('/pallets')) return json(201, { id: 77, reference: 'P-000077', logisticsStatus: 'Received', locationContext: 'Warehouse', warehouseLocation: null });
            if (url.endsWith('/content-lines')) {
                const next = contentAnswers.shift();
                if (next) return next();
                const body = call.body!;
                return json(201, { id: 900, palletId: 77, supplyCommitmentLineId: 501, productVariantId: body.receivedProductVariantId, lotNumber: body.lotNumber, quantity: body.quantity, unit: 'colli', netWeightKg: body.netWeightKg, weightMethod: 'Calculated', netKgPerPackage: 4, ggn: null, qualityStatus: 'Quarantine' });
            }
            return json(404, {});
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('empty fields: the ordered origin, size, packaging and kg are not shown in advance', async () => {
        await arrange();
        for (const field of ['line.501.origin', 'line.501.size', 'line.501.packaging', 'line.501.netKgPerPackage']) assert.equal(input(field).value, '', field);
        const text = mounted.container.textContent ?? '';
        assert.equal(text.includes('Peru'), false, 'no ordered origin');
        assert.equal(text.includes('40 kg'), false, 'no ordered kg');
        // Klasse en merk zijn bij dit artikel overal gelijk: niet gevraagd.
        assert.equal(mounted.container.querySelector('[id="line.501.tradeClass-input"]'), null);
    });

    test('keyboard order: origin, size, packaging, lot, quantity, kg', async () => {
        await arrange();
        const order = Array.from(mounted.container.querySelectorAll<HTMLInputElement>('input')).map((element) => element.id);
        const wanted = ['line.501.origin-input', 'line.501.size-input', 'line.501.packaging-input', 'line.501.lotNumber-input', 'line.501.quantity-input', 'line.501.netKgPerPackage-input'];
        assert.deepEqual(order.filter((id) => wanted.includes(id)), wanted);
    });

    test('the shortcut "PE" chooses Peru and the sizes follow the chosen origin (only existing combinations)', async () => {
        await arrange();
        await pick('line.501.origin', 'PE');
        assert.equal(input('line.501.origin').value, 'Peru');
        await type(input('line.501.size'), '2');
        assert.ok(mounted.hasText('20'), 'Peru has size 20');
        await type(input('line.501.size'), '');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[id="line.501.origin-input"] + button')!.click(); });
        await pick('line.501.origin', 'ZA');
        await type(input('line.501.size'), '2');
        assert.ok(mounted.hasText('receipt_attr_none'), 'South Africa has no size 20');
    });

    test('exactly what was ordered books without a dialog', async () => {
        await arrange();
        await fill('PE', '18');
        await submit();
        await mounted.waitFor(() => contentPosts().length === 1, 'content posted');
        assert.equal(dialog(), null);
        assert.equal(previews()[0].get('receivedProductVariantId'), '10');
        assert.equal(previews()[0].get('netWeightKg'), '40');
        assert.deepEqual([contentPosts()[0].receivedProductVariantId, contentPosts()[0].confirmDeviation], [10, null]);
    });

    test('another origin asks first: No books nothing (not even a pallet), Yes sends the confirmation', async () => {
        await arrange();
        await fill('ZA', '18');
        await submit();
        const shown = await mounted.waitFor(dialog, 'dialog');
        assert.equal(posts().length, 0);
        assert.equal(doc().activeElement?.textContent, 'deviation_no');
        const red = Array.from(shown.querySelectorAll('[data-differs="true"]')).map((row) => row.textContent ?? '');
        assert.equal(red.length, 1);
        assert.ok(red[0].includes('Peru') && red[0].includes('Zuid-Afrika'), 'plain country names, never codes');
        await key(shown, 'Escape');
        assert.equal(posts().length, 0, 'No changes nothing');

        await submit();
        await mounted.waitFor(dialog, 'dialog again');
        await act(async () => { setSelectValue(dialog()!.querySelector<HTMLSelectElement>('[data-tour="deviation-reason"]')!, 'SupplierDeliveredDifferently'); });
        await click(dialog()!.querySelector<HTMLButtonElement>('[data-tour="deviation-yes"]')!);
        await mounted.waitFor(() => contentPosts().length === 1, 'posted');
        assert.deepEqual([contentPosts()[0].receivedProductVariantId, contentPosts()[0].confirmDeviation, contentPosts()[0].deviationReason], [12, true, 'SupplierDeliveredDifferently']);
    });

    test('another size is a deviation too', async () => {
        await arrange();
        await fill('PE', '20');
        await submit();
        await mounted.waitFor(dialog, 'dialog');
        assert.equal(previews()[0].get('receivedProductVariantId'), '11');
    });

    test('another weight (4 -> 6 kg per colli) of the ordered variant gives the dialog', async () => {
        await arrange();
        await fill('PE', '18', '6');
        await submit();
        const shown = await mounted.waitFor(dialog, 'dialog');
        assert.ok((shown.textContent ?? '').includes('deviation_question_weight'));
        assert.equal(posts().length, 0);
    });

    test('a combination that does not exist gives the message with a link to Articles; nothing is checked or created', async () => {
        await arrange();
        await pick('line.501.origin', 'ZA');
        await type(input('line.501.size'), '20');
        await key(input('line.501.size'), 'Tab');
        await pick('line.501.packaging', '4');
        await type(input('line.501.lotNumber'), 'TEST-001');
        await type(input('line.501.quantity'), '10');
        await type(input('line.501.netKgPerPackage'), '4');
        await submit();
        await mounted.waitFor(() => mounted.hasText('receipt_combination_missing'), 'message');
        assert.ok(mounted.container.querySelector('a[href="/articles"]'));
        assert.equal(previews().length, 0);
        assert.equal(posts().length, 0);
    });

    test('origin and size are required before anything is checked', async () => {
        await arrange();
        await type(input('line.501.lotNumber'), 'TEST-001');
        await type(input('line.501.quantity'), '10');
        await type(input('line.501.netKgPerPackage'), '4');
        await submit();
        assert.equal(input('line.501.origin').getAttribute('aria-invalid'), 'true');
        assert.equal(input('line.501.size').getAttribute('aria-invalid'), 'true');
        assert.equal(previews().length, 0);
    });

    test('a 409 from the server shows the same dialog and Yes resends the same request', async () => {
        await arrange();
        await fill('PE', '18');
        contentAnswers.push(() => json(409, { message: 'confirm', code: 'DEVIATION_CONFIRMATION_REQUIRED', deviation: preview('Deviation', dto(10, 'Peru', '18'), ['Weight']) }));
        await submit();
        await mounted.waitFor(dialog, 'dialog from the 409');
        await click(dialog()!.querySelector<HTMLButtonElement>('[data-tour="deviation-yes"]')!);
        await mounted.waitFor(() => contentPosts().length === 2, 'resent');
        const [first, second] = contentPosts();
        assert.equal(first.requestId, second.requestId);
        assert.deepEqual([first.confirmDeviation, second.confirmDeviation], [null, true]);
        assert.equal(posts().filter((call) => call.url.endsWith('/pallets')).length, 1);
    });
});

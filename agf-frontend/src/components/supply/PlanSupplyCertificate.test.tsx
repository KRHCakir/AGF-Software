import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import { setInputLocale } from '../../lib/decimal.ts';
import { checkLines } from '../certificates/CertificateCheckDialog';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Certificaten deel 1: een avocado-order bij een leverancier met een GGN voor mango en limoen. De server vraagt "Klopt dit?" (409); Ja stuurt
// dezelfde opdracht (zelfde requestId) met het bevestigde product; Nee laat het formulier aanpassen, en daarna is het een nieuwe opdracht.

const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Teler Uno', searchCode: 'UNO', vatNumber: null, isActive: true, defaultCurrency: null }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: '4 kg box', sizeCategory: '18', shortcutCode: 'avo', packagingUnit: 'box' as const,
        defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: null }] }],
};
const checks = [{
    productId: 1, productName: 'Avocado', supplierName: 'Teler Uno', kinds: ['NotCovered', 'ExpiresBeforeDelivery'], deliveryDate: '2026-12-05',
    certificates: [{ certificateId: 9, number: '4049928415684', validUntil: '2026-12-01', productNames: ['Mango', 'Limoen'] }], message: 'GGN …',
}];

let mounted: Mounted;
let posted: { url: string; body: string }[];
let answers: Response[];
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = (id: string, keyName: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true })); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
const dialog = () => window.document.querySelector('[data-testid="certificate-check-dialog"]');
const button = (text: string) => Array.from(window.document.querySelectorAll('button')).find((b) => b.textContent === text)!;

setInputLocale('en');

describe('purchase order: certificate check', () => {
    beforeEach(() => {
        answers = [];
        posted = stubFetch({
            onGet: (url) => url.includes('/settings/purchase-currencies') ? json(200, { currencies: [{ code: 'EUR', hasEcbRate: false }] }) : json(404, {}),
            nextPost: async () => answers.shift() ?? json(500, { message: 'stop' }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const fill = async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await type('vendorId-input', 'uno');
        await key('vendorId-input', 'Enter');
        await type('expectedAt-input', '05-12-2026');
        await type('lines[0].productVariantId-input', 'avo');
        await key('lines[0].productVariantId-input', 'Enter');
        await type('lines[0].expectedQuantity-input', '100');
    };

    test('Yes, that is right sends the same order again with the confirmed product', async () => {
        answers = [json(409, { code: 'CERTIFICATE_CONFIRMATION_REQUIRED', message: 'confirm', checks })];
        await fill();
        await submit();
        await mounted.waitFor(() => dialog() !== null, 'dialog');
        assert.ok(dialog()!.textContent!.includes('cert_check_not_covered'));
        assert.ok(dialog()!.textContent!.includes('cert_check_expires_before'));
        assert.equal(window.document.activeElement, button('cert_check_no'), 'No has the focus');
        await act(async () => { button('cert_check_yes').click(); });
        await mounted.waitFor(() => posted.length === 2, 'sent again');
        const [first, second] = posted.map((p) => JSON.parse(p.body));
        assert.equal(second.requestId, first.requestId, 'the same command');
        assert.equal(first.confirmedCertificateProductIds, undefined);
        assert.deepEqual(second.confirmedCertificateProductIds, [1]);
        assert.equal(dialog(), null);
    });

    test('No, I will change it closes the question; saving again is a new command', async () => {
        answers = [json(409, { code: 'CERTIFICATE_CONFIRMATION_REQUIRED', message: 'confirm', checks })];
        await fill();
        await submit();
        await mounted.waitFor(() => dialog() !== null, 'dialog');
        await act(async () => { button('cert_check_no').click(); });
        assert.equal(dialog(), null);
        await submit();
        await mounted.waitFor(() => posted.length === 2, 'second save');
        assert.notEqual(JSON.parse(posted[1].body).requestId, JSON.parse(posted[0].body).requestId);
    });

    test('the lines of the question: not in the GGN, expired, expires before delivery', () => {
        const t = (k: string, params?: Record<string, string>) => `${k}${params ? JSON.stringify(params) : ''}`;
        const [notCovered, before] = checkLines(checks[0] as Parameters<typeof checkLines>[0], t, 'nl');
        assert.equal(notCovered, 'cert_check_not_covered{"ggns":"4049928415684","supplier":"Teler Uno","covered":"mangocert_check_andlimoen","product":"avocado"}'.replace('mangocert_check_andlimoen', 'mango cert_check_and limoen'));
        assert.ok(before.startsWith('cert_check_expires_before{"date":"1 dec 2026","delivery":"5 dec 2026"'), before);
    });
});

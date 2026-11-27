import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setSelectValue, setValue, stubFetch } from '../../test/reactHarness.ts';
import { setInputLocale } from '../../lib/decimal.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Feedback 22 november: op de inkooporder de ontvangstlocatie (directe handel = een locatie Onderweg), het laadadres ("Wij halen op" vult het
// adres van de leverancier) en het afleveradres (bij Onderweg het adres van een klant); alles gaat mee bij opslaan.
const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Fictieve Teler', searchCode: 'TEL', vatNumber: null, isActive: true }],
    customers: [{ id: 8, name: 'Fictieve Klant AE', searchCode: 'KLA', vatNumber: null, isActive: true } as ReferenceData['customers'][number]],
    products: [{ id: 1, name: 'Mango', variants: [{ id: 1, name: 'Kent', origin: 'Peru', packaging: '4 kg box', sizeCategory: null, shortcutCode: 'mango', packagingUnit: 'box', defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 240 }] }],
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let gets: string[];
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const tour = (name: string) => mounted.container.querySelector<HTMLElement>(`[data-tour="${name}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = (id: string, keyName: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true })); });
setInputLocale('en');

describe('purchase order: location and addresses', () => {
    beforeEach(() => {
        gets = [];
        posted = stubFetch({
            onGet: (url) => {
                gets.push(url);
                if (url.includes('/stock-locations/receive-defaults')) return json(200, { defaultReceiveLocationId: null, ownWarehouseAddress: 'DEMO-loods' });
                if (url.includes('/stock-locations')) return json(200, [{ id: 5, name: 'Onderweg naar Griekenland', kind: 'InTransit', address: null, countryCode: 'GR', isActive: true, inUse: false, version: 0 }]);
                if (url.includes('/address-proposals?vendorId=3')) return json(200, { vendorAddress: 'Fictieve Teler\\nCalle 1, Lima, PE', customerAddress: null });
                if (url.includes('/address-proposals?customerId=8')) return json(200, { vendorAddress: null, customerAddress: 'Fictieve Klant AE\\nOdos 2, Athene, GR' });
                return json(404, {});
            },
            nextPost: async () => json(500, { message: 'stop' }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('direct trade: a location in transit, we collect at the supplier and deliver at the customer', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        // November 2026: Eigen loods staat er als tekst met Afwijkend?; pas dan de lijst (met Onderweg en + Locatie toevoegen).
        await mounted.waitFor(() => mounted.hasText('DEMO-loods') && tour('supply-receive-location')?.tagName === 'BUTTON', 'default as text');
        await type('vendorId-input', 'tel');
        await key('vendorId-input', 'Enter');
        await act(async () => { tour('supply-receive-location').click(); });
        await mounted.waitFor(() => tour('supply-receive-location')?.querySelectorAll('option').length === 3, 'locations listed');
        await act(async () => setSelectValue(tour('supply-receive-location') as unknown as HTMLSelectElement, '5'));
        assert.ok(mounted.hasText('po_logistics_location_in_transit'), 'explains direct trade');

        await act(async () => { (tour('supply-pick-up') as HTMLInputElement).click(); });
        await mounted.waitFor(() => (tour('supply-loading-address') as HTMLTextAreaElement).value.startsWith('Fictieve Teler'), 'vendor address as loading address');

        await type('logistics.customer-input', 'kla');
        await key('logistics.customer-input', 'Enter');
        await mounted.waitFor(() => (tour('supply-delivery-address') as HTMLTextAreaElement).value.startsWith('Fictieve Klant AE'), 'customer address as delivery address');

        await type('expectedAt-input', '30-11-2026');
        await type('lines[0].productVariantId-input', 'mango');
        await key('lines[0].productVariantId-input', 'Enter');
        await type('lines[0].expectedQuantity-input', '240');
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => posted.length === 1, 'POST');
        assert.deepEqual(JSON.parse(posted[0].body).logistics, {
            receiveLocationId: 5, loadingAddress: 'Fictieve Teler\\nCalle 1, Lima, PE', deliveryAddress: 'Fictieve Klant AE\\nOdos 2, Athene, GR',
        });
    });
});

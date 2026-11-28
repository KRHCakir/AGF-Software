import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act, useState } from 'react';
import { Mounted, click, json, setSelectValue, stubFetch } from '../../test/reactHarness.ts';
import { type OrderLogisticsDraft, emptyLogistics, logisticsPayload, suggestedDeliveryCustomer } from '../../lib/orderLogistics.ts';
import type { CustomerOption } from '../../types/referenceData';
import type { StockLocation } from '../../types/stockLocation';
import OrderLogisticsFields from './OrderLogisticsFields';

// Blok Logistiek op de inkooporder (november 2026): twee plekken. Waar het vandaan komt (standaard levert de leverancier; Wij halen op toont
// het laadadres) en waar het binnenkomt (Eigen loods als tekst; een locatie met haar adres; Direct naar klant met het afleveradres van de
// klant). Geen los afleveradres meer; een eerder ingevuld afleveradres blijft zichtbaar met Leegmaken.

const location = (id: number, name: string, kind: StockLocation['kind'], address: string | null = null): StockLocation =>
    ({ id, name, kind, address, countryCode: null, isActive: true, inUse: false, version: 0 });
const direct = location(1, 'Direct naar klant (onderweg)', 'InTransit');
const venlo = location(3, 'Cross-dock Venlo', 'Crossdock', 'DEMO Cross-dock\nIndustrieweg 1, Venlo');
const customers = [{ id: 8, name: 'DEMO Klant Athene', searchCode: 'ATH', isActive: true } as CustomerOption];

let mounted: Mounted;
let current: OrderLogisticsDraft;

function Harness({ initial, locations = [direct, venlo], locked = false, suggested = null }: { initial: OrderLogisticsDraft; locations?: StockLocation[]; locked?: boolean; suggested?: number | null }) {
    const [value, setValue] = useState(initial);
    current = value;
    return (
        <form>
            <input id="before" />
            <OrderLogisticsFields value={value} onChange={(next) => { current = next; setValue(next); }} locations={locations} vendorId={3} customers={customers} locked={locked}
                errors={() => undefined} ownWarehouseAddress="DEMO-loods, Veilingweg 5" suggestedCustomerId={suggested} />
            <input id="after" />
        </form>
    );
}

const q = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);
const tour = (name: string) => q(`[data-tour="${name}"]`);
const tabbables = () => Array.from(mounted.container.querySelectorAll<HTMLElement>('input, select, textarea, button'))
    .filter((element) => !(element as HTMLInputElement).disabled).map((element) => element.getAttribute('data-tour') ?? element.id);
const choose = (id: string) => act(async () => setSelectValue(tour('supply-receive-location') as unknown as HTMLSelectElement, id));

describe('purchase order logistics', () => {
    beforeEach(() => {
        stubFetch({
            onGet: (url) => {
                if (url.includes('vendorId=3')) return json(200, { vendorAddress: 'DEMO Teler\nCalle 1, Lima', customerAddress: null });
                if (url.includes('customerId=8')) return json(200, { vendorAddress: null, customerAddress: 'DEMO Klant Athene\nOdos 2, Athene' });
                return json(404, {});
            },
            nextPost: async () => json(500, {}),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the default shows Wij halen op and Eigen loods as text: no list, no address fields, and only two tab stops', async () => {
        await mounted.render(<Harness initial={emptyLogistics()} />);
        assert.equal(q('select'), null);
        assert.equal(q('textarea'), null);
        assert.ok(mounted.hasText('po_logistics_vendor_delivers'));
        assert.ok(mounted.hasText('stock_location_own'));
        assert.deepEqual(tabbables(), ['before', 'supply-pick-up', 'supply-receive-location', 'after']);
    });

    test('Wij halen op shows the loading address filled from the supplier; unticking hides and empties it', async () => {
        await mounted.render(<Harness initial={emptyLogistics()} />);
        await click(tour('supply-pick-up')!);
        await mounted.waitFor(() => ((tour('supply-loading-address') as HTMLTextAreaElement | null)?.value.startsWith('DEMO Teler') ? true : null), 'loading address');
        await click(tour('supply-pick-up')!);
        assert.equal(tour('supply-loading-address'), null);
        assert.equal(current.loadingAddress, '');
    });

    test('a cross-dock shows its own address and no delivery address', async () => {
        await mounted.render(<Harness initial={{ ...emptyLogistics(), receiveLocationId: '3' }} />);
        assert.equal(q('[data-testid="receive-location-address"]')!.textContent, 'DEMO Cross-dock\nIndustrieweg 1, Venlo');
        assert.equal(tour('supply-delivery-address'), null);
        assert.equal(tour('supply-delivery-customer'), null);
        assert.deepEqual(Array.from((tour('supply-receive-location') as HTMLSelectElement).options).map((option) => option.textContent),
            ['stock_location_own', 'Cross-dock Venlo · stock_location_kind_Crossdock', 'Direct naar klant (onderweg) · stock_location_kind_InTransit']);
    });

    test('Direct naar klant shows the customer delivery address; back to a location empties it for saving', async () => {
        await mounted.render(<Harness initial={{ ...emptyLogistics(), receiveLocationId: '1', deliveryAddress: 'DEMO Klant Athene\nOdos 2, Athene' }} />);
        assert.ok(mounted.hasText('po_logistics_delivery_customer'));
        assert.ok(tour('supply-delivery-customer'));
        assert.equal((tour('supply-delivery-address') as HTMLTextAreaElement).value, 'DEMO Klant Athene\nOdos 2, Athene');
        await choose('3');
        assert.equal(tour('supply-delivery-address'), null);
        assert.equal(q('[data-testid="logistics-delivery-earlier"]'), null);
        assert.deepEqual(logisticsPayload(current), { receiveLocationId: 3, loadingAddress: null, deliveryAddress: null });
    });

    test('choosing Direct naar klant proposes the one customer of the linked sales order', async () => {
        await mounted.render(<Harness initial={{ ...emptyLogistics(), receiveLocationId: '3' }} suggested={8} />);
        await choose('1');
        await mounted.waitFor(() => (current.deliveryAddress.startsWith('DEMO Klant Athene') ? true : null), 'customer address');
        assert.equal(suggestedDeliveryCustomer([{ customerId: 8 }, { customerId: 8 }]), 8);
        assert.equal(suggestedDeliveryCustomer([{ customerId: 8 }, { customerId: 9 }]), null);
        assert.equal(suggestedDeliveryCustomer(undefined), null);
    });

    test('an earlier delivery address on Eigen loods is never dropped silently: shown with Leegmaken', async () => {
        await mounted.render(<Harness initial={{ ...emptyLogistics(), deliveryAddress: 'Oud adres\nStraat 1' }} />);
        assert.ok(mounted.hasText('po_logistics_delivery_earlier'));
        assert.equal(logisticsPayload(current).deliveryAddress, 'Oud adres\nStraat 1');
        await click(q('[data-testid="logistics-delivery-clear"]')!);
        assert.equal(current.deliveryAddress, '');
        assert.equal(q('[data-testid="logistics-delivery-earlier"]'), null);
    });

    test('a locked order shows only text: no list, no Afwijkend?, no Leegmaken', async () => {
        await mounted.render(<Harness initial={{ ...emptyLogistics(), receiveLocationId: '3', deliveryAddress: 'Oud adres', pickUp: true, loadingAddress: 'DEMO Teler' }} locked />);
        assert.equal(q('select'), null);
        assert.equal(tour('supply-receive-location'), null);
        assert.equal(q('[data-testid="logistics-delivery-clear"]'), null);
        assert.equal(q('[data-testid="receive-location-name"]')!.textContent, 'Cross-dock Venlo');
        assert.deepEqual(tabbables(), ['before', 'after']);
    });
});

import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { navigate } from '../../test/nextNavigationStub.ts';
import { emptyApiResponse } from '../../test/emptyApi.ts';
import { Mounted, json } from '../../test/reactHarness.ts';
import { hasOpenDirectTrade, isOpenDirectTradeSupply } from '../../lib/availability.ts';
import type { DirectTradeSupply, VariantAvailability } from '../../types/availability';
import AvailabilityPage from './availability/page';
import SalesOrdersPage from './sales-orders/page';
import nl from '../../i18n/nl.json';

// Opruimen (november 2026): Direct trade is geen aparte route meer. Goederen direct naar de klant zijn een inkooporder met ontvangstlocatie
// Direct naar klant (onderweg), verkocht met Uit verwachte aanvoer. De verkooppagina heeft geen paarse uitlegbalk meer, Beschikbaarheid toont
// het blok "Oude directe handel" alleen bij open oude aanvoer, en nl.json heeft geen interne termen meer.

const q = (quantity: number) => ({ quantity, netWeightKg: 0 });
const breakdown = (approved = 0) => ({ approved: q(approved), quarantine: q(0), blocked: q(0), rejected: q(0) });
const directSupply = (inTransit: number, expected = 0, warnings: DirectTradeSupply['warnings'] = []): DirectTradeSupply => ({
    customerId: 6, customerName: 'DEMO Klant Oud', unit: 'colli', customerBound: true, receivedOutsideWarehouse: breakdown(), inTransit: breakdown(inTransit),
    expectedOnPallets: breakdown(), expectedUnregistered: { quantity: expected, netWeightKg: null }, warnings,
});
const overview = (direct: DirectTradeSupply[]) => ({
    generatedAt: '2026-11-28T10:00:00Z', basis: { source: 'x', reservationsApplied: true, legacyInventoryIncluded: false, stockLedgerIncluded: false },
    variants: [{ productVariantId: 7, productName: 'Avocado', variantName: 'Hass', directTradeSupply: direct, warehouseSupply: [{
        unit: 'colli', weightBasis: null, availableBeforeReservations: q(40), reserved: { ...q(0), applied: true }, reservedOnHold: q(0), availableAfterReservations: q(40),
        warehouse: breakdown(40), receivedOutsideWarehouse: breakdown(), inTransit: breakdown(), expectedOnPallets: breakdown(), expectedUnregistered: { quantity: 0, netWeightKg: null }, warnings: [],
        sales: { productVariantId: 7, unit: 'colli', stock: 40, expected: 0, expectedNotReceived: 0, expectedInInspection: 0, sold: 0, soldReserved: 0, free: 40, freeStock: 40, shortageQuantity: 0, shortage: false },
    }] }],
});

let mounted: Mounted;
let availability: unknown;
const text = () => mounted.container.textContent ?? '';
const oldBlock = () => mounted.container.querySelector('[data-testid="availability-old-direct"]');

describe('old direct trade only where old data needs it', () => {
    beforeEach(() => {
        availability = overview([]);
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            const url = String(input);
            return url.includes('/availability') && !url.includes('/variants/') ? json(200, availability) : json(200, emptyApiResponse(url));
        }) as typeof fetch;
        navigate('/');
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the sales page has no purple bar: the subtitle says a line comes from stock or expected supply', async () => {
        await mounted.render(<SalesOrdersPage />);
        await mounted.waitFor(() => (text().includes('sales_subtitle') ? true : null), 'sales page');
        assert.equal(text().includes('sales_direct_trade_note'), false);
        assert.equal(mounted.container.querySelector('.bg-violet-50'), null, 'no violet notice on an empty sales page');
        assert.equal(nl.sales_subtitle.includes('Een regel komt uit voorraad of uit verwachte aanvoer (ook goederen die onderweg zijn naar de klant).'), true);
        assert.equal('sales_direct_trade_note' in nl, false);
    });

    test('Beschikbaarheid without open old direct trade shows no old block and no choice of blocks', async () => {
        availability = overview([directSupply(0)]);
        await mounted.render(<AvailabilityPage />);
        await mounted.waitFor(() => mounted.container.querySelector('#availability-warehouse'), 'warehouse block');
        assert.equal(oldBlock(), null);
        assert.equal(text().includes('availability_direct_title'), false);
        assert.equal(mounted.container.querySelectorAll('input[type="checkbox"]').length, 0);
    });

    test('Beschikbaarheid with open old direct trade shows the block as Oude directe handel', async () => {
        availability = overview([directSupply(20)]);
        await mounted.render(<AvailabilityPage />);
        await mounted.waitFor(() => oldBlock(), 'old direct trade block');
        assert.ok(oldBlock()!.textContent?.includes('availability_direct_title'));
        assert.equal(mounted.container.querySelectorAll('input[type="checkbox"]').length, 2);
        assert.equal(nl.availability_direct_title, 'Oude directe handel');
    });

    test('open means something still received outside, in transit, expected or signalled; closed rows are left out', () => {
        assert.equal(isOpenDirectTradeSupply(directSupply(0)), false);
        assert.equal(isOpenDirectTradeSupply(directSupply(5)), true);
        assert.equal(isOpenDirectTradeSupply(directSupply(0, 3)), true);
        assert.equal(isOpenDirectTradeSupply(directSupply(0, 0, [{ code: 'DIRECT_TRADE_IN_WAREHOUSE', supplyCommitmentLineId: 1, palletContentLineId: 2, quantity: 1 } as unknown as DirectTradeSupply['warnings'][number]])), true);
        const variant = (direct: DirectTradeSupply[]) => ({ directTradeSupply: direct }) as unknown as VariantAvailability;
        assert.equal(hasOpenDirectTrade([variant([directSupply(0)]), variant([])]), false);
        assert.equal(hasOpenDirectTrade([variant([directSupply(0)]), variant([directSupply(1)])]), true);
    });

    test('no visible "DirectTrade", "Direct trade" or "route Warehouse" in nl.json (placeholders such as {directTrade} are not visible)', () => {
        const visible = Object.entries(nl as Record<string, string>).map(([key, value]) => [key, value.replace(/\{[^}]*\}/g, '')] as const);
        assert.deepEqual(visible.filter(([, value]) => /DirectTrade|Direct trade|route Warehouse|Klantgebonden aanvoer/i.test(value)).map(([key]) => key), []);
        const page = readFileSync(join(process.cwd(), 'src/app/(admin)/sales-orders/page.tsx'), 'utf8');
        assert.equal(page.includes('sales_direct_trade_note'), false);
    });
});

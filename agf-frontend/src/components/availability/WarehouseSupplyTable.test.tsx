import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import { WarehouseRow } from '../../lib/availability';
import WarehouseSupplyTable from './WarehouseSupplyTable';

// Compact (standaard) versus alle kolommen; een bekende nul is "0", een onbekend gewicht blijft expliciet onbekend.

const q = (quantity: number, netWeightKg: number) => ({ quantity, netWeightKg });
const breakdown = (approved = 0, quarantine = 0, blocked = 0, rejected = 0) => ({
    approved: q(approved, approved * 4), quarantine: q(quarantine, quarantine * 4), blocked: q(blocked, blocked * 4), rejected: q(rejected, rejected * 4),
});
const row = (overrides: Record<string, unknown>): WarehouseRow => ({
    key: '1:box',
    variant: { productVariantId: 1, productName: 'Avocado', variantName: 'Hass', warehouseSupply: [], directTradeSupply: [] },
    supply: {
        unit: 'box', availableBeforeReservations: q(0, 0), reserved: q(0, 0), availableAfterReservations: q(0, 0), reservedOnHold: q(0, 0),
        warehouse: breakdown(), receivedOutsideWarehouse: breakdown(), expectedOnPallets: breakdown(), inTransit: breakdown(),
        expectedUnregistered: { quantity: 0, netWeightKg: null }, warnings: [], ...overrides,
    },
} as unknown as WarehouseRow);

let mounted: Mounted;
describe('WarehouseSupplyTable', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    const headers = () => Array.from(mounted.container.querySelectorAll('thead th')).map((th) => th.textContent?.trim() ?? '');

    test('compact view shows the five figures plus product, unit and signals; detailed view shows the full breakdown', async () => {
        const rows = [row({ warehouse: breakdown(10, 20), reserved: q(5, 20), availableAfterReservations: q(5, 20), expectedUnregistered: { quantity: 30, netWeightKg: null } })];
        await mounted.render(<WarehouseSupplyTable rows={rows} onOpen={() => {}} view="compact" />);
        assert.equal(mounted.container.querySelector('[data-view]')?.getAttribute('data-view'), 'compact');
        const compact = headers();
        assert.equal(compact.length, 8);
        assert.ok(compact.some((h) => h.startsWith('availability_col_on_hand')));
        assert.ok(compact.some((h) => h.startsWith('availability_col_expected_supply')));
        assert.ok(!compact.some((h) => h.startsWith('availability_now_available')), 'before-reservations column is detail only');
        assert.ok(mounted.hasText('availability_kg_not_known_yet'), 'expected supply keeps its unknown weight explicit');
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(<WarehouseSupplyTable rows={rows} onOpen={() => {}} view="detailed" />);
        assert.equal(mounted.container.querySelector('[data-view]')?.getAttribute('data-view'), 'detailed');
        assert.equal(headers().length, 17);
        assert.ok(headers().some((h) => h.startsWith('availability_now_available')));
    });

    test('a known zero renders as 0 with its 0 kg, never as a dash; an expected zero shows no fabricated weight', async () => {
        await mounted.render(<WarehouseSupplyTable rows={[row({ warehouse: breakdown(0, 60) })]} onOpen={() => {}} view="compact" />);
        const cells = Array.from(mounted.container.querySelectorAll('tbody td'));
        const reserved = cells[2];
        assert.match(reserved.textContent ?? '', /^0\s*0 kg$/);
        assert.equal(reserved.querySelector('[data-zero]')?.getAttribute('data-zero'), 'true');
        assert.ok(!mounted.hasText('–'), 'no dash for a known zero');
        const expected = cells[5];
        assert.equal(expected.textContent?.trim(), '0');
        assert.ok(!(expected.textContent ?? '').includes('kg'), 'unknown weight is not shown as 0 kg');
        const quarantine = cells[4];
        assert.match(quarantine.textContent ?? '', /60/);
    });

    test('opening a row passes the row to onOpen', async () => {
        let opened: WarehouseRow | null = null;
        await mounted.render(<WarehouseSupplyTable rows={[row({})]} onOpen={(r) => { opened = r; }} />);
        mounted.container.querySelector('tbody button')!.dispatchEvent(new window.MouseEvent('click', { bubbles: true }));
        assert.equal((opened as WarehouseRow | null)?.key, '1:box');
    });
});

import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import type { SalesOrder, SalesOrderLine } from '../../types/salesOrder';
import SalesStepBar from './SalesStepBar';

// De volgorde op de verkooporder: 1. Reserveren › 2. Verzenden › 3. Aflevering bevestigen, met de stap die nu aan de beurt is.
const line = (over: Partial<SalesOrderLine>) => ({ openQuantity: 0, reservedQuantity: 0, allocatedQuantity: 0, ...over } as SalesOrderLine);
const order = (lines: SalesOrderLine[], over: Partial<SalesOrder> = {}) => ({ status: 'Open', delivered: false, lines, ...over } as SalesOrder);
let mounted: Mounted;
const current = () => mounted.container.querySelector('[aria-current="step"]')?.textContent?.replace('›', '');

describe('SalesStepBar', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('marks the step that is next and says what to do', async () => {
        await mounted.render(<SalesStepBar order={order([line({ openQuantity: 4 })])} />);
        assert.equal(current(), '1. sales_step_reserve');
        assert.ok(mounted.hasText('sales_next_reserve'));
        await mounted.render(<SalesStepBar order={order([line({ reservedQuantity: 4 })])} />);
        assert.equal(current(), '2. sales_step_dispatch');
        assert.ok(mounted.hasText('✓ sales_step_reserve'), 'a passed step is ticked');
        await mounted.render(<SalesStepBar order={order([line({})], { delivered: true })} />);
        assert.equal(current(), undefined);
        assert.ok(mounted.hasText('sales_next_done'));
    });

    test('nothing on a cancelled order', async () => {
        await mounted.render(<SalesStepBar order={order([line({ openQuantity: 4 })], { status: 'Cancelled' })} />);
        assert.equal(mounted.container.textContent, '');
    });
});

import { randomUUID } from 'node:crypto';
import { ensureFlowMasterData } from './fixtures';
import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

// De hele keten uit de testlijst van de eigenaar, één keer via de knop Verkopen op Beschikbaarheid en één keer direct vanaf
// Verkooporders → Nieuwe verkooporder: Voorraad 40 en Verwacht 20 → 50 verkopen → Splitsen (40 uit voorraad, 10 uit verwachte
// aanvoer) → vastleggen met VO-nummer → Verkocht 50, Vrij 10 → 20 uit verwachte aanvoer → tekort 10 → Vrij −10 in rood met
// "Tekort". Elke run maakt een eigen artikel (kopie van het rondleidingsartikel met een eigen merk en snelcode), zodat de getallen
// exact zijn. Er wordt nergens een medewerker getypt. Draait mee met `npm run test:tour` (zie docs/demo-tour.md).

const API = process.env.TOUR_API_URL ?? 'http://localhost:5198/api';

type Variant = { id: number; name: string; shortcutCode: string | null; packagingUnit: string | null; defaultNetKgPerPackage: number | null; isArchived?: boolean };

async function get<T>(request: APIRequestContext, path: string): Promise<T> {
    const response = await request.get(`${API}${path}`);
    expect(response.ok(), `GET ${path}`).toBeTruthy();
    return (await response.json()) as T;
}

async function post<T>(request: APIRequestContext, path: string, data: unknown): Promise<T> {
    const response = await request.post(`${API}${path}`, { data });
    expect(response.ok(), `POST ${path}: ${await response.text()}`).toBeTruthy();
    return (await response.json()) as T;
}

// Een vers artikel met 40 goedgekeurd op voorraad (inkooporder 1, ontvangen en gekeurd) en 20 verwacht (inkooporder 2).
async function freshArticleWithStock(request: APIRequestContext) {
    const products = await get<{ name: string; variants: Variant[] }[]>(request, '/products?includeVariants=true');
    const hass = (products.find((product) => product.name.toLowerCase() === 'avocado')?.variants ?? []).filter((variant) => variant.name.toLowerCase().startsWith('hass'));
    const template = [...hass].sort((a, b) => a.id - b.id).find((variant) => variant.shortcutCode && variant.packagingUnit && variant.defaultNetKgPerPackage && !variant.isArchived);
    expect(template, 'Avocado Hass met snelcode, eenheid en kg per verpakking').toBeTruthy();
    const run = randomUUID().slice(0, 6).toUpperCase();
    const full = await get<Record<string, unknown>>(request, `/articles/${template!.id}`);
    const article = await post<{ id: number; shortcutCode: string; packagingUnit: string }>(request, '/articles', { ...full, id: undefined, shortcutCode: `E2E${run}`, brand: `E2E-${run}` });
    const unit = article.packagingUnit;
    const vendors = await get<{ id: number; name: string; isActive: boolean }[]>(request, '/purchase-invoices/vendors');
    const vendorId = vendors.find((vendor) => vendor.isActive && /^nature/i.test(vendor.name))?.id ?? vendors.find((vendor) => vendor.isActive)!.id;
    const day = (n: number) => new Date(Date.now() + n * 86_400_000).toISOString();
    const order = (quantity: number, inDays: number, reference: string) => post<{ id: number; orderNumber: string; lines: { id: number }[] }>(request, '/supply-commitments', {
        requestId: randomUUID(), vendorId, route: 'Warehouse', origin: 'Peru', expectedAt: day(inDays), supplierReference: reference,
        lines: [{ productVariantId: article.id, expectedQuantity: quantity, expectedUnit: unit }],
    });
    const stocked = await order(40, -1, `E2E-CHAIN-${run}-A`);
    const expected = await order(20, 2, `E2E-CHAIN-${run}-B`);
    const pallet = await post<{ id: number }>(request, '/pallets', { requestId: randomUUID(), logisticsStatus: 'Received', locationContext: 'Warehouse', warehouseLocation: 'E2E-01' });
    const content = await post<{ id: number; version: number }>(request, `/pallets/${pallet.id}/content-lines`, {
        requestId: randomUUID(), supplyCommitmentLineId: stocked.lines[0].id, lotNumber: `E2E-${run}`, quantity: 40, unit, weightMethod: 'Calculated', netKgPerPackage: 4,
    });
    await post(request, `/pallets/${pallet.id}/content-lines/${content.id}/quality-status`, {
        requestId: randomUUID(), expectedContentLineVersion: content.version, toStatus: 'Approved', inspectedBy: 'E2E-keurder',
    });
    return { variantId: article.id, code: article.shortcutCode, run, expectedOrder: expected.orderNumber };
}

async function openAvailability(page: Page, variantId: number) {
    await page.goto('/availability');
    await page.locator('[data-tour="availability-view-sales"]').click();
    await expect(page.locator(`[data-tour="availability-available"][data-tour-key="${variantId}"]`)).toBeVisible({ timeout: 30_000 });
}

async function expectRow(page: Page, variantId: number, stock: RegExp, free: RegExp) {
    await expect(page.locator(`[data-tour="availability-available"][data-tour-key="${variantId}"]`)).toContainText(stock);
    await expect(page.locator(`[data-tour="availability-free"][data-tour-key="${variantId}"]`)).toContainText(free);
}

// Kop van de order: klant, referentie en leverdatum. Geen medewerker-veld meer.
async function fillHeader(page: Page, reference: string) {
    await expect(page.locator('#actorReference-page')).toHaveCount(0);
    await page.locator('[data-tour="sales-customer"]').click();
    await page.keyboard.type('Lo');
    await page.waitForTimeout(200);
    await page.keyboard.press('Tab');
    await page.locator('[data-tour="sales-reference"]').fill(reference);
    const tomorrow = new Date(Date.now() + 86_400_000);
    await page.locator('[data-tour="sales-delivery"]').fill(`${tomorrow.getDate()}-${tomorrow.getMonth() + 1}-${String(tomorrow.getFullYear()).slice(2)}`);
    await page.locator('[data-tour="sales-delivery"]').press('Tab');
}

async function saveAndExpectNumber(page: Page): Promise<string> {
    await page.locator('[data-tour="sales-save"]').click();
    const number = page.locator('[data-tour="sales-saved-number"]');
    await expect(number).toContainText(/VO-\d{4}-\d{4}/, { timeout: 30_000 });
    return (await number.innerText()).trim();
}

async function firstOrderThenSplit(page: Page, reference: string) {
    await fillHeader(page, reference);
    await page.locator('[id="lines[0].quantity-input"]').fill('50');
    await expect(page.getByText(/Voorraad 40 · Verwacht 20 · Vrij 60/).first()).toBeVisible();
    await page.locator('[data-tour="sales-line-split"]').click();
    await expect(page.locator('[id="lines[0].quantity-input"]')).toHaveValue('40');
    await expect(page.locator('[id="lines[1].quantity-input"]')).toHaveValue('10');
    return saveAndExpectNumber(page);
}

async function secondOrderWithShortage(page: Page, reference: string) {
    await fillHeader(page, reference);
    await page.locator('[id="lines[0].quantity-input"]').fill('20');
    await page.locator('[data-tour="sales-line-source"][data-tour-key="Expected"]').check();
    await saveAndExpectNumber(page);
    await expect(page.getByRole('alert').filter({ hasText: /tekort/i }).first()).toContainText('10');
}

async function expectOrdersInList(page: Page, run: string, first: string) {
    await page.goto('/sales-orders');
    await page.locator('[data-tour="sales-search"]').fill(`E2E-SELL-${run}`);
    await expect(page.getByText(first).first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('Tekort').first()).toBeVisible();
}

test.describe('verkopen gekoppeld aan voorraad, de hele keten', () => {
    test.beforeAll(async ({ request }) => { await ensureFlowMasterData(request); });

    test.use({ timezoneId: 'Europe/Amsterdam' });
    test.beforeEach(async ({ context }) => {
        await context.addInitScript(() => { localStorage.setItem('agf_locale', 'nl'); });
    });

    test('via de knop Verkopen op Beschikbaarheid', async ({ page, request }) => {
        const setup = await freshArticleWithStock(request);
        await openAvailability(page, setup.variantId);
        await expectRow(page, setup.variantId, /^\s*40/, /^\s*60/);

        await page.locator(`[data-tour="availability-sell"][data-tour-key="${setup.variantId}"]`).first().click();
        await expect(page.locator('[id="lines[0].quantity-input"]')).toHaveValue('60', { timeout: 30_000 });
        const first = await firstOrderThenSplit(page, `E2E-SELL-${setup.run}-1`);

        await openAvailability(page, setup.variantId);
        await expectRow(page, setup.variantId, /^\s*40/, /^\s*10/);
        await page.locator(`[data-tour="availability-sell"][data-tour-key="${setup.variantId}"]`).first().click();
        await expect(page.locator('[id="lines[0].quantity-input"]')).toHaveValue('10', { timeout: 30_000 });
        await secondOrderWithShortage(page, `E2E-SELL-${setup.run}-2`);

        await openAvailability(page, setup.variantId);
        await expectRow(page, setup.variantId, /^\s*40/, /[-−]10/);
        await expect(page.locator(`[data-tour="availability-free"][data-tour-key="${setup.variantId}"]`)).toContainText(/tekort/i);
        await expectOrdersInList(page, setup.run, first);
    });

    test('direct vanaf Verkooporders → Nieuwe verkooporder, met dezelfde stand, waarschuwingen en splitsknop', async ({ page, request }) => {
        const setup = await freshArticleWithStock(request);
        await page.goto('/sales-orders');
        const create = page.locator('[data-tour="sales-new"]');
        await expect(create).toBeEnabled({ timeout: 30_000 });
        await create.click();
        await page.locator('[id="lines[0].productVariantId-input"]').fill(setup.code);
        await page.waitForTimeout(200);
        await page.locator('[id="lines[0].productVariantId-input"]').press('Tab');
        const first = await firstOrderThenSplit(page, `E2E-SELL-${setup.run}-1`);

        await openAvailability(page, setup.variantId);
        await expectRow(page, setup.variantId, /^\s*40/, /^\s*10/);

        await page.goto('/sales-orders');
        await page.locator('[data-tour="sales-new"]').click();
        await page.locator('[id="lines[0].productVariantId-input"]').fill(setup.code);
        await page.waitForTimeout(200);
        await page.locator('[id="lines[0].productVariantId-input"]').press('Tab');
        await secondOrderWithShortage(page, `E2E-SELL-${setup.run}-2`);

        await openAvailability(page, setup.variantId);
        await expectRow(page, setup.variantId, /^\s*40/, /[-−]10/);
        await expectOrdersInList(page, setup.run, first);
        // De order die vóór aankomst verkocht is, wijst naar de verwachte inkooporder.
        await page.getByText(first).first().click();
        await expect(page.getByText(setup.expectedOrder).first()).toBeVisible({ timeout: 30_000 });
    });
});

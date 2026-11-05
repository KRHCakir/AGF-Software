import { randomUUID } from 'node:crypto';
import { ensureFlowMasterData } from './fixtures';
import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

// Een volledige orderregel alleen met het toetsenbord (Tab, pijlen, Enter) op Inkooporder aanmaken en op een nieuwe verkooporder,
// en opslaan. De volgorde in de regel is artikel, aantal, eenheid, kg: de eenheid wordt niet meer overgeslagen. De kop van het
// formulier (leverancier of klant, datum) wordt gewoon ingevuld; het gaat om de regel. Draait mee met `npm run test:tour` (zelfde
// frontend en API, zie docs/demo-tour.md) en schrijft echte orders met referentie E2E-KEYS-… in de testdatabase.

const API = process.env.TOUR_API_URL ?? 'http://localhost:5198/api';
const UNITS = ['box', 'colli', 'tray', 'piece', 'dozen'];

type Variant = { id: number; name: string; shortcutCode: string | null; packagingUnit: string | null; defaultNetKgPerPackage: number | null; isArchived?: boolean };

// Het rondleidingsartikel (Avocado Hass met snelcode, eenheid en kg per verpakking), zoals de rondleiding dat ook gebruikt.
async function tourArticle(request: APIRequestContext): Promise<Variant> {
    const response = await request.get(`${API}/products?includeVariants=true`);
    expect(response.ok()).toBeTruthy();
    const products = (await response.json()) as { name: string; variants: Variant[] }[];
    const hass = (products.find((product) => product.name.toLowerCase() === 'avocado')?.variants ?? []).filter((variant) => variant.name.toLowerCase().startsWith('hass'));
    const article = [...hass].sort((a, b) => a.id - b.id).find((variant) => variant.shortcutCode && variant.packagingUnit && variant.defaultNetKgPerPackage && !variant.isArchived);
    expect(article, 'Avocado Hass met snelcode, eenheid en kg per verpakking').toBeTruthy();
    return article!;
}

const focusedId = (page: Page) => page.evaluate(() => document.activeElement?.id ?? '');

// Vanaf het artikelveld: code typen, Tab neemt de suggestie; aantal; Tab naar de eenheid; pijlen tot de eenheid van het artikel;
// Enter naar de kg. Geeft de id van het kg-veld terug.
async function keyboardLine(page: Page, prefix: string, quantityField: string, unitField: string, article: Variant, quantity: string) {
    await page.locator(`[id="${prefix}.productVariantId-input"]`).focus();
    await page.keyboard.type(article.shortcutCode!);
    await page.waitForTimeout(200);
    await page.keyboard.press('Tab');
    await expect.poll(() => focusedId(page)).toBe(`${prefix}.${quantityField}-input`);
    await page.keyboard.type(quantity);
    await page.keyboard.press('Tab');
    await expect.poll(() => focusedId(page)).toBe(`${prefix}.${unitField}-input`);
    const unit = page.locator(`[id="${prefix}.${unitField}-input"]`);
    const target = UNITS.indexOf(article.packagingUnit!);
    // Altijd met de pijlen naar de juiste eenheid, ook als die al was voorgevuld: eerst helemaal naar boven, dan omlaag.
    for (let i = 0; i < UNITS.length; i++) await page.keyboard.press('ArrowUp');
    await expect(unit).toHaveValue(UNITS[0]);
    for (let i = 0; i < target; i++) await page.keyboard.press('ArrowDown');
    await expect(unit).toHaveValue(article.packagingUnit!);
    await page.keyboard.press('Enter');
    await expect.poll(() => focusedId(page)).toMatch(new RegExp(`^${prefix.replace(/[[\]]/g, '\\$&')}\\.(expected)?[nN]etWeightKg-input$`));
}

test.describe('orderregel met alleen het toetsenbord', () => {
    test.beforeAll(async ({ request }) => { await ensureFlowMasterData(request); });

    test.use({ timezoneId: 'Europe/Amsterdam' });

    test.beforeEach(async ({ context }) => {
        await context.addInitScript(() => {
            localStorage.setItem('agf_locale', 'nl');
        });
    });

    test('Inkooporder aanmaken: artikel, aantal, eenheid met pijlen, kg en opslaan met Enter', async ({ page, request }) => {
        const article = await tourArticle(request);
        const reference = `E2E-KEYS-${randomUUID().slice(0, 8)}`;
        await page.goto('/purchase-orders/new');
        const vendor = page.locator('[data-tour="supply-vendor"]');
        await vendor.waitFor({ timeout: 30_000 });
        await vendor.click();
        await page.keyboard.type('Nature');
        await page.waitForTimeout(200);
        await page.keyboard.press('Tab');
        await page.locator('[data-tour="supply-reference"]').fill(reference);
        await page.locator('[data-tour="supply-expected"]').fill('04-11-26');
        await page.locator('[data-tour="supply-expected"]').press('Enter');
        await expect(page.locator('[data-tour="supply-expected"]')).toHaveValue('04-11-2026');

        await keyboardLine(page, 'lines[0]', 'expectedQuantity', 'expectedUnit', article, '7');
        await page.keyboard.press('Enter');
        await expect(page.getByText(/IO-\d{4}-\d{4}/).first()).toBeVisible({ timeout: 30_000 });

        const saved = await request.get(`${API}/purchase-orders?query=${reference}`);
        const orders = ((await saved.json()) as { items: { supplierReference: string; expectedQuantity: number }[] }).items.filter((order) => order.supplierReference === reference);
        expect(orders).toHaveLength(1);
        expect(orders[0].expectedQuantity).toBe(7);
    });

    test('Nieuwe verkooporder: artikel, aantal, eenheid met pijlen, kg en opslaan met Enter', async ({ page, request }) => {
        const article = await tourArticle(request);
        const reference = `E2E-KEYS-${randomUUID().slice(0, 8)}`;
        await page.goto('/sales-orders');
        const create = page.locator('[data-tour="sales-new"]');
        await expect(create).toBeEnabled({ timeout: 30_000 });
        await create.click();
        await page.locator('[data-tour="sales-customer"]').click();
        await page.keyboard.type('Lo');
        await page.waitForTimeout(200);
        await page.keyboard.press('Tab');
        await page.locator('[data-tour="sales-reference"]').fill(reference);
        await page.locator('[data-tour="sales-delivery"]').fill('4-11-26');
        await page.locator('[data-tour="sales-delivery"]').press('Tab');
        await expect(page.locator('[data-tour="sales-delivery"]')).toHaveValue('04-11-2026');

        await keyboardLine(page, 'lines[0]', 'quantity', 'unit', article, '2');
        await page.keyboard.press('Enter');
        await expect(page.locator('[data-tour="sales-saved-number"]')).toContainText(/VO-\d{4}-\d{4}/, { timeout: 30_000 });

        const saved = await request.get(`${API}/sales-orders?q=${reference}`);
        const body = (await saved.json()) as { items?: { customerReference: string; lines: { quantity: number; unit: string }[] }[] } | { customerReference: string; lines: { quantity: number; unit: string }[] }[];
        const orders = (Array.isArray(body) ? body : body.items ?? []).filter((order) => order.customerReference === reference);
        expect(orders).toHaveLength(1);
        expect(orders[0].lines.map((line) => [line.quantity, line.unit])).toEqual([[2, article.packagingUnit]]);
    });
});

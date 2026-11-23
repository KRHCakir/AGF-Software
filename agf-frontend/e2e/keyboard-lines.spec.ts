import { randomUUID } from 'node:crypto';
import { ensureFlowMasterData } from './fixtures';
import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

// Een volledige orderregel alleen met het toetsenbord (Tab, pijlen, Enter) op Inkooporder aanmaken en op een nieuwe verkooporder,
// en opslaan. Feedback 22 november: inkoop- en verkooporder hebben dezelfde regel: artikel, aantal, prijs, korting, btw (inkoop daarna GGN);
// eenheid en kg komen uit het artikel; Tab op het laatste veld maakt een nieuwe regel. De kop van het
// formulier (leverancier of klant, datum) wordt gewoon ingevuld; het gaat om de regel. Draait mee met `npm run test:tour` (zelfde
// frontend en API, zie docs/demo-tour.md) en schrijft echte orders met referentie E2E-KEYS-… in de testdatabase.

const API = process.env.TOUR_API_URL ?? 'http://localhost:5198/api';

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

// Vanaf het artikelveld: code typen, Tab neemt de suggestie; aantal, prijs, korting (leeg) en btw (voorstel uit het artikel) met Tab.
async function keyboardLine(page: Page, prefix: string, quantityField: string, article: Variant, quantity: string, price: string) {
    await page.locator(`[id="${prefix}.productVariantId-input"]`).focus();
    await page.keyboard.type(article.shortcutCode!);
    await page.waitForTimeout(200);
    await page.keyboard.press('Tab');
    await expect.poll(() => focusedId(page)).toBe(`${prefix}.${quantityField}-input`);
    await page.keyboard.type(quantity);
    await page.keyboard.press('Tab');
    await expect.poll(() => focusedId(page)).toBe(`${prefix}.unitPrice-input`);
    await page.keyboard.type(price);
    await page.keyboard.press('Tab');
    await expect.poll(() => focusedId(page)).toBe(`${prefix}.discountPercentage-input`);
    await page.keyboard.press('Tab');
    await expect.poll(() => focusedId(page)).toBe(`${prefix}.vatPercentage-input`);
}

test.describe('orderregel met alleen het toetsenbord', () => {
    test.beforeAll(async ({ request }) => { await ensureFlowMasterData(request); });

    test.use({ timezoneId: 'Europe/Amsterdam' });

    test.beforeEach(async ({ context }) => {
        await context.addInitScript(() => {
            localStorage.setItem('agf_locale', 'nl');
        });
    });

    test('Inkooporder aanmaken: snelcode, aantal, prijs; Tab op de prijs maakt een nieuwe regel; opslaan met Enter zonder lege regel', async ({ page, request }) => {
        const article = await tourArticle(request);
        const reference = `E2E-KEYS-${randomUUID().slice(0, 8)}`;
        await page.goto('/purchase-orders/new');
        const vendor = page.locator('[data-tour="supply-vendor"]');
        await vendor.waitFor({ timeout: 30_000 });
        await vendor.click();
        await page.keyboard.type('Nature');
        await page.waitForTimeout(200);
        // Tab-volgorde van de kop: Leverancier, Ordernummer leverancier, Besteldatum (daarna de knoppen van het datumveld en Verwacht).
        await page.keyboard.press('Tab');
        await expect.poll(() => focusedId(page)).toBe('supplierReference-input');
        await page.keyboard.type(reference);
        await page.keyboard.press('Tab');
        await expect.poll(() => focusedId(page)).toBe('orderedOn-input');
        await page.locator('[data-tour="supply-expected"]').fill('04-11-26');
        await page.locator('[data-tour="supply-expected"]').press('Enter');
        await expect(page.locator('[data-tour="supply-expected"]')).toHaveValue('04-11-2026');

        // Artikel, aantal, prijs, korting, btw, GGN; eenheid en kg komen uit het artikel en worden overgeslagen.
        await keyboardLine(page, 'lines[0]', 'expectedQuantity', article, '7', '12');
        // Certificaten deel 2: na de btw het GGN van het product (een voorstel of leeg); Tab daarop maakt de nieuwe regel.
        await page.keyboard.press('Tab');
        await expect.poll(() => focusedId(page)).toBe('lines[0].ggn-input');
        await page.keyboard.press('Tab');
        await expect.poll(() => focusedId(page)).toBe('lines[1].productVariantId-input');
        await expect(page.locator('[id="lines[0].unitPrice-input"]')).toHaveValue('12,00');
        await expect(page.locator('[data-testid="line-totals-amount"]')).toHaveText(/^84,00/);
        // Terug naar de prijs en Enter: opslaan; de lege tweede regel telt niet mee.
        await page.keyboard.press('Shift+Tab');
        await expect.poll(() => focusedId(page)).toBe('lines[0].ggn-input');
        for (let i = 0; i < 3; i++) await page.keyboard.press('Shift+Tab');
        await expect.poll(() => focusedId(page)).toBe('lines[0].unitPrice-input');
        await page.keyboard.press('Enter');
        // Het ordernummer van de leverancier is het hoofdnummer, het eigen IO-nummer staat eronder als intern nummer.
        await expect(page.locator('[data-testid="supply-order-number"] [data-testid="order-number-main"]')).toHaveText(reference, { timeout: 30_000 });
        await expect(page.locator('[data-testid="supply-order-number"] [data-testid="order-number-internal"]')).toHaveText(/Intern nummer IO-\d{4}-\d{4}/);

        const saved = await request.get(`${API}/purchase-orders?query=${reference}`);
        const orders = ((await saved.json()) as { items: { id: number; supplierReference: string; expectedQuantity: number }[] }).items.filter((order) => order.supplierReference === reference);
        expect(orders).toHaveLength(1);
        expect(orders[0].expectedQuantity).toBe(7);
        const detail = (await (await request.get(`${API}/purchase-orders/${orders[0].id}`)).json()) as { lines: { unitPrice: number; lineTotal: number; expectedUnit?: string; unit: string }[] };
        expect(detail.lines.map((line) => [line.unitPrice, line.lineTotal, line.unit])).toEqual([[12, 84, article.packagingUnit]]);
    });

    test('Nieuwe verkooporder: artikel, aantal, prijs, korting, btw zoals de inkooporder; Tab op de btw maakt een nieuwe regel; opslaan met Enter', async ({ page, request }) => {
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

        await keyboardLine(page, 'lines[0]', 'quantity', article, '2', '13,35');
        await page.keyboard.press('Tab');
        await expect.poll(() => focusedId(page)).toBe('lines[1].productVariantId-input');
        await expect(page.locator('[data-testid="sales-lines"] [data-testid="line-totals-amount"]')).toHaveText(/26,70/);
        // Terug met Shift+Tab: langs de bronkeuze onder de regel (Uit voorraad / Uit verwachte aanvoer), Btw en Korting naar Prijs.
        for (let i = 0; i < 8 && (await focusedId(page)) !== 'lines[0].unitPrice-input'; i++) await page.keyboard.press('Shift+Tab');
        await expect.poll(() => focusedId(page)).toBe('lines[0].unitPrice-input');
        await page.keyboard.press('Enter');
        await expect(page.locator('[data-tour="sales-saved-number"]')).toContainText(/VO-\d{4}-\d{4}/, { timeout: 30_000 });

        const saved = await request.get(`${API}/sales-orders?q=${reference}`);
        type Saved = { customerReference: string; lines: { quantity: number; unit: string; unitPrice: number; lineTotal: number }[] };
        const body = (await saved.json()) as { items?: Saved[] } | Saved[];
        const orders = (Array.isArray(body) ? body : body.items ?? []).filter((order) => order.customerReference === reference);
        expect(orders).toHaveLength(1);
        expect(orders[0].lines.map((line) => [line.quantity, line.unit, line.unitPrice, line.lineTotal])).toEqual([[2, article.packagingUnit, 13.35, 26.7]]);
    });
});

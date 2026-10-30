import { randomUUID } from 'node:crypto';
import { expect, test, type APIRequestContext, type BrowserContext, type Page } from '@playwright/test';

// Elk hoofdstuk van de demo-rondleiding door het echte paneel (Kijk mee, Snel) tegen een testdatabase: alle stappen
// komen voorbij, geen stap faalt (een mislukte controle stopt de rondleiding), de gebruikersnaam komt terug en het
// resultaat staat via de gewone GET-API in het systeem. Zie docs/demo-tour.md voor het opzetten van de testomgeving.

const API = process.env.TOUR_API_URL ?? 'http://localhost:5198/api';
const DATABASE = process.env.TOUR_TEST_DATABASE ?? '';
const ACTOR = 'E2E-tester';

let context: BrowserContext;

async function json<T>(request: APIRequestContext, path: string): Promise<T> {
    const response = await request.get(`${API}${path}`);
    expect(response.ok(), `GET ${path}`).toBeTruthy();
    return (await response.json()) as T;
}

// Voorwaarden via de gewone API: leverancier "Nature's Pride" en een klant op "Lo" (alleen aanmaken als ze ontbreken).
async function ensurePrerequisites(request: APIRequestContext) {
    const vendors = await json<{ name: string; isActive: boolean }[]>(request, '/purchase-invoices/vendors');
    if (!vendors.some((vendor) => vendor.isActive && /^nature/i.test(vendor.name))) {
        const created = await request.post(`${API}/relations`, { data: { requestId: randomUUID(), searchCode: 'NATURE', name: "Nature's Pride", preferredLanguage: 'nl', vendor: {} } });
        expect(created.ok(), await created.text()).toBeTruthy();
    }
    const customers = await json<{ name: string; isActive?: boolean }[]>(request, '/customers');
    if (!customers.some((customer) => customer.isActive !== false && /^lo/i.test(customer.name))) {
        const created = await request.post(`${API}/relations`, { data: { requestId: randomUUID(), searchCode: 'LOKAAL', name: 'Lokaal Fruit B.V.', preferredLanguage: 'nl', customer: {} } });
        expect(created.ok(), await created.text()).toBeTruthy();
    }
    // Afwijking bij ontvangst: een tweede variant van het rondleidingsartikel met een andere herkomst (zelfde maat en verpakking).
    type Variant = { id: number; name: string; shortcutCode: string | null; packagingUnit: string | null; defaultNetKgPerPackage: number | null; originCountryCode: string | null };
    const products = await json<{ name: string; variants: Variant[] }[]>(request, '/products?includeVariants=true');
    const hass = (products.find((product) => product.name.toLowerCase() === 'avocado')?.variants ?? []).filter((variant) => variant.name.toLowerCase().startsWith('hass'));
    const article = [...hass].sort((a, b) => a.id - b.id).find((variant) => variant.shortcutCode && variant.packagingUnit && variant.defaultNetKgPerPackage);
    if (article && !hass.some((variant) => variant.id !== article.id && variant.name === article.name && variant.originCountryCode !== article.originCountryCode)) {
        const full = await json<Record<string, unknown>>(request, `/articles/${article.id}`);
        const created = await request.post(`${API}/articles`, { data: { ...full, id: undefined, shortcutCode: `${article.shortcutCode}-ZA`, originCountryCode: article.originCountryCode === 'ZA' ? 'CL' : 'ZA' } });
        expect(created.ok(), await created.text()).toBeTruthy();
    }
}

// Speelt een hoofdstuk af en geeft de stapnummers die voorbijkwamen en de afsluittekst terug.
async function play(page: Page, chapter: string): Promise<{ steps: Set<number>; total: number; done: string }> {
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Demo-assistent' }).click();
    await page.getByRole('button', { name: 'Snel', exact: true }).first().click();
    await page.getByRole('button', { name: 'Kijk mee' }).click();
    await page.getByTestId(`tour-chapter-${chapter}`).getByRole('button').click();
    const panel = page.locator('section[aria-label="Demo-assistent"]');
    const steps = new Set<number>();
    let total = 0;
    let done = '';
    await expect.poll(async () => {
        const text = (await panel.innerText()).replace(/\s+/g, ' ');
        const counter = /Stap (\d+) van (\d+)/.exec(text);
        if (counter) {
            steps.add(Number(counter[1]));
            total = Number(counter[2]);
        }
        if (/kon niet uitgevoerd|kan nog niet starten/.test(text)) throw new Error(`Rondleiding gestopt: ${text}`);
        if (/Klaar:/.test(text)) done = text.slice(text.indexOf('Klaar:'));
        return done !== '';
    }, { timeout: 200_000, intervals: [200] }).toBe(true);
    return { steps, total, done };
}

function expectAllSteps(result: { steps: Set<number>; total: number }) {
    expect(result.total).toBeGreaterThan(0);
    expect([...result.steps].sort((a, b) => a - b)).toEqual(Array.from({ length: result.total }, (_, index) => index + 1));
}

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser, request }) => {
    if (DATABASE === '' || DATABASE === 'agf_smoke') {
        throw new Error('Zet TOUR_TEST_DATABASE op de naam van de testdatabase waar de API op draait (nooit agf_smoke).');
    }
    await ensurePrerequisites(request);
    context = await browser.newContext();
    // Een eigen DEMO-TOUR-reeks per run, zodat partijnummers niet botsen met eerdere runs op dezelfde testdatabase.
    const sequence = String(100 + (Date.now() % 800000));
    await context.addInitScript(([actor, start]) => {
        if (sessionStorage.getItem('tour-e2e-init')) return;
        sessionStorage.setItem('tour-e2e-init', '1');
        localStorage.setItem('agf_locale', 'nl');
        localStorage.setItem('agf_actor_reference', actor);
        if (!localStorage.getItem('agf_demo_tour_seq')) localStorage.setItem('agf_demo_tour_seq', start);
    }, [ACTOR, sequence]);
});

test.afterAll(async () => {
    await context?.close();
});

test('hoofdstuk Kennismaken: alleen navigeren, geen enkele schrijfactie', async () => {
    const page = await context.newPage();
    const writes: string[] = [];
    page.on('request', (request) => { if (request.url().startsWith(API) && request.method() !== 'GET') writes.push(`${request.method()} ${request.url()}`); });
    const result = await play(page, 'screens');
    expectAllSteps(result);
    expect(writes).toEqual([]);
    await page.close();
});

let tourRef = '';
let orderNumber = '';

test('hoofdstuk 1, Inkopen bestellen: order met ordernummer, leveranciersref en factuurnummer', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'order');
    expectAllSteps(result);
    [, orderNumber, tourRef] = /Order (IO-\d{4}-\d+) .* leveranciersref (DEMO-TOUR-\d+)/.exec(result.done) ?? [];
    expect(orderNumber).toMatch(/^IO-\d{4}-\d+$/);
    const list = await json<{ items: { orderNumber: string; supplierReference: string; status: string; invoiceNumbers: { invoiceNumber: string }[] }[] }>(request, `/purchase-orders?status=all&query=${tourRef}`);
    const order = list.items.find((item) => item.orderNumber === orderNumber)!;
    expect(order.supplierReference).toBe(tourRef);
    expect(order.status).toBe('Ordered');
    expect(order.invoiceNumbers.map((ref) => ref.invoiceNumber)).toEqual([`F-${tourRef}`]);
    expect(await page.evaluate(() => localStorage.getItem('agf_actor_reference'))).toBe(ACTOR);
    await page.close();
});

test('hoofdstuk 2, Ontvangen en keuren: ontvangen, goedgekeurd, deel teruggestuurd, creditnota verwacht', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'receive');
    expectAllSteps(result);
    expect(result.done).toContain(orderNumber);
    const list = await json<{ items: { id: number; orderNumber: string }[] }>(request, `/purchase-orders?status=all&query=${orderNumber}`);
    const detail = await json<{ receivedQuantity: number; rejectedQuantity: number; invoiceIndicator: string; receipts: { lotNumber: string; qualityStatus: string; origin: string }[] }>(
        request, `/purchase-orders/${list.items.find((item) => item.orderNumber === orderNumber)!.id}`);
    expect(detail.receivedQuantity).toBe(20);
    expect(detail.rejectedQuantity).toBe(5);
    expect(detail.invoiceIndicator).toBe('CreditNoteExpected');
    const lot = /Partij (DEMO-TOUR-\d+)/.exec(result.done)?.[1];
    expect(detail.receipts.filter((receipt) => receipt.lotNumber === lot).map((receipt) => [receipt.origin, receipt.qualityStatus]))
        .toEqual([['Receipt', 'Approved'], ['RejectionSplit', 'Rejected']]);
    expect(await page.evaluate(() => localStorage.getItem('agf_actor_reference'))).toBe(ACTOR);
    await page.close();
});

test('hoofdstuk 3, Verkopen en reserveren: order gereserveerd op een pallet en verzonden', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'sales');
    expectAllSteps(result);
    const reference = /Verkooporder (DEMO-TOUR-\d+)/.exec(result.done)?.[1];
    const orders = await json<{ customerReference: string | null; actorReference: string; lines: { quantity: number; dispatchedQuantity: number }[] }[]>(request, '/sales-orders');
    const order = orders.find((candidate) => candidate.customerReference === reference)!;
    expect(order.actorReference).toBe('Demo-assistent');
    expect(order.lines.map((line) => [line.quantity, line.dispatchedQuantity])).toEqual([[4, 4]]);
    expect(await page.evaluate(() => localStorage.getItem('agf_actor_reference'))).toBe(ACTOR);
    await page.close();
});

test('Doe zelf: de assistent wacht op de gebruiker en "Doe dit stuk voor mij" maakt de stap af', async () => {
    const page = await context.newPage();
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Demo-assistent' }).click();
    await page.getByRole('button', { name: 'Snel', exact: true }).first().click();
    await page.getByRole('button', { name: 'Doe zelf' }).click();
    await page.getByTestId('tour-chapter-order').getByRole('button').click();
    const panel = page.locator('section[aria-label="Demo-assistent"]');
    await expect(panel).toContainText('Jouw beurt', { timeout: 30_000 });
    // De assistent wacht echt: zonder actie blijft het bij stap 1.
    await page.waitForTimeout(1500);
    await expect(panel).toContainText('Stap 1 van');
    await expect(page.getByTestId('tour-blocker')).toHaveCount(0);
    // De gebruiker kiest zelf de bestemming; daarna laat hij de rest doen.
    await page.locator('[data-tour="supply-destination"][data-tour-key="Warehouse"]').click();
    await expect.poll(async () => {
        const text = (await panel.innerText()).replace(/\s+/g, ' ');
        if (/kon niet uitgevoerd/.test(text)) throw new Error(text);
        const help = panel.getByRole('button', { name: 'Doe dit stuk voor mij' });
        if (await help.isVisible()) await help.click();
        return /Klaar:/.test(text);
    }, { timeout: 200_000, intervals: [300] }).toBe(true);
    expect(await page.evaluate(() => localStorage.getItem('agf_actor_reference'))).toBe(ACTOR);
    await page.close();
});

test('Afwijking bij ontvangst: blind ingevuld, "Klopt dit?" en de afwijking op de order', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'deviation');
    expectAllSteps(result);
    const number = /Order (IO-\d{4}-\d+)/.exec(result.done)?.[1];
    const list = await json<{ items: { id: number; orderNumber: string; hasDeviation: boolean }[] }>(request, `/purchase-orders?status=all&query=${number}`);
    const order = list.items.find((item) => item.orderNumber === number)!;
    expect(order.hasDeviation).toBe(true);
    const detail = await json<{ deviations: { kinds: string[]; actorReference: string; reason: string }[] }>(request, `/purchase-orders/${order.id}`);
    expect(detail.deviations.map((deviation) => [deviation.actorReference, deviation.reason])).toEqual([['Demo-assistent', 'SupplierDeliveredDifferently']]);
    expect(await page.evaluate(() => localStorage.getItem('agf_actor_reference'))).toBe(ACTOR);
    await page.close();
});

test('hoofdstuk 4, Factureren: alleen een plek in de lijst', async () => {
    const page = await context.newPage();
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Demo-assistent' }).click();
    const chapter = page.getByTestId('tour-chapter-invoice');
    await expect(chapter).toContainText('Komt zodra facturen klaar zijn.');
    await expect(chapter.getByRole('button')).toHaveCount(0);
    await page.close();
});

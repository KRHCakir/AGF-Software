import { expect, test, type APIRequestContext, type BrowserContext, type Page } from '@playwright/test';

// Elk hoofdstuk van de demo-rondleiding door het echte paneel (Kijk mee, Snel) tegen een testdatabase: alle stappen komen voorbij of
// zijn bewust overgeslagen (wat ze aanmaken bestond al), geen stap faalt, en het resultaat staat via de gewone GET-API in het systeem.
// De rondleiding maakt haar eigen voorbeeldgegevens (DEMO-TOUR-…): de test zet niets klaar. Draai hem op een lege database
// (scripts/new-clean-db.sh, TOUR_EMPTY_DATABASE=true) en op een gevulde; zie docs/demo-tour.md.

const API = process.env.TOUR_API_URL ?? 'http://localhost:5198/api';
const DATABASE = process.env.TOUR_TEST_DATABASE ?? '';
// Lege database: het eerste hoofdstuk (De rode draad) moet dan alles zelf aanmaken, er wordt niets overgeslagen.
const EMPTY = process.env.TOUR_EMPTY_DATABASE === 'true';
// Screenshots per hoofdstuk (halverwege en bij Klaar) voor de documentatie, alleen als deze map is opgegeven.
const SHOTS = process.env.TOUR_SHOTS_DIR ?? '';
// De eigen onthouden beoordelaar bij de keuring; de rondleiding keurt even als Demo-assistent en zet hem daarna terug.
const ACTOR = 'E2E-tester';

let context: BrowserContext;

async function json<T>(request: APIRequestContext, path: string): Promise<T> {
    const response = await request.get(`${API}${path}`);
    expect(response.ok(), `GET ${path}`).toBeTruthy();
    return (await response.json()) as T;
}

// Wie de rondleiding vastlegt, is de ingelogde gebruiker van de API (nu de standaardgebruiker), nooit een getypte naam.
async function signedIn(request: APIRequestContext): Promise<string> {
    return (await json<{ displayName: string }>(request, '/me')).displayName;
}

interface Played { steps: Set<number>; skipped: number[]; total: number; done: string }

// Speelt een hoofdstuk af en geeft de stapnummers die voorbijkwamen, de overgeslagen stappen en de afsluittekst terug.
async function play(page: Page, chapter: string): Promise<Played> {
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Demo-assistent' }).click();
    await page.getByRole('button', { name: 'Snel', exact: true }).first().click();
    await page.getByRole('button', { name: 'Kijk mee' }).click();
    await page.getByTestId(`tour-chapter-${chapter}`).getByRole('button').click();
    const panel = page.locator('section[aria-label="Demo-assistent"]');
    const steps = new Set<number>();
    let total = 0;
    let done = '';
    let halfway = false;
    await expect.poll(async () => {
        const text = (await panel.innerText()).replace(/\s+/g, ' ');
        const counter = /Stap (\d+) van (\d+)/.exec(text);
        if (counter) {
            steps.add(Number(counter[1]));
            total = Number(counter[2]);
            if (SHOTS !== '' && !halfway && Number(counter[1]) >= Math.ceil(total * 0.6)) {
                halfway = true;
                await page.screenshot({ path: `${SHOTS}/tour-${chapter}-bezig.png` });
            }
        }
        if (/kon niet uitgevoerd|kan nog niet starten|ontbreekt nog iets/.test(text)) throw new Error(`Rondleiding gestopt: ${text}`);
        if (/Klaar:/.test(text)) done = text.slice(text.indexOf('Klaar:'));
        return done !== '';
    }, { timeout: 420_000, intervals: [150] }).toBe(true);
    if (SHOTS !== '') await page.screenshot({ path: `${SHOTS}/tour-${chapter}-klaar.png` });
    const skipped = ((await panel.locator('[data-tour-skipped]').getAttribute('data-tour-skipped')) ?? '').split(',').filter(Boolean).map(Number);
    return { steps, skipped, total, done };
}

// Elke stap kwam voorbij of is overgeslagen; een overgeslagen stap kan in de teller even te zien zijn geweest.
function expectAllSteps(result: Played) {
    expect(result.total).toBeGreaterThan(0);
    const covered = new Set([...result.steps, ...result.skipped]);
    expect([...covered].sort((a, b) => a - b)).toEqual(Array.from({ length: result.total }, (_, index) => index + 1));
}

const tourRefOf = (done: string) => /(DEMO-TOUR-\d+)/.exec(done)?.[1] ?? '';

// Op een lege database doet een los gestart hoofdstuk alles zelf (stamgegevens, order, ontvangst, verkoop): ruim de tijd.
test.describe.configure({ mode: 'serial', timeout: 600_000 });

test.beforeAll(async ({ browser }) => {
    if (DATABASE === '' || DATABASE === 'agf_smoke') {
        throw new Error('Zet TOUR_TEST_DATABASE op de naam van de testdatabase waar de API op draait (nooit agf_smoke).');
    }
    context = await browser.newContext();
    // Een eigen DEMO-TOUR-reeks per run, zodat kenmerken en partijnummers niet botsen met eerdere runs op dezelfde testdatabase.
    const sequence = String(100 + (Date.now() % 800000));
    await context.addInitScript(([actor, start]) => {
        if (sessionStorage.getItem('tour-e2e-init')) return;
        sessionStorage.setItem('tour-e2e-init', '1');
        localStorage.setItem('agf_locale', 'nl');
        localStorage.setItem('agf_inspection_inspector', actor);
        if (!localStorage.getItem('agf_demo_tour_seq')) localStorage.setItem('agf_demo_tour_seq', start);
    }, [ACTOR, sequence]);
});

test.afterAll(async () => {
    await context?.close();
});

async function expectActorBack(page: Page) {
    expect(await page.evaluate(() => localStorage.getItem('agf_inspection_inspector'))).toBe(ACTOR);
}

test('hoofdstuk Kennismaken: langs de vier menugroepen, geen enkele schrijfactie', async () => {
    const page = await context.newPage();
    const writes: string[] = [];
    page.on('request', (request) => { if (request.url().startsWith(API) && request.method() !== 'GET') writes.push(`${request.method()} ${request.url()}`); });
    const result = await play(page, 'screens');
    expectAllSteps(result);
    expect(result.total).toBe(10);
    expect(writes).toEqual([]);
    await page.close();
});

test('De rode draad, los gestart: maakt zelf aan wat ontbreekt (op een lege database alles) en toont de strip en de partij', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'chain');
    expectAllSteps(result);
    // Lege database: elke voorwaarde (stamgegevens, order, ontvangst, verkoop) is echt afgespeeld.
    if (EMPTY) expect(result.skipped).toEqual([]);
    // Eindigt op de partij, met de inkooporder erin.
    await expect(page.locator('[data-tour="partij-detail"]')).toBeVisible();
    await expect(page.locator('[data-tour="partij-order"]').first()).toHaveText(/IO-\d{4}-\d+/);
    const vendors = await json<{ name: string }[]>(request, '/purchase-invoices/vendors');
    expect(vendors.some((vendor) => vendor.name.startsWith('DEMO-TOUR-'))).toBe(true);
    await expectActorBack(page);
    await page.close();
});

test('hoofdstuk Stamgegevens: leverancier en klant met adres, artikel met herkomst, maat en klasse', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'masterdata');
    expectAllSteps(result);
    expect(result.skipped).toEqual([]);
    const ref = tourRefOf(result.done);
    expect(ref).toMatch(/^DEMO-TOUR-\d+$/);
    const relations = await json<{ items: { name: string; searchCode: string; customerId: number | null; vendorId: number | null; city: string | null }[] }>(request, `/relations?role=all&query=${ref}&includeInactive=false&page=1&pageSize=10`);
    expect(relations.items.map((item) => [item.searchCode, item.name, item.vendorId !== null, item.customerId !== null, item.city]).sort())
        .toEqual([[`${ref}-KLANT`, `${ref} Groothandel`, false, true, 'Rotterdam'], [`${ref}-LEV`, `${ref} Kwekerij`, true, false, 'Barendrecht']]);
    const articles = await json<{ shortcutCode: string; originCountryCode: string; sizeCategory: string; tradeClass: string; brand: string; packagingUnit: string; defaultNetKgPerPackage: number }[]>(request, '/articles');
    const article = articles.find((candidate) => candidate.shortcutCode === ref)!;
    expect([article.originCountryCode, article.sizeCategory, article.tradeClass, article.brand, article.packagingUnit, article.defaultNetKgPerPackage]).toEqual(['PE', '18', 'I', ref, 'colli', 4]);
    await page.close();
});

let orderNumber = '';

test('hoofdstuk Inkooporder aanmaken: stamgegevens bestaan al, order met partij, leveranciersref, datum en factuurnummer', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'order');
    expectAllSteps(result);
    // Leverancier en artikel van de rondleiding bestaan al (vorige hoofdstukken): die stappen zijn overgeslagen.
    expect(result.skipped.length).toBeGreaterThan(0);
    let tourRef = '';
    [, orderNumber, tourRef] = /Order (IO-\d{4}-\d+) .* leveranciersref (DEMO-TOUR-\d+)/.exec(result.done) ?? [];
    expect(orderNumber).toMatch(/^IO-\d{4}-\d+$/);
    const list = await json<{ items: { id: number; orderNumber: string; supplierReference: string; status: string; partijNumber: string | null; invoiceNumbers: { invoiceNumber: string }[] }[] }>(request, `/purchase-orders?status=all&query=${tourRef}`);
    const order = list.items.find((item) => item.orderNumber === orderNumber)!;
    expect(order.supplierReference).toBe(tourRef);
    expect(order.partijNumber).toBeTruthy();
    expect(result.done).toContain(`partij ${order.partijNumber}`);
    expect(order.status).toBe('Ordered');
    expect(order.invoiceNumbers.map((ref) => ref.invoiceNumber)).toEqual([`F-${tourRef}`]);
    // De verwachte datum is getypt met een tweecijferig jaar en als volledige datum opgeslagen (over twee dagen).
    const detail = await json<{ expectedAt: string; supplierLotReference: string | null }>(request, `/purchase-orders/${order.id}`);
    expect(detail.supplierLotReference).toBe(`LOT-${tourRef}`);
    expect(new Date(detail.expectedAt).getTime()).toBeGreaterThan(Date.now());
    await expectActorBack(page);
    await page.close();
});

test('hoofdstuk Ontvangst en keuring: blind ontvangen, goedgekeurd met Wie beoordeelt, deel teruggestuurd, creditnota verwacht', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'receive');
    expectAllSteps(result);
    expect(result.done).toContain(orderNumber);
    const list = await json<{ items: { id: number; orderNumber: string }[] }>(request, `/purchase-orders?status=all&query=${orderNumber}`);
    const detail = await json<{ partijNumber: string | null; receivedQuantity: number; rejectedQuantity: number; invoiceIndicator: string; receipts: { lotNumber: string; qualityStatus: string; origin: string }[]; history: { type: string; actorReference: string | null; inspectedBy?: string | null }[] }>(
        request, `/purchase-orders/${list.items.find((item) => item.orderNumber === orderNumber)!.id}`);
    expect(detail.receivedQuantity).toBe(20);
    expect(detail.rejectedQuantity).toBe(5);
    expect(detail.invoiceIndicator).toBe('CreditNoteExpected');
    const lot = /Partij (\S+) van/.exec(result.done)?.[1];
    expect(detail.partijNumber ?? lot).toBe(lot);
    expect(detail.receipts.filter((receipt) => receipt.lotNumber === lot).map((receipt) => [receipt.origin, receipt.qualityStatus]))
        .toEqual([['Receipt', 'Approved'], ['RejectionSplit', 'Rejected']]);
    // Wie beoordeelde (gekozen bij de keuring) én wie er ingelogd was.
    const approvals = detail.history.filter((event) => event.type === 'QualityChanged');
    expect(approvals.map((event) => [event.inspectedBy, event.actorReference])).toContainEqual(['Demo-assistent', await signedIn(request)]);
    await expectActorBack(page);
    await page.close();
});

test('hoofdstuk Afwijking bij ontvangst: andere herkomst en zwaarder, "Klopt dit?", afwijking op de order, voorraad op de exacte variant', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'deviation');
    expectAllSteps(result);
    const number = /Order (IO-\d{4}-\d+)/.exec(result.done)?.[1];
    const list = await json<{ items: { id: number; orderNumber: string; hasDeviation: boolean }[] }>(request, `/purchase-orders?status=all&query=${number}`);
    const order = list.items.find((item) => item.orderNumber === number)!;
    expect(order.hasDeviation).toBe(true);
    const detail = await json<{ deviations: { kinds: string[]; actorReference: string; reason: string }[] }>(request, `/purchase-orders/${order.id}`);
    expect(detail.deviations.map((deviation) => [deviation.actorReference, deviation.reason])).toEqual([[await signedIn(request), 'SupplierDeliveredDifferently']]);
    expect(detail.deviations[0].kinds).toEqual(expect.arrayContaining(['Origin', 'Weight']));
    // Het scherm eindigt op Beschikbaarheid van de variant uit het andere land: een andere variant dan besteld, die nu als regel op de order staat.
    const lines = (await json<{ lines: { id: number; productVariantId: number }[] }>(request, `/purchase-orders/${order.id}`)).lines.sort((a, b) => a.id - b.id);
    const shown = await page.locator('[data-tour="availability-free"]').getAttribute('data-tour-key');
    expect(shown).not.toBe(String(lines[0].productVariantId));
    expect(lines.map((line) => String(line.productVariantId))).toContain(shown);
    await expectActorBack(page);
    await page.close();
});

test('hoofdstuk Voorraad en verkopen voor aankomst: verkocht met tekort, ontvangen en gekeurd, oudste partij eerst gereserveerd', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'presell');
    expectAllSteps(result);
    const number = /Verkooporder (VO-\d{4}-\d+)/.exec(result.done)?.[1];
    expect(number, result.done).toBeTruthy();
    const orders = await json<{ orderNumber: string; actorReference: string; shortage: boolean; lines: { supplySource: string; reservedQuantity: number; reservations: { supplyOrderNumber: string | null; lotNumber: string | null }[] }[] }[]>(request, `/sales-orders?q=${number}`);
    const order = orders.find((candidate) => candidate.orderNumber === number)!;
    expect(order.actorReference).toBe(await signedIn(request));
    expect(order.lines.map((line) => line.supplySource)).toEqual(['Expected']);
    expect(order.shortage).toBe(true);
    expect(order.lines[0].reservedQuantity).toBeGreaterThan(0);
    expect(order.lines[0].reservations.every((reservation) => /IO-\d{4}-\d+/.test(reservation.supplyOrderNumber ?? '') && !!reservation.lotNumber)).toBe(true);
    await expectActorBack(page);
    await page.close();
});

test('hoofdstuk Verkooporder: VO-nummer, gereserveerd met inkooporder, partij en pallet, verzonden', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'sales');
    expectAllSteps(result);
    const number = /Verkooporder (VO-\d{4}-\d+)/.exec(result.done)?.[1];
    const orders = await json<{ orderNumber: string; customerReference: string | null; actorReference: string; lines: { quantity: number; dispatchedQuantity: number }[] }[]>(request, `/sales-orders?q=${number}`);
    const order = orders.find((candidate) => candidate.orderNumber === number)!;
    expect(order.customerReference).toMatch(/^DEMO-TOUR-\d+$/);
    expect(order.actorReference).toBe(await signedIn(request));
    expect(order.lines.map((line) => [line.quantity, line.dispatchedQuantity])).toEqual([[4, 4]]);
    await expectActorBack(page);
    await page.close();
});

test('hoofdstuk Inkoopfactuur invoeren: factuur van de rondleidingsleverancier met bedrag 120,00', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'purchase-invoice');
    expectAllSteps(result);
    const number = /(F-DEMO-TOUR-\d+)/.exec(result.done)?.[1];
    const invoices = await json<{ invoiceNumber: string | null; vendorName: string | null; totalAmount: number }[]>(request, '/purchase-invoices');
    const invoice = invoices.find((candidate) => candidate.invoiceNumber === number)!;
    expect(invoice.vendorName).toMatch(/^DEMO-TOUR-\d+ Kwekerij$/);
    expect(invoice.totalAmount).toBeGreaterThanOrEqual(120);
    await page.close();
});

// Kosten verdelen (P2): een directe factuur van derden (Handling € 5.000) zonder inkooporder; € 500 op de verkoop-partij van de rondleiding,
// € 4.500 blijft algemene kosten, en de partij toont de kosten. Op een lege database zou het hoofdstuk eerst een verkoop aanmaken; hier
// bestaat die al (hoofdstuk Verkooporder hierboven), dus worden die stappen overgeslagen.
test('hoofdstuk Kosten verdelen: handling € 5.000 zonder inkooporder, € 500 op een verkoop-partij, de rest algemene kosten', async ({ request }) => {
    const page = await context.newPage();
    const result = await play(page, 'costs');
    expectAllSteps(result);
    const ref = tourRefOf(result.done);
    const lot = /verkoop-partij (\S+?),/.exec(result.done)?.[1] ?? '';
    const invoices = await json<{ id: number; invoiceNumber: string | null; vendorName: string | null; subTotal: number; lines: { costArticleName?: string | null }[] }[]>(request, '/purchase-invoices');
    const invoice = invoices.find((candidate) => candidate.invoiceNumber === `K-${ref}`)!;
    expect([invoice.vendorName?.startsWith('DEMO-TOUR-'), invoice.subTotal, invoice.lines[0].costArticleName]).toEqual([true, 5000, 'Handling']);
    const allocations = await json<{ lines: { allocated: number; general: number; allocations: { number: string; partijKind: string; amount: number; partijId: number }[] }[] }>(request, `/purchase-invoices/${invoice.id}/allocations`);
    const line = allocations.lines[0];
    expect([line.allocated, line.general, line.allocations.map((allocation) => [allocation.number, allocation.partijKind, allocation.amount])]).toEqual([500, 4500, [[lot, 'Sales', 500]]]);
    const partij = await json<{ costs: { kind: string; invoiceNumber: string; amount: number }[]; costTotal: number }>(request, `/partijen/${line.allocations[0].partijId}`);
    expect(partij.costs.filter((cost) => cost.invoiceNumber === `K-${ref}`).map((cost) => [cost.kind, cost.amount])).toEqual([['Handling', 500]]);
    await page.close();
});

test('Doe zelf: de gebruiker kiest zelf de leverancier en de rondleiding gaat verder; "Doe dit stuk voor mij" doet de rest', async ({ request }) => {
    // De leverancier van de rondleiding (hoofdstuk Stamgegevens hierboven), zoals de gebruiker hem zou typen: zijn zoekcode.
    const vendors = await json<{ id: number; searchCode: string | null; isActive: boolean }[]>(request, '/purchase-invoices/vendors');
    const search = vendors.filter((vendor) => vendor.isActive && (vendor.searchCode ?? '').startsWith('DEMO-TOUR-')).sort((a, b) => b.id - a.id)[0].searchCode!;
    const page = await context.newPage();
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Demo-assistent' }).click();
    await page.getByRole('button', { name: 'Snel', exact: true }).first().click();
    await page.getByRole('button', { name: 'Doe zelf' }).click();
    await page.getByTestId('tour-chapter-order').getByRole('button').click();
    const panel = page.locator('section[aria-label="Demo-assistent"]');
    await expect(panel).toContainText('Leverancier kiezen', { timeout: 60_000 });
    await expect(panel).toContainText('Jouw beurt');
    await expect(page.getByTestId('tour-blocker')).toHaveCount(0);
    // De gebruiker doet de gevraagde stap zelf; de assistent ziet dat en gaat door naar de volgende stap.
    const vendor = page.locator('[data-tour="supply-vendor"]');
    await vendor.click();
    await page.keyboard.type(search);
    await page.waitForTimeout(300);
    await page.keyboard.press('Tab');
    await expect(panel).not.toContainText('Leverancier kiezen', { timeout: 30_000 });
    await expect(panel).toContainText('Partij');
    await expect.poll(async () => {
        const text = (await panel.innerText()).replace(/\s+/g, ' ');
        if (/kon niet uitgevoerd/.test(text)) throw new Error(text);
        const help = panel.getByRole('button', { name: 'Doe dit stuk voor mij' });
        if (await help.isVisible()) await help.click();
        return /Klaar:/.test(text);
    }, { timeout: 300_000, intervals: [300] }).toBe(true);
    await expectActorBack(page);
    await page.close();
});

test('Verkoopfacturen: alleen een plek in de lijst', async () => {
    const page = await context.newPage();
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Demo-assistent' }).click();
    const chapter = page.getByTestId('tour-chapter-invoice');
    await expect(chapter).toContainText('Komt zodra facturen klaar zijn.');
    await expect(chapter.getByRole('button')).toHaveCount(0);
    await page.close();
});

import { expect, test, type Page } from '@playwright/test';

// De datum van vandaag in de twee schermen waar "Vul een geldige datum in." verscheen (Nieuwe verkooporder en Aanvoer plannen),
// in Chrome met een Engelse (en-US) en een Nederlandse (nl-NL) browsertaal. Het veld is een gewoon tekstveld met dd-mm-jjjj,
// dus de browsertaal verandert niets aan de volgorde. Er wordt niets opgeslagen: de overige verplichte velden blijven leeg, zodat
// verzenden alleen de controle laat zien. Draait mee met `npm run test:tour` (zelfde frontend en API, zie docs/demo-tour.md).

const ACTOR = 'E2E-tester';

// Vandaag in Europe/Amsterdam, zoals de browsercontext hieronder (timezoneId) hem ziet.
function todayInAmsterdam(): { dutch: string; iso: string } {
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Amsterdam', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
    const get = (type: string) => parts.find((part) => part.type === type)!.value;
    return { dutch: `${get('day')}-${get('month')}-${get('year')}`, iso: `${get('year')}-${get('month')}-${get('day')}` };
}

async function openSalesOrder(page: Page) {
    await page.goto('/sales-orders');
    const create = page.locator('[data-tour="sales-new"]');
    await expect(create).toBeEnabled({ timeout: 30_000 });
    await create.click();
    return { field: page.locator('[id="requestedDeliveryDate-input"]'), error: page.locator('[id="requestedDeliveryDate"]'), other: page.locator('[id="customerId"]') };
}

async function openPlanSupply(page: Page) {
    await page.goto('/supply');
    await page.locator('input[name="supply-destination"][value="Warehouse"]').check({ timeout: 30_000 });
    return { field: page.locator('[id="expectedAt-input"]'), error: page.locator('[id="expectedAt"]'), other: page.locator('[id="vendorId"]') };
}

for (const locale of ['en-US', 'nl-NL']) {
    test.describe(`datumveld in Chrome met browsertaal ${locale}`, () => {
        test.use({ locale, timezoneId: 'Europe/Amsterdam' });

        test.beforeEach(async ({ context }) => {
            await context.addInitScript((actor) => {
                localStorage.setItem('agf_locale', 'nl');
                localStorage.setItem('agf_actor_reference', actor);
            }, ACTOR);
        });

        for (const [screen, open] of [['Nieuwe verkooporder', openSalesOrder], ['Aanvoer plannen', openPlanSupply]] as const) {
            test(`${screen}: vandaag getypt en via "Vandaag" wordt nooit afgekeurd`, async ({ page }) => {
                const today = todayInAmsterdam();
                const { field, error, other } = await open(page);
                await expect(field).toHaveAttribute('type', 'text');
                await expect(field).toHaveAttribute('placeholder', 'dd-mm-jjjj');

                // Typen zoals een Nederlandse gebruiker, ook bij een Engelstalige browser.
                await field.click();
                await page.keyboard.type(today.dutch);
                await field.press('Enter');
                await expect(other).toBeVisible();
                await expect(error).toHaveCount(0);
                await expect(field).toHaveValue(today.dutch);

                // Leegmaken geeft de melding; "Vandaag" vult de lokale dag in en de melding verdwijnt bij de volgende controle.
                await field.fill('');
                await field.press('Enter');
                await expect(error).toBeVisible();
                await field.locator('xpath=..').getByRole('button', { name: 'Datum van vandaag invullen' }).click();
                await expect(field).toHaveValue(today.dutch);
                await field.press('Enter');
                await expect(error).toHaveCount(0);

                // Korte vorm d-m (dit jaar) en een niet-bestaande datum.
                await field.fill(`${Number(today.dutch.slice(0, 2))}-${Number(today.dutch.slice(3, 5))}`);
                await field.press('Enter');
                await expect(error).toHaveCount(0);
                await field.fill(`31-02-${today.iso.slice(0, 4)}`);
                await field.press('Enter');
                await expect(error).toContainText('Vul een volledige, bestaande datum in');
            });
        }
    });
}

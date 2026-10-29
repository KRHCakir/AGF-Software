import { defineConfig } from '@playwright/test';

// E2E-tests van de demo-rondleiding (npm run test:tour). Draait tegen een lokale frontend met NEXT_PUBLIC_DEMO_ASSISTANT
// en NEXT_PUBLIC_DEMO_TOUR aan, en een API op een aparte testdatabase (nooit agf_smoke); zie docs/demo-tour.md.
// Standaard met de geïnstalleerde Google Chrome (TOUR_BROWSER_CHANNEL), zodat er geen browser gedownload hoeft te worden.
export default defineConfig({
    testDir: './e2e',
    timeout: 240_000,
    workers: 1,
    fullyParallel: false,
    reporter: [['list']],
    use: {
        baseURL: process.env.TOUR_BASE_URL ?? 'http://localhost:3001',
        channel: process.env.TOUR_BROWSER_CHANNEL ?? 'chrome',
        headless: process.env.TOUR_HEADED !== 'true',
        viewport: { width: 1440, height: 900 },
        locale: 'nl-NL',
        screenshot: 'only-on-failure',
        trace: 'retain-on-failure',
    },
});

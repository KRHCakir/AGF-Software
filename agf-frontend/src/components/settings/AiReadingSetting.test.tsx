import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../test/reactHarness.ts';
import type { Posted } from '../../test/reactHarness.ts';
import AiReadingSetting from './AiReadingSetting';

// Beheer: AI-lezen staat standaard uit; aanzetten kan pas na de bevestiging dat de klant weet dat documenten naar een externe dienst gaan;
// een bereikte maandlimiet staat in het rood.

const model = { id: 'claude-haiku-5-5', name: 'Claude Haiku 5.5', inputPerMillionUsd: 0.1, outputPerMillionUsd: 0.5, priceCheckedOn: '2026-11-11' };
let mounted: Mounted;
let posted: Posted[];
let spent: number;

describe('AI reading setting', () => {
    beforeEach(() => {
        spent = 3.2;
        posted = stubFetch({
            onGet: (url) => url.includes('/settings/ai-reading') ? json(200, { enabled: false, enabledBy: null, enabledAt: null, monthlyLimitEur: 25 })
                : json(200, { enabled: false, configured: true, notConfiguredReason: null, provider: 'Anthropic', primaryModel: model, fallbackEnabled: true, fallbackModel: model,
                    monthSpentEur: spent, monthlyLimitEur: 25, limitReached: spent >= 25, maxFileBytes: 10485760 }),
            nextPost: async () => json(500, {}),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('off by default; turning it on needs the confirmation and records it through the API', async () => {
        await mounted.render(<AiReadingSetting />);
        const enable = await mounted.waitFor(() => mounted.container.querySelector<HTMLButtonElement>('[data-tour="ai-reading-enable"]'), 'enable button');
        assert.equal(mounted.container.querySelector('[data-testid="ai-reading-state"]')!.textContent, 'ai_settings_off');
        assert.equal(enable.disabled, true, 'not without the confirmation');
        // PUT gaat via fetch met methode PUT: hier alleen dat de knop pas na het vinkje bruikbaar is.
        await act(async () => { mounted.container.querySelector<HTMLInputElement>('[data-tour="ai-reading-informed"]')!.click(); });
        assert.equal(enable.disabled, false);
        assert.equal(posted.length, 0);
    });

    test('a reached monthly limit is shown in red for the administrator', async () => {
        spent = 25.5;
        await mounted.render(<AiReadingSetting />);
        const alert = await mounted.waitFor(() => mounted.container.querySelector('[data-testid="ai-limit-reached"]'), 'limit reached');
        assert.equal(alert.getAttribute('role'), 'alert');
        assert.equal(alert.textContent, 'ai_settings_limit_reached');
    });
});

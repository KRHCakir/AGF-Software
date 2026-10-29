import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import nl from '../../i18n/nl.json';
import en from '../../i18n/en.json';
import { isAllowedDemoHref } from '../demoAssistant.ts';
import { TOUR_CHAPTERS } from './chapters.ts';
import { interpolate, targetSelector } from './dom.ts';

// Elk hoofdstuk blijft binnen de vaste routes, gebruikt alleen geldige data-tour-namen en heeft al zijn teksten in
// het Nederlands en het Engels.

const sample = { tourRef: 'DEMO-TOUR-001', orderNumber: 'IO-2026-0001', actor: 'Demo-assistent', today: '2026-10-29', orderId: '47', lineId: '52', variantId: '10' };

describe('tour chapters', () => {
    test('every chapter has a unique id, its texts in nl and en, and only allowed routes and valid targets', () => {
        const ids = new Set<string>();
        for (const chapter of TOUR_CHAPTERS) {
            assert.ok(!ids.has(chapter.id), `duplicate chapter ${chapter.id}`);
            ids.add(chapter.id);
            const keys = [chapter.titleKey, chapter.summaryKey, ...(chapter.doneKey ? [chapter.doneKey] : [])];
            const stepIds = new Set<string>();
            for (const step of chapter.steps) {
                assert.ok(!stepIds.has(step.id), `duplicate step ${chapter.id}/${step.id}`);
                stepIds.add(step.id);
                keys.push(step.titleKey, step.whatKey, step.whyKey);
                assert.ok(isAllowedDemoHref(interpolate(step.route, sample)), `${chapter.id}/${step.id} route ${step.route}`);
                for (const action of step.actions) {
                    if (action.kind === 'navigate') assert.ok(isAllowedDemoHref(interpolate(action.href, sample)), `${chapter.id}/${step.id} ${action.href}`);
                    if ('target' in action && action.target) assert.doesNotThrow(() => targetSelector(action.target!, sample), `${chapter.id}/${step.id} ${action.target}`);
                }
                if (step.expect) assert.doesNotThrow(() => targetSelector(step.expect!.target, sample));
            }
            for (const key of keys) {
                assert.ok(key in nl, `${key} in nl`);
                assert.ok(key in en, `${key} in en`);
            }
        }
    });

    test('the read-only introduction chapter never writes', () => {
        const screens = TOUR_CHAPTERS.find((chapter) => chapter.id === 'screens')!;
        assert.equal(screens.writes, false);
        assert.ok(screens.steps.every((step) => step.actions.every((action) => ['navigate', 'highlight', 'wait', 'assert'].includes(action.kind))));
    });
});

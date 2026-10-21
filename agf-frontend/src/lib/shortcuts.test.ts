import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { isMacPlatform, matchShortcut, shortcutLabel } from './shortcuts.ts';

const press = (over: Partial<{ key: string; code: string; metaKey: boolean; ctrlKey: boolean; altKey: boolean; shiftKey: boolean }>) =>
    ({ key: '', code: '', metaKey: false, ctrlKey: false, altKey: false, shiftKey: false, ...over });

describe('shortcuts', () => {
    test('⌘ on macOS and Ctrl elsewhere; the other modifier never matches', () => {
        assert.equal(matchShortcut(press({ key: 's', code: 'KeyS', metaKey: true }), true), 'save');
        assert.equal(matchShortcut(press({ key: 's', code: 'KeyS', ctrlKey: true }), true), null);
        assert.equal(matchShortcut(press({ key: 's', code: 'KeyS', ctrlKey: true }), false), 'save');
        assert.equal(matchShortcut(press({ key: 'Enter', code: 'Enter', metaKey: true }), true), 'saveAndNew');
        assert.equal(matchShortcut(press({ key: 'Enter', code: 'Enter' }), true), null);
    });

    test('Option combinations are recognised on the physical key, even when Option changes the character', () => {
        assert.equal(matchShortcut(press({ key: 'Dead', code: 'KeyN', altKey: true }), true), 'newDocument');
        assert.equal(matchShortcut(press({ key: 'ƒ', code: 'KeyF', altKey: true }), true), 'focusSearch');
        assert.equal(matchShortcut(press({ key: 'e', code: 'KeyE', altKey: true }), false), 'toggleExtra');
        assert.equal(matchShortcut(press({ key: 'd', code: 'KeyD', altKey: true }), false), 'duplicateLine');
        assert.equal(matchShortcut(press({ key: 'r', code: 'KeyR', altKey: true }), false), 'removeLine');
        assert.equal(matchShortcut(press({ key: 'n', code: 'KeyN', altKey: true, shiftKey: true }), false), null);
    });

    test('labels follow the platform notation', () => {
        assert.equal(shortcutLabel('save', true), '⌘S');
        assert.equal(shortcutLabel('save', false), 'Ctrl+S');
        assert.equal(shortcutLabel('saveAndNew', true), '⌘↩');
        assert.equal(shortcutLabel('saveAndNew', false), 'Ctrl+Enter');
        assert.equal(shortcutLabel('newDocument', true), '⌥N');
        assert.equal(shortcutLabel('toggleExtra', false), 'Alt+E');
        assert.ok(isMacPlatform('Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5)'));
        assert.equal(isMacPlatform('Mozilla/5.0 (Windows NT 10.0)'), false);
    });
});

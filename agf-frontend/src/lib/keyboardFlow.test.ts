import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { FlowKey, FlowTarget, adjacentField, fieldKind, firstInvalidField, flowAction, flowFields } from './keyboardFlow.ts';

// Toetsenbordflow: Enter naar het volgende veld (textarea: nieuwe regel), pijl links/rechts alleen aan het begin/einde of in een leeg
// veld, nooit met een modifier of tijdens compositie, nooit in keuzelijsten/vinkjes/datumvelden; ⌘S/Ctrl+S = opslaan.

const key = (k: string, over: Partial<FlowKey> = {}): FlowKey => ({ key: k, code: '', metaKey: false, ctrlKey: false, altKey: false, shiftKey: false, ...over });
const text = (value: string, start: number, end = start): FlowTarget => ({ kind: 'text', selectionStart: start, selectionEnd: end, valueLength: value.length });

describe('flowAction', () => {
    test('Enter moves on from text, select, checkbox and date fields; a textarea keeps its newline', () => {
        assert.equal(flowAction(key('Enter'), text('abc', 1), false), 'next');
        for (const kind of ['select', 'toggle', 'picker'] as const) assert.equal(flowAction(key('Enter'), { kind, selectionStart: null, selectionEnd: null, valueLength: 0 }, false), 'next', kind);
        assert.equal(flowAction(key('Enter'), { kind: 'textarea', selectionStart: 0, selectionEnd: 0, valueLength: 0 }, false), null);
        assert.equal(flowAction(key('Enter', { shiftKey: true }), text('abc', 1), false), null);
    });

    test('arrows leave a field only at the start/end of the text or when it is empty; mid-text and selections keep the caret', () => {
        assert.equal(flowAction(key('ArrowLeft'), text('30', 0), false), 'previous');
        assert.equal(flowAction(key('ArrowRight'), text('30', 2), false), 'next');
        assert.equal(flowAction(key('ArrowLeft'), text('30', 1), false), null, 'middle');
        assert.equal(flowAction(key('ArrowRight'), text('30', 1), false), null, 'middle');
        assert.equal(flowAction(key('ArrowRight'), text('30', 0), false), null, 'start, going right');
        assert.equal(flowAction(key('ArrowLeft'), text('30', 2), false), null, 'end, going left');
        assert.equal(flowAction(key('ArrowRight'), text('30', 0, 2), false), null, 'selected text collapses first');
        assert.equal(flowAction(key('ArrowLeft'), text('', 0), false), 'previous', 'empty');
        assert.equal(flowAction(key('ArrowRight'), text('', 0), false), 'next', 'empty');
        assert.equal(flowAction(key('ArrowLeft'), { kind: 'textarea', selectionStart: 0, selectionEnd: 0, valueLength: 5 }, false), 'previous', 'textarea like text');
        assert.equal(flowAction(key('ArrowLeft', { shiftKey: true }), text('30', 0), false), null, 'shift selects');
        assert.equal(flowAction(key('ArrowLeft', { altKey: true }), text('30', 0), false), null, 'word jump');
        assert.equal(flowAction(key('ArrowLeft', { isComposing: true }), text('30', 0), false), null, 'IME');
        assert.equal(flowAction(key('ArrowUp'), text('30', 0), false), null, 'up/down never handled');
    });

    test('selects, checkboxes and date fields keep their own arrow keys', () => {
        for (const kind of ['select', 'toggle', 'picker'] as const) {
            assert.equal(flowAction(key('ArrowRight'), { kind, selectionStart: null, selectionEnd: null, valueLength: 0 }, false), null, kind);
        }
    });

    test('Ctrl+S saves on Windows/Linux, Cmd+S on a Mac, from any field', () => {
        assert.equal(flowAction(key('s', { ctrlKey: true }), text('x', 0), false), 'save');
        assert.equal(flowAction(key('S', { metaKey: true }), { kind: 'select', selectionStart: null, selectionEnd: null, valueLength: 0 }, true), 'save');
        assert.equal(flowAction(key('s', { metaKey: true }), text('x', 0), false), null, 'Cmd+S is not save on Windows');
        assert.equal(flowAction(key('s', { ctrlKey: true }), text('x', 0), true), null, 'Ctrl+S is not save on a Mac');
    });
});

describe('field order', () => {
    const build = (html: string) => { const root = window.document.createElement('form'); root.innerHTML = html; return root; };

    test('fieldKind: text-like inputs, textarea, select, checkbox and date fields; buttons are no fields', () => {
        const kinds = ['<input>', '<input type="email">', '<input type="checkbox">', '<input type="date">', '<textarea></textarea>', '<select></select>', '<button></button>', '<input type="hidden">']
            .map((html) => fieldKind(build(html).firstElementChild as HTMLElement & { type?: string }));
        assert.deepEqual(kinds, ['text', 'text', 'toggle', 'picker', 'textarea', 'select', 'other', 'other']);
    });

    test('flowFields follows DOM order and skips disabled (also via fieldset), hidden, tabIndex -1 and buttons', () => {
        const root = build(`<input id="a"><button id="b"></button><input id="c" disabled><fieldset disabled><input id="d"></fieldset><div hidden><input id="e"></div>
            <input id="f" tabindex="-1"><select id="g"></select><input id="h" type="checkbox"><input id="i" type="hidden"><textarea id="j" aria-invalid="true"></textarea><input id="k" aria-invalid="true">`);
        const fields = flowFields(root as unknown as { querySelectorAll(s: string): ArrayLike<HTMLInputElement> });
        assert.deepEqual(fields.map((f) => f.id), ['a', 'g', 'h', 'j', 'k']);
        assert.equal(adjacentField(fields, fields[0], 'next')?.id, 'g');
        assert.equal(adjacentField(fields, fields[0], 'previous'), null, 'no wrap at the start');
        assert.equal(adjacentField(fields, fields[4], 'next'), null, 'no wrap at the end');
        assert.equal(firstInvalidField(root as unknown as { querySelectorAll(s: string): ArrayLike<HTMLInputElement> })?.id, 'j', 'first invalid in visual order');
    });
});

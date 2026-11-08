import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { ROW_PLAIN, ROW_SELECTED, ROW_STRIPE, listRowClass, rowClick, rowClickOpens } from './listRows.ts';

// Lijstregels: om en om wit en grijs, de gekozen regel blauw; een klik op de rij opent, behalve op een knop, link of veld erin of
// tijdens het selecteren van tekst.

function row() {
    const tr = document.createElement('tr');
    tr.innerHTML = '<td class="cell"><span class="text">Factuur</span></td><td><button type="button"><span class="icon">x</span></button></td><td><a href="/partijen?id=1">P1</a></td><td><input /></td>';
    document.body.appendChild(tr);
    return tr;
}

describe('list rows', () => {
    test('first row white, second grey, third white; the selected row is blue whatever its place', () => {
        assert.deepEqual([0, 1, 2, 3].map((index) => listRowClass(index).split(' ')[0]), [ROW_PLAIN, ROW_STRIPE, ROW_PLAIN, ROW_STRIPE]);
        assert.equal(listRowClass(1, { selected: true }).split(' ')[0], ROW_SELECTED);
        assert.equal(listRowClass(0, { selected: true, selectedClass: 'bg-blue-100' }).split(' ')[0], 'bg-blue-100');
        assert.ok(listRowClass(0, { clickable: true }).includes('cursor-pointer'));
        assert.ok(!listRowClass(0).includes('cursor-pointer'), 'a row that opens nothing does not look clickable');
    });

    test('a click on a cell or its text opens the row; a click on a button, link or field in the row does not', () => {
        const tr = row();
        const at = (selector: string) => tr.querySelector(selector);
        assert.equal(rowClickOpens(at('.cell'), tr, ''), true);
        assert.equal(rowClickOpens(at('.text'), tr, ''), true);
        assert.equal(rowClickOpens(tr, tr, ''), true);
        assert.equal(rowClickOpens(at('button'), tr, ''), false);
        assert.equal(rowClickOpens(at('.icon'), tr, ''), false, 'inside the button');
        assert.equal(rowClickOpens(at('a'), tr, ''), false);
        assert.equal(rowClickOpens(at('input'), tr, ''), false);
        assert.equal(rowClickOpens(null, tr, ''), true);
    });

    test('selecting text to copy it never opens the row', () => {
        const tr = row();
        assert.equal(rowClickOpens(tr.querySelector('.text'), tr, 'Factuur'), false);
        assert.equal(rowClickOpens(tr.querySelector('.text'), tr, '   '), true);
    });

    test('rowClick opens once on a real click on the row and not on a click on its button', () => {
        const tr = row();
        let opened = 0;
        const handler = rowClick(() => { opened += 1; });
        tr.addEventListener('click', (event) => handler({ target: event.target, currentTarget: event.currentTarget }));
        tr.querySelector<HTMLElement>('.text')!.click();
        tr.querySelector<HTMLElement>('button')!.click();
        assert.equal(opened, 1);
    });
});

import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { MIN_FILL_HEIGHT, fillHeight } from './viewportFill.ts';

describe('viewport fill height', () => {
    test('fills from the block top to the window bottom, minus the gap', () => {
        assert.equal(fillHeight(900, 380, 24), 496);
        assert.equal(fillHeight(800, 300.6, 24), 475);
    });
    test('never shrinks below a usable minimum on short windows', () => {
        assert.equal(fillHeight(600, 380, 24), MIN_FILL_HEIGHT);
        assert.equal(fillHeight(800, 368, 24), 408, '1280 x 800 without banner still fits');
        assert.equal(fillHeight(600, 380, 24, 200), 200);
    });
});

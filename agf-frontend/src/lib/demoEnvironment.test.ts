import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { demoEnvironmentEnabled, demoRobots, isSimulatedSend } from './demoEnvironment.ts';

describe('demo environment', () => {
    test('only the build flag true switches it on; only then the pages ask search engines not to index them', () => {
        assert.equal(demoEnvironmentEnabled('true'), true);
        for (const value of [undefined, '', 'false', 'TRUE', '1']) assert.equal(demoEnvironmentEnabled(value), false, String(value));
        assert.deepEqual(demoRobots(true), { index: false, follow: false });
        assert.equal(demoRobots(false), undefined);
    });

    test('a message id of the simulated sender marks a mail that was not really sent', () => {
        assert.equal(isSimulatedSend('<9b1c@nagebootst.agf.local>'), true);
        assert.equal(isSimulatedSend('<CAF1234@mail.gmail.com>'), false);
        assert.equal(isSimulatedSend(null), false);
        assert.equal(isSimulatedSend(undefined), false);
    });
});

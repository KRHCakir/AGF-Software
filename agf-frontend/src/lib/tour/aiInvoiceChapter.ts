// Hoofdstuk Factuur uploaden (stap 7a): AI-lezen aanzetten (met de bevestiging), een demofactuur uploaden, het voorstel naast het document
// bekijken (gele twijfel bij de datum, de voorgestelde leverancier en inkooporder) en goedkeuren. Altijd met de nagebootste lezer: geen echte
// AI-aanroep, een voorspelbare uitkomst met het kenmerk van de rondleiding (DEMO-TOUR-…).

import { step } from './compose';
import { precheckAiInvoice } from './prechecks';
import type { TourAction, TourChapter } from './types';

const ai = (id: string, route: string, actions: readonly TourAction[]) => step('aiinv', id, route, actions);

export const AI_INVOICE_CHAPTER: TourChapter = {
    id: 'ai-invoice',
    titleKey: 'tour_ch_aiinv_title',
    summaryKey: 'tour_ch_aiinv_summary',
    doneKey: 'tour_ch_aiinv_done',
    writes: true,
    precheck: precheckAiInvoice,
    steps: [
        ai('setting', '/settings', [
            { kind: 'highlight', target: 'ai-reading-setting' },
            { kind: 'click', target: 'ai-reading-informed', when: '{{aiOff}}' },
            { kind: 'click', target: 'ai-reading-enable', when: '{{aiOff}}' },
            { kind: 'wait', target: 'ai-reading-on' },
        ]),
        ai('upload', '/purchase-invoices/new', [
            { kind: 'highlight', target: 'pinvoice-ai-upload' },
            { kind: 'upload', target: 'pinvoice-ai-file', file: '/demo/demo-factuur.png', name: 'demo-factuur--{{vendorName}}--{{orderRef}}.png' },
            { kind: 'wait', target: 'pinvoice-ai-review' },
        ]),
        ai('review', '/purchase-invoices/new', [
            { kind: 'highlight', target: 'pinvoice-ai-document' },
            { kind: 'highlight', target: 'pinvoice-ai-review' },
            { kind: 'assert', target: 'pinvoice-number', value: 'AI-{{orderRef}}' },
            { kind: 'highlight', target: 'pinvoice-totals' },
        ]),
        ai('approve', '/purchase-invoices/new', [
            { kind: 'click', target: 'pinvoice-save' },
            { kind: 'wait', target: 'pinvoice-ai-approved' },
            { kind: 'highlight', target: 'pinvoice-ai-approved' },
        ]),
    ],
};

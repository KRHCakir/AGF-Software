// Hoofdstuk Certificaat laten lezen (certificaten deel 2): bij de leverancier van de rondleiding een certificaat-PDF uploaden (een verzonnen
// GLOBALG.A.P.-certificaat met Lettuce op pagina 1 en Arugula en Mizuna in de bijlage), het voorstel naast het document bekijken (gele
// twijfel bij de einddatum, producten met hun koppeling of "niet gekoppeld") en goedkeuren. Altijd met de nagebootste lezer: geen echte
// AI-aanroep, een voorspelbare uitkomst.

import { step } from './compose';
import { precheckAiCertificate } from './prechecks';
import type { TourAction, TourChapter } from './types';

const cert = (id: string, route: string, actions: readonly TourAction[]) => step('aicert', id, route, actions);
const RELATION = '/relations?open={{vendorRelationId}}&section=certificates';

export const AI_CERTIFICATE_CHAPTER: TourChapter = {
    id: 'ai-certificate',
    titleKey: 'tour_ch_aicert_title',
    summaryKey: 'tour_ch_aicert_summary',
    doneKey: 'tour_ch_aicert_done',
    writes: true,
    precheck: precheckAiCertificate,
    steps: [
        cert('setting', '/settings', [
            { kind: 'highlight', target: 'ai-reading-setting' },
            { kind: 'click', target: 'ai-reading-informed', when: '{{aiOff}}' },
            { kind: 'click', target: 'ai-reading-enable', when: '{{aiOff}}' },
            { kind: 'wait', target: 'ai-reading-on' },
        ]),
        cert('upload', RELATION, [
            { kind: 'wait', target: 'relation-certificate-ai-upload' },
            { kind: 'highlight', target: 'relation-certificate-ai-upload' },
            { kind: 'upload', target: 'relation-certificate-ai-file', file: '/demo/demo-certificaat.pdf', name: 'demo-certificaat--{{vendorName}}.pdf' },
            { kind: 'wait', target: 'relation-certificate-ai-review' },
        ]),
        cert('review', RELATION, [
            { kind: 'highlight', target: 'relation-certificate-ai-document' },
            { kind: 'assert', target: 'relation-certificate-ai-number', value: '4063061591012' },
            { kind: 'highlight', target: 'relation-certificate-ai-until' },
            { kind: 'highlight', target: 'relation-certificate-ai-products' },
        ]),
        cert('approve', RELATION, [
            { kind: 'click', target: 'relation-certificate-ai-approve' },
            { kind: 'wait', target: 'relation-certificate-ai-done' },
            { kind: 'highlight', target: 'relation-certificate' },
        ]),
    ],
};

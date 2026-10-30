'use client';

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { searchCandidates, variantLabel } from '../../lib/receiptDeviation';
import { listDeviationCandidates } from '../../services/receiptDeviationService';
import type { DeviationCandidate } from '../../types/receiptDeviation';
import LookupField, { LookupMatch } from '../shared/LookupField';

// "Ander artikel ontvangen" bij een open orderregel: alleen varianten van hetzelfde product (andere herkomst, maat, verpakking,
// klasse of merk), typen met suggesties; Tab en Enter nemen de bovenste. Een ander ras of artikel wordt nooit gekozen: dan de
// melding dat dit een nieuwe aanvoerregel is. Kiezen boekt niets; het venster "Klopt dit?" volgt bij het registreren.
export default function ReceivedVariantPicker({ lineId, value, onChange, disabled }: {
    lineId: number;
    value: DeviationCandidate | null;
    onChange: (candidate: DeviationCandidate | null) => void;
    disabled: boolean;
}) {
    const { t } = useTranslation();
    const [candidates, setCandidates] = useState<DeviationCandidate[] | 'loading' | 'error'>('loading');
    const [otherArticle, setOtherArticle] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        listDeviationCandidates(lineId, controller.signal)
            .then(setCandidates)
            .catch(() => { if (!controller.signal.aborted) setCandidates('error'); });
        return () => controller.abort();
    }, [lineId]);

    const search = useCallback((query: string): LookupMatch<DeviationCandidate>[] =>
        (Array.isArray(candidates) ? searchCandidates(candidates, query) : []).map((candidate) => ({
            key: String(candidate.variant.productVariantId),
            item: candidate,
            description: variantLabel(candidate.variant),
            exact: candidate.shortcutCode !== null && candidate.shortcutCode.toLowerCase() === query.trim().toLowerCase(),
            badge: candidate.otherArticle ? t('deviation_other_article_badge') : candidate.shortcutCode,
            badgeStrong: candidate.otherArticle,
        })), [candidates, t]);

    return (
        <div data-tour="receive-deviation-variant" data-tour-key={lineId}>
            <LookupField<DeviationCandidate>
                id={`line.${lineId}.receivedVariant`}
                dataTour="receive-deviation-search"
                ariaLabel={t('deviation_received_article')}
                selected={value === null ? null : { description: variantLabel(value.variant) }}
                search={search}
                onSelect={(candidate) => {
                    setOtherArticle(candidate.otherArticle);
                    onChange(candidate.otherArticle ? null : candidate);
                }}
                onClear={() => { setOtherArticle(false); onChange(null); }}
                disabled={disabled || !Array.isArray(candidates)}
                takeHighlighted
                placeholderKey="deviation_search_placeholder"
                hintKey="deviation_search_hint"
                noneKey="deviation_search_none"
                moreKey="product_picker_more"
                changeKey="deviation_search_change"
            />
            {candidates === 'error' && <p className="mt-1 text-xs text-red-800">{t('deviation_candidates_error')}</p>}
            {otherArticle && <p role="alert" className="mt-1 text-xs font-medium text-red-800">{t('deviation_other_article')}</p>}
        </div>
    );
}

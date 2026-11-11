'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { getAiDocument } from '../../services/aiReadingService';
import type { AiExtraction } from '../../types/aiReading';

// Het geüploade document naast het voorstel: een PDF in de PDF-weergave van de browser (met haar eigen zoom), een foto met zoomknoppen.
// Op een telefoon staat het boven het formulier en is het in te klappen.
export default function AiDocumentViewer({ extraction }: { extraction: AiExtraction }) {
    const { t } = useTranslation();
    const [url, setUrl] = useState<string | null>(null);
    const [failed, setFailed] = useState(false);
    const [zoom, setZoom] = useState(1);
    const [open, setOpen] = useState(true);
    const pdf = extraction.displayMediaType === 'application/pdf';

    useEffect(() => {
        const controller = new AbortController();
        let objectUrl: string | null = null;
        setFailed(false);
        getAiDocument(extraction.id, controller.signal)
            .then((blob) => { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); })
            .catch(() => { if (!controller.signal.aborted) setFailed(true); });
        return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
    }, [extraction.id]);

    const zoomButton = 'rounded border border-gray-300 bg-white px-2 py-0.5 text-sm text-gray-800 hover:bg-gray-50';
    return (
        <section className="min-w-0 rounded-md border border-gray-300 bg-white lg:sticky lg:top-2 lg:self-start" aria-labelledby="ai-document-title" data-tour="pinvoice-ai-document">
            <div className="flex flex-wrap items-center justify-between gap-2 border-b border-gray-200 px-3 py-2">
                <h2 id="ai-document-title" className="min-w-0 truncate text-sm font-semibold text-gray-900" title={extraction.fileName}>{t('ai_document')} · {extraction.fileName}</h2>
                <div className="flex items-center gap-1">
                    {!pdf && open && (
                        <>
                            <button type="button" className={zoomButton} onClick={() => setZoom((z) => Math.max(0.5, z - 0.25))} aria-label={t('ai_zoom_out')}>−</button>
                            <button type="button" className={zoomButton} onClick={() => setZoom(1)} aria-label={t('ai_zoom_fit')}>{Math.round(zoom * 100)}%</button>
                            <button type="button" className={zoomButton} onClick={() => setZoom((z) => Math.min(4, z + 0.25))} aria-label={t('ai_zoom_in')}>+</button>
                        </>
                    )}
                    <button type="button" className={`${zoomButton} lg:hidden`} aria-expanded={open} onClick={() => setOpen((o) => !o)} data-testid="ai-document-toggle">
                        {open ? t('ai_document_hide') : t('ai_document_show')}
                    </button>
                </div>
            </div>
            {open && (
                <div className="p-2">
                    {failed && <p className="text-sm text-gray-700">{t('ai_document_unavailable')}</p>}
                    {url && pdf && <iframe src={url} title={extraction.fileName} className="h-[60vh] w-full rounded border border-gray-200 lg:h-[78vh]" data-testid="ai-document-pdf" />}
                    {url && !pdf && (
                        <div className="max-h-[60vh] overflow-auto rounded border border-gray-200 lg:max-h-[78vh]">
                            {/* eslint-disable-next-line @next/next/no-img-element -- een object-URL van het eigen document, geen afbeelding van de site */}
                            <img src={url} alt={extraction.fileName} style={{ width: `${zoom * 100}%`, maxWidth: 'none' }} data-testid="ai-document-image" />
                        </div>
                    )}
                </div>
            )}
        </section>
    );
}

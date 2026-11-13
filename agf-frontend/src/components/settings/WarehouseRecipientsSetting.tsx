'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type RecipientProblem, type RecipientRow, recipientProblems, recipientsFrom, rowsFrom } from '../../lib/documentRecipients';
import { ApiError, toApiError } from '../../services/apiClient';
import { getWarehouseRecipients, saveWarehouseRecipients } from '../../services/documentMailService';
import DocumentRecipientsEditor from '../relations/DocumentRecipientsEditor';
import ErrorNotice from '../supply/ErrorNotice';
import { primaryButtonClass } from '../supply/SuccessPanel';

// De e-mailadressen van de loods (documenten deel 3): de laadbon gaat daarheen. De loods is geen relatie; ze staat bij de instellingen.
export default function WarehouseRecipientsSetting() {
    const { t } = useTranslation();
    const [rows, setRows] = useState<RecipientRow[] | null>(null);
    const [version, setVersion] = useState('0');
    const [problems, setProblems] = useState<RecipientProblem[]>([]);
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getWarehouseRecipients(controller.signal).then((found) => { setRows(rowsFrom(found.recipients)); setVersion(found.version); })
            .catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (rows === null || saving) return;
        const found = recipientProblems(rows);
        setProblems(found);
        if (found.length > 0) return;
        setSaving(true);
        setError(null);
        try {
            const result = await saveWarehouseRecipients({ expectedVersion: version, recipients: recipientsFrom(rows) });
            setRows(rowsFrom(result.recipients));
            setVersion(result.version);
            setSaved(true);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };

    return (
        <section aria-labelledby="warehouse-recipients" className="space-y-2 rounded-md border border-gray-200 bg-white px-4 py-3" data-testid="warehouse-recipients" data-tour="warehouse-recipients">
            <h2 id="warehouse-recipients" className="text-lg font-semibold text-gray-900">{t('warehouse_recipients_title')}</h2>
            <p className="text-sm text-gray-700">{t('warehouse_recipients_text')}</p>
            {error && <ErrorNotice error={error} />}
            {rows !== null && (
                <form onSubmit={(event) => void save(event)} className="space-y-2">
                    <DocumentRecipientsEditor idPrefix="warehouse" rows={rows} onChange={(next) => { setRows(next); setSaved(false); setProblems([]); }} problems={problems} disabled={saving} defaultKinds={['LoadingSlip']} dataTour="warehouse-recipient" />
                    <div className="flex items-center gap-2">
                        <button type="submit" className={primaryButtonClass} disabled={saving} data-tour="warehouse-recipients-save">{t('loading_slip_save')}</button>
                        {saved && <span role="status" className="text-sm text-green-800">{t('loading_slip_saved')}</span>}
                    </div>
                </form>
            )}
        </section>
    );
}

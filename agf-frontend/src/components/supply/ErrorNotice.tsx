'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError } from '../../services/apiClient';
import { outcomeUnknown } from './messages';

// Foutmelding bovenaan een formulier. Maakt expliciet of het verzoek mogelijk al is opgeslagen.
export default function ErrorNotice({ error, actions }: { error: ApiError; actions?: React.ReactNode }) {
    const { t } = useTranslation();
    const title = (() => {
        switch (error.kind) {
            case 'config': return t('availability_error_config', { detail: error.message });
            case 'shape': return t('supply_error_shape');
            case 'http':
                if (error.status === 400) return t('supply_error_http_400');
                if (error.status === 404) return t('supply_error_http_404');
                if (error.status === 409) return t('supply_error_http_409');
                return t('availability_error_http', { status: String(error.status ?? '') });
            default: return t('availability_error_network');
        }
    })();
    return (
        <div role="alert" className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900">
            <p className="font-medium">{title}</p>
            {error.kind === 'http' && error.status !== 400 && error.message && <p className="mt-1">{error.message}</p>}
            {outcomeUnknown(error) && <p className="mt-1">{t('supply_error_outcome_unknown')}</p>}
            {actions && <div className="mt-3 flex flex-wrap gap-2">{actions}</div>}
        </div>
    );
}

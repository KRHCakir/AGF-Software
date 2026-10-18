'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { AvailabilityApiError } from '../../services/availabilityService';

export type LoadState<T> =
    | { status: 'loading' }
    | { status: 'error'; error: AvailabilityApiError }
    | { status: 'ready'; data: T };

export function LoadingRows({ rows = 6, label }: { rows?: number; label: string }) {
    return (
        <div aria-busy="true" aria-live="polite" className="space-y-2">
            <span className="sr-only">{label}</span>
            {Array.from({ length: rows }, (_, index) => (
                <div key={index} className="h-10 animate-pulse rounded bg-gray-200" />
            ))}
        </div>
    );
}

export function EmptyState({ title, description, action }: { title: string; description?: string; action?: React.ReactNode }) {
    return (
        <div className="rounded-md border border-dashed border-gray-300 bg-white px-4 py-8 text-center">
            <p className="font-medium text-gray-800">{title}</p>
            {description && <p className="mt-1 text-sm text-gray-600">{description}</p>}
            {action && <div className="mt-3">{action}</div>}
        </div>
    );
}

export function ErrorState({ error, onRetry }: { error: AvailabilityApiError; onRetry: () => void }) {
    const { t } = useTranslation();
    const message = (() => {
        switch (error.kind) {
            case 'config': return t('availability_error_config', { detail: error.message });
            case 'http': return t('availability_error_http', { status: String(error.status ?? '') });
            case 'shape': return t('availability_error_shape');
            default: return t('availability_error_network');
        }
    })();
    const fieldMessages = Object.entries(error.fieldErrors).flatMap(([field, messages]) => messages.map((m) => `${field}: ${m}`));

    return (
        <div role="alert" className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900">
            <p className="font-medium">{message}</p>
            {error.kind === 'http' && error.message && <p className="mt-1">{error.message}</p>}
            {fieldMessages.length > 0 && (
                <ul className="mt-1 list-disc pl-5">
                    {fieldMessages.map((m) => <li key={m}>{m}</li>)}
                </ul>
            )}
            <button type="button" onClick={onRetry}
                className="mt-3 rounded border border-red-400 bg-white px-3 py-1 font-medium text-red-900 hover:bg-red-100 focus:outline-none focus:ring-2 focus:ring-red-500">
                {t('availability_retry')}
            </button>
        </div>
    );
}

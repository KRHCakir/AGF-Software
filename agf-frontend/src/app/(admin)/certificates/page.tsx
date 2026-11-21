'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import CertificateExpiryList from '../../../components/certificates/CertificateExpiryList';
import CertificateStatusBadge from '../../../components/certificates/CertificateStatusBadge';
import { inputClass } from '../../../components/supply/FieldError';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatDateOnly } from '../../../lib/calendarDate';
import { certificateLink, shortTypeName } from '../../../lib/certificateDisplay';
import { listRowClass } from '../../../lib/listRows';
import { toApiError } from '../../../services/apiClient';
import { getCertificateOverview, getCertificateReference } from '../../../services/certificateService';
import type { CertificateOverview, CertificateReference } from '../../../types/certificate';

const STATUSES = ['', 'attention', 'Expired', 'ExpiringSoon', 'Valid', 'NoEndDate', 'productsPending'] as const;
const PAGE_SIZE = 100;

// Stamgegevens › Certificaten (certificaten deel 1): bovenaan de certificaten die bijna verlopen, daaronder alle certificaten van alle
// relaties en het eigen bedrijf, te filteren op soort en status en te zoeken op nummer of relatie. Een regel opent de relatie met het blok
// Certificaten (het eigen bedrijf: Instellingen). Wijzigen gebeurt daar; deze pagina leest alleen.
export default function CertificatesPage() {
    const { t, locale } = useTranslation();
    const [query, setQuery] = useState('');
    const [debounced, setDebounced] = useState('');
    const [type, setType] = useState('');
    const [status, setStatus] = useState<string>('');
    const [includeArchived, setIncludeArchived] = useState(false);
    const [list, setList] = useState<LoadState<CertificateOverview>>({ status: 'loading' });
    const [reference, setReference] = useState<CertificateReference | null>(null);
    const [reload, setReload] = useState(0);
    const [page, setPage] = useState(1);
    useEffect(() => { setPage(1); }, [debounced, type, status, includeArchived]);

    useEffect(() => {
        const handle = setTimeout(() => setDebounced(query.trim()), 200);
        return () => clearTimeout(handle);
    }, [query]);
    useEffect(() => {
        const controller = new AbortController();
        getCertificateReference(controller.signal).then(setReference).catch(() => setReference(null));
        return () => controller.abort();
    }, []);
    useEffect(() => {
        const controller = new AbortController();
        setList((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        getCertificateOverview({ query: debounced, type, status, includeArchived, page, pageSize: PAGE_SIZE }, controller.signal)
            .then((data) => setList({ status: 'ready', data }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setList({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [debounced, type, status, includeArchived, reload, page]);
    const pages = list.status === 'ready' ? Math.max(1, Math.ceil(list.data.total / PAGE_SIZE)) : 1;

    const role = (item: { relationId: number | null; isCustomer: boolean; isVendor: boolean; isCarrier: boolean }) => (item.relationId === null ? t('cert_own_company')
        : [item.isVendor ? t('relations_role_vendor') : '', item.isCustomer ? t('relations_role_customer') : '', item.isCarrier ? t('cert_role_carrier') : ''].filter(Boolean).join(', ') || '–');

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div>
                <h1 className="text-2xl font-bold text-gray-900" data-tour="page-title">{t('cert_page_title')}</h1>
                <p className="text-sm text-gray-600">{t('cert_page_subtitle')}</p>
            </div>
            <CertificateExpiryList />
            <div className="flex flex-wrap items-center gap-3">
                <label htmlFor="certificate-search" className="sr-only">{t('cert_page_search')}</label>
                <input id="certificate-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('cert_page_search')} className={`${inputClass} mt-0 max-w-md flex-1`} />
                <label htmlFor="certificate-type-filter" className="sr-only">{t('cert_type')}</label>
                <select id="certificate-type-filter" value={type} onChange={(event) => setType(event.target.value)} className={`${inputClass} mt-0 w-auto max-w-xs`}>
                    <option value="">{t('cert_page_all_types')}</option>
                    {reference?.types.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
                    <option value="Custom">{t('cert_type_group_custom')}</option>
                </select>
                <label htmlFor="certificate-status-filter" className="sr-only">{t('cert_page_status')}</label>
                <select id="certificate-status-filter" value={status} onChange={(event) => setStatus(event.target.value)} className={`${inputClass} mt-0 w-auto max-w-xs`}>
                    {STATUSES.map((option) => <option key={option} value={option}>{t(`cert_filter_${option || 'all'}`)}</option>)}
                </select>
                <label className="flex items-center gap-1 text-sm text-gray-800">
                    <input type="checkbox" checked={includeArchived} onChange={(event) => setIncludeArchived(event.target.checked)} />{t('cert_page_archived')}
                </label>
                <button type="button" className="text-sm text-blue-800 underline" onClick={() => setReload((n) => n + 1)}>↻ {t('availability_refresh')}</button>
                {list.status === 'ready' && <span className="text-xs text-gray-600" data-testid="certificate-count">{t('cert_page_count', { shown: String(list.data.items.length), total: String(list.data.total) })}</span>}
            </div>
            {list.status === 'loading' && <LoadingRows label={t('cert_loading')} />}
            {list.status === 'error' && <ErrorState error={list.error} onRetry={() => setReload((n) => n + 1)} />}
            {list.status === 'ready' && list.data.items.length === 0 && (
                <EmptyState testId="certificates-empty" title={t('cert_page_empty_title')} description={t('cert_page_empty_text')}
                    action={<Link href="/relations" className="text-sm font-medium text-blue-800 underline">{t('nav_relations')}</Link>} />
            )}
            {list.status === 'ready' && list.data.items.length > 0 && (
                <div className="overflow-x-auto rounded-md border border-gray-300 bg-white">
                    <table className="min-w-full text-sm" data-testid="certificate-table">
                        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-3 py-2">{t('cert_col_relation')}</th>
                                <th scope="col" className="px-3 py-2">{t('cert_type')}</th>
                                <th scope="col" className="px-3 py-2">{t('cert_number')}</th>
                                <th scope="col" className="px-3 py-2">{t('cert_products')}</th>
                                <th scope="col" className="px-3 py-2">{t('cert_valid_until')}</th>
                                <th scope="col" className="px-3 py-2">{t('cert_page_status')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {list.data.items.map((item, index) => (
                                <tr key={item.certificateId} className={listRowClass(index, {})} data-testid={`certificate-row-${item.certificateId}`}>
                                    <td className="px-3 py-2">
                                        <Link href={certificateLink(item.relationId)} className="font-medium text-blue-800 underline-offset-2 hover:underline">{item.relationId === null ? t('cert_own_company') : item.relationName}</Link>
                                        <span className="block text-xs text-gray-600">{role(item)}</span>
                                    </td>
                                    <td className="px-3 py-2">{shortTypeName(item.typeCode, item.typeName)}{!item.isActive && <span className="ml-1 text-xs text-gray-600">({t('cert_archived')})</span>}</td>
                                    <td className="px-3 py-2 font-mono text-xs">{item.number}</td>
                                    <td className="px-3 py-2 text-xs text-gray-800">
                                        {item.productNames.join(', ') || (item.productsPending ? <span className="text-amber-900">{t('cert_products_pending_short')}</span> : '–')}
                                    </td>
                                    <td className="px-3 py-2 text-xs">{item.validUntil ? formatDateOnly(item.validUntil, locale) : '–'}</td>
                                    <td className="px-3 py-2"><CertificateStatusBadge status={item.status} daysLeft={item.daysLeft} /></td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    {pages > 1 && (
                        <div className="flex items-center justify-between border-t border-gray-200 px-3 py-2 text-xs text-gray-700">
                            <button type="button" className="rounded border border-gray-300 px-2 py-1" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>‹ {t('relations_page_previous')}</button>
                            <span>{t('relations_page_of', { page: String(page), pages: String(pages) })}</span>
                            <button type="button" className="rounded border border-gray-300 px-2 py-1" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>{t('relations_page_next')} ›</button>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}

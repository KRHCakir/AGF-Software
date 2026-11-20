'use client';

import { KeyboardEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type CertificateRow, certificateStatus, certificateWarnings, daysLeft, emptyCertificateRow, tabAddsRow } from '../../lib/certificateRules';
import { amsterdamDateOf, formatDateOnly } from '../../lib/calendarDate';
import { CUSTOM_TYPE, GGN_TYPE, type CertificateFile, type CertificateReference } from '../../types/certificate';
import DateField from '../shared/DateField';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';
import GgnPortalButton from '../relations/GgnPortalButton';
import CertificateStatusBadge from './CertificateStatusBadge';

export interface ProductOption { id: number; name: string }

const CUSTOM_PREFIX = 'custom:';
const FILE_ACCEPT = 'application/pdf,image/jpeg,image/png,image/heic,image/heif,.heic,.heif';

// Het blok "Certificaten" (certificaten deel 1) bij een relatie of het eigen bedrijf: per certificaat de soort (vaste lijst of een eigen
// soort), het nummer (GGN: 13 cijfers), de instantie, geldig van en tot met de status, de producten (verplicht bij GGN, anders een
// waarschuwing), wanneer gecontroleerd (wie: de ingelogde gebruiker, bij opslaan) en de PDF's en foto's als versies. Tab op het laatste veld
// van de laatste (ingevulde) regel maakt een nieuwe regel; de knoppen om iets weg te halen staan buiten de Tab-volgorde. Een opgeslagen
// certificaat verdwijnt nooit: archiveren (Actief uit).
export default function CertificatesEditor({ idPrefix, rows, onChange, reference, products, errors, today, warningDays, disabled = false, onOpenFile, onAddFile, uploading = null, dataTour = 'certificate' }: {
    idPrefix: string;
    rows: CertificateRow[];
    onChange: (rows: CertificateRow[]) => void;
    reference: CertificateReference;
    products: ProductOption[];
    errors: Record<string, string[]>;
    today: string;
    warningDays: number;
    disabled?: boolean;
    onOpenFile: (certificateId: number, file: CertificateFile) => void;
    // Een opgeslagen certificaat krijgt het bestand meteen (een nieuwe versie); bij een nieuw certificaat gaat het mee met Opslaan.
    onAddFile: (row: CertificateRow, file: File) => void;
    uploading?: string | null;
    dataTour?: string;
}) {
    const { t, locale } = useTranslation();
    const [focusKey, setFocusKey] = useState<string | null>(null);
    const container = useRef<HTMLDivElement | null>(null);
    useEffect(() => {
        if (focusKey === null) return;
        container.current?.querySelector<HTMLSelectElement>(`[data-row="${focusKey}"] select[data-field="typeCode"]`)?.focus();
        setFocusKey(null);
    }, [focusKey, rows]);

    const edit = (key: string, patch: Partial<CertificateRow>) => onChange(rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));
    const add = () => { const row = emptyCertificateRow(); onChange([...rows, row]); setFocusKey(row.key); };
    const remove = (key: string) => onChange(rows.filter((row) => row.key !== key));
    const tabOnLast = (event: KeyboardEvent<HTMLElement>, key: string) => {
        if (event.key !== 'Tab' || event.shiftKey || !tabAddsRow(rows, key)) return;
        event.preventDefault();
        add();
    };
    const productName = (id: number) => products.find((p) => p.id === id)?.name ?? `#${id}`;
    const typeValue = (row: CertificateRow) => (row.typeCode !== CUSTOM_TYPE ? row.typeCode
        : reference.customTypes.some((n) => n.toLowerCase() === row.customTypeName.trim().toLowerCase()) ? `${CUSTOM_PREFIX}${row.customTypeName.trim()}` : CUSTOM_TYPE);
    const chooseType = (row: CertificateRow, value: string) => {
        if (value.startsWith(CUSTOM_PREFIX)) edit(row.key, { typeCode: CUSTOM_TYPE, customTypeName: value.slice(CUSTOM_PREFIX.length) });
        // "Eigen soort toevoegen…": een nieuwe naam (een naam uit de lijst kies je in de lijst zelf).
        else edit(row.key, { typeCode: value, customTypeName: '' });
    };

    return (
        <div ref={container} className="space-y-2" data-tour={dataTour}>
            {rows.length === 0 && <p className="text-xs text-gray-600">{t('cert_none')}</p>}
            {rows.map((row, index) => {
                const k = (field: string) => `certificates.${row.key}.${field}`;
                const e = (field: string) => errors[k(field)];
                const type = reference.types.find((item) => item.code === row.typeCode);
                const isGgn = row.typeCode === GGN_TYPE;
                const status = certificateStatus(row.validUntil, today, warningDays);
                const left = daysLeft(row.validUntil, today);
                const warnings = certificateWarnings(row);
                const free = products.filter((p) => !row.productIds.includes(p.id));
                const storedCheck = row.checkedBy !== null && row.id !== null;
                return (
                    <fieldset key={row.key} data-row={row.key} data-testid={`certificate-${index}`} disabled={disabled}
                        className={`space-y-2 rounded border p-2 ${row.isActive ? 'border-gray-200' : 'border-dashed border-gray-300 bg-gray-50'}`}>
                        <legend className="sr-only">{t('cert_row', { number: String(index + 1) })}</legend>
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 md:grid-cols-4">
                            <div>
                                <label htmlFor={`${k('typeCode')}-input`} className="text-xs font-medium text-gray-700">{t('cert_type')} *</label>
                                <select {...fieldProps(k('typeCode'), e('typeCode'))} data-field="typeCode" value={typeValue(row)} className={inputClass}
                                    onChange={(event) => chooseType(row, event.target.value)} data-tour={index === 0 ? `${dataTour}-type` : undefined}>
                                    <option value="">{t('cert_type_choose')}</option>
                                    <optgroup label={t('cert_type_group_builtin')}>
                                        {reference.types.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
                                    </optgroup>
                                    {reference.customTypes.length > 0 && (
                                        <optgroup label={t('cert_type_group_custom')}>
                                            {reference.customTypes.map((name) => <option key={name} value={`${CUSTOM_PREFIX}${name}`}>{name}</option>)}
                                        </optgroup>
                                    )}
                                    <option value={CUSTOM_TYPE}>{t('cert_type_custom_new')}</option>
                                </select>
                                <FieldError id={k('typeCode')} messages={e('typeCode')} />
                                {type && <span className="block text-xs text-gray-500" data-testid={`certificate-${index}-type-info`}>{t(`cert_type_info_${type.code}`)}</span>}
                            </div>
                            {typeValue(row) === CUSTOM_TYPE && (
                                <div>
                                    <label htmlFor={`${k('customTypeName')}-input`} className="text-xs font-medium text-gray-700">{t('cert_custom_name')} *</label>
                                    <input {...fieldProps(k('customTypeName'), e('customTypeName'))} value={row.customTypeName} maxLength={80} className={inputClass}
                                        onChange={(event) => edit(row.key, { customTypeName: event.target.value })} />
                                    <FieldError id={k('customTypeName')} messages={e('customTypeName')} />
                                </div>
                            )}
                            <div>
                                <label htmlFor={`${k('number')}-input`} className="text-xs font-medium text-gray-700">{isGgn ? t('cert_number_ggn') : t('cert_number')} *</label>
                                <input {...fieldProps(k('number'), e('number'))} value={row.number} maxLength={isGgn ? 20 : 100} inputMode={isGgn ? 'numeric' : undefined}
                                    className={`${inputClass} font-mono`} onChange={(event) => edit(row.key, { number: event.target.value })} data-tour={index === 0 ? `${dataTour}-number` : undefined} />
                                <FieldError id={k('number')} messages={e('number')} />
                            </div>
                            <div>
                                <label htmlFor={`${k('certificationBody')}-input`} className="text-xs font-medium text-gray-700">{t('cert_body')}</label>
                                <input {...fieldProps(k('certificationBody'), e('certificationBody'))} value={row.certificationBody} maxLength={200} className={inputClass}
                                    onChange={(event) => edit(row.key, { certificationBody: event.target.value })} />
                                <FieldError id={k('certificationBody')} messages={e('certificationBody')} />
                            </div>
                        </div>
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 md:grid-cols-4">
                            <div>
                                <label htmlFor={`${k('validFrom')}-input`} className="text-xs font-medium text-gray-700">{t('cert_valid_from')}</label>
                                <DateField {...fieldProps(k('validFrom'), e('validFrom'))} value={row.validFrom} disabled={disabled} onChange={(value) => edit(row.key, { validFrom: value })} />
                                <FieldError id={k('validFrom')} messages={e('validFrom')} />
                            </div>
                            <div>
                                <label htmlFor={`${k('validUntil')}-input`} className="text-xs font-medium text-gray-700">{t('cert_valid_until')}</label>
                                <DateField {...fieldProps(k('validUntil'), e('validUntil'))} value={row.validUntil} disabled={disabled} onChange={(value) => edit(row.key, { validUntil: value })}
                                    dataTour={index === 0 ? `${dataTour}-valid-until` : undefined} />
                                <FieldError id={k('validUntil')} messages={e('validUntil')} />
                            </div>
                            <div className="flex items-end pb-1 sm:col-span-2">
                                <CertificateStatusBadge status={status} daysLeft={left} testId={`certificate-${index}-status`} />
                            </div>
                        </div>
                        <div>
                            <span className="text-xs font-medium text-gray-700" id={`${idPrefix}-${index}-products`}>{t('cert_products')}{isGgn ? ' *' : ''}</span>
                            <div className="mt-1 flex flex-wrap items-center gap-1" role="group" aria-labelledby={`${idPrefix}-${index}-products`} data-testid={`certificate-${index}-products`}>
                                {row.productIds.map((id) => (
                                    <span key={id} className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-xs text-emerald-900 ring-1 ring-emerald-200">
                                        {productName(id)}
                                        <button type="button" tabIndex={-1} aria-label={t('cert_product_remove', { name: productName(id) })} className="text-emerald-900"
                                            onClick={() => edit(row.key, { productIds: row.productIds.filter((p) => p !== id) })}>×</button>
                                    </span>
                                ))}
                                <select {...fieldProps(k('productIds'), e('productIds'))} aria-label={t('cert_product_add')} value="" className={`${inputClass} mt-0 w-auto max-w-[14rem]`}
                                    onChange={(event) => { const chosen = products.find((p) => String(p.id) === event.target.value); if (chosen) edit(row.key, { productIds: [...row.productIds, chosen.id] }); }}
                                    data-tour={index === 0 ? `${dataTour}-product` : undefined}>
                                    <option value="">{t('cert_product_add')}</option>
                                    {free.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                                </select>
                            </div>
                            <FieldError id={k('productIds')} messages={e('productIds')} />
                            {warnings.filter((w) => w !== 'ggn_check_digit').map((w) => <p key={w} className="text-xs text-amber-900" data-testid={`certificate-${index}-warning-${w}`}>{t(`cert_warning_${w}`)}</p>)}
                        </div>
                        {warnings.includes('ggn_check_digit') && <p className="text-xs text-amber-900" data-testid={`certificate-${index}-warning-ggn_check_digit`}>{t('cert_warning_ggn_check_digit')}</p>}
                        {isGgn && (
                            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 md:grid-cols-4">
                                <div className="md:col-span-2">
                                    <label htmlFor={`${k('producerName')}-input`} className="text-xs font-medium text-gray-700">{t('cert_producer')}</label>
                                    <input {...fieldProps(k('producerName'), e('producerName'))} value={row.producerName} maxLength={200} className={inputClass}
                                        onChange={(event) => edit(row.key, { producerName: event.target.value })} />
                                </div>
                                <label className="flex items-end gap-1 pb-1 text-xs text-gray-800">
                                    <input type="checkbox" checked={row.hasGgnLabel} onChange={(event) => edit(row.key, { hasGgnLabel: event.target.checked })} />{t('cert_ggn_label')}
                                </label>
                            </div>
                        )}
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 md:grid-cols-4">
                            <div>
                                <label htmlFor={`${k('checkedOn')}-input`} className="text-xs font-medium text-gray-700">{t('cert_checked_on')}</label>
                                <DateField {...fieldProps(k('checkedOn'), e('checkedOn'))} max={today} value={row.checkedOn} disabled={disabled} onChange={(value) => edit(row.key, { checkedOn: value, checkedBy: null })} />
                                <FieldError id={k('checkedOn')} messages={e('checkedOn')} />
                                {row.checkedOn !== '' && <span className="block text-xs text-gray-600" data-testid={`certificate-${index}-checked-by`}>{storedCheck ? t('cert_checked_by', { name: row.checkedBy ?? '' }) : t('cert_checked_by_you')}</span>}
                            </div>
                            {isGgn && (
                                <div className="flex flex-wrap items-end gap-2 pb-1 sm:col-span-3">
                                    <GgnPortalButton ggn={row.number.replace(/ /g, '')} portalUrl={reference.globalGapPortalUrl} disabled={disabled} />
                                    <a href={reference.globalGapDatabaseUrl} target="_blank" rel="noreferrer" tabIndex={-1} className="text-xs font-medium text-blue-800 underline underline-offset-2">{t('relations_ggn_official')}</a>
                                </div>
                            )}
                        </div>
                        <div>
                            <span className="text-xs font-medium text-gray-700">{t('cert_files')}</span>
                            <ul className="mt-1 space-y-1 text-xs" data-testid={`certificate-${index}-files`}>
                                {row.files.length === 0 && row.pendingFiles.length === 0 && <li className="text-gray-600">{t('cert_files_none')}</li>}
                                {row.files.map((file) => (
                                    <li key={file.id} className="flex flex-wrap items-center gap-2">
                                        <span className="rounded bg-gray-100 px-1 font-medium text-gray-800">{t('cert_file_version', { version: String(file.version) })}</span>
                                        <span className="break-all text-gray-900">{file.fileName}</span>
                                        <span className="text-gray-600">{formatDateOnly(amsterdamDateOf(file.createdAt) ?? file.createdAt.slice(0, 10), locale)} · {file.createdBy}</span>
                                        <button type="button" tabIndex={-1} className="font-medium text-blue-800 underline underline-offset-2" onClick={() => row.id !== null && onOpenFile(row.id, file)}>{t('cert_file_open')}</button>
                                    </li>
                                ))}
                                {row.pendingFiles.map((file, i) => (
                                    <li key={`${file.name}-${i}`} className="flex flex-wrap items-center gap-2 text-gray-800">
                                        <span className="break-all">{t('cert_file_pending', { name: file.name })}</span>
                                        <button type="button" tabIndex={-1} className="text-red-800 underline" onClick={() => edit(row.key, { pendingFiles: row.pendingFiles.filter((_, j) => j !== i) })}>{t('cert_remove')}</button>
                                    </li>
                                ))}
                            </ul>
                            <label className={`${secondaryButtonClass} mt-1 inline-flex cursor-pointer items-center px-2 py-0.5 text-xs focus-within:ring-2 focus-within:ring-blue-500`}>
                                {uploading === row.key ? t('cert_file_uploading') : t('cert_file_add')}
                                <input type="file" accept={FILE_ACCEPT} className="sr-only" disabled={disabled || uploading === row.key} data-testid={`certificate-${index}-file`}
                                    onChange={(event) => { const file = event.target.files?.[0]; event.target.value = ''; if (file) onAddFile(row, file); }}
                                    data-tour={index === 0 ? `${dataTour}-file` : undefined} />
                            </label>
                            <FieldError id={k('file')} messages={e('file')} />
                        </div>
                        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-gray-800">
                            <label className="flex items-center gap-1">
                                <input type="checkbox" checked={row.isActive} onChange={(event) => edit(row.key, { isActive: event.target.checked })} onKeyDown={(event) => tabOnLast(event, row.key)}
                                    data-testid={`certificate-${index}-active`} />
                                {t('cert_active')}
                                {!row.isActive && <span className="text-gray-600">({t('cert_archived')})</span>}
                            </label>
                            {row.id === null && <button type="button" tabIndex={-1} className="text-red-800 underline" onClick={() => remove(row.key)}>{t('cert_remove')}</button>}
                        </div>
                    </fieldset>
                );
            })}
            <div className="flex flex-wrap items-center gap-3">
                <button type="button" className={`${secondaryButtonClass} text-xs`} disabled={disabled} onClick={add} data-tour={`${dataTour}-add`}>+ {t('cert_add')}</button>
                <span className="text-xs text-gray-600">{t('cert_tab_hint')}</span>
            </div>
        </div>
    );
}

'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { type CertificateRow, buildCertificatesPayload, certificatesChanged, isBlankRow, rowsFromCertificates } from '../../lib/certificateRules';
import { todayInAmsterdam } from '../../lib/calendarDate';
import { ApiError, toApiError } from '../../services/apiClient';
import { getCertificateFile, getCertificateReference, getCertificates, listProductOptions, saveCertificates, uploadCertificateFile } from '../../services/certificateService';
import type { CertificateFile, CertificateReference, CertificateWarning } from '../../types/certificate';
import type { ProductOption } from './CertificatesEditor';

// Laden en opslaan van het blok "Certificaten" (certificaten deel 1), los van de rest van het formulier, zoals de e-mailadressen voor
// documenten: eigen endpoint en eigen versie. owner: een relatie (id null = nog niet opgeslagen: leeg, opslaan na het aanmaken) of het eigen
// bedrijf. Zolang de opgeslagen lijst laadt of laden mislukte, is het blok dicht voor invoer: nooit een lege lijst die een opgeslagen lijst
// overschrijft. Bestanden: bij een opgeslagen certificaat meteen een nieuwe versie, bij een nieuw certificaat na het opslaan.
export type CertificateOwner = { kind: 'relation'; id: number | null } | { kind: 'own' };

const ownerId = (owner: CertificateOwner): number | null | undefined => (owner.kind === 'own' ? null : owner.id ?? undefined);

export function useCertificates(owner: CertificateOwner, enabled = true) {
    const [reference, setReference] = useState<CertificateReference | null>(null);
    const [products, setProducts] = useState<ProductOption[]>([]);
    const [rows, setRowsState] = useState<CertificateRow[]>([]);
    const [stored, setStored] = useState<CertificateRow[]>([]);
    const [version, setVersion] = useState('0');
    const [warningDays, setWarningDays] = useState(30);
    const [loading, setLoading] = useState(true);
    const [failed, setFailed] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [conflict, setConflict] = useState(false);
    const [errors, setErrors] = useState<Record<string, string[]>>({});
    const [problems, setProblems] = useState<{ field: string; code: string }[]>([]);
    const [warnings, setWarnings] = useState<CertificateWarning[]>([]);
    const [uploading, setUploading] = useState<string[]>([]);
    const [reloadToken, setReloadToken] = useState(0);
    const rowsRef = useRef<CertificateRow[]>([]);
    const id = ownerId(owner);

    const setRows = useCallback((next: CertificateRow[]) => {
        rowsRef.current = next;
        setRowsState(next);
        setErrors({});
        setProblems([]);
        setError(null);
    }, []);

    useEffect(() => {
        // Pas laden als het blok nodig is (bij een nieuwe relatie: zodra het opengaat).
        if (!enabled) return;
        const controller = new AbortController();
        setLoading(true);
        Promise.all([
            getCertificateReference(controller.signal),
            listProductOptions(controller.signal),
            id === undefined ? Promise.resolve(null) : getCertificates(id, controller.signal),
        ]).then(([ref, productList, list]) => {
            setReference(ref);
            setProducts(productList);
            setWarningDays(list?.warningDays ?? ref.warningDays);
            const loaded = list === null ? [] : rowsFromCertificates(list.certificates);
            rowsRef.current = loaded;
            setRowsState(loaded);
            setStored(loaded);
            setVersion(list?.version ?? '0');
            setFailed(false);
            setError(null);
            setConflict(false);
        }).catch((caught) => {
            const apiError = toApiError(caught);
            if (apiError.kind === 'aborted') return;
            setError(apiError);
            setFailed(true);
        }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
        return () => controller.abort();
        // Alleen bij een andere eigenaar (of bewust opnieuw laden), niet bij elke render.
    }, [owner.kind, id, reloadToken, enabled]);

    const reload = () => setReloadToken((n) => n + 1);
    // Na het opslaan van de instellingen: de eigen soorten en de waarschuwingstermijn overnemen, zonder de ingevulde regels te herladen.
    const applySettings = useCallback((settings: { warningDays: number; customTypes: string[] }) => {
        setReference((current) => (current === null ? current : { ...current, warningDays: settings.warningDays, customTypes: settings.customTypes }));
        setWarningDays(settings.warningDays);
    }, []);
    const changed = certificatesChanged(rows, stored);

    // Vertaalt "certificates[i].veld" van de server naar de sleutel van de i-de ingevulde regel.
    const mapServerErrors = (fieldErrors: Record<string, string[]>, sent: CertificateRow[]) => {
        const mapped: Record<string, string[]> = {};
        for (const [field, messages] of Object.entries(fieldErrors)) {
            const match = /^certificates\[(\d+)\]\.(\w+)$/i.exec(field);
            const row = match ? sent[Number(match[1])] : undefined;
            const key = row ? `certificates.${row.key}.${match![2].charAt(0).toLowerCase()}${match![2].slice(1)}` : 'certificates';
            (mapped[key] ??= []).push(...messages);
        }
        return mapped;
    };

    // Vooraf controleren met dezelfde regels als bij opslaan: fouten tonen zonder iets te versturen (de relatiekaart doet dit vóór het
    // opslaan van de relatie, zodat er nooit half wordt opgeslagen).
    const validate = (): boolean => {
        if (failed || loading || !certificatesChanged(rowsRef.current, stored)) return true;
        const built = buildCertificatesPayload(rowsRef.current, todayInAmsterdam());
        if (built.ok) return true;
        setProblems(built.errors);
        return false;
    };

    const upload = async (certificateId: number, file: File): Promise<CertificateFile> => uploadCertificateFile(certificateId, file);

    // Opslaan (met de relatie mee, of los bij het eigen bedrijf). target = het id van een net aangemaakte relatie. true = klaar.
    const save = async (target?: number): Promise<boolean> => {
        if (failed || loading) return !changed;
        if (!changed) return true;
        if (conflict) return false;
        const destination = owner.kind === 'own' ? null : target ?? owner.id;
        if (destination === undefined) return false;
        const current = rowsRef.current;
        const built = buildCertificatesPayload(current, todayInAmsterdam());
        if (!built.ok) {
            setProblems(built.errors);
            return false;
        }
        const sent = current.filter((r) => !isBlankRow(r));
        try {
            const saved = await saveCertificates(destination, { expectedVersion: version, certificates: built.value });
            let next = rowsFromCertificates(saved.certificates);
            // Bestanden van nieuwe certificaten: nu het certificaat een id heeft (zelfde volgorde als verstuurd).
            let uploadFailed: ApiError | null = null;
            for (let i = 0; i < sent.length; i++) {
                const row = next[i];
                for (const file of sent[i].pendingFiles) {
                    try {
                        const added = await upload(row.id!, file);
                        next = next.map((r) => (r.key === row.key ? { ...r, files: [added, ...r.files] } : r));
                    } catch (caught) {
                        uploadFailed = toApiError(caught);
                        next = next.map((r) => (r.key === row.key ? { ...r, pendingFiles: [...r.pendingFiles, file] } : r));
                    }
                }
            }
            rowsRef.current = next;
            setRowsState(next);
            setStored(next.map((r) => ({ ...r, pendingFiles: [] })));
            setVersion(saved.version);
            // Een nieuwe eigen soort komt bij het opslaan in de bedrijfslijst: de keuzelijst haalt die er meteen bij.
            if (built.value.some((c) => c.typeCode === 'Custom' && c.customTypeName !== null && !(reference?.customTypes ?? []).some((n) => n.toLowerCase() === c.customTypeName!.toLowerCase()))) {
                getCertificateReference().then((fresh) => setReference((current) => (current === null ? fresh : { ...current, customTypes: fresh.customTypes }))).catch(() => {});
            }
            setWarnings(saved.warnings);
            setErrors({});
            setProblems([]);
            setError(uploadFailed);
            return uploadFailed === null;
        } catch (caught) {
            const apiError = toApiError(caught);
            if (apiError.status === 409) {
                setConflict(true);
                setError(apiError);
                return false;
            }
            setErrors(mapServerErrors(apiError.fieldErrors, sent));
            setError(apiError);
            return false;
        }
    };

    // Een bestand bij een opgeslagen certificaat: meteen een nieuwe versie (de lijst zelf verandert er niet door).
    const addFile = async (row: CertificateRow, file: File) => {
        if (row.id === null) {
            setRows(rowsRef.current.map((r) => (r.key === row.key ? { ...r, pendingFiles: [...r.pendingFiles, file] } : r)));
            return;
        }
        setUploading((keys) => [...keys, row.key]);
        setError(null);
        try {
            const added = await upload(row.id, file);
            const withFile = (list: CertificateRow[]) => list.map((r) => (r.id === row.id ? { ...r, files: [added, ...r.files] } : r));
            rowsRef.current = withFile(rowsRef.current);
            setRowsState(rowsRef.current);
            setStored(withFile);
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            const message = apiError.fieldErrors.file;
            if (message) setErrors({ [`certificates.${row.key}.file`]: message });
        } finally {
            setUploading((keys) => { const at = keys.indexOf(row.key); return at < 0 ? keys : [...keys.slice(0, at), ...keys.slice(at + 1)]; });
        }
    };

    // Openen: het venster gaat direct bij de klik open (anders kan de pop-upblokkering het tegenhouden) en krijgt het bestand zodra het binnen
    // is. Blokkeert de browser het venster toch, dan wordt het bestand gedownload met zijn eigen naam.
    const openFile = async (certificateId: number, file: CertificateFile) => {
        const win = typeof window.open === 'function' ? window.open('', '_blank') : null;
        if (win) win.opener = null;
        try {
            const url = URL.createObjectURL(await getCertificateFile(certificateId, file.id));
            if (win && !win.closed) {
                win.location.href = url;
            } else {
                const link = document.createElement('a');
                link.href = url;
                link.download = file.fileName;
                link.rel = 'noopener';
                document.body.appendChild(link);
                link.click();
                link.remove();
            }
            setTimeout(() => URL.revokeObjectURL(url), 60_000);
        } catch (caught) {
            win?.close();
            setError(toApiError(caught));
        }
    };

    return {
        reference, products, rows, setRows, stored, warningDays, loading, failed, error, conflict, errors, problems, warnings, uploading, changed,
        reload, validate, applySettings, save, addFile, openFile,
    };
}

// Fouten voor het blok: die van de server (al per regel) en die van de eigen controle (codes, vertaald).
export function certificateMessages(errors: Record<string, string[]>, problems: { field: string; code: string }[], t: (key: string) => string): Record<string, string[]> {
    const messages: Record<string, string[]> = { ...errors };
    for (const problem of problems) (messages[problem.field] ??= []).push(t(`cert_error_${problem.code}`));
    return messages;
}

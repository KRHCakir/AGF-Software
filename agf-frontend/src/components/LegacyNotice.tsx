'use client';

import Link from 'next/link';
import { useTranslation } from '../i18n/LanguageContext';

// Vaste melding bovenaan een oud scherm: zegt dat dit de oude registratie is en wijst naar de nieuwe werkplek.
export default function LegacyNotice({ noticeKey, linkHref, linkKey }: { noticeKey: string; linkHref: string; linkKey: string }) {
    const { t } = useTranslation();
    return (
        <p role="note" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
            {t(noticeKey)}{' '}
            <Link href={linkHref} className="font-medium underline underline-offset-2">{t(linkKey)}</Link>
        </p>
    );
}

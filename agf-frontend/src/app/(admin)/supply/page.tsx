'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from '../../../i18n/LanguageContext';
import { supplyRedirect } from '../../../lib/navigation';

// Oud adres: de werkplek "Aanvoer" is opgesplitst in Inkooporder aanmaken, Ontvangst en Klantgebonden aanvoer. Bladwijzers en oude
// links (/supply, /supply#plan, /supply#receive) blijven werken en gaan naar het scherm met de nieuwe naam.
export default function SupplyRedirectPage() {
    const { t } = useTranslation();
    const router = useRouter();
    useEffect(() => {
        router.replace(supplyRedirect(window.location.hash));
    }, [router]);
    return <p className="p-4 text-sm text-gray-600" role="status">{t('nav_redirecting')}</p>;
}

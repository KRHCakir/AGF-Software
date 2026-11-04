'use client';

import Link from 'next/link';

// Het partijnummer uit de partij zelf als link naar het partijdetail; zonder partij het oude lotnummer van de regel (geen link).
export default function PartijNumberLink({ partijId, partijNumber, lotNumber, className = 'font-mono' }: {
    partijId?: number | null;
    partijNumber?: string | null;
    lotNumber: string | null;
    className?: string;
}) {
    if (partijId) {
        return <Link href={`/partijen?id=${partijId}`} className={`${className} text-blue-800 underline underline-offset-2`} data-tour="partij-link-number" data-tour-key={partijNumber ?? partijId}>{partijNumber ?? lotNumber ?? `#${partijId}`}</Link>;
    }
    return <span className={className}>{lotNumber ?? '–'}</span>;
}

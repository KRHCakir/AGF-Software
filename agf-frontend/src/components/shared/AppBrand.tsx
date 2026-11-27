import Link from 'next/link';
import { APP_NAME, APP_NAME_PARTS } from '../../lib/appName';

// Links boven in de kopbalk: de naam als link naar Start. Het eerste deel in de hoofdkleur, het tweede rustig grijs.
export default function AppBrand() {
    const [first, second] = APP_NAME_PARTS;
    return (
        <Link href="/dashboard" aria-label={APP_NAME} data-testid="app-brand" className="font-bold text-xl text-blue-800 mr-2 lg:mr-4 tracking-tight shrink-0">
            {first}<span className="text-gray-600 font-normal">{second}</span>
        </Link>
    );
}

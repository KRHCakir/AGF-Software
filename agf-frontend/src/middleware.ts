import { NextResponse, type NextRequest } from 'next/server';
import { requestTooLarge, TOO_LARGE_MESSAGE } from './lib/apiProxy';

// Alleen voor /api, het pad dat de website doorstuurt naar de backend (next.config.ts): een te groot verzoek stopt hier al met 413,
// in dezelfde vorm als een fout van de API ({ message }), zodat het scherm de melding gewoon toont.
export function middleware(request: NextRequest) {
    if (requestTooLarge(request.headers.get('content-length'))) {
        return NextResponse.json({ message: TOO_LARGE_MESSAGE }, { status: 413 });
    }
    return NextResponse.next();
}

export const config = { matcher: '/api/:path*' };

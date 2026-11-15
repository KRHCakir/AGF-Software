// Gezondheidscontrole van de website voor Docker Compose en een eenvoudige externe bewaking (docs/demo-online.md).
// Bewust zonder de API: die heeft een eigen /health; zo zie je welke van de twee niet antwoordt.
export const dynamic = 'force-dynamic';

export function GET() {
    return Response.json({ status: 'ok' }, { headers: { 'Cache-Control': 'no-store' } });
}

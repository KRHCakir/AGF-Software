import type { HTMLAttributes } from 'react';

// Paginatitel in een kader (feedback 24 november): dunne rand, zachte achtergrond en links een balk in de hoofdkleur van de app
// (blauw 700, dezelfde als de primaire knop). Het kader is zo breed als de titel (w-fit) en nooit breder dan de pagina; een lange titel
// loopt door op een tweede regel. De ondertitel staat er buiten, als gewone <p> eronder, met een kleine marge (mb-1) ertussen.
// className is alleen voor een andere marge en vervangt dan mb-1.
export const pageTitleClass =
    'w-fit max-w-full break-words rounded-md border border-gray-200 border-l-4 border-l-blue-700 bg-gray-50 px-4 py-2.5 text-2xl font-bold text-gray-900';

export default function PageTitle({ className, children, ...rest }: HTMLAttributes<HTMLHeadingElement>) {
    return (
        <h1 {...rest} className={`${pageTitleClass} ${className ?? 'mb-1'}`}>
            {children}
        </h1>
    );
}

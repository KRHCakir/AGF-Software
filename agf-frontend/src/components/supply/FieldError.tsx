'use client';

// Foutmelding onder een veld; het veld verwijst ernaar via aria-describedby.
export default function FieldError({ id, messages }: { id: string; messages: string[] | undefined }) {
    if (!messages || messages.length === 0) {
        return null;
    }
    return (
        <p id={id} className="mt-1 text-xs text-red-800">
            {messages.join(' ')}
        </p>
    );
}

export function fieldProps(id: string, messages: string[] | undefined) {
    const invalid = messages !== undefined && messages.length > 0;
    return {
        id: `${id}-input`,
        'aria-invalid': invalid,
        'aria-describedby': invalid ? id : undefined,
    };
}

// Regeltabel in een formulier: op een smal scherm (< md) staan de velden van een regel onder elkaar met zichtbaar label,
// zodat getallen volledig zichtbaar blijven; op brede schermen gewoon een tabel met kolomkoppen.
export const lineTableClass = 'min-w-full divide-y divide-gray-200 text-sm max-md:block max-md:[&_thead]:hidden max-md:[&_tbody]:block max-md:[&_tr]:block '
    + 'max-md:[&_td]:block max-md:[&_td]:w-full max-md:[&_td>label.sr-only]:not-sr-only max-md:[&_td>div>label.sr-only:first-child]:not-sr-only max-md:[&_label]:text-xs';

export const inputClass =
    'mt-1 w-full rounded border border-gray-300 px-2 py-1 text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100 disabled:text-gray-600 aria-[invalid=true]:border-red-500';

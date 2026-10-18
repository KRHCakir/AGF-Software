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

export const inputClass =
    'mt-1 w-full rounded border border-gray-300 px-2 py-1 text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100 disabled:text-gray-600 aria-[invalid=true]:border-red-500';

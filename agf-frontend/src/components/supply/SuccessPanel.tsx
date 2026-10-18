'use client';

// Bevestiging na opslaan, met de volgende acties.
export default function SuccessPanel({ title, note, children, actions }: {
    title: string;
    note?: string;
    children?: React.ReactNode;
    actions: React.ReactNode;
}) {
    return (
        <section role="status" aria-live="polite" className="rounded-md border border-green-300 bg-green-50 p-4">
            <h3 className="flex items-center gap-2 font-semibold text-green-900">
                <span aria-hidden="true">✓</span>{title}
            </h3>
            {note && <p className="mt-1 text-sm text-green-900">{note}</p>}
            {children && <div className="mt-3 text-sm text-gray-900">{children}</div>}
            <div className="mt-4 flex flex-wrap gap-2">{actions}</div>
        </section>
    );
}

export const primaryButtonClass =
    'inline-flex items-center rounded bg-blue-700 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-800 disabled:cursor-not-allowed disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-1';

export const secondaryButtonClass =
    'inline-flex items-center rounded border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-800 hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-blue-500';

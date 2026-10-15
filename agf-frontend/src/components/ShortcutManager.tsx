'use client';

import { useState } from 'react';
import { ProductService } from '../services/productService';
import { useTranslation } from '../i18n/LanguageContext';

interface ShortcutManagerProps {
    onSuccess: () => void;
}

export default function ShortcutManager({ onSuccess }: ShortcutManagerProps) {
    const { t } = useTranslation();
    const [loading, setLoading] = useState(false);

    const [product, setProduct] = useState('');
    const [variant, setVariant] = useState('');
    const [origin, setOrigin] = useState('');
    const [size, setSize] = useState('');
    const [shortcut, setShortcut] = useState('');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);

        try {
            await ProductService.quickAddShortcut({
                productName: product, variantName: variant, origin: origin,
                sizeCategory: size, shortcutCode: shortcut
            });

            // Geen succesmelding meer, gewoon direct velden leegmaken en lijst verversen
            setProduct(''); setVariant(''); setOrigin(''); setSize(''); setShortcut('');
            onSuccess();
        } catch {
            // Alleen meldingen als er iets fout gaat
            alert('Error saving shortcut. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6">
            <div className="flex items-center gap-3 mb-6">
                <div className="bg-blue-100 p-2 rounded-lg text-blue-600">
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
                    </svg>
                </div>
                <h2 className="text-lg font-bold text-gray-800">{t('add_new_shortcut')}</h2>
            </div>

            <form onSubmit={handleSubmit} className="grid grid-cols-1 md:grid-cols-6 gap-4">
                <div className="md:col-span-1">
                    <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('product')}</label>
                    <input type="text" required className="w-full border-gray-300 rounded-md p-2 border" value={product} onChange={(e) => setProduct(e.target.value)} />
                </div>
                <div className="md:col-span-1">
                    <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('variant')}</label>
                    <input type="text" className="w-full border-gray-300 rounded-md p-2 border" value={variant} onChange={(e) => setVariant(e.target.value)} />
                </div>
                <div className="md:col-span-1">
                    <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('origin')}</label>
                    <input type="text" className="w-full border-gray-300 rounded-md p-2 border" value={origin} onChange={(e) => setOrigin(e.target.value)} />
                </div>
                <div className="md:col-span-1">
                    <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('size')}</label>
                    <input type="text" className="w-full border-gray-300 rounded-md p-2 border" value={size} onChange={(e) => setSize(e.target.value)} />
                </div>
                <div className="md:col-span-1">
                    <label className="block text-xs font-bold text-blue-600 uppercase mb-1">{t('shortcut')}</label>
                    <input type="text" required className="w-full border-blue-200 bg-blue-50 rounded-md p-2 border font-bold text-blue-900" value={shortcut} onChange={(e) => setShortcut(e.target.value)} />
                </div>
                <div className="md:col-span-1 flex items-end">
                    <button type="submit" disabled={loading} className="w-full bg-blue-600 text-white rounded-md py-2 font-bold hover:bg-blue-700 disabled:opacity-50">
                        {loading ? t('saving') : t('save')}
                    </button>
                </div>
            </form>
        </div>
    );
}
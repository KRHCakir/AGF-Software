'use client';

import React, { useState, useEffect } from 'react';
import { ProductService, Product } from '../../../services/productService';
import ShortcutManager from '../../../components/ShortcutManager';
import WeightSettings from '../../../components/products/WeightSettings';
import { isWeightMethod } from '../../../lib/weight';
import { useTranslation } from '../../../i18n/LanguageContext'; // Importeer de vertaler

export default function ProductsPage() {
  const { t } = useTranslation(); // Haal t() op
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);
  const [expandedProductId, setExpandedProductId] = useState<number | null>(null);

  const loadProducts = async () => {
    try {
      const data = await ProductService.getAll();
      setProducts(data);
    } catch (error) {
      console.error('Fout bij laden:', error);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { loadProducts(); }, []);

  const toggleRow = (id: number) => {
    setExpandedProductId(expandedProductId === id ? null : id);
  };

  return (
    <div className="p-8 max-w-7xl mx-auto space-y-10">
      <div>
        <h1 className="text-3xl font-black text-gray-900">{t('products_catalog_title')}</h1>
        <p className="text-gray-500">{t('products_catalog_desc')}</p>
      </div>

      <ShortcutManager onSuccess={loadProducts} />

      <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200 bg-gray-50">
          <h3 className="font-bold text-gray-700">{t('products_overview')}</h3>
        </div>

        <table className="w-full text-left">
          <thead className="bg-gray-100 text-xs uppercase text-gray-500 font-bold border-b border-gray-200">
            <tr>
              <th className="px-6 py-3 w-10"></th>
              <th className="px-6 py-3">{t('main_product')}</th>
              <th className="px-6 py-3 text-right">{t('active_variants')}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {loading ? (
              <tr><td colSpan={3} className="p-10 text-center text-gray-400">{t('loading_data')}</td></tr>
            ) : (
              products.map((p) => (
                <React.Fragment key={p.id}>
                  <tr onClick={() => toggleRow(p.id)} className="hover:bg-blue-50 cursor-pointer transition-colors">
                    <td className="px-6 py-4 text-gray-400">{expandedProductId === p.id ? '▼' : '▶'}</td>
                    <td className="px-6 py-4 font-bold text-gray-900 text-lg">{p.name}</td>
                    <td className="px-6 py-4 text-right text-gray-500 font-medium">
                      {t('variant_count', { count: p.variants.length.toString() })}
                    </td>
                  </tr>

                  {expandedProductId === p.id && (
                    <tr className="bg-gray-50/50">
                      <td colSpan={3} className="px-6 py-6 border-t border-blue-100 border-b-4 border-b-gray-200">
                        <div className="bg-white border border-gray-200 rounded-lg shadow-sm">
                          <table className="w-full text-sm">
                            <thead className="bg-gray-50 border-b border-gray-200 text-gray-500 font-semibold uppercase text-[10px]">
                              <tr>
                                <th className="px-4 py-2">{t('variant')}</th>
                                <th className="px-4 py-2">{t('origin')} / {t('size')}</th>
                                <th className="px-4 py-2">{t('shortcut')}</th>
                                <th className="px-4 py-2">{t('season_info')}</th>
                                <th className="px-4 py-2">{t('products_weight')}</th>
                              </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                              {p.variants.length === 0 ? (
                                <tr><td colSpan={5} className="px-4 py-4 text-center text-gray-400 italic">{t('no_variants')}</td></tr>
                              ) : (
                                p.variants.map((v) => (
                                  <tr key={v.id} className="hover:bg-gray-50">
                                    <td className="px-4 py-3 font-medium text-gray-800">{v.name || '-'}</td>
                                    <td className="px-4 py-3 text-gray-600">
                                      {v.origin || '-'} {v.sizeCategory ? `/ ${v.sizeCategory}` : ''}
                                    </td>
                                    <td className="px-4 py-3">
                                      {v.shortcutCode ? (
                                        <code className="bg-blue-50 text-blue-700 px-2 py-1 rounded font-bold border border-blue-100">{v.shortcutCode}</code>
                                      ) : '-'}
                                    </td>
                                    <td className="px-4 py-3 text-gray-500 text-xs">
                                      {v.season && <div><span className="font-semibold text-gray-700">{t('season_label')}</span> {v.season}</div>}
                                      {v.notes && <div><span className="font-semibold text-gray-700">{t('info_label')}</span> <span className="italic">{v.notes}</span></div>}
                                      {!v.season && !v.notes && <span className="text-gray-300">{t('no_extra_info')}</span>}
                                    </td>
                                    <td className="px-4 py-3">
                                      <WeightSettings variantId={v.id} weightMethod={isWeightMethod(v.weightMethod) ? v.weightMethod : 'Weighed'}
                                        defaultNetKgPerPackage={typeof v.defaultNetKgPerPackage === 'number' ? v.defaultNetKgPerPackage : null} onSaved={loadProducts} />
                                    </td>
                                  </tr>
                                ))
                              )}
                            </tbody>
                          </table>
                        </div>
                      </td>
                    </tr>
                  )}
                </React.Fragment>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
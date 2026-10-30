/**
 * i18n ligero para PCSuite (frontend).
 *
 * Carga los archivos de traducción JSON como módulos estáticos (Vite los
 * procesa directamente), gestiona el idioma actual (persistido en
 * localStorage) y expone una función t(key) para resolver claves de
 * traducción con interpolación simple {{variable}}.
 *
 * No usa dependencias externas.
 */

import esDict from '../locales/es.json?raw';
import enDict from '../locales/en.json?raw';

export type Lang = 'es' | 'en';

interface TranslationDict {
    [key: string]: string | TranslationDict;
}

const DICTS: Record<Lang, TranslationDict> = {
    es: JSON.parse(esDict.replace(/^\uFEFF/, '')) as TranslationDict,
    en: JSON.parse(enDict.replace(/^\uFEFF/, '')) as TranslationDict,
};

let _currentLang: Lang = 'es';
let _dict: TranslationDict = DICTS.es;
const _subscribers = new Set<(lang: Lang) => void>();

/**
 * Resuelve una clave anidada en el diccionario, ej. "sidebar.inicio".
 * Si no existe, devuelve la propia clave como fallback.
 */
function resolve(dict: TranslationDict, key: string): string {
    const parts = key.split('.');
    // Intenta en el diccionario actual; si falta, cae al inglés.
    for (const candidate of [dict, DICTS.en]) {
        let node: unknown = candidate;
        let ok = true;
        for (const part of parts) {
            if (node && typeof node === 'object' && part in (node as Record<string, unknown>)) {
                node = (node as Record<string, unknown>)[part];
            } else {
                ok = false;
                break;
            }
        }
        if (ok && typeof node === 'string') return node;
    }
    return key;
}

/**
 * Interpolación simple: reemplaza {{nombre}} por el valor dado.
 */
function interpolate(text: string, values?: Record<string, string | number>): string {
    if (!values) return text;
    return text.replace(/\{\{(\w+)\}\}/g, (_match, name) => {
        const value = values[name];
        return value !== undefined ? String(value) : `{{${name}}}`;
    });
}

/**
 * Traduce una clave. Ejemplo: t('pages.ai.title') o t('buttons.convertir', { nombre: 'Juan' }).
 */
export function t(key: string, values?: Record<string, string | number>): string {
    const text = resolve(_dict, key);
    return interpolate(text, values);
}

/**
 * Idioma actual.
 */
export function getLang(): Lang {
    return _currentLang;
}

/**
 * Establece el idioma actual, lo persiste en localStorage y notifica a los suscriptores.
 */
export async function setLang(lang: Lang): Promise<void> {
    _currentLang = lang;
    _dict = DICTS[lang];
    try {
        localStorage.setItem('pcsuite.lang', lang);
    } catch {
        // localStorage puede no estar disponible en algunos entornos.
    }
    for (const cb of _subscribers) {
        try {
            cb(lang);
        } catch {
            /* un suscriptor roto no debe romper el cambio de idioma */
        }
    }
}

/**
 * Suscribe un callback que se ejecuta cuando cambia el idioma. Devuelve una función de baja.
 */
export function onLangChange(cb: (lang: Lang) => void): () => void {
    _subscribers.add(cb);
    return () => {
        _subscribers.delete(cb);
    };
}

/**
 * Inicializa el i18n: detecta el idioma guardado o del navegador.
 * Debe llamarse al arrancar la app (en App.tsx o similar).
 * Retorna el idioma resuelto.
 */
export function initI18n(): Lang {
    let stored: Lang | null = null;
    try {
        const raw = localStorage.getItem('pcsuite.lang');
        if (raw === 'es' || raw === 'en') {
            stored = raw;
        }
    } catch {
        // localStorage no disponible
    }

    const browserLang = (typeof navigator !== 'undefined' ? navigator.language : 'es').toLowerCase();
    const detected: Lang = browserLang.startsWith('en') ? 'en' : 'es';

    const lang = stored ?? detected;
    _currentLang = lang;
    _dict = DICTS[lang];
    return lang;
}

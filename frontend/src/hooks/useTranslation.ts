import { useState, useEffect, useCallback } from 'react';
import { getLang, setLang, onLangChange, t, type Lang } from '../lib/i18n';

/**
 * Hook de internacionalización.
 *
 * Ejemplo:
 *   const t = useTranslation();
 *   <h1>{t('pages.ai.title')}</h1>
 *
 * El componente se re-renderiza automáticamente cuando cambia el idioma.
 */
export function useTranslation() {
    const [lang, setLangState] = useState<Lang>(getLang());

    // Re-renderizar cuando cambie el idioma.
    useEffect(() => {
        const unsubscribe = onLangChange((newLang) => {
            setLangState(newLang);
        });
        return unsubscribe;
    }, []);

    const changeLanguage = useCallback(async (newLang: Lang) => {
        await setLang(newLang);
    }, []);

    return {
        t,
        lang,
        setLang: changeLanguage,
        isSpanish: lang === 'es',
        isEnglish: lang === 'en',
    };
}

export type { Lang };

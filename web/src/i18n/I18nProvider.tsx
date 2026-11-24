import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { en } from './en';
import type { TranslationKey } from './en';
import { pt } from './pt';
import { zh } from './zh';
import { I18nContext, LANGUAGE_STORAGE_KEY } from './context';
import type { Lang, TranslateParams } from './context';

const DICTIONARIES: Record<Lang, Record<TranslationKey, string>> = { en, pt, zh };

function isLang(value: string | null): value is Lang {
  return value === 'en' || value === 'pt' || value === 'zh';
}

function readStoredLang(): Lang | null {
  try {
    const stored = window.localStorage.getItem(LANGUAGE_STORAGE_KEY);
    return isLang(stored) ? stored : null;
  } catch {
    return null;
  }
}

function detectLang(): Lang {
  const stored = readStoredLang();
  if (stored) {
    return stored;
  }

  const candidates = navigator.languages?.length ? navigator.languages : [navigator.language];
  for (const tag of candidates) {
    const lower = tag.toLowerCase();
    if (lower.startsWith('pt')) {
      return 'pt';
    }
    if (lower.startsWith('zh')) {
      return 'zh';
    }
  }

  return 'en';
}

function format(template: string, params?: TranslateParams): string {
  if (!params) {
    return template;
  }
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in params ? String(params[name]) : match,
  );
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(() => detectLang());

  useEffect(() => {
    document.documentElement.lang = lang === 'zh' ? 'zh-CN' : lang;
    try {
      window.localStorage.setItem(LANGUAGE_STORAGE_KEY, lang);
    } catch {
      // Storage can be unavailable (private mode); the choice simply is not persisted.
    }
  }, [lang]);

  const setLang = useCallback((next: Lang) => setLangState(next), []);

  const t = useCallback(
    (key: TranslationKey, params?: TranslateParams) => {
      const dictionary = DICTIONARIES[lang] ?? en;
      return format(dictionary[key] ?? en[key], params);
    },
    [lang],
  );

  const value = useMemo(() => ({ lang, setLang, t }), [lang, setLang, t]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

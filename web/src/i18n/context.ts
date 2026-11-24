import { createContext } from 'react';
import type { TranslationKey } from './en';

export type Lang = 'en' | 'pt' | 'zh';

export type TranslateParams = Record<string, string | number>;

export interface I18nValue {
  lang: Lang;
  setLang: (lang: Lang) => void;
  t: (key: TranslationKey, params?: TranslateParams) => string;
}

export interface LanguageOption {
  code: Lang;
  label: string;
  short: string;
}

export const LANGUAGES: LanguageOption[] = [
  { code: 'en', label: 'English', short: 'EN' },
  { code: 'pt', label: 'Português', short: 'PT' },
  { code: 'zh', label: '中文', short: '中文' },
];

export const LANGUAGE_STORAGE_KEY = 'tug-of-war.language.v1';

export const I18nContext = createContext<I18nValue | null>(null);

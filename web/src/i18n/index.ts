export { I18nProvider } from './I18nProvider';
export { useI18n } from './useI18n';
export { LANGUAGES } from './context';
export type { Lang, LanguageOption, TranslateParams } from './context';
export type { TranslationKey } from './en';

/** Card rules shown on the lobby sheet, in display order. */
export const RULES_KEYS = [
  'rules.1',
  'rules.2',
  'rules.3',
  'rules.4',
  'rules.5',
  'rules.6',
  'rules.7',
  'rules.8',
] as const;

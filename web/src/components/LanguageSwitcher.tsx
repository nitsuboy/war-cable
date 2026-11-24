import { LANGUAGES, useI18n } from '../i18n';

export function LanguageSwitcher({ className = '' }: { className?: string }) {
  const { lang, setLang, t } = useI18n();

  return (
    <div
      className={`lang-switch${className ? ` ${className}` : ''}`}
      role="group"
      aria-label={t('language.label')}
    >
      {LANGUAGES.map((option) => (
        <button
          key={option.code}
          type="button"
          className={`lang-option${option.code === lang ? ' active' : ''}`}
          onClick={() => setLang(option.code)}
          aria-pressed={option.code === lang}
          title={option.label}
        >
          {option.short}
        </button>
      ))}
    </div>
  );
}

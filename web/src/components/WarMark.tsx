interface WarMarkProps {
  className?: string;
  muted?: boolean;
}

/** Battle standard: a shield bearing two chevrons. Used for flags and the crest. */
export function WarMark({ className = '', muted = false }: WarMarkProps) {
  return (
    <svg
      className={`war-mark${muted ? ' is-muted' : ''}${className ? ` ${className}` : ''}`}
      viewBox="0 0 64 64"
      aria-hidden="true"
      focusable="false"
    >
      <path
        className="war-halo"
        d="M32 4 56 12v22c0 12-11 22-24 26C19 56 8 46 8 34V12Z"
      />
      <path
        className="war-line"
        d="M32 4 56 12v22c0 12-11 22-24 26C19 56 8 46 8 34V12Z"
      />
      <path className="war-chevron" d="M20 24 32 33l12-9M20 34l12 9 12-9" />
    </svg>
  );
}

interface DragonMarkProps {
  className?: string;
  muted?: boolean;
}

/** Original sigil: a stylised dragon eye with horns. */
export function DragonMark({ className = '', muted = false }: DragonMarkProps) {
  return (
    <svg
      className={`dragon-mark${muted ? ' is-muted' : ''}${className ? ` ${className}` : ''}`}
      viewBox="0 0 64 64"
      aria-hidden="true"
      focusable="false"
    >
      <path
        className="dragon-halo"
        d="M4 32c10-12 22-18 34-18 8 0 15 3 22 9-6 5-9 9-9 9s3 4 9 9c-7 6-14 9-22 9-12 0-24-6-34-18Z"
      />
      <path
        className="dragon-line"
        d="M4 32c10-12 22-18 34-18 8 0 15 3 22 9-6 5-9 9-9 9s3 4 9 9c-7 6-14 9-22 9-12 0-24-6-34-18Z"
      />
      <path className="dragon-horn" d="M43 20 50 8M51 21 58 11" />
      <ellipse className="dragon-eye" cx="30" cy="32" rx="7" ry="11" />
      <ellipse className="dragon-pupil" cx="30" cy="32" rx="2" ry="9" />
    </svg>
  );
}

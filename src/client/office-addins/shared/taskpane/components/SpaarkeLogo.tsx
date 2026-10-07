import React, { useId } from 'react';

/**
 * Props for the SpaarkeLogo component.
 */
export interface SpaarkeLogoProps {
  /** Rendered size (height) in pixels; width preserves the mark's aspect ratio. */
  size?: number;
  /** Additional class name */
  className?: string;
  /** Aria label for accessibility */
  'aria-label'?: string;
}

/**
 * Spaarke brand logo (color mark) — the gradient asterisk + blue dot.
 *
 * Source: projects/email-communication-intelligence-r2/spaarke-color-logo-only.svg.
 * Gradient ids are made unique per render (useId) so multiple instances never collide.
 * viewBox is the artwork's native box; the mark preserves aspect ratio inside `size`.
 */
export const SpaarkeLogo: React.FC<SpaarkeLogoProps> = ({
  size = 24,
  className,
  'aria-label': ariaLabel = 'Spaarke logo',
}) => {
  const uid = useId();
  const g0 = `sprk-logo-${uid}-0`;
  const g1 = `sprk-logo-${uid}-1`;
  const g2 = `sprk-logo-${uid}-2`;

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 2221 1618"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-label={ariaLabel}
      role="img"
      preserveAspectRatio="xMidYMid meet"
    >
      <rect y="767" width="1618" height="85" fill="#FF0000" />
      <rect
        x="1016.11"
        y="1358.1"
        width="1618"
        height="85"
        transform="rotate(-45 1016.11 1358.1)"
        fill={`url(#${g0})`}
      />
      <path d="M1076.11 214L2220.2 1358.1L2160.1 1418.2L1016 274.104L1076.11 214Z" fill={`url(#${g1})`} />
      <rect x="1661.1" width="1618" height="85" transform="rotate(90 1661.1 0)" fill={`url(#${g2})`} />
      <circle cx="1618" cy="809" r="70" fill="#000BFF" />
      <defs>
        <linearGradient id={g0} x1="1016.11" y1="1400.6" x2="2634.11" y2="1400.6" gradientUnits="userSpaceOnUse">
          <stop stopColor="#FFD200" />
          <stop offset="0.5" stopColor="#FF0000" />
          <stop offset="1" stopColor="#FFD200" />
        </linearGradient>
        <linearGradient id={g1} x1="1046.05" y1="244.052" x2="2190.15" y2="1388.15" gradientUnits="userSpaceOnUse">
          <stop stopColor="#FFD200" />
          <stop offset="0.5" stopColor="#FF0000" />
          <stop offset="1" stopColor="#FFD200" />
        </linearGradient>
        <linearGradient id={g2} x1="3279.1" y1="42.5" x2="1661.1" y2="42.5" gradientUnits="userSpaceOnUse">
          <stop stopColor="#FFD200" />
          <stop offset="0.519231" stopColor="#FF0000" />
          <stop offset="1" stopColor="#FFD200" />
        </linearGradient>
      </defs>
    </svg>
  );
};

export default SpaarkeLogo;

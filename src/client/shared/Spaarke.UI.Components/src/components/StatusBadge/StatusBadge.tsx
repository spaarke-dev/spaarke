/**
 * StatusBadge — generic status / severity badge.
 *
 * The ONE legitimately-new UI primitive added by spaarke-ontology-platform-r1
 * task 012 (C-4, spec FR-28/FR-41). Every badge that already existed in the
 * shared libraries before this task was domain-specific — `CitationBadge`
 * (legal-citation verification), `PinnedMemoryProvenanceBadge` (pin source),
 * `ChannelBadge` (email vs message) — and none of them accepts a generic
 * status or tone. Extending one of those would mean teaching (e.g.) a
 * citation badge to render a Signal's status, coupling two unrelated
 * domains. StatusBadge instead takes a plain `label` + `tone`: it has no
 * knowledge of `Signal`, `Policy`, or any other ontology-platform type, so
 * any future consumer (worklist row, grid cell, card) can reuse it without
 * taking a dependency on this project's vocabulary.
 *
 * Tones map onto Fluent v9 `Badge` semantic `color` values — no hex/rgb/named
 * color literal anywhere in this file, so light and dark both render
 * correctly automatically via the host `FluentProvider` theme (ADR-021).
 * An unrecognised tone value falls back to the `neutral` treatment rather
 * than throwing or rendering invisibly.
 *
 * React 19. Context-agnostic (ADR-012) — no PCF or Xrm dependency.
 *
 * Task: spaarke-ontology-platform-r1, task 012 (C-4).
 */

import * as React from 'react';
import { Badge } from '@fluentui/react-components';
import type { BadgeProps } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Generic visual tone for a status/severity badge (`success` added by
 * spaarke-ontology-platform-r1 task 057, D-24). Deliberately NOT a
 * domain vocabulary (no `Signal`, `Policy`, `Disposition`, ...). Callers map
 * their own status or severity values onto one of these five tones.
 */
export type StatusBadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'critical';

const KNOWN_TONES: ReadonlySet<string> = new Set<StatusBadgeTone>([
  'neutral',
  'info',
  'success',
  'warning',
  'critical',
]);

/** Fluent v9 `Badge` semantic color per tone. Semantic names, not literals. */
const TONE_TO_COLOR: Record<StatusBadgeTone, BadgeProps['color']> = {
  neutral: 'subtle',
  info: 'informative',
  success: 'success',
  warning: 'warning',
  critical: 'danger',
};

export interface StatusBadgeProps {
  /** Text shown inside the badge. The caller supplies the exact wording. */
  label: string;
  /**
   * Visual tone. Defaults to `neutral` when omitted or when an unrecognised
   * string is passed — the component never throws on a bad tone value.
   */
  tone?: StatusBadgeTone;
  /** Optional className forwarded to the badge root. */
  className?: string;
  /**
   * Optional accessible label override. Defaults to `label` when omitted.
   */
  ariaLabel?: string;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Resolve an (possibly invalid) tone value to a known tone, defaulting to neutral. */
function resolveTone(tone: StatusBadgeTone | undefined): StatusBadgeTone {
  if (tone && KNOWN_TONES.has(tone)) {
    return tone;
  }
  return 'neutral';
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * StatusBadge — reusable Fluent v9 badge for any generic status or severity
 * value. Takes a `label` + `tone`, nothing domain-specific.
 *
 * @example
 * ```tsx
 * <StatusBadge label="Open" tone="info" />
 * <StatusBadge label="Critical" tone="critical" />
 * <StatusBadge label="Unknown" tone={someUnrecognisedString as StatusBadgeTone} />
 * ```
 */
export const StatusBadge: React.FC<StatusBadgeProps> = ({ label, tone, className, ariaLabel }) => {
  const resolvedTone = resolveTone(tone);
  const color = TONE_TO_COLOR[resolvedTone];

  return (
    <Badge
      appearance="tint"
      color={color}
      size="small"
      className={className}
      data-testid="status-badge"
      data-tone={resolvedTone}
      aria-label={ariaLabel ?? label}
    >
      {label}
    </Badge>
  );
};

StatusBadge.displayName = 'StatusBadge';

export default StatusBadge;

/**
 * PrivacyMarkers.tsx
 *
 * Renders the FR-21 privilege/privacy markers for one `sprk_communication`
 * row as accessible Fluent v9 badges: privilege classification
 * (`sprk_privilegeclassification`), internal-only (`sprk_isinternalonly`), and
 * private (`sprk_isprivate`).
 *
 * DISPLAY ONLY — this component makes NO access decision. It renders exactly
 * the markers the BFF returned on an already-access-filtered row (the client
 * never infers access; NFR-01). A row the caller may not see never reaches
 * this component (the server drops it), so a rendered marker can never imply
 * access anyone lacks. In particular, `isInternalOnly` is only ever `true` on a
 * row a permitted (internal) caller already sees — the BFF's shared access
 * filter drops internal-only rows for external callers.
 *
 * Shared by both conversation render surfaces (`MessageBubble` chat bubbles,
 * `EmailInFlowBlock` email blocks) AND the `MessageRow` timeline row, so the
 * marker vocabulary + a11y labelling stay identical across every surface
 * (§11 — one marker component, not three drifting copies).
 *
 * Fluent v9 only — `Badge` with SEMANTIC `color` tokens (no hardcoded colors);
 * dark mode passes through the host `FluentProvider` (ADR-021). Text-only
 * badges (the text IS the screen-reader label); the badge cluster is a labelled
 * region (NFR-05). Renders `null` when a row carries no markers, so an
 * unmarked message adds no empty DOM.
 */
import * as React from 'react';
import { Badge, makeStyles, tokens } from '@fluentui/react-components';
import { PRIVILEGE_POTENTIALLY_PRIVILEGED, PRIVILEGE_PRIVILEGED } from '../CommunicationTimeline.types';

export interface IPrivacyMarkersProps {
  /** `sprk_privilegeclassification` choice int. None (100000000) renders nothing. */
  privilege: number;
  /** `sprk_isinternalonly` marker (display only). */
  isInternalOnly?: boolean;
  /** `sprk_isprivate` marker (display only). */
  isPrivate?: boolean;
}

const useStyles = makeStyles({
  cluster: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
});

export const MARKER_LABEL_PRIVILEGED = 'Privileged';
export const MARKER_LABEL_POTENTIALLY_PRIVILEGED = 'May be privileged';
export const MARKER_LABEL_INTERNAL_ONLY = 'Internal only';
export const MARKER_LABEL_PRIVATE = 'Private';

export const PrivacyMarkers: React.FC<IPrivacyMarkersProps> = ({ privilege, isInternalOnly, isPrivate }) => {
  const styles = useStyles();

  const privilegeLabel =
    privilege === PRIVILEGE_PRIVILEGED
      ? MARKER_LABEL_PRIVILEGED
      : privilege === PRIVILEGE_POTENTIALLY_PRIVILEGED
        ? MARKER_LABEL_POTENTIALLY_PRIVILEGED
        : null;

  // Nothing to show — render no wrapper (an unmarked, non-privileged row).
  if (!privilegeLabel && !isInternalOnly && !isPrivate) return null;

  return (
    <div className={styles.cluster} role="group" aria-label="Privilege and privacy markers">
      {privilegeLabel && (
        <Badge
          appearance="tint"
          color={privilege === PRIVILEGE_PRIVILEGED ? 'danger' : 'warning'}
          size="small"
          aria-label={privilegeLabel}
        >
          {privilegeLabel}
        </Badge>
      )}
      {isInternalOnly && (
        <Badge appearance="tint" color="informative" size="small" aria-label={MARKER_LABEL_INTERNAL_ONLY}>
          {MARKER_LABEL_INTERNAL_ONLY}
        </Badge>
      )}
      {isPrivate && (
        <Badge appearance="tint" color="important" size="small" aria-label={MARKER_LABEL_PRIVATE}>
          {MARKER_LABEL_PRIVATE}
        </Badge>
      )}
    </div>
  );
};

PrivacyMarkers.displayName = 'PrivacyMarkers';

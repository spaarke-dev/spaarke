/**
 * AssociationChips.tsx
 *
 * Renders `associations[]` as read-only Fluent Tags — e.g. "Matter: Smith v.
 * Jones" (design §5.6.6). Reuses the existing `ICommunicationAssociation`
 * shape from `communicationApi.ts` (rather than inventing a parallel type)
 * since these associations flow straight into `sendCommunication()`
 * unchanged — see task 020 Decisions Made ("no separate IComposerAssociation").
 *
 * When `onRemove` is supplied (owner UAT 2026-07-30, item 10) each chip becomes
 * dismissible (×) so the author can drop a parent-inherited association that
 * doesn't belong on this email. Absent `onRemove` the chips stay read-only.
 */
import * as React from 'react';
import { Tag, TagGroup, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import type { TagGroupProps } from '@fluentui/react-components';
import type { ICommunicationAssociation } from '../../../services/communicationApi';

export interface IAssociationChipsProps {
  associations: ICommunicationAssociation[];
  /** When supplied, chips render a dismiss (×) affordance; called with the association to remove. */
  onRemove?: (association: ICommunicationAssociation) => void;
  /**
   * Index of the PRIMARY (regarding) association — owner UAT 2026-07-30 (item 8). The chip at
   * this index renders GREEN so it reads as the confirmed primary, distinct from any non-primary
   * entry (mirrors the reading-pane resolver's confirmed-primary treatment; green tokens only,
   * dark-mode correct per ADR-021). Omit → no chip is styled as primary.
   */
  primaryIndex?: number;
}

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
  // Confirmed-primary chip: GREEN. Token family matches EmailConnectionsReview.styles.ts (the
  // reading-pane resolver's confirmed-primary chip) — background/border/foreground green tokens
  // only, so both light + dark themes resolve (ADR-021, no hex).
  primaryTag: {
    backgroundColor: tokens.colorPaletteGreenBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorPaletteGreenBorder2}`,
    color: tokens.colorPaletteGreenForeground1,
  },
});

/** Humanizes a Dataverse logical entity name for chip labels (e.g. "sprk_matter" → "Matter"). */
function humanizeEntityType(entityType: string): string {
  const stripped = entityType.startsWith('sprk_') ? entityType.slice(5) : entityType;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

export const AssociationChips: React.FC<IAssociationChipsProps> = ({ associations, onRemove, primaryIndex }) => {
  const styles = useStyles();

  const handleDismiss = React.useCallback<NonNullable<TagGroupProps['onDismiss']>>(
    (_e, data) => {
      if (!onRemove) return;
      const target = associations.find(
        a => `${a.entityType}:${a.entityId}`.toLowerCase() === String(data.value).toLowerCase()
      );
      if (target) onRemove(target);
    },
    [associations, onRemove]
  );

  if (!associations || associations.length === 0) return null;

  return (
    <TagGroup
      className={styles.wrapper}
      role="region"
      aria-label="Linked records"
      onDismiss={onRemove ? handleDismiss : undefined}
    >
      {associations.map((a, index) => {
        const isPrimary = index === primaryIndex;
        return (
          <Tag
            key={`${a.entityType}:${a.entityId}:${index}`}
            value={`${a.entityType}:${a.entityId}`}
            appearance="outline"
            shape="rounded"
            className={isPrimary ? mergeClasses(styles.primaryTag) : undefined}
            data-primary={isPrimary ? 'true' : undefined}
            dismissible={!!onRemove}
            dismissIcon={onRemove ? { 'aria-label': 'Remove' } : undefined}
          >
            {humanizeEntityType(a.entityType)}
            {a.entityName ? `: ${a.entityName}` : ''}
          </Tag>
        );
      })}
    </TagGroup>
  );
};

AssociationChips.displayName = 'AssociationChips';

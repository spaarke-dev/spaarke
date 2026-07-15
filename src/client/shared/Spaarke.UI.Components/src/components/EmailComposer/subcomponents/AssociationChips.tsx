/**
 * AssociationChips.tsx
 *
 * Renders `associations[]` as read-only Fluent Tags — e.g. "Matter: Smith v.
 * Jones" (design §5.6.6). Reuses the existing `ICommunicationAssociation`
 * shape from `communicationApi.ts` (rather than inventing a parallel type)
 * since these associations flow straight into `sendCommunication()`
 * unchanged — see task 020 Decisions Made ("no separate IComposerAssociation").
 *
 * Read-only in R4 (no "Add association" affordance — matches design's
 * explicit "Future: out of scope for R3/R4" note).
 */
import * as React from 'react';
import { Tag, TagGroup, makeStyles, tokens } from '@fluentui/react-components';
import type { ICommunicationAssociation } from '../../../services/communicationApi';

export interface IAssociationChipsProps {
  associations: ICommunicationAssociation[];
}

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
});

/** Humanizes a Dataverse logical entity name for chip labels (e.g. "sprk_matter" → "Matter"). */
function humanizeEntityType(entityType: string): string {
  const stripped = entityType.startsWith('sprk_') ? entityType.slice(5) : entityType;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

export const AssociationChips: React.FC<IAssociationChipsProps> = ({ associations }) => {
  const styles = useStyles();

  if (!associations || associations.length === 0) return null;

  return (
    <TagGroup className={styles.wrapper} role="region" aria-label="Linked records">
      {associations.map((a, index) => (
        <Tag key={`${a.entityType}:${a.entityId}:${index}`} appearance="outline" shape="rounded">
          {humanizeEntityType(a.entityType)}
          {a.entityName ? `: ${a.entityName}` : ''}
        </Tag>
      ))}
    </TagGroup>
  );
};

AssociationChips.displayName = 'AssociationChips';

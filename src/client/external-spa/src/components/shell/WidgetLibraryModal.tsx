/**
 * WidgetLibraryModal — the entitlement-gated "Add widget" library (task 012 §5).
 *
 * Lists ONLY the widgets the caller is entitled to per `/me` (`libraryWidgetsFor`) — an
 * unentitled widget is absent entirely (entitlement-honest, spec FR-02). Anchored to the
 * owner-approved P0 prototype's `WidgetLibraryModal.tsx` (grid of `ActionCard`s inside a
 * `FormModal`, "Added" badge for already-open tabs) — reused here against the production
 * registry + `/me` contract instead of the prototype's mock.
 *
 * Shared-library mandate (CLAUDE.md §11): `FormModal` (SprkModal preset, ADR-050) + `ActionCard`
 * — no hand-rolled dialog. Fluent v9 semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import { makeStyles, tokens, Text, Badge } from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import { ActionCard } from '@spaarke/ui-components/components/WorkspaceShell/ActionCard';
import type { MeEntitlementsResponse } from '../../api/me-client';
import { libraryWidgetsFor } from '../../registry/widgetRegistry';

const useStyles = makeStyles({
  intro: { color: tokens.colorNeutralForeground3, marginBottom: tokens.spacingVerticalS },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr))',
    gap: tokens.spacingHorizontalM,
  },
  cell: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  caption: { color: tokens.colorNeutralForeground3, textAlign: 'center' },
  badgeRow: { display: 'flex', justifyContent: 'center' },
});

export interface WidgetLibraryModalProps {
  open: boolean;
  onClose: () => void;
  me: MeEntitlementsResponse;
  /** Widget ids already open as tabs (rendered "Added", non-addable). */
  openWidgetIds: string[];
  /** Add a widget tab (caller re-checks entitlement — see WorkspaceHomePage.onEntitledOpenWidget). */
  onAdd: (id: string) => void;
}

export const WidgetLibraryModal: React.FC<WidgetLibraryModalProps> = ({ open, onClose, me, openWidgetIds, onAdd }) => {
  const s = useStyles();
  const items = React.useMemo(() => libraryWidgetsFor(me), [me]);
  const openSet = React.useMemo(() => new Set(openWidgetIds), [openWidgetIds]);

  return (
    <FormModal
      open={open}
      onClose={onClose}
      onSubmit={onClose}
      title="Add a widget"
      submitLabel="Done"
      cancelLabel="Close"
    >
      <Text className={s.intro}>You only see widgets you&apos;re entitled to. Pick one to add it as a tab.</Text>
      <div className={s.grid}>
        {items.map(w => {
          const added = openSet.has(w.id);
          return (
            <div key={w.id} className={s.cell}>
              <ActionCard
                icon={w.icon}
                label={w.title}
                ariaLabel={w.ariaLabel}
                disabled={added}
                onClick={added ? undefined : () => onAdd(w.id)}
              />
              {added ? (
                <div className={s.badgeRow}>
                  <Badge appearance="tint" color="success">
                    Added
                  </Badge>
                </div>
              ) : (
                <Text size={100} className={s.caption}>
                  {w.description}
                </Text>
              )}
            </div>
          );
        })}
        {items.length === 0 && <Text className={s.caption}>No widgets are available for your account yet.</Text>}
      </div>
    </FormModal>
  );
};

export default WidgetLibraryModal;

/**
 * CreateOnSaveAssociationPrompt.tsx
 *
 * FR-05 — the optional parent-association picker for create-on-save. Lets the
 * user associate the newly-created `sprk_document` to a matter / project /
 * invoice / work assignment, or explicitly choose "none" (a standalone
 * Document is a valid outcome — Save is NEVER blocked on a parent).
 *
 * IMPORTANT — this is NOT a standalone dialog or banner. Per spec FR-05 /
 * the core `GateDecisionV2` design (`GateAssociationAffordance`), this prompt
 * is meant to be mounted INSIDE the one Tier-2c gate dialog
 * (`ActionConfirmationDialog` in `@spaarke/ui-components`'s SprkChat family).
 * Rendering it as its own modal/overlay would reintroduce the bespoke
 * confirmation-banner friction class Policy v2 explicitly kills. This
 * component therefore renders a plain content section (no Dialog/overlay
 * chrome of its own) so a host dialog can embed it directly.
 *
 * The gate-dialog-HOSTING wiring itself (reading a live `GateDecisionV2` from
 * the BFF and mounting this component inside `ActionConfirmationDialog`) is
 * OUT OF SCOPE for this task — that dialog lives in
 * `@spaarke/ui-components` (`Spaarke.UI.Components`), outside this task's
 * write boundary (`src/solutions/SpaarkeAi/src/components/**` only). See
 * `gateAssociationContract.ts` for the adapter a future host task consumes.
 *
 * Reuses the EXISTING association mechanism (no new association surface,
 * CLAUDE.md §11): the record-type + record-selection UI is the shared
 * `AssociateToStep` (`@spaarke/ui-components`) — the same component used by
 * `CreateMatterWizard` / `CreateProjectWizard` / `CreateInvoiceWizard` /
 * `CreateWorkAssignmentWizard` — mounted UNMODIFIED, scoped to whichever
 * single target type the user picks via the radio choice above it.
 *
 * @see documentAssociationWrite.ts — the write path consuming this component's `onChange` result
 * @see ADR-021 — Fluent v9 + dark mode (tokens only, no hardcoded colors)
 * @see ADR-028 — `@spaarke/auth` for BFF fetches (N/A here — the association
 *      write is a direct client-side Dataverse call via `IDataService`, not a
 *      BFF fetch, so there is no bearer-token concern on this surface)
 */

import * as React from 'react';
import { makeStyles, tokens, Radio, RadioGroup, Text } from '@fluentui/react-components';
import { AssociateToStep } from '@spaarke/ui-components';
import type { AssociationResult, EntityTypeOption, INavigationService } from '@spaarke/ui-components';
import { DOCUMENT_ASSOCIATION_TARGETS, type DocumentAssociationEntityType } from './documentAssociationWrite';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** The five choices FR-05 offers: the four parent types, plus explicit "none". */
export type DocumentAssociationChoice = 'none' | DocumentAssociationEntityType;

export interface ICreateOnSaveAssociationPromptProps {
  /** Navigation service used by `AssociateToStep` to open the Dataverse lookup side pane. */
  navigationService: INavigationService;
  /** Current selection (controlled). `null` means "none" (standalone document). */
  value: AssociationResult | null;
  /** Fired when the selection changes -- a new record, or `null` for "none"/cleared. */
  onChange: (result: AssociationResult | null) => void;
  /**
   * Restricts which of the four parent types are offered. Defaults to all
   * four (`DOCUMENT_ASSOCIATION_TARGETS`). A future gate-dialog host can
   * narrow this via a `GateAssociationAffordance.allowedTargets` restriction
   * (see `gateAssociationContract.ts`).
   */
  allowedTargets?: ReadonlyArray<EntityTypeOption>;
  /**
   * Seeds the initial radio choice on mount only (uncontrolled thereafter --
   * the radio choice tracks `value` once the component is live). Lets a host
   * pre-select a choice (e.g. from a `GateAssociationAffordance.selected`)
   * without forcing full control of the internal radio state.
   */
  initialValue?: AssociationResult | null;
  /** When true, all interactive controls are disabled (e.g. while saving). */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  title: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  subtitle: {
    color: tokens.colorNeutralForeground3,
  },
  choices: {
    display: 'flex',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalS,
  },
  standaloneNote: {
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function _choiceFromEntityType(entityType: string | undefined): DocumentAssociationChoice {
  if (!entityType) return 'none';
  const isKnown = DOCUMENT_ASSOCIATION_TARGETS.some(t => t.entityType === entityType);
  return isKnown ? (entityType as DocumentAssociationEntityType) : 'none';
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Renders the FR-05 optional-association prompt: a "None / Matter / Project /
 * Invoice / Work Assignment" radio choice, and (when a parent type is
 * chosen) the shared `AssociateToStep` record picker scoped to that type.
 *
 * @example
 * ```tsx
 * <CreateOnSaveAssociationPrompt
 *   navigationService={navigationService}
 *   value={association}
 *   onChange={setAssociation}
 * />
 * ```
 */
export const CreateOnSaveAssociationPrompt: React.FC<ICreateOnSaveAssociationPromptProps> = ({
  navigationService,
  value,
  onChange,
  allowedTargets = DOCUMENT_ASSOCIATION_TARGETS,
  initialValue = null,
  disabled = false,
}) => {
  const styles = useStyles();

  const [choice, setChoice] = React.useState<DocumentAssociationChoice>(() =>
    _choiceFromEntityType((value ?? initialValue ?? undefined)?.entityType)
  );

  // Keep the radio choice in sync when `value` changes externally (e.g. the
  // user clicks AssociateToStep's own "Clear" affordance -- value becomes
  // null, but the user is still "in" the chosen record-type tab until they
  // explicitly pick "None").
  const previousEntityType = React.useRef(value?.entityType);
  React.useEffect(() => {
    if (value?.entityType !== previousEntityType.current) {
      previousEntityType.current = value?.entityType;
      if (value?.entityType) {
        setChoice(_choiceFromEntityType(value.entityType));
      }
    }
  }, [value]);

  const handleChoiceChange = React.useCallback(
    (_ev: unknown, data: { value: string }) => {
      const next = data.value as DocumentAssociationChoice;
      setChoice(next);

      if (next === 'none') {
        onChange(null);
      } else if (value && value.entityType !== next) {
        // Switching target type invalidates any prior record selection -- the
        // user must pick a new record for the newly chosen type.
        onChange(null);
      }
    },
    [value, onChange]
  );

  const activeEntityTypes: EntityTypeOption[] = React.useMemo(() => {
    if (choice === 'none') return [];
    const match = allowedTargets.find(t => t.entityType === choice);
    return match ? [match] : [];
  }, [choice, allowedTargets]);

  return (
    <div className={styles.root} data-testid="create-on-save-association-prompt">
      <div className={styles.header}>
        <Text className={styles.title}>Associate to (optional)</Text>
        <Text size={200} className={styles.subtitle}>
          Link the new document to a matter, project, invoice, or work assignment -- or save it as a standalone
          document.
        </Text>
      </div>

      <RadioGroup
        layout="horizontal"
        className={styles.choices}
        value={choice}
        onChange={handleChoiceChange}
        disabled={disabled}
        data-testid="association-choice-radio-group"
      >
        <Radio value="none" label="None (standalone document)" data-testid="association-choice-none" />
        {allowedTargets.map(t => (
          <Radio
            key={t.entityType}
            value={t.entityType}
            label={t.label}
            data-testid={`association-choice-${t.entityType}`}
          />
        ))}
      </RadioGroup>

      {choice === 'none' ? (
        <Text size={200} className={styles.standaloneNote} data-testid="association-standalone-note">
          This document will be saved as a standalone document. You can link it to a parent record later.
        </Text>
      ) : (
        <AssociateToStep
          entityTypes={activeEntityTypes}
          navigationService={navigationService}
          value={value}
          onChange={onChange}
          disabled={disabled}
        />
      )}
    </div>
  );
};

export default CreateOnSaveAssociationPrompt;

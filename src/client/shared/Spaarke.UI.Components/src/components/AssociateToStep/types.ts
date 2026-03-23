/**
 * types.ts
 * Type definitions for the shared AssociateToStep component.
 *
 * @see ADR-012 — Shared Component Library (reusable across all create wizards)
 * @see ADR-021 — Fluent UI v9 design system
 */

import type { INavigationService } from "../../types/serviceInterfaces";

export type { INavigationService };

// ---------------------------------------------------------------------------
// EntityTypeOption
// ---------------------------------------------------------------------------

/**
 * Describes a Dataverse entity type that can be selected as an association target.
 *
 * @example
 * ```typescript
 * const entityTypes: EntityTypeOption[] = [
 *   { label: "Matter",  entityType: "sprk_matter",  defaultViewId: "matter-lookup-view-guid" },
 *   { label: "Project", entityType: "sprk_project" },
 * ];
 * ```
 */
export interface EntityTypeOption {
  /** Human-readable display label shown in the record type dropdown. */
  label: string;
  /** Dataverse logical name of the entity (e.g., "sprk_matter"). */
  entityType: string;
  /**
   * Optional GUID of the default view to display in the Dataverse lookup dialog.
   * When omitted the entity's default lookup view is used.
   */
  defaultViewId?: string;
}

// ---------------------------------------------------------------------------
// AssociationResult
// ---------------------------------------------------------------------------

/**
 * Represents the record selected by the user via the Dataverse lookup dialog.
 * Returned via the `onChange` callback once the user picks a record.
 */
export interface AssociationResult {
  /** Dataverse logical name of the selected record's entity type. */
  entityType: string;
  /** GUID of the selected record (lowercase, no braces). */
  recordId: string;
  /** Display name of the selected record as returned by the lookup dialog. */
  recordName: string;
}

// ---------------------------------------------------------------------------
// AssociateToStepProps
// ---------------------------------------------------------------------------

/**
 * Props for the shared `AssociateToStep` wizard step component.
 *
 * The component renders:
 *   1. A record-type dropdown populated from `entityTypes`
 *   2. A "Select Record" button that triggers `navigationService.openLookup()`
 *   3. A selected-record display card with a Clear action
 *   4. A Skip option for proceeding without association
 *
 * @example Usage in CreateMatterWizard:
 * ```tsx
 * <AssociateToStep
 *   entityTypes={[
 *     { label: "Project", entityType: "sprk_project" },
 *     { label: "Account", entityType: "account" },
 *   ]}
 *   navigationService={navigationService}
 *   value={associationResult}
 *   onChange={setAssociationResult}
 *   onSkip={handleSkip}
 * />
 * ```
 */
export interface AssociateToStepProps {
  /**
   * Available record types the user can associate with.
   * Rendered as options in the record type dropdown.
   * Must contain at least one entry.
   */
  entityTypes: EntityTypeOption[];

  /**
   * Navigation service used to open the Dataverse lookup side pane.
   * Typically injected by the consuming wizard from a PCF or Code Page adapter.
   */
  navigationService: INavigationService;

  /**
   * Current selection (controlled component).
   * `null` means no association selected; `undefined` also treated as no selection.
   */
  value?: AssociationResult | null;

  /**
   * Called when the association changes:
   * - A new record is selected → receives `AssociationResult`
   * - The user clears a selection → receives `null`
   */
  onChange?: (result: AssociationResult | null) => void;

  /**
   * Called when the user explicitly clicks "Skip" to proceed without linking a record.
   * The consumer is responsible for advancing the wizard step.
   */
  onSkip?: () => void;

  /**
   * When `true`, all interactive controls are disabled.
   * Useful while the wizard is in a loading or submitting state.
   */
  disabled?: boolean;
}

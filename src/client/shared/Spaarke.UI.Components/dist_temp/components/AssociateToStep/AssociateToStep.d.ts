/**
 * AssociateToStep.tsx
 * Shared wizard step for optionally associating a new record with an existing parent.
 *
 * Layout:
 *   ┌────────────────────────────────────────────────────────────────────┐
 *   │  Associate To                                                      │
 *   │  Link this record to an existing record.                           │
 *   │                                                                    │
 *   │  Record Type:  [ Matter  ▼ ]      [ Select Record 🔍 ]           │
 *   │                                                                    │
 *   │  ┌────────────────────────────────────────────────────────┐       │
 *   │  │  ✅ Smith v. Jones (MAT-2024-001)       [ ✕ Clear ]  │       │
 *   │  └────────────────────────────────────────────────────────┘       │
 *   │                                                                    │
 *   │  You can always link records later.                                │
 *   └────────────────────────────────────────────────────────────────────┘
 *
 * The component is fully controlled: the caller owns `value` / `onChange`.
 * When the user clicks "Select Record", the Dataverse lookup side pane is
 * opened via `INavigationService.openLookup()`.  When "Skip" is clicked,
 * `onSkip()` is invoked so the wizard can advance the step index.
 *
 * @see ADR-012 — Shared Component Library (reusable across all create wizards)
 * @see ADR-021 — Fluent UI v9 design system; semantic tokens only
 */
import * as React from "react";
import type { AssociateToStepProps } from "./types";
/**
 * Renders a wizard step that lets the user optionally associate the record
 * being created with an existing Dataverse record via a lookup dialog.
 *
 * @example
 * ```tsx
 * <AssociateToStep
 *   entityTypes={[
 *     { label: "Matter",  entityType: "sprk_matter" },
 *     { label: "Project", entityType: "sprk_project" },
 *   ]}
 *   navigationService={navigationService}
 *   value={association}
 *   onChange={setAssociation}
 *   onSkip={handleSkip}
 * />
 * ```
 */
export declare const AssociateToStep: React.FC<AssociateToStepProps>;
//# sourceMappingURL=AssociateToStep.d.ts.map
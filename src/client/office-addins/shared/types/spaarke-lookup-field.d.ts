/**
 * Ambient TYPE shim for `@spaarke/ui-components/lookup-field` (spaarkeai-word-add-in-r1 task 100).
 *
 * RUNTIME: the specifier resolves to the REAL shared source,
 * `src/client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx` — the host-agnostic
 * search-as-you-type lookup the twelve `Create*Wizard` steps use (`onSearch` is injected; its import closure is
 * Fluent v9, react-icons, `types/LookupTypes.ts` and `theme/scrollbar.ts` — no Xrm, no host) — through exact aliases
 * in `webpack.config.js` (bundle) and `jest.config.js` (tests). Never the `@spaarke/ui-components` barrel (owner
 * decision C / ADR-012 as amended 2026-10-05: reuse by exact path).
 *
 * TYPES ONLY, for the same reason as `spaarke-send-email-pane.d.ts`: this package's stricter compiler options
 * (`noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`) flag the shared source (e.g. `results[highlightedIndex]`),
 * and CI's production typecheck would count that as this package's debt. `CreateRecordForm.test.tsx` renders the
 * REAL component through the alias, so a prop named here that the component no longer honours fails a test.
 *
 * SCOPE: a strict SUBSET of `ILookupFieldProps`, copied verbatim — only what the "+ New" form passes.
 */
declare module '@spaarke/ui-components/lookup-field' {
  import type * as React from 'react';

  /** `types/LookupTypes.ts` — `ILookupItem`. */
  export interface ILookupItem {
    /** Unique identifier (e.g., Dataverse GUID). */
    id: string;
    /** Display name (e.g., "John Smith (john@example.com)"). */
    name: string;
    /** The record's email address as a first-class field, when the source search populates it. */
    email?: string;
    entityType?: 'contact' | 'systemuser';
  }

  /** `LookupField.tsx` — `ILookupFieldProps` (the subset the "+ New" form passes). */
  export interface ILookupFieldProps {
    /** Field label displayed above the input. */
    label: string;
    /** Whether the field is required. */
    required?: boolean;
    /** Placeholder text for the search input. */
    placeholder?: string;
    /** Currently selected lookup item (or null). */
    value: ILookupItem | null;
    /** Called when the user selects or clears an item. */
    onChange: (item: ILookupItem | null) => void;
    /** Async search function — called with the query string, returns results. */
    onSearch: (query: string) => Promise<ILookupItem[]>;
    /** Minimum characters before search fires. Default: 1. */
    minSearchLength?: number;
  }

  export const LookupField: React.FC<ILookupFieldProps>;
}

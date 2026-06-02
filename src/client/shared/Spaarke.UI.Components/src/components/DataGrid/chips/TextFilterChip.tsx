/**
 * TextFilterChip — Fluent v9 "contains"-semantics text filter chip.
 *
 * Renders a compact trigger button labelled either with `label` (no value)
 * or the active filter value in quotes (e.g. `"Acme"`). Clicking opens a
 * Fluent v9 `<Popover>` containing a single `<Input>` and a Clear button.
 *
 * Emits `string | undefined` to the parent:
 *   - non-empty string → operator binds it as FetchXML
 *     `<condition attribute="X" operator="like" value="%TYPED%" />`
 *     (the chip itself does NOT build FetchXML — that's the caller's job;
 *     this keeps the chip context-agnostic per ADR-012).
 *   - `undefined` → filter cleared.
 *
 * **Spec**: projects/spaarke-datagrid-framework-r1/spec.md FR-DG-07 + FR-DG-13
 * **Design**: design.md §6.4 (filter chip primitives)
 * **ADRs**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe)
 *
 * **NFR-03 compliance**: Popover content re-wrapped in
 * `<FluentProvider applyStylesToPortals theme={...}>`.
 *
 * **NFR-02 compliance**: zero raw hex; all colors via `tokens.*` and
 * `dataGridTokens`.
 *
 * **React-16-safe**: no `useId`, no `useSyncExternalStore`, no `createRoot`.
 */

import * as React from 'react';
import {
  Button,
  Input,
  Field,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  FluentProvider,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { ChevronDownRegular, SearchRegular } from '@fluentui/react-icons';

import { dataGridTokens } from '../tokens';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for {@link TextFilterChip}.
 *
 * Fully controlled: pass `value === undefined` for "no filter". The chip
 * holds NO internal value state outside its open/close + draft text state.
 */
export interface TextFilterChipProps {
  /** Currently applied filter text, or `undefined` when no filter is set. */
  value: string | undefined;

  /**
   * Fires when the user clicks Apply on a non-empty string OR clears the
   * filter. Empty input on Apply is normalised to `undefined`.
   */
  onChange: (next: string | undefined) => void;

  /** Chip trigger label when `value === undefined` (e.g. `"Name contains"`). */
  label: string;

  /**
   * OPTIONAL — placeholder shown inside the `<Input>` while empty.
   * Defaults to `"Type to filter..."`.
   */
  placeholder?: string;

  /** OPTIONAL — additional class merged AFTER component classes. */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (module scope per ADR-021)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** Trigger wrapper — parent owns gap between chips. */
  root: {
    display: 'inline-flex',
    alignItems: 'center',
  },
  trigger: {
    fontSize: dataGridTokens.filterChip.fontSize,
  },
  surface: {
    minWidth: '240px',
    maxWidth: '320px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalS),
  },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const TextFilterChip: React.FC<TextFilterChipProps> = ({
  value,
  onChange,
  label,
  placeholder,
  className,
}) => {
  const styles = useStyles();

  // Open/close + draft text state
  const [open, setOpen] = React.useState<boolean>(false);
  const [draft, setDraft] = React.useState<string>('');

  // Hydrate draft from `value` on open.
  const handleOpenChange = React.useCallback(
    (_event: unknown, data: { open: boolean }) => {
      if (data.open) {
        setDraft(value ?? '');
      }
      setOpen(data.open);
    },
    [value],
  );

  const handleInputChange: React.ChangeEventHandler<HTMLInputElement> = React.useCallback(
    (ev) => setDraft(ev.target.value),
    [],
  );

  const handleApply = React.useCallback(() => {
    const trimmed = draft.trim();
    onChange(trimmed === '' ? undefined : trimmed);
    setOpen(false);
  }, [draft, onChange]);

  const handleClear = React.useCallback(() => {
    setDraft('');
    onChange(undefined);
    setOpen(false);
  }, [onChange]);

  // Enter submits (matches typical filter-bar UX).
  const handleKeyDown: React.KeyboardEventHandler<HTMLInputElement> = React.useCallback(
    (ev) => {
      if (ev.key === 'Enter') {
        ev.preventDefault();
        handleApply();
      }
    },
    [handleApply],
  );

  const triggerLabel: string = value ? `"${value}"` : label;

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="text-filter-chip">
      <Popover
        open={open}
        onOpenChange={handleOpenChange}
        trapFocus
        positioning="below-start"
      >
        <PopoverTrigger disableButtonEnhancement>
          <Button
            className={styles.trigger}
            appearance="subtle"
            iconPosition="before"
            icon={<SearchRegular aria-hidden="true" />}
            aria-label={value ? `${label}: ${value}` : label}
            data-testid="text-filter-chip-trigger"
          >
            {triggerLabel}
            <ChevronDownRegular aria-hidden="true" />
          </Button>
        </PopoverTrigger>

        <PopoverSurface>
          {/*
            NFR-03 portal-theme re-wrap — same rationale as
            LookupMultiFilterChip and DateRangeFilterChip. Theme is inherited
            via React context (NOT passed explicitly) so the customer-tenant
            theme is never accidentally shadowed.
            See .claude/patterns/ui/fluent-v9-portal-gotcha.md Option A.
          */}
          <FluentProvider applyStylesToPortals>
            <div className={styles.surface}>
              <Field label={label}>
                <Input
                  value={draft}
                  onChange={handleInputChange}
                  onKeyDown={handleKeyDown}
                  placeholder={placeholder ?? 'Type to filter...'}
                  aria-label={label}
                  data-testid="text-filter-chip-input"
                />
              </Field>
              <div className={styles.footer}>
                <Button
                  appearance="subtle"
                  size="small"
                  onClick={handleClear}
                  aria-label={`Clear ${label} filter`}
                  data-testid="text-filter-chip-clear"
                >
                  Clear
                </Button>
                <Button
                  appearance="primary"
                  size="small"
                  onClick={handleApply}
                  aria-label={`Apply ${label} filter`}
                  data-testid="text-filter-chip-apply"
                >
                  Apply
                </Button>
              </div>
            </div>
          </FluentProvider>
        </PopoverSurface>
      </Popover>
    </div>
  );
};

export default TextFilterChip;

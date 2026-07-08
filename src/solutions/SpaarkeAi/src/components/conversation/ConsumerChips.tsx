/**
 * ConsumerChips — the Click-path next-step chip strip (FR-P1-04 / ADR-039).
 *
 * ai-architecture-redesign-r1 task 023. Renders the chips a completed Binding
 * declared via `sprk_chiptransitions` (delivered by the task-022 server chip
 * SSE contract). Each chip CARRIES its `binding_id` — clicking one calls the
 * ONE shared `dispatchConsumer(bindingId, args)` helper. The chip label is
 * presentation only; the client never re-detects intent from it (ADR-039 D4).
 *
 * Empty-attachments Click precondition (task 025 handoff): a chip whose
 * target capability requires attachments renders DISABLED (with a tooltip
 * explaining why) when the session has zero attachments — it cannot dispatch.
 * The `dispatchConsumer` helper enforces the same guard defensively.
 *
 * ADR-021: Fluent UI v9 design tokens ONLY — no hardcoded colors; correct in
 * dark mode (brand-tinted subtle chip treatment via semantic tokens).
 */

import * as React from "react";
import {
  Button,
  Tooltip,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import type { ConsumerChip } from "@spaarke/ui-components";

const useStyles = makeStyles({
  strip: {
    display: "flex",
    flexWrap: "wrap",
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  chip: {
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
    ...shorthands.borderColor(tokens.colorBrandStroke2),
    fontWeight: tokens.fontWeightRegular,
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
      color: tokens.colorBrandForeground2Hover,
    },
    ":hover:active": {
      backgroundColor: tokens.colorBrandBackground2Pressed,
      color: tokens.colorBrandForeground2Pressed,
    },
    ":disabled": {
      backgroundColor: tokens.colorNeutralBackgroundDisabled,
      color: tokens.colorNeutralForegroundDisabled,
      ...shorthands.borderColor(tokens.colorNeutralStrokeDisabled),
    },
  },
});

export interface ConsumerChipsProps {
  /** Chips to render (from the Binding row's chip transitions). Empty → renders nothing. */
  readonly chips: ReadonlyArray<ConsumerChip>;
  /**
   * Current session attachment count. Chips with `requiresAttachments` are
   * disabled when this is zero (empty-attachments Click precondition).
   */
  readonly attachmentCount: number;
  /** Disables the whole strip (e.g. while a dispatch is already in flight). */
  readonly disabled?: boolean;
  /** Chip click → the host calls dispatchConsumer(chip.bindingId, args). */
  readonly onChipClick: (chip: ConsumerChip) => void;
}

/**
 * Curated next-step chips. Stateless — the host owns the chip list and the
 * dispatch (single-responsibility: this component ONLY renders + guards).
 */
export function ConsumerChips(props: ConsumerChipsProps): React.JSX.Element | null {
  const { chips, attachmentCount, disabled, onChipClick } = props;
  const styles = useStyles();

  if (chips.length === 0) {
    return null;
  }

  return (
    <div
      className={styles.strip}
      role="group"
      aria-label="Suggested next steps"
      data-testid="consumer-chips"
    >
      {chips.map((chip) => {
        const blockedByAttachments =
          chip.requiresAttachments === true && attachmentCount === 0;
        const isDisabled = disabled === true || blockedByAttachments;

        const button = (
          <Button
            key={chip.bindingId}
            className={styles.chip}
            appearance="secondary"
            size="small"
            shape="circular"
            disabled={isDisabled}
            onClick={() => onChipClick(chip)}
            aria-label={
              blockedByAttachments
                ? `${chip.label} (requires an attached file)`
                : chip.label
            }
            data-testid={`consumer-chip-${chip.bindingId}`}
            data-binding-id={chip.bindingId}
          >
            {chip.label}
          </Button>
        );

        // Explain WHY a chip is disabled when the empty-attachments guard
        // trips — discoverability without a dead-feeling control.
        return blockedByAttachments ? (
          <Tooltip
            key={chip.bindingId}
            content="Attach a file first — this action needs at least one attachment."
            relationship="description"
          >
            {button}
          </Tooltip>
        ) : (
          button
        );
      })}
    </div>
  );
}

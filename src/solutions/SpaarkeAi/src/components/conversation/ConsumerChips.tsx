/**
 * ConsumerChips — the Click-path Suggested Next Steps (SNS) card row
 * (FR-P1-04 / ADR-039; upgraded from a flat pill strip to ranked actionable
 * cards by task 043 / FR-G1).
 *
 * ai-architecture-redesign-r1 task 023. Renders the chips a completed Binding
 * declared via `sprk_chiptransitions` (delivered by the task-022 server chip
 * SSE contract) as ranked, actionable cards. Each card CARRIES its
 * `binding_id` — clicking one calls the ONE shared `dispatchConsumer(bindingId,
 * args)` helper. The chip label is presentation only; the client never
 * re-detects intent from it (ADR-039 D4). Display ORDER may be biased by a
 * deterministic, preference-keyed reorder applied by the caller (see
 * `chipDisplayOrder.ts`) BEFORE the chips reach this component — this
 * component itself does no reordering; it renders whatever order it is given.
 *
 * Empty-attachments Click precondition (task 025 handoff): a chip whose
 * target capability requires attachments renders DISABLED (with a tooltip
 * explaining why) when the session has zero attachments — it cannot dispatch.
 * The `dispatchConsumer` helper enforces the same guard defensively.
 *
 * task 043 / FR-G1: an optional trailing "More" card (`onMore`) opens the
 * existing playbook/capability library modal (`usePlaybookOptions.
 * handleOpenLibraryModal` — the ONLY library-modal surface in the codebase;
 * no parallel "NBA library" is introduced here). Test ids are preserved
 * verbatim (`consumer-chips` / `consumer-chip-{bindingId}`) across the
 * pill→card visual upgrade so the existing Click-path test suite
 * (`ConsumerChips.test.tsx`, `ConversationPane.consumer-chips.test.tsx`,
 * `ConversationPane.event-path.test.tsx`) keeps passing unmodified.
 *
 * ADR-021: Fluent UI v9 design tokens ONLY — no hardcoded colors; correct in
 * dark mode (bordered card treatment via semantic tokens).
 */

import * as React from "react";
import {
  Button,
  Tooltip,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import { ArrowRightRegular, MoreHorizontalRegular } from "@fluentui/react-icons";
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
  // Ranked actionable CARD (task 043): a bordered, rounded surface (not the
  // former circular pill) so each next-step reads as a discrete card in a
  // grid rather than a flat tag strip. Tokens only (ADR-021) — dark-mode
  // adapts automatically.
  chip: {
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorBrandForeground2,
    ...shorthands.border("1px", "solid", tokens.colorBrandStroke2),
    ...shorthands.padding(tokens.spacingVerticalSNudge, tokens.spacingHorizontalM),
    fontWeight: tokens.fontWeightRegular,
    boxShadow: tokens.shadow2,
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
      color: tokens.colorBrandForeground2Hover,
      boxShadow: tokens.shadow4,
    },
    ":hover:active": {
      backgroundColor: tokens.colorBrandBackground2Pressed,
      color: tokens.colorBrandForeground2Pressed,
    },
    ":disabled": {
      backgroundColor: tokens.colorNeutralBackgroundDisabled,
      color: tokens.colorNeutralForegroundDisabled,
      boxShadow: "none",
      ...shorthands.borderColor(tokens.colorNeutralStrokeDisabled),
    },
  },
  // "More" affordance (task 043 FR-G1): a visually distinct trailing card
  // that opens the existing playbook/capability library modal.
  moreChip: {
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    ...shorthands.border("1px", "dashed", tokens.colorNeutralStroke2),
    ...shorthands.padding(tokens.spacingVerticalSNudge, tokens.spacingHorizontalM),
    fontWeight: tokens.fontWeightRegular,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      color: tokens.colorNeutralForeground2Hover,
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
  /**
   * task 043 / FR-G1: renders a trailing "More" card when supplied, wired by
   * the host to the EXISTING playbook/capability library modal
   * (`usePlaybookOptions.handleOpenLibraryModal` — no new modal surface).
   * Omitted → no "More" card (e.g. standalone/unit-test rendering).
   */
  readonly onMore?: () => void;
}

/**
 * Curated next-step chips rendered as ranked actionable CARDS (task 043).
 * Stateless — the host owns the chip list (already reordered for display, if
 * at all — see `chipDisplayOrder.ts`) and the dispatch (single-responsibility:
 * this component ONLY renders + guards).
 */
export function ConsumerChips(props: ConsumerChipsProps): React.JSX.Element | null {
  const { chips, attachmentCount, disabled, onChipClick, onMore } = props;
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
            shape="rounded"
            icon={<ArrowRightRegular />}
            iconPosition="after"
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

      {onMore && (
        <Button
          className={styles.moreChip}
          appearance="subtle"
          size="small"
          shape="rounded"
          icon={<MoreHorizontalRegular />}
          iconPosition="after"
          onClick={onMore}
          aria-label="More suggestions — open the capability library"
          data-testid="consumer-chips-more"
        >
          More
        </Button>
      )}
    </div>
  );
}

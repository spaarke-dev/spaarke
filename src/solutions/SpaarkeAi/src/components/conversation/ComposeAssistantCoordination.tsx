/**
 * ComposeAssistantCoordination — the `conversation`-channel leg of the Compose
 * three-pane coordination layer (spaarkeai-compose-r2 task 104, E2E-R5).
 *
 * The Assistant pane OWNS the reaction for the two Compose flows whose
 * destination is the Assistant:
 *   - Flow 2 `compose_selection_offer` (Workspace → Assistant): the editor
 *     selection settled → the Assistant surfaces that playbook actions are
 *     available for that selection.
 *   - Flow 4 `compose_context_offer`  (Context → Assistant): a Context-pane
 *     precedent is staged as an input for the next Compose action.
 *
 * These reactions used to sit as log-only handlers on the WRONG pane
 * (ComposeWorkspace); task 104 relocates them here (gap 5.1). Both events are
 * TYPED discriminants on the `conversation` channel (ADR-030) — no `as any`.
 *
 * This is a small dedicated receiver component (rather than inline hooks in the
 * thin ConversationPane host) so it can be MOUNTED + REACHABLE-tested against a
 * REAL PaneEventBus in isolation (project E2E DoD row 3) — the exact component
 * ConversationPane renders is the one the forcing-function test exercises.
 *
 * Renders nothing until a compose flow fires; the affordances are read-only
 * status chips (Fluent v9 tokens — ADR-021, dark-mode safe). Clicking them is a
 * follow-up UI task (this task closes the reception + reachability gap; the
 * action wiring lands with the FR-14 toolbar hand-off).
 *
 * @see @spaarke/ai-widgets PaneEventTypes.ts — ConversationPaneEvent compose fields
 * @see ContextPaneController.tsx — the CONTEXT-leg counterpart (Flows 1 + 6)
 */

import * as React from "react";
import { makeStyles, shorthands, tokens, Text } from "@fluentui/react-components";
import { usePaneEvent } from "@spaarke/ai-widgets";
import type { ConversationPaneEvent } from "@spaarke/ai-widgets";

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground3,
  },
  label: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
});

/**
 * Assistant-pane receiver for Compose Flows 2 + 4. Returns `null` until a flow
 * fires.
 */
export function ComposeAssistantCoordination(): React.JSX.Element | null {
  const styles = useStyles();

  // Flow 2 — a short label for the selection the Assistant can act on.
  const [selectionOffer, setSelectionOffer] = React.useState<string | null>(null);
  // Flow 4 — the id of the precedent staged as the next action's input.
  const [stagedContext, setStagedContext] = React.useState<string | null>(null);

  usePaneEvent(
    "conversation",
    React.useCallback((event: ConversationPaneEvent): void => {
      if (event.type === "compose_selection_offer") {
        // Prefer the human-readable clause label; fall back to a short preview.
        const sel = event.selection;
        const label = sel?.contextLabel ?? (sel?.selectionText ? sel.selectionText.slice(0, 60) : "");
        setSelectionOffer(label.length > 0 ? label : "the selected text");
        return;
      }
      if (event.type === "compose_context_offer") {
        // Identifier only (Tier-1); the precedent content is not surfaced here.
        setStagedContext(event.sourceClauseId ?? "precedent");
      }
    }, [])
  );

  if (selectionOffer === null && stagedContext === null) {
    return null;
  }

  return (
    <div className={styles.root} data-testid="assistant-compose-coordination">
      {selectionOffer !== null && (
        <div data-testid="assistant-compose-selection-offer">
          <Text size={200} className={styles.label}>
            Actions available for{" "}
          </Text>
          <Text size={200}>{selectionOffer}</Text>
        </div>
      )}
      {stagedContext !== null && (
        <div data-testid="assistant-compose-staged-context" data-clause-id={stagedContext}>
          <Text size={200} className={styles.label}>
            Precedent staged:{" "}
          </Text>
          <Text size={200}>{stagedContext}</Text>
        </div>
      )}
    </div>
  );
}

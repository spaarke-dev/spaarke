/**
 * AssistantToolMenu.tsx — Assistant pane "Tools" drop-down (task 040 / FR-F1).
 *
 * Mirrors the existing pane-menu pattern established by `WorkspacePaneMenu`
 * ("Workspace ▾") and `ContextPaneMenu` ("Tools ▾") — a Fluent v9 `<Menu>`
 * with a subtle `<Button>` trigger (`ChevronDownRegular`, iconPosition
 * "after"), `MenuPopover` + `MenuList` + `MenuGroupHeader` + `MenuItem`s.
 * Per CLAUDE.md §11 (component justification / default to reuse), this is
 * the SAME menu idiom as those two siblings — no parallel menu
 * implementation is introduced here.
 *
 * Scope (task 040 — self-contained):
 *   This task delivers the drop-down CONTAINER + its two entries only. The
 *   entries' real behavior is out of scope and lands in follow-on tasks:
 *     - "Quick Start"   → task 041 (FR-F2): opens a modal presenting the
 *       existing `Create*` wizard library (reuse `GetStartedCardsWidget`).
 *     - "My Assistant"  → task 042 (FR-F3): opens the stated-profile
 *       questionnaire (writes `sprk_userprofile` + seeds a User-scope
 *       `MemoryItem`).
 *   Both entries call handler props (`onQuickStart` / `onMyAssistant`);
 *   when the host does not supply one, a console-only placeholder runs
 *   instead (see the `TODO(041)` / `TODO(042)` markers below) so the
 *   drop-down is fully interactive ahead of those tasks.
 *
 * Mounting:
 *   Rendered in `ConversationPane`'s `<PaneHeader rightSlot>`, alongside the
 *   existing "New session" button and `HistoryMenu` ("History ▾") — a
 *   second, independent Menu trigger in the same rightSlot region (History
 *   lists past sessions; this lists Assistant tools). The PaneHeader
 *   rightSlot wrapper already applies `stopPropagation` on clicks (task 094)
 *   so opening this menu never collapses the pane. Purely additive: the
 *   three-pane layout / pane-width fractions are untouched.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local component (mirrors sibling pane menus).
 *   - ADR-021: Fluent v9 semantic tokens only — no hex / rgba literals;
 *     dark-mode adapts automatically via `tokens.*`.
 *   - ADR-022: React 19, functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *
 * @see ContextPaneMenu.tsx — sibling pattern this mirrors (task 095)
 * @see WorkspacePaneMenu.tsx — sibling pattern this mirrors (task 089/098)
 * @see ConversationPane.tsx — mounts this in the Assistant PaneHeader rightSlot
 */

import * as React from "react";
import {
  makeStyles,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuGroupHeader,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import {
  ChevronDownRegular,
  RocketRegular,
  PersonRegular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AssistantToolMenuProps {
  /**
   * Called when the user selects "Quick Start" from the drop-down.
   *
   * TODO(041): wire to the Quick Start modal launcher (FR-F2) — a modal
   * presenting the existing `Create*` wizard library (reuse
   * `GetStartedCardsWidget`). Until task 041 lands, an omitted prop falls
   * back to a console-only placeholder (see `defaultQuickStartHandler`).
   */
  onQuickStart?: () => void;

  /**
   * Called when the user selects "My Assistant" from the drop-down.
   *
   * TODO(042): wire to the My Assistant questionnaire launcher (FR-F3) — it
   * writes the stated profile (`sprk_userprofile` typed columns, FR-E1) and
   * seeds a User-scope `MemoryItem` (`source=user`). Until task 042 lands,
   * an omitted prop falls back to a console-only placeholder (see
   * `defaultMyAssistantHandler`).
   */
  onMyAssistant?: () => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: "auto",
  },
});

// ---------------------------------------------------------------------------
// Placeholder handlers — replaced by task 041 (Quick Start) / 042 (My Assistant)
// ---------------------------------------------------------------------------

// TODO(041): remove this placeholder once the Quick Start modal launcher
// (FR-F2) is wired via the `onQuickStart` prop.
function defaultQuickStartHandler(): void {
  console.log(
    "[AssistantToolMenu] Quick Start selected — placeholder handler; task 041 wires the real modal launcher (FR-F2).",
  );
}

// TODO(042): remove this placeholder once the My Assistant questionnaire
// launcher (FR-F3) is wired via the `onMyAssistant` prop.
function defaultMyAssistantHandler(): void {
  console.log(
    "[AssistantToolMenu] My Assistant selected — placeholder handler; task 042 wires the real questionnaire launcher (FR-F3).",
  );
}

// ---------------------------------------------------------------------------
// Tool catalog
// ---------------------------------------------------------------------------

interface AssistantToolEntry {
  id: "quick-start" | "my-assistant";
  label: string;
}

const ASSISTANT_TOOLS: readonly AssistantToolEntry[] = [
  { id: "quick-start", label: "Quick Start" },
  { id: "my-assistant", label: "My Assistant" },
];

// ---------------------------------------------------------------------------
// AssistantToolMenu component
// ---------------------------------------------------------------------------

/**
 * `AssistantToolMenu` — Fluent v9 Menu rendered in the Assistant pane's
 * `<PaneHeader rightSlot>`. See file header for full design rationale.
 */
export const AssistantToolMenu: React.FC<AssistantToolMenuProps> = ({
  onQuickStart = defaultQuickStartHandler,
  onMyAssistant = defaultMyAssistantHandler,
}) => {
  const styles = useStyles();
  const [menuOpen, setMenuOpen] = React.useState(false);

  const handleOpenChange = React.useCallback(
    (_e: unknown, data: { open: boolean }) => {
      setMenuOpen(data.open);
    },
    [],
  );

  const handleSelect = React.useCallback(
    (id: AssistantToolEntry["id"]) => {
      setMenuOpen(false);
      if (id === "quick-start") {
        onQuickStart();
      } else {
        onMyAssistant();
      }
    },
    [onQuickStart, onMyAssistant],
  );

  return (
    <Menu open={menuOpen} onOpenChange={handleOpenChange} positioning="below-end">
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content="Open Assistant tools menu" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronDownRegular />}
            iconPosition="after"
            aria-label="Open Assistant tools menu"
            className={styles.trigger}
            data-testid="assistant-tool-menu-trigger"
          >
            Tools
          </Button>
        </Tooltip>
      </MenuTrigger>

      <MenuPopover data-testid="assistant-tool-menu-popover">
        <MenuList>
          <MenuGroupHeader>Assistant Tools</MenuGroupHeader>
          {ASSISTANT_TOOLS.map((tool) => (
            <MenuItem
              key={tool.id}
              icon={tool.id === "quick-start" ? <RocketRegular /> : <PersonRegular />}
              onClick={() => handleSelect(tool.id)}
              data-testid={`assistant-tool-${tool.id}`}
            >
              {tool.label}
            </MenuItem>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

AssistantToolMenu.displayName = "AssistantToolMenu";

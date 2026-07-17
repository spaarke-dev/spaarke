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
 * Scope (task 040 delivered the CONTAINER + two entries; task 041 wires
 * "Quick Start"; "My Assistant" is wired by the host per task 042):
 *     - "Quick Start"   → task 041 (FR-F2): this component now OWNS the
 *       `QuickStartModal` (a thin host over the existing `GetStartedCardsWidget`
 *       wizard library) and opens it directly when `onQuickStart` is NOT
 *       supplied by the host — which is the current production wiring
 *       (`ConversationPane.tsx` mounts `<AssistantToolMenu onMyAssistant={...} />`
 *       with no `onQuickStart` override). An explicit `onQuickStart` prop is
 *       still honored (back-compat / test override) and takes priority over
 *       the internal modal.
 *     - "My Assistant"  → task 042 (FR-F3): opens the stated-profile
 *       questionnaire (writes `sprk_userprofile` + seeds a User-scope
 *       `MemoryItem`) via the host-supplied `onMyAssistant` handler; when the
 *       host does not supply one, a console-only placeholder runs instead
 *       (see the `TODO(042)` marker below).
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
 * @see QuickStartModal.tsx — the Quick Start modal this menu owns (task 041)
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
import { QuickStartModal } from "./QuickStartModal";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AssistantToolMenuProps {
  /**
   * Called when the user selects "Quick Start" from the drop-down, INSTEAD
   * OF opening the built-in `QuickStartModal` (back-compat / test override).
   *
   * The current production wiring (`ConversationPane.tsx`) does NOT supply
   * this prop — in that case the component opens its own `QuickStartModal`
   * (task 041, FR-F2), a thin host over the existing `GetStartedCardsWidget`
   * wizard library.
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
// Placeholder handlers — "My Assistant" placeholder remains until task 042's
// host wires a real `onMyAssistant`; "Quick Start" is now self-hosted (see
// the `QuickStartModal` mount in the component body below) so it no longer
// needs a placeholder.
// ---------------------------------------------------------------------------

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
  onQuickStart,
  onMyAssistant = defaultMyAssistantHandler,
}) => {
  const styles = useStyles();
  const [menuOpen, setMenuOpen] = React.useState(false);

  // Task 041 (FR-F2) — the built-in Quick Start modal's open state. Owned
  // here (not by the host) so `ConversationPane.tsx` needs no changes: it
  // already mounts `<AssistantToolMenu onMyAssistant={...} />` with no
  // `onQuickStart` override, which is exactly the "use the internal modal"
  // path below.
  const [quickStartOpen, setQuickStartOpen] = React.useState(false);

  const handleOpenChange = React.useCallback(
    (_e: unknown, data: { open: boolean }) => {
      setMenuOpen(data.open);
    },
    [],
  );

  const handleQuickStart = React.useCallback(() => {
    if (onQuickStart) {
      // Explicit override (back-compat / test injection) takes priority.
      onQuickStart();
      return;
    }
    setQuickStartOpen(true);
  }, [onQuickStart]);

  const handleSelect = React.useCallback(
    (id: AssistantToolEntry["id"]) => {
      setMenuOpen(false);
      if (id === "quick-start") {
        handleQuickStart();
      } else {
        onMyAssistant();
      }
    },
    [handleQuickStart, onMyAssistant],
  );

  return (
    <>
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

      <QuickStartModal open={quickStartOpen} onClose={() => setQuickStartOpen(false)} />
    </>
  );
};

AssistantToolMenu.displayName = "AssistantToolMenu";

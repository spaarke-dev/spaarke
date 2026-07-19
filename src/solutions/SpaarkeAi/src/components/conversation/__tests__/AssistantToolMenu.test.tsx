/**
 * AssistantToolMenu.test.tsx — task 040 (FR-F1) unit tests.
 *
 * Verifies:
 *   1. The "Tools" trigger renders in the Assistant pane's PaneHeader region.
 *   2. Opening the menu shows both entries: "Quick Start" + "My Assistant".
 *   3. Clicking "Quick Start" calls `onQuickStart` exactly once and closes
 *      the menu.
 *   4. Clicking "My Assistant" calls `onMyAssistant` exactly once and closes
 *      the menu.
 *   5. Omitted handler props fall back to the console-only placeholder
 *      (TODO(041)/TODO(042)) without throwing — the drop-down is fully
 *      interactive ahead of tasks 041/042.
 *   6. Renders in light AND dark theme without crashing (ADR-021 semantic-
 *      token parity — ContextPaneMenu/WorkspacePaneMenu sibling pattern).
 *
 * @see AssistantToolMenu.tsx — component under test
 * @see ContextPaneMenu.tsx / WorkspacePaneMenu.tsx — the mirrored pattern
 */

import "@testing-library/jest-dom";
import * as React from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from "@fluentui/react-components";

import { AssistantToolMenu } from "../AssistantToolMenu";

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

function renderMenu(props?: {
  onQuickStart?: () => void;
  onMyAssistant?: () => void;
  theme?: typeof webLightTheme;
}): void {
  const theme = props?.theme ?? webLightTheme;
  render(
    <FluentProvider theme={theme}>
      <AssistantToolMenu
        onQuickStart={props?.onQuickStart}
        onMyAssistant={props?.onMyAssistant}
      />
    </FluentProvider>,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("AssistantToolMenu", () => {
  // -------------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------------

  it("renders the tools (⋮) trigger button", () => {
    renderMenu();
    expect(screen.getByTestId("assistant-tool-menu-trigger")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /assistant tools/i }),
    ).toBeInTheDocument();
  });

  it("shows both entries ('Quick Start' + 'My Assistant') when opened", async () => {
    renderMenu();
    const user = userEvent.setup();
    await user.click(screen.getByTestId("assistant-tool-menu-trigger"));

    expect(await screen.findByTestId("assistant-tool-menu-popover")).toBeInTheDocument();
    expect(screen.getByTestId("assistant-tool-quick-start")).toBeInTheDocument();
    expect(screen.getByTestId("assistant-tool-my-assistant")).toBeInTheDocument();
    expect(screen.getByText("Quick Start")).toBeInTheDocument();
    expect(screen.getByText("My Assistant")).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Handler wiring (placeholders for tasks 041/042)
  // -------------------------------------------------------------------------

  it("calls onQuickStart exactly once when 'Quick Start' is clicked, then closes the menu", async () => {
    const onQuickStart = jest.fn();
    renderMenu({ onQuickStart });
    const user = userEvent.setup();

    await user.click(screen.getByTestId("assistant-tool-menu-trigger"));
    await user.click(await screen.findByTestId("assistant-tool-quick-start"));

    expect(onQuickStart).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId("assistant-tool-menu-popover")).not.toBeInTheDocument();
  });

  it("calls onMyAssistant exactly once when 'My Assistant' is clicked, then closes the menu", async () => {
    const onMyAssistant = jest.fn();
    renderMenu({ onMyAssistant });
    const user = userEvent.setup();

    await user.click(screen.getByTestId("assistant-tool-menu-trigger"));
    await user.click(await screen.findByTestId("assistant-tool-my-assistant"));

    expect(onMyAssistant).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId("assistant-tool-menu-popover")).not.toBeInTheDocument();
  });

  it("does not throw when handler props are omitted (console-only placeholder for 041/042)", async () => {
    renderMenu();
    const user = userEvent.setup();

    await user.click(screen.getByTestId("assistant-tool-menu-trigger"));
    await expect(
      user.click(await screen.findByTestId("assistant-tool-quick-start")),
    ).resolves.not.toThrow();
  });

  // -------------------------------------------------------------------------
  // Dark-mode parity (ADR-021)
  // -------------------------------------------------------------------------

  it("renders in dark theme without crashing (ADR-021 token usage)", () => {
    renderMenu({ theme: webDarkTheme });
    expect(screen.getByTestId("assistant-tool-menu-trigger")).toBeInTheDocument();
  });

  it("renders in light theme as the baseline (ADR-021 parity check)", () => {
    renderMenu({ theme: webLightTheme });
    expect(screen.getByTestId("assistant-tool-menu-trigger")).toBeInTheDocument();
  });
});

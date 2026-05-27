/**
 * WorkspaceLayoutWidget — C-4 renderer-seam tests (R4 task 052)
 *
 * Covers Risk R-4 mitigation (zero behavioural change vs pre-refactor) by
 * asserting:
 *   1. Default-renderer resolution: when the host has registered a default
 *      renderer, the widget mounts that renderer with the expected props.
 *   2. Injected-renderer wins: an explicit `renderer` prop overrides the
 *      registered default.
 *   3. Empty-state when no Xrm: widget renders the existing no-Xrm message
 *      (preserved verbatim from pre-refactor).
 *   4. Empty-state when no renderer: widget renders a "no renderer" message
 *      when neither injected nor default renderer is available (graceful
 *      degradation — production hosts always register a default).
 *
 * Pre-refactor behaviour that MUST be preserved (Risk R-4 binding):
 *   - When Xrm is present and the default renderer is registered, the widget
 *     renders the renderer with `version="embedded" allocatedWidth={0}
 *     allocatedHeight={0} embedded` plus the host-supplied `webApi`, `userId`,
 *     `initialWorkspaceId`. These are the exact props passed to
 *     `LegalWorkspaceApp` in the pre-refactor code (see git history of
 *     `WorkspaceLayoutWidget.tsx` lines 173-185 prior to task 052).
 *
 * Mocks:
 *   - `@fluentui/react-components` is mocked to avoid pulling in the full
 *     Fluent v9 surface (matches the wrapper-test pattern in
 *     `WorkspaceWidgetWrapper.test.tsx`).
 *   - `window.Xrm` / `window.parent.Xrm` is stubbed via Object.defineProperty
 *     to control the locateXrm() outcome.
 */

import * as React from "react";
import { render, screen } from "@testing-library/react";

// Mock @spaarke/ui-components BEFORE importing the widget. The widget imports
// `getDefaultWorkspaceRenderer` from this module — we substitute a real, in-test
// slot so the test can register/clear renderers without touching the full
// library barrel (which pulls in legacy `.js` files that Jest 30 cannot parse
// in this runtime — documented under R4 task 068 / Jest+React 19 env fix).
jest.mock("@spaarke/ui-components", () => {
  let _slot: unknown = null;
  return {
    setDefaultWorkspaceRenderer: (r: unknown) => {
      _slot = r;
    },
    getDefaultWorkspaceRenderer: () => _slot,
    clearDefaultWorkspaceRenderer: () => {
      _slot = null;
    },
  };
});

// Re-import the mocked exports so tests can drive the slot directly. The
// `WorkspaceRenderer` type alias is structural; we re-declare it locally to
// avoid pulling in the full ui-components type tree (also impacted by the
// barrel-import issue above).
import {
  setDefaultWorkspaceRenderer,
  clearDefaultWorkspaceRenderer,
} from "@spaarke/ui-components";
type WorkspaceRenderer = React.ComponentType<unknown>;

import { WorkspaceLayoutWidget } from "../WorkspaceLayoutWidget";

// ---------------------------------------------------------------------------
// Mock Fluent UI (matches WorkspaceWidgetWrapper.test.tsx pattern)
// ---------------------------------------------------------------------------

jest.mock("@fluentui/react-components", () => ({
  makeStyles: () => () => ({
    root: "mock-root",
    emptyState: "mock-empty-state",
  }),
  tokens: {
    colorNeutralBackground1: "#fff",
    colorNeutralForeground3: "#666",
    spacingVerticalXL: "24px",
  },
  Text: ({ children, size }: { children: React.ReactNode; size?: number }) => (
    <span data-testid="text" data-size={size}>
      {children}
    </span>
  ),
}));

// ---------------------------------------------------------------------------
// Xrm fixture helpers — stub locateXrm() targets via window.Xrm
// ---------------------------------------------------------------------------

const FAKE_USER_ID = "11111111-2222-3333-4444-555555555555";

const makeFakeXrm = () => ({
  WebApi: {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  },
  Utility: {
    getGlobalContext: () => ({
      getUserId: () => `{${FAKE_USER_ID}}`,
    }),
  },
});

function installXrm(): void {
  // jsdom doesn't define window.Xrm by default; assigning to (window as any).Xrm
  // is the canonical pattern used elsewhere in the codebase (see locateXrm()
  // in WorkspaceLayoutWidget.tsx).
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = makeFakeXrm();
}

function uninstallXrm(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("WorkspaceLayoutWidget (C-4 renderer seam)", () => {
  afterEach(() => {
    clearDefaultWorkspaceRenderer();
    uninstallXrm();
    jest.clearAllMocks();
  });

  it("renders the registered default renderer with the same props the pre-refactor widget passed to LegalWorkspaceApp", () => {
    installXrm();

    const StubRenderer = jest
      .fn()
      .mockReturnValue(<div data-testid="stub-renderer" />);
    setDefaultWorkspaceRenderer(StubRenderer as unknown as WorkspaceRenderer);

    render(
      <WorkspaceLayoutWidget
        data={{ layoutId: "layout-abc", layoutName: "Test Layout" }}
        widgetType="workspace"
      />
    );

    expect(screen.getByTestId("workspace-layout-widget-root")).toBeTruthy();
    expect(screen.getByTestId("stub-renderer")).toBeTruthy();

    // The first call's props must match the pre-refactor LegalWorkspaceApp invocation.
    expect(StubRenderer).toHaveBeenCalledTimes(1);
    const props = StubRenderer.mock.calls[0][0];
    expect(props.version).toBe("embedded");
    expect(props.allocatedWidth).toBe(0);
    expect(props.allocatedHeight).toBe(0);
    expect(props.embedded).toBe(true);
    expect(props.initialWorkspaceId).toBe("layout-abc");
    expect(props.userId).toBe(FAKE_USER_ID);
    // webApi reference is the frame-walked Xrm.WebApi — verify the methods are
    // exposed via structural-equality (not deep equality, which would fail on
    // jest mock-function identity).
    expect(typeof props.webApi.retrieveMultipleRecords).toBe("function");
  });

  it("uses the injected renderer prop in preference to the registered default", () => {
    installXrm();

    const DefaultRenderer = jest
      .fn()
      .mockReturnValue(<div data-testid="default-renderer" />);
    const InjectedRenderer = jest
      .fn()
      .mockReturnValue(<div data-testid="injected-renderer" />);

    setDefaultWorkspaceRenderer(DefaultRenderer as unknown as WorkspaceRenderer);

    render(
      <WorkspaceLayoutWidget
        data={{ layoutId: "layout-xyz", layoutName: "Test Layout" }}
        widgetType="workspace"
        renderer={InjectedRenderer as unknown as WorkspaceRenderer}
      />
    );

    expect(screen.queryByTestId("default-renderer")).toBeNull();
    expect(screen.getByTestId("injected-renderer")).toBeTruthy();
    expect(InjectedRenderer).toHaveBeenCalledTimes(1);
    expect(DefaultRenderer).not.toHaveBeenCalled();
  });

  it("renders the no-Xrm empty state when Xrm is unavailable (pre-refactor behaviour preserved)", () => {
    // No installXrm() — leave window.Xrm undefined to exercise the dev-fallback branch.
    const StubRenderer = jest.fn().mockReturnValue(<div />);
    setDefaultWorkspaceRenderer(StubRenderer as unknown as WorkspaceRenderer);

    render(
      <WorkspaceLayoutWidget
        data={{ layoutId: "layout-abc", layoutName: "Test Layout" }}
        widgetType="workspace"
      />
    );

    expect(screen.getByTestId("workspace-layout-widget-no-xrm")).toBeTruthy();
    expect(StubRenderer).not.toHaveBeenCalled();
  });

  it("renders the no-renderer empty state when neither injected nor default renderer is available (graceful degradation)", () => {
    installXrm();
    // Do NOT register a default; do NOT inject a renderer.

    render(
      <WorkspaceLayoutWidget
        data={{ layoutId: "layout-abc", layoutName: "Test Layout" }}
        widgetType="workspace"
      />
    );

    expect(screen.getByTestId("workspace-layout-widget-no-renderer")).toBeTruthy();
  });
});

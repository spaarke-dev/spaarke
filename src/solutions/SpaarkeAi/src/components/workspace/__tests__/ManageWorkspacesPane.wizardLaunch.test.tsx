/**
 * ManageWorkspacesPane.wizardLaunch.test.tsx — the Manage Workspaces drawer launches the Workspace layout wizard
 * through the shared in-app seam (spaarke-ontology-platform-r1 task 113; D-26; ADR-050 as amended 2026-10-07,
 * launch rule (a)).
 *
 * "New" and a row's "Edit" used to call a private `Xrm.Navigation.navigateTo(webresource sprk_workspacelayoutwizard)`;
 * in the Console that is the un-themeable platform dialog. Now, with the Console's in-app host registered, both open
 * in-app (no `navigateTo`) and keep working end to end: the wizard writes its result to sessionStorage exactly as
 * before (it is the same window now, not a popup), and the drawer, after the wizard closes, refetches and opens the
 * new workspace as a tab.
 *
 * `@spaarke/ai-widgets` (session + pane-event hooks) and the layouts hook are stubbed; the launcher seam is real.
 *
 * Classification (ADR-038 §7): MAINTAIN — the drawer's wizard launch + result bridge.
 */
import "@testing-library/jest-dom";
import * as React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { registerInAppWizardHost } from "@spaarke/ui-components";

const mockDispatch = jest.fn();
jest.mock("@spaarke/ai-widgets", () => ({
  useAiSession: () => ({
    authenticatedFetch: jest.fn(),
    bffBaseUrl: "https://bff.example",
    isAuthenticated: true,
  }),
  useDispatchPaneEvent: () => mockDispatch,
}));

const mockRefetch = jest.fn();
const LAYOUT = {
  id: "layout-7",
  name: "Litigation",
  layoutTemplateId: "two-column",
  sectionsJson: "{}",
  isDefault: false,
  sortOrder: 1,
  isSystem: false,
  modifiedOn: "2026-10-01T00:00:00+00:00",
};
jest.mock("../../../hooks/useWorkspaceLayouts", () => ({
  useWorkspaceLayouts: () => ({ layouts: [LAYOUT], isLoading: false, refetch: mockRefetch }),
}));
jest.mock("../../../services/workspaceLayoutMutations", () => ({
  renameWorkspaceLayout: jest.fn(),
  deleteWorkspaceLayout: jest.fn(),
}));

import { ManageWorkspacesPane } from "../ManageWorkspacesPane";

const BFF = "https://bff.example";
const RESULT_KEY = "spaarke:workspace-wizard:last-result";
let navigateTo: jest.Mock;
let unregister: (() => void) | null = null;

beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = { Navigation: { navigateTo } };
  mockDispatch.mockReset();
  mockRefetch.mockReset();
  sessionStorage.clear();
});

afterEach(() => {
  unregister?.();
  unregister = null;
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

function mountPane() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ManageWorkspacesPane open onOpenChange={jest.fn()} tabs={[]} />
    </FluentProvider>
  );
}

describe("ManageWorkspacesPane — Workspace layout wizard launch", () => {
  it("New opens the wizard in-app with the SpaarkeAi template subset, then opens the saved workspace as a tab", async () => {
    // The in-app wizard "saves" and closes: it writes the same sessionStorage result the popup wizard wrote.
    const opener = jest.fn().mockImplementation(async () => {
      sessionStorage.setItem(RESULT_KEY, JSON.stringify({ confirmed: true, layoutId: "new-layout", at: Date.now() }));
    });
    unregister = registerInAppWizardHost(opener, ["sprk_workspacelayoutwizard"]);
    mountPane();

    fireEvent.click(await screen.findByTestId("manage-workspaces-new"));

    await waitFor(() => expect(opener).toHaveBeenCalledTimes(1));
    const request = opener.mock.calls[0][0];
    expect(request.webresourceName).toBe("sprk_workspacelayoutwizard");
    const params = new URLSearchParams(request.data);
    expect(params.get("mode")).toBe("create");
    expect(params.get("bffBaseUrl")).toBe(BFF);
    expect(params.get("templateFilter")).toBeTruthy();
    expect(navigateTo).not.toHaveBeenCalled();

    await waitFor(() => expect(mockRefetch).toHaveBeenCalled());
    await waitFor(() =>
      expect(mockDispatch).toHaveBeenCalledWith(
        "workspace",
        expect.objectContaining({ type: "widget_load", widgetData: { layoutId: "new-layout", layoutName: "Workspace" } })
      )
    );
    expect(sessionStorage.getItem(RESULT_KEY)).toBeNull(); // consumed
  });

  it("Edit opens the wizard in-app for that layout and refetches when it closes", async () => {
    const opener = jest.fn().mockResolvedValue(undefined);
    unregister = registerInAppWizardHost(opener, ["sprk_workspacelayoutwizard"]);
    mountPane();

    fireEvent.click(await screen.findByTestId(`manage-more-${LAYOUT.id}`));
    fireEvent.click(await screen.findByTestId(`manage-menu-edit-${LAYOUT.id}`));

    await waitFor(() => expect(opener).toHaveBeenCalledTimes(1));
    const params = new URLSearchParams(opener.mock.calls[0][0].data);
    expect(params.get("mode")).toBe("edit");
    expect(params.get("layoutId")).toBe(LAYOUT.id);
    expect(navigateTo).not.toHaveBeenCalled();
    await waitFor(() => expect(mockRefetch).toHaveBeenCalled());
  });

  it("New while another in-app wizard is open warns 'already open', not 'Xrm.Navigation not available'", async () => {
    const warn = jest.spyOn(console, "warn").mockImplementation(() => undefined);
    const opener = jest.fn().mockResolvedValue({ busy: true });
    unregister = registerInAppWizardHost(opener, ["sprk_workspacelayoutwizard"]);
    mountPane();

    fireEvent.click(await screen.findByTestId("manage-workspaces-new"));

    await waitFor(() => expect(warn).toHaveBeenCalled());
    const text = warn.mock.calls.map(c => String(c[0])).join("\n");
    expect(text).toMatch(/already open/);
    expect(text).not.toMatch(/not available/);
    warn.mockRestore();
  });

  it("with no host mounted, New keeps the navigateTo dialog and its title", async () => {
    mountPane();

    fireEvent.click(await screen.findByTestId("manage-workspaces-new"));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0]).toMatchObject({ pageType: "webresource", webresourceName: "sprk_workspacelayoutwizard" });
    expect(navigateTo.mock.calls[0][1]).toMatchObject({ target: 2, title: "Create New Workspace" });
  });
});

/**
 * workspaceGrid.launchSites.test.tsx — the LegalWorkspace `WorkspaceGrid` launch sites for the four wizards moved
 * off `navigateTo` by spaarke-ontology-platform-r1 task 113 (D-26; ADR-050 as amended 2026-10-07, launch rule (a)):
 * Summarize Files, Find Similar, Upload Documents and the Workspace layout wizard (edit / save-as / create).
 *
 * LegalWorkspace has no test runner of its own, so the real `WorkspaceGrid` is mounted here (the Console runs it
 * embedded). Its data hooks, config builders and the shell are stubbed; the launch handlers are captured from what
 * the grid passes to `buildWorkspaceConfig` (card handlers, `onAddDocument`) and to `onHeaderReady`
 * (`onEditClick` / `onCreateClick`) and INVOKED. The in-app host is a registered recorder, so the assertion is on
 * what the real `navigateToWebResourceSurfaceAsync` seam receives.
 *
 * Proves: with the Console host registered each site opens in-app and `navigateTo` is NOT called; with no host
 * each site keeps the exact `navigateTo` shape it had before (so the model-driven-page / hostless entry points
 * are unchanged); the document list / layouts refetch exactly as before.
 *
 * Classification (ADR-038 §7): MAINTAIN — the launch-site wiring for the four migrated wizards.
 */
import * as React from "react";
import { act, render } from "@testing-library/react";
import { registerInAppWizardHost } from "@spaarke/ui-components";

jest.mock("@spaarke/ui-components", () => ({
  ...jest.requireActual("@spaarke/ui-components"),
  // The shell renders nothing: only the grid's handlers are under test.
  WorkspaceShell: () => null,
}));

const mockRefetchLayouts = jest.fn();
const mockActiveLayout = {
  id: "layout-1",
  name: "My Workspace",
  isSystem: false,
  layoutTemplateId: "two-column",
  sectionsJson: '{"rows":[]}',
};
jest.mock("../../../../../LegalWorkspace/src/hooks/useWorkspaceLayouts", () => ({
  useWorkspaceLayouts: () => ({
    layouts: [mockActiveLayout],
    activeLayout: mockActiveLayout,
    activeLayoutJson: null,
    isLoading: false,
    status: "loaded",
    error: null,
    setActiveLayoutById: jest.fn(),
    refetch: mockRefetchLayouts,
  }),
}));
jest.mock("../../../../../LegalWorkspace/src/hooks/useDataverseService", () => ({ useDataverseService: () => ({}) }));
jest.mock("../../../../../LegalWorkspace/src/hooks/useDailyDigestAutoPopup", () => ({ useDailyDigestAutoPopup: () => undefined }));
jest.mock("../../../../../LegalWorkspace/src/config/runtimeConfig", () => ({ getBffBaseUrl: () => "https://bff.example" }));
jest.mock("../../../../../LegalWorkspace/src/services/authInit", () => ({ authenticatedFetch: jest.fn() }));
jest.mock("../../../../../LegalWorkspace/src/components/GetStarted/ActionCardHandlers", () => ({
  createPlaybookHandlers: () => ({}),
}));
jest.mock("../../../../../LegalWorkspace/src/components/WorkspaceHeader", () => ({ WorkspaceHeader: () => null }));
jest.mock("../../../../../LegalWorkspace/src/components/WorkspaceLoadingStates", () => ({
  WorkspaceSkeleton: () => null,
  PersonalizeBanner: () => null,
  FetchErrorBar: () => null,
}));
// Force the static-config fallback so the card / document handlers are handed to `buildWorkspaceConfig`.
const mockDynamicArgs: { current: any[] | null } = { current: null };
jest.mock("../../../../../LegalWorkspace/src/workspace/buildDynamicWorkspaceConfig", () => ({
  buildDynamicWorkspaceConfig: (...args: any[]) => {
    mockDynamicArgs.current = args;
    throw new Error("force static fallback");
  },
}));
const mockBuildConfigArgs: { current: any } = { current: null };
jest.mock("../../../../../LegalWorkspace/src/workspaceConfig", () => ({
  buildWorkspaceConfig: (args: unknown) => {
    mockBuildConfigArgs.current = args;
    return {};
  },
}));

import { WorkspaceGrid } from "../../../../../LegalWorkspace/src/components/Shell/WorkspaceGrid";

const BFF = "https://bff.example";
let navigateTo: jest.Mock;
let unregister: (() => void) | null = null;
let headerState: any;

function mountGrid() {
  headerState = null;
  render(
    <WorkspaceGrid
      allocatedWidth={800}
      allocatedHeight={600}
      webApi={{ retrieveRecord: jest.fn().mockResolvedValue({}) } as never}
      userId="user-1"
      embedded
      onHeaderReady={s => {
        headerState = s;
      }}
    />
  );
}

/** Register a recording in-app host that closes immediately (the launch promise resolves). */
function registerHost(): jest.Mock {
  const opener = jest.fn().mockResolvedValue(undefined);
  unregister = registerInAppWizardHost(opener, [
    "sprk_summarizefileswizard",
    "sprk_findsimilar",
    "sprk_documentuploadwizard",
    "sprk_workspacelayoutwizard",
  ]);
  return opener;
}

beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Navigation: { navigateTo } };
  mockRefetchLayouts.mockReset();
  mockBuildConfigArgs.current = null;
  mockDynamicArgs.current = null;
  // The static-config fallback and the "Xrm not available" paths log a warning by design.
  jest.spyOn(console, "warn").mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
  unregister?.();
  unregister = null;
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

const bffParam = `bffBaseUrl=${encodeURIComponent(BFF)}`;

describe("WorkspaceGrid launch sites — Summarize Files / Find Similar", () => {
  it("open in-app with the Console host registered; no navigateTo; the document list refetches after close", async () => {
    const opener = registerHost();
    mountGrid();
    const docRefetch = jest.fn();
    mockBuildConfigArgs.current.onDocRefetchReady(docRefetch);
    const handlers = mockBuildConfigArgs.current.cardClickHandlers;

    await act(async () => {
      await handlers["summarize-new-files"](["d1", "d2"]);
    });
    expect(opener).toHaveBeenLastCalledWith({
      webresourceName: "sprk_summarizefileswizard",
      data: `documentIds=d1,d2&${bffParam}`,
    });
    expect(docRefetch).toHaveBeenCalledTimes(1);

    await act(async () => {
      await handlers["find-similar"]("doc-9", "cont-3");
    });
    expect(opener).toHaveBeenLastCalledWith({
      webresourceName: "sprk_findsimilar",
      data: `documentId=doc-9&containerId=cont-3&${bffParam}`,
    });
    expect(docRefetch).toHaveBeenCalledTimes(2);
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it("keep the exact navigateTo shape when no host is mounted (hostless unchanged)", async () => {
    mountGrid();
    const docRefetch = jest.fn();
    mockBuildConfigArgs.current.onDocRefetchReady(docRefetch);
    const handlers = mockBuildConfigArgs.current.cardClickHandlers;

    await act(async () => {
      await handlers["summarize-new-files"]();
      await handlers["find-similar"]();
    });

    expect(navigateTo).toHaveBeenCalledTimes(2);
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: "webresource",
      webresourceName: "sprk_summarizefileswizard",
      data: bffParam,
    });
    expect(navigateTo.mock.calls[0][1]).toMatchObject({ target: 2, title: "Summarize Files" });
    expect(navigateTo.mock.calls[1][0]).toEqual({
      pageType: "webresource",
      webresourceName: "sprk_findsimilar",
      data: `documentId=&containerId=&${bffParam}`,
    });
    expect(navigateTo.mock.calls[1][1]).toMatchObject({ target: 2, title: "Find Similar Documents" });
    expect(docRefetch).toHaveBeenCalledTimes(2);
  });
});

describe("WorkspaceGrid launch sites — the generic section-card launcher (Get Started cards)", () => {
  it("routes Summarize Files and Find Similar through the in-app seam, and leaves other web resources on navigateTo", async () => {
    const opener = registerHost();
    mountGrid();
    const onOpenWizard = mockDynamicArgs.current![2].onOpenWizard;

    await act(async () => {
      await onOpenWizard("sprk_summarizefileswizard");
      await onOpenWizard("sprk_findsimilar");
      await onOpenWizard("sprk_playbooklibrary", "intent=email-compose", { width: { value: 50, unit: "%" } });
    });

    expect(opener.mock.calls.map(c => c[0])).toEqual([
      { webresourceName: "sprk_summarizefileswizard", data: bffParam },
      { webresourceName: "sprk_findsimilar", data: bffParam },
    ]);
    // Playbook Library is not a wizard in scope: it keeps navigateTo and its caller-supplied width.
    expect(navigateTo).toHaveBeenCalledTimes(1);
    expect(navigateTo.mock.calls[0][0].webresourceName).toBe("sprk_playbooklibrary");
    expect(navigateTo.mock.calls[0][1].width).toEqual({ value: 50, unit: "%" });
  });

  it("with no host, Summarize Files from a card keeps the navigateTo call", async () => {
    mountGrid();
    const onOpenWizard = mockDynamicArgs.current![2].onOpenWizard;

    await act(async () => {
      await onOpenWizard("sprk_summarizefileswizard");
    });

    expect(navigateTo).toHaveBeenCalledTimes(1);
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: "webresource",
      webresourceName: "sprk_summarizefileswizard",
      data: bffParam,
    });
  });
});

describe("WorkspaceGrid launch sites — Upload Documents", () => {
  const expectedEnvelope = (theme: string) =>
    encodeURIComponent(`parentEntityType=sprk_document&parentEntityId=&parentEntityName=&theme=${theme}&${bffParam}`);

  it("opens in-app with the once-encoded envelope the code page and the renderer both decode", async () => {
    const opener = registerHost();
    mountGrid();

    await act(async () => {
      await mockBuildConfigArgs.current.onAddDocument();
    });

    expect(opener).toHaveBeenCalledTimes(1);
    expect(opener.mock.calls[0][0].webresourceName).toBe("sprk_documentuploadwizard");
    expect(opener.mock.calls[0][0].data).toBe(expectedEnvelope("light"));
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it("keeps the navigateTo shape (no title, wizard size) when no host is mounted", async () => {
    mountGrid();

    await act(async () => {
      await mockBuildConfigArgs.current.onAddDocument();
    });

    expect(navigateTo).toHaveBeenCalledTimes(1);
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: "webresource",
      webresourceName: "sprk_documentuploadwizard",
      data: expectedEnvelope("light"),
    });
    expect(navigateTo.mock.calls[0][1]).toMatchObject({ target: 2 });
    expect(navigateTo.mock.calls[0][1]).not.toHaveProperty("title");
  });
});

describe("WorkspaceGrid launch sites — Workspace layout wizard", () => {
  it("edit opens in-app; layouts refetch after it closes; no navigateTo", async () => {
    const opener = registerHost();
    mountGrid();

    await act(async () => {
      await headerState.onEditClick();
    });

    expect(opener).toHaveBeenCalledWith({
      webresourceName: "sprk_workspacelayoutwizard",
      data: `mode=edit&layoutId=layout-1&${bffParam}`,
    });
    expect(mockRefetchLayouts).toHaveBeenCalledTimes(1);
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it("create opens in-app; layouts refetch after it closes; no navigateTo", async () => {
    const opener = registerHost();
    mountGrid();

    await act(async () => {
      await headerState.onCreateClick();
    });

    expect(opener).toHaveBeenCalledWith({ webresourceName: "sprk_workspacelayoutwizard", data: `mode=create&${bffParam}` });
    expect(mockRefetchLayouts).toHaveBeenCalledTimes(1);
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it("with no host, edit and create keep their navigateTo shape and titles, and refetch on close", async () => {
    mountGrid();

    await act(async () => {
      await headerState.onEditClick();
      await headerState.onCreateClick();
    });

    expect(navigateTo).toHaveBeenCalledTimes(2);
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: "webresource",
      webresourceName: "sprk_workspacelayoutwizard",
      data: `mode=edit&layoutId=layout-1&${bffParam}`,
    });
    expect(navigateTo.mock.calls[0][1]).toMatchObject({ target: 2, title: "Edit Workspace" });
    expect(navigateTo.mock.calls[1][1]).toMatchObject({ target: 2, title: "Create New Workspace" });
    expect(mockRefetchLayouts).toHaveBeenCalledTimes(2);
  });

  it("a hostless navigateTo REJECTION (a real dialog failure) does not refetch the layouts, as before", async () => {
    navigateTo.mockRejectedValue(new Error("dialog blew up"));
    jest.spyOn(console, "error").mockImplementation(() => undefined);
    mountGrid();

    await act(async () => {
      await headerState.onEditClick();
      await headerState.onCreateClick();
    });

    expect(navigateTo).toHaveBeenCalledTimes(2);
    expect(mockRefetchLayouts).not.toHaveBeenCalled();
  });

  it("with neither a host nor Xrm nothing opens and nothing refetches", async () => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    mountGrid();

    await act(async () => {
      await headerState.onCreateClick();
    });

    expect(mockRefetchLayouts).not.toHaveBeenCalled();
  });
});

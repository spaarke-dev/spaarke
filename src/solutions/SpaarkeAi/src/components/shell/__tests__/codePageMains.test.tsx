/**
 * codePageMains.test.tsx — direct tests of two code-page entry points (`main.tsx`) that mount themselves on import
 * (spaarke-ontology-platform-r1 task 113; closes #1420's code-page half and #1479's code-page half).
 *
 *  - Create Work Assignment `main.tsx` `handleComplete`: on a successful create it writes the committed hand-off
 *    result (the new record id) for the URL's `handoffId`, then closes the platform dialog; the cancel path writes
 *    nothing; a direct open (no hand-off) just closes.
 *  - Find Similar `main.tsx`: passes the `documentId` / `containerId` launch params to the app as `initialDocument`.
 *
 * Harness: the entry module is imported in isolation against a `#root` element and a `?handoffId=…` /
 * `?documentId=…` URL; the wizard components, auth and the Xrm navigation adapter are stubs that record their props.
 *
 * Classification (ADR-038 §7): MAINTAIN — the code-page halves of two behaviours the in-app path also has.
 */
import "@testing-library/jest-dom";
import { waitFor } from "@testing-library/react";
import { readHandoffResult } from "@spaarke/ui-components/services/surfaceHandoff/handoffStorage";

jest.setTimeout(240000); // cold module graph of the code-page entry points

const mockWaProps: { current: any } = { current: null };
const mockFindSimilarProps: { current: any } = { current: null };
const mockCloseDialog = jest.fn();

jest.mock("@spaarke/auth", () => ({
  resolveRuntimeConfig: jest.fn().mockResolvedValue({
    msalClientId: "client",
    bffBaseUrl: "https://bff.example",
    bffOAuthScope: "scope",
    tenantId: "tenant-1",
    msalAuthority: "",
  }),
  initAuth: jest.fn().mockResolvedValue(undefined),
  getAuthProvider: () => ({ getTenantId: jest.fn().mockResolvedValue("tenant-1") }),
  authenticatedFetch: jest.fn(),
}));
// The barrel pulls in the whole component library on every isolated import; the pages only need the theme helpers.
jest.mock("@spaarke/ui-components", () => ({
  resolveCodePageTheme: () => ({}),
  setupCodePageThemeListener: () => () => undefined,
}));
jest.mock("@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter", () => ({
  createXrmNavigationService: () => ({ closeDialog: mockCloseDialog }),
}));
jest.mock("@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter", () => ({
  createXrmDataService: () => ({}),
}));
jest.mock("@spaarke/ui-components/components/CreateWorkAssignmentWizard", () => ({
  WorkAssignmentWizardDialog: (p: any) => {
    mockWaProps.current = p;
    return null;
  },
}));
jest.mock("../../../../../FindSimilarCodePage/src/App", () => ({
  FindSimilarApp: (p: any) => {
    mockFindSimilarProps.current = p;
    return null;
  },
}));

function mountPage(search: string, load: () => void): void {
  jest.resetModules();
  document.body.innerHTML = '<div id="root"></div>';
  window.history.pushState({}, "", `/${search}`);
  jest.isolateModules(load);
}

beforeEach(() => {
  sessionStorage.clear();
  mockWaProps.current = null;
  mockFindSimilarProps.current = null;
  mockCloseDialog.mockReset();
  jest.spyOn(console, "error").mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
  window.history.pushState({}, "", "/");
});

describe("CreateWorkAssignmentWizard main.tsx — handleComplete (#1420)", () => {
  const load = () => require("../../../../../CreateWorkAssignmentWizard/src/main");

  it("a successful create writes the committed hand-off result with the record id, then closes the dialog", async () => {
    mountPage("?handoffId=h-9", load);
    await waitFor(() => expect(mockWaProps.current?.onComplete).toBeDefined());

    mockWaProps.current.onComplete("wa-5");

    expect(readHandoffResult("h-9")).toEqual({ committed: true, recordId: "wa-5" });
    expect(mockCloseDialog).toHaveBeenCalledWith({ confirmed: true });
  });

  it("cancelling writes no result (cancellation is inferred) and closes the dialog", async () => {
    mountPage("?handoffId=h-10", load);
    await waitFor(() => expect(mockWaProps.current?.onClose).toBeDefined());

    mockWaProps.current.onClose();

    expect(readHandoffResult("h-10")).toBeNull();
    expect(mockCloseDialog).toHaveBeenCalledWith({ confirmed: true });
  });

  it("a direct open (no hand-off) just closes on completion and writes nothing", async () => {
    mountPage("", load);
    await waitFor(() => expect(mockWaProps.current?.onComplete).toBeDefined());

    mockWaProps.current.onComplete("wa-6");

    expect(mockCloseDialog).toHaveBeenCalledWith({ confirmed: true });
    expect(sessionStorage.length).toBe(0);
  });
});

describe("FindSimilarCodePage main.tsx — launch params (#1479)", () => {
  const load = () => require("../../../../../FindSimilarCodePage/src/main");

  it("passes documentId (cleaned) and containerId to the app as initialDocument", async () => {
    mountPage("?documentId=%7BABC-123%7D&containerId=cont-1", load);
    await waitFor(() => expect(mockFindSimilarProps.current).not.toBeNull());

    expect(mockFindSimilarProps.current.initialDocument).toEqual({ documentId: "abc-123", containerId: "cont-1" });
  });

  it("reads them from the Xrm data envelope too, and an empty documentId preselects nothing", async () => {
    mountPage(`?data=${encodeURIComponent("documentId=&containerId=&bffBaseUrl=x")}`, load);
    await waitFor(() => expect(mockFindSimilarProps.current).not.toBeNull());

    expect(mockFindSimilarProps.current.initialDocument).toEqual({ documentId: "", containerId: "" });
  });
});

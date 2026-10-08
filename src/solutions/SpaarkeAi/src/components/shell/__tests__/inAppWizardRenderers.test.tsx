/**
 * inAppWizardRenderers.test.tsx — the Console's renderers for the code-page wizards it hosts in-app
 * (spaarke-ontology-platform-r1 task 113; D-26; ADR-050 as amended 2026-10-07, launch rule (a)).
 *
 * The three heavy wizard components are replaced by recorders: this file proves the MAPPING from the host's
 * render context (launch `data`, `onClose`, `uiScale`, auth, tenant, hand-off files) to each wizard's props — in
 * particular that every renderer mounts the wizard IN-APP (non-embedded / `inApp`) and routes closing through the
 * host's `onClose`. `inAppWizards.hosted.test.tsx` mounts the REAL components and proves they never reach into the
 * parent document.
 *
 * Classification (ADR-038 §7): MAINTAIN — the seam between the Console host and three code-page wizards.
 */
import * as React from "react";
import { render } from "@testing-library/react";
import type { IInAppWizardRenderContext } from "@spaarke/ui-components";

const mockUploadProps: { current: any } = { current: null };
const mockFindSimilarProps: { current: any } = { current: null };
const mockLayoutProps: { current: any } = { current: null };

jest.mock("../../../../../DocumentUploadWizard/src/DocumentUploadWizardDialog", () => ({
  DocumentUploadWizardDialog: (p: any) => {
    mockUploadProps.current = p;
    return null;
  },
}));
jest.mock("../../../../../FindSimilarCodePage/src/App", () => ({
  FindSimilarApp: (p: any) => {
    mockFindSimilarProps.current = p;
    return null;
  },
}));
jest.mock("../../../../../WorkspaceLayoutWizard/src/App", () => ({
  App: (p: any) => {
    mockLayoutProps.current = p;
    return null;
  },
}));

import {
  IN_APP_WIZARD_RENDERERS,
  renderDocumentUploadWizard,
  renderFindSimilarWizard,
  renderWorkspaceLayoutWizard,
} from "../inAppWizardRenderers";

const authenticatedFetch = jest.fn();
const onClose = jest.fn();

function ctx(overrides: Partial<IInAppWizardRenderContext> = {}): IInAppWizardRenderContext {
  return {
    data: "",
    onClose,
    authenticatedFetch,
    bffBaseUrl: "https://bff.example",
    tenantId: "tenant-7",
    uiScale: 1.5,
    dataService: {} as never,
    navigationService: {} as never,
    ...overrides,
  };
}

beforeEach(() => {
  mockUploadProps.current = null;
  mockFindSimilarProps.current = null;
  mockLayoutProps.current = null;
  onClose.mockReset();
});

describe("inAppWizardRenderers", () => {
  it("supplies exactly the three code-page wizards (the host opens only names it has a renderer for)", () => {
    expect(Object.keys(IN_APP_WIZARD_RENDERERS).sort()).toEqual([
      "sprk_documentuploadwizard",
      "sprk_findsimilar",
      "sprk_workspacelayoutwizard",
    ]);
  });

  it("Upload Documents: resolves the parent from the once-encoded envelope and mounts non-embedded", () => {
    const dataString =
      "parentEntityType=sprk_matter&parentEntityId=m-1&parentEntityName=" + encodeURIComponent("Acme & Co") + "&bffBaseUrl=x";
    render(<>{renderDocumentUploadWizard(ctx({ data: encodeURIComponent(dataString) }))}</>);

    const p = mockUploadProps.current;
    expect(p.parentEntityType).toBe("sprk_matter");
    expect(p.parentEntityId).toBe("m-1");
    expect(p.parentEntityName).toBe("Acme & Co");
    expect(p.embedded).toBe(false);
    expect(p.uiScale).toBe(1.5);
    expect(p.bffBaseUrl).toBe("https://bff.example");
    expect(p.onClose).toBe(onClose);
  });

  it("Upload Documents: the standalone WorkspaceGrid launch (no parent) opens in standalone mode", () => {
    render(
      <>
        {renderDocumentUploadWizard(
          ctx({ data: encodeURIComponent("parentEntityType=sprk_document&parentEntityId=&parentEntityName=&theme=dark") })
        )}
      </>
    );
    expect(mockUploadProps.current.parentEntityId).toBe("");
  });

  it("Find Similar: mounts in SprkModal mode with the tenant, the session files and the host onClose", () => {
    const initialFileRefs = { sessionId: "s1", fileIds: ["f1"], fileNames: ["a.pdf"] };
    render(<>{renderFindSimilarWizard(ctx({ initialFileRefs }))}</>);

    const p = mockFindSimilarProps.current;
    expect(p.apiBaseUrl).toBe("https://bff.example");
    expect(p.tenantId).toBe("tenant-7");
    expect(p.authenticatedFetch).toBe(authenticatedFetch);
    expect(p.initialFileRefs).toEqual(initialFileRefs);
    expect(p.inApp).toEqual({ onClose, uiScale: 1.5 });
  });

  it("Find Similar: a host with no resolved tenant passes an empty string, never undefined", () => {
    render(<>{renderFindSimilarWizard(ctx({ tenantId: undefined }))}</>);
    expect(mockFindSimilarProps.current.tenantId).toBe("");
  });

  it("Workspace layout: parses the launch data exactly as the code page does and mounts in-app", () => {
    const data = "mode=edit&bffBaseUrl=x&layoutId=l-9&templateFilter=" + encodeURIComponent("one-column,two-column");
    render(<>{renderWorkspaceLayoutWizard(ctx({ data }))}</>);

    const p = mockLayoutProps.current;
    expect(p.mode).toBe("edit");
    expect(p.layoutId).toBe("l-9");
    expect(p.templateFilter).toEqual(["one-column", "two-column"]);
    expect(p.authenticatedFetch).toBe(authenticatedFetch);
    expect(p.inApp).toEqual({ onClose, uiScale: 1.5 });
  });
});

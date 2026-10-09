/**
 * inAppWizards.hosted.test.tsx — the REAL code-page wizard components, mounted the way the Console hosts them
 * (spaarke-ontology-platform-r1 task 113; D-26; ADR-050 as amended 2026-10-07, launch rules (a) and (b)).
 *
 * Before task 113 these wizards could only close by DOM-walking into the parent document and clicking the platform
 * dialog's `dialogCloseIconButton`, then `window.close()` — correct under a `navigateTo` dialog, but in-app that
 * button belongs to whatever platform dialog the Console itself runs in. The tests plant exactly such a button and
 * a `window.close` spy, and assert that (1) in-app the wizard closes ONLY through the host's `onClose` and never
 * touches either; and (2) with no host (the `navigateTo` path, ribbon / subgrid entry points) the hack still runs,
 * so the code pages are unchanged.
 *
 * `inAppWizards.upload.test.tsx` covers Upload Documents (its shell is mocked, which would also stub the layout wizard's).
 *
 * Classification (ADR-038 §7): MAINTAIN — the close contract that keeps a hosted wizard from closing the Console.
 */
import "@testing-library/jest-dom";
import * as React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

import { FindSimilarApp } from "../../../../../FindSimilarCodePage/src/App";
import { App as WorkspaceLayoutWizardApp } from "../../../../../WorkspaceLayoutWizard/src/App";

let platformClose: jest.Mock;
let platformButton: HTMLButtonElement;
let windowClose: jest.SpyInstance;

beforeEach(() => {
  platformClose = jest.fn();
  platformButton = document.createElement("button");
  platformButton.setAttribute("data-id", "dialogCloseIconButton");
  platformButton.addEventListener("click", platformClose);
  document.body.appendChild(platformButton);
  windowClose = jest.spyOn(window, "close").mockImplementation(() => undefined);
});

afterEach(() => {
  platformButton.remove();
  windowClose.mockRestore();
});

function inHost(node: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

// ---------------------------------------------------------------------------
// Find Similar
// ---------------------------------------------------------------------------

describe("FindSimilarApp — in-app (SprkModal)", () => {
  const baseProps = { apiBaseUrl: "https://bff.example", tenantId: "t1", authenticatedFetch: jest.fn() };

  it("renders as a dialog and Cancel closes ONLY through the host onClose", () => {
    const onClose = jest.fn();
    inHost(<FindSimilarApp {...baseProps} inApp={{ onClose }} />);

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Find Similar Documents")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(platformClose).not.toHaveBeenCalled();
    expect(windowClose).not.toHaveBeenCalled();
  });

  it("the header close button routes to onClose; Escape does not close (explicit dismiss)", () => {
    const onClose = jest.fn();
    inHost(<FindSimilarApp {...baseProps} inApp={{ onClose }} />);

    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(platformClose).not.toHaveBeenCalled();
    expect(windowClose).not.toHaveBeenCalled();
  });

  it("a preselected documentId (#1479) opens with that document chosen and Find Similar enabled, in both layouts", () => {
    const initialDocument = { documentId: "abc-123", containerId: "c1" };
    const first = inHost(<FindSimilarApp {...baseProps} initialDocument={initialDocument} inApp={{ onClose: jest.fn() }} />);
    expect(screen.getByText("Selected document")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Find Similar/ })).toBeEnabled();
    first.unmount();

    inHost(<FindSimilarApp {...baseProps} initialDocument={initialDocument} />); // the code-page layout
    expect(screen.getByText("Selected document")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Find Similar/ })).toBeEnabled();
  });

  it("an empty documentId preselects nothing", () => {
    inHost(<FindSimilarApp {...baseProps} initialDocument={{ documentId: "" }} inApp={{ onClose: jest.fn() }} />);
    expect(screen.queryByText("Selected document")).toBeNull();
    expect(screen.getByRole("button", { name: /Find Similar/ })).toBeDisabled();
  });

  it("Find Similar stays disabled until a record or file is chosen", () => {
    inHost(<FindSimilarApp {...baseProps} inApp={{ onClose: jest.fn() }} />);
    expect(screen.getByRole("button", { name: /Find Similar/ })).toBeDisabled();
  });

  it("with no host (the navigateTo code page) Cancel still clicks the platform close button, unchanged", () => {
    inHost(<FindSimilarApp {...baseProps} />);

    expect(screen.queryByRole("dialog")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(platformClose).toHaveBeenCalledTimes(1);
  });
});

// ---------------------------------------------------------------------------
// Workspace layout
// ---------------------------------------------------------------------------

describe("WorkspaceLayoutWizard App — in-app (SprkModal)", () => {
  const baseProps = {
    mode: "create" as const,
    layoutId: null,
    layoutTemplateId: null,
    sectionsJson: null,
    sourceName: null,
    authenticatedFetch: jest.fn(),
  };

  it("mounts non-embedded in a dialog with its title, and the close button routes to the host onClose only", () => {
    const onClose = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <WorkspaceLayoutWizardApp {...baseProps} inApp={{ onClose, uiScale: 1 }} />
      </FluentProvider>
    );

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Create Workspace Layout")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Close" }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(platformClose).not.toHaveBeenCalled();
    expect(windowClose).not.toHaveBeenCalled();
  });

  it("Escape does not close the wizard (explicit dismiss)", () => {
    const onClose = jest.fn();
    inHost(<WorkspaceLayoutWizardApp {...baseProps} inApp={{ onClose }} />);
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(onClose).not.toHaveBeenCalled();
  });

  describe("edit mode (Save then Done, and Delete)", () => {
    const layout = {
      name: "Mine",
      layoutTemplateId: "single-column",
      sectionsJson: JSON.stringify({ schemaVersion: 1, rows: [{ id: "r1", columns: "1fr", sections: ["documents"] }] }),
      isDefault: false,
      modifiedOn: "2026-01-01T00:00:00+00:00",
    };
    let fetchMock: jest.Mock;

    beforeEach(() => {
      fetchMock = jest.fn(async (_url: string, init?: RequestInit) => {
        const method = init?.method ?? "GET";
        if (method === "GET") return { ok: true, status: 200, headers: { get: () => null }, json: async () => layout };
        if (method === "PUT") return { ok: true, status: 200, headers: { get: () => null }, json: async () => ({ id: "L1" }) };
        return { ok: true, status: 204, headers: { get: () => null }, json: async () => ({}) }; // DELETE
      });
      jest.spyOn(window, "confirm").mockReturnValue(true);
    });

    function mountEdit(inApp?: { onClose: () => void }) {
      return render(
        <FluentProvider theme={webLightTheme}>
          <WorkspaceLayoutWizardApp
            {...baseProps}
            mode="edit"
            layoutId="L1"
            authenticatedFetch={fetchMock as never}
            inApp={inApp}
          />
        </FluentProvider>
      );
    }

    it("the success screen's Done closes through the host onClose only", async () => {
      const onClose = jest.fn();
      mountEdit({ onClose });

      fireEvent.click(await screen.findByRole("button", { name: "Save Layout" }));
      fireEvent.click(await screen.findByRole("button", { name: "Done" }));

      expect(onClose).toHaveBeenCalledTimes(1);
      expect(platformClose).not.toHaveBeenCalled();
      expect(windowClose).not.toHaveBeenCalled();
    });

    it("Delete closes through the host onClose only (never window.close on the Console)", async () => {
      const onClose = jest.fn();
      mountEdit({ onClose });

      fireEvent.click(await screen.findByRole("button", { name: "Delete" }));

      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
      expect(fetchMock).toHaveBeenCalledWith("/api/workspace/layouts/L1", { method: "DELETE" });
      expect(platformClose).not.toHaveBeenCalled();
      expect(windowClose).not.toHaveBeenCalled();
    });

    it("with no host the code-page Done still clicks the platform close button, unchanged", async () => {
      mountEdit();

      fireEvent.click(await screen.findByRole("button", { name: "Save Layout" }));
      fireEvent.click(await screen.findByRole("button", { name: "Done" }));

      expect(platformClose).toHaveBeenCalledTimes(1);
    });
  });

  it("with no host (the navigateTo code page) closing still clicks the platform close button, unchanged", () => {
    render(<WorkspaceLayoutWizardApp {...baseProps} />);

    expect(screen.queryByRole("dialog")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /Cancel|Close/ }));

    expect(platformClose).toHaveBeenCalledTimes(1);
  });
});

/**
 * inAppWizards.upload.test.tsx - the Document Upload wizard as the Console hosts it
 * (spaarke-ontology-platform-r1 task 113; D-26; ADR-050 as amended 2026-10-07, launch rules (a) and (b)).
 *
 * `WizardShell` is a recorder here (the dialog's heavy step graph is not under test): the tests prove the props the
 * dialog hands its shell in-app vs on the code page, that no layout wrapper is left in the host tree in-app, and that
 * the success screen's Done never calls `window.close()` in-app (it would target the Console's own window) while the
 * code page keeps doing so.
 *
 * Classification (ADR-038 section 7): MAINTAIN - the hosting contract of the shipped Upload Documents wizard.
 */
import "@testing-library/jest-dom";
import * as React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

const mockUploadShell: { props: any } = { props: null };
jest.mock("@spaarke/ui-components/components/Wizard", () => ({
  WizardShell: React.forwardRef((props: any, _ref: unknown) => {
    mockUploadShell.props = props;
    return <div data-testid="upload-shell" />;
  }),
}));

import { DocumentUploadWizardDialog } from "../../../../../DocumentUploadWizard/src/DocumentUploadWizardDialog";
import { buildSuccessConfig } from "../../../../../DocumentUploadWizard/src/components/SuccessScreen";

let windowClose: jest.SpyInstance;

beforeEach(() => {
  windowClose = jest.spyOn(window, "close").mockImplementation(() => undefined);
  mockUploadShell.props = null;
});

afterEach(() => {
  windowClose.mockRestore();
});

function inHost(node: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

describe("DocumentUploadWizardDialog — hosting props", () => {
  const parent = { parentEntityType: "sprk_document", parentEntityId: "", parentEntityName: "" };

  it("in-app (embedded=false): the shell gets a title bar, uiScale and the host onClose; no layout wrapper is left in the host tree", () => {
    const onClose = jest.fn();
    const { container } = inHost(
      <DocumentUploadWizardDialog {...parent} onClose={onClose} embedded={false} uiScale={2} bffBaseUrl="https://bff.example" />
    );

    const p = mockUploadShell.props;
    expect(p.embedded).toBe(false);
    expect(p.hideTitle).toBe(false);
    expect(p.uiScale).toBe(2);
    expect(p.onClose).toBe(onClose);
    expect(p).not.toHaveProperty("size");
    expect(p).not.toHaveProperty("dismiss");
    expect(p).not.toHaveProperty("maxWidth");
    expect(p).not.toHaveProperty("height");
    // Only the (mocked) shell is rendered: no `styles.root` 100%x100% wrapper div around it.
    expect(container.firstElementChild?.firstElementChild).toBe(screen.getByTestId("upload-shell"));
  });

  it("the code page (default embedded=true) keeps the embedded, title-less shell and its wrapper", () => {
    (window as any).__SPAARKE_BFF_BASE_URL__ = "https://bff.example";
    try {
      inHost(<DocumentUploadWizardDialog {...parent} onClose={jest.fn()} />);
    } finally {
      delete (window as any).__SPAARKE_BFF_BASE_URL__;
    }
    expect(mockUploadShell.props.embedded).toBe(true);
    expect(mockUploadShell.props.hideTitle).toBe(true);
    expect(mockUploadShell.props).not.toHaveProperty("uiScale");
    expect(screen.getByTestId("upload-shell").parentElement).not.toBeNull();
  });

  it("the success screen Done never calls window.close in-app, and still does on the code page", () => {
    const onClose = jest.fn();

    const inApp = buildSuccessConfig({ uploadResults: null, onClose, closeWindow: false });
    const first = render(<div>{inApp.actions}</div>);
    fireEvent.click(screen.getByRole("button", { name: /Done/ }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(windowClose).not.toHaveBeenCalled();
    first.unmount();

    const codePage = buildSuccessConfig({ uploadResults: null, onClose });
    render(<div>{codePage.actions}</div>);
    fireEvent.click(screen.getByRole("button", { name: /Done/ }));
    expect(onClose).toHaveBeenCalledTimes(2);
    expect(windowClose).toHaveBeenCalledTimes(1);
  });
});

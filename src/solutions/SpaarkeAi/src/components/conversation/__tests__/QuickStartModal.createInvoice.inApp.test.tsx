/**
 * QuickStartModal.createInvoice.inApp.test.tsx — ebilling-r1 def071 (GitHub #3335): the Console's Quick Start has a
 * "Create Invoice" card that opens the Create Invoice wizard IN-APP (the mounted InAppWizardHost; ADR-050 launch rule (a)),
 * not as a separate navigateTo page.
 *
 * Runs the REAL chain: QuickStartModal -> real `launchCreateInvoiceWizard` -> real `InAppWizardHost` -> real `CreateInvoiceWizard`.
 *
 * Classification (ADR-038 §7): MAINTAIN.
 */
import "@testing-library/jest-dom";
import * as React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { InAppWizardHost } from "@spaarke/ui-components";

jest.mock("../../../config/runtimeConfig", () => ({ getBffBaseUrl: () => "https://test-bff.example.com" }));

import { QuickStartModal } from "../QuickStartModal";

let navigateTo: jest.Mock;
const authenticatedFetch = jest.fn(async () => ({ ok: true, status: 200, json: async () => [] }));

// The real WizardShell / SprkModal measure with ResizeObserver, which jsdom lacks.
beforeAll(() => {
  (window as unknown as { ResizeObserver: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
});

beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = {
    WebApi: { retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }) },
    Utility: { lookupObjects: jest.fn() },
    Navigation: { navigateTo },
  };
  sessionStorage.clear();
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

function mount(withHost: boolean) {
  render(
    <FluentProvider theme={webLightTheme}>
      {withHost && (
        <InAppWizardHost authenticatedFetch={authenticatedFetch as never} bffBaseUrl="https://test-bff.example.com" />
      )}
      <QuickStartModal open onClose={jest.fn()} />
    </FluentProvider>
  );
}

describe("Quick Start — Create Invoice (#3335)", () => {
  it("the card is in the Create tab", () => {
    mount(true);
    expect(screen.getByText("Create Invoice")).toBeInTheDocument();
  });

  it("clicking it opens the Create Invoice wizard in-app, at the Record step with the lookup, and never navigateTo", async () => {
    mount(true);
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Invoice"));

    expect(await screen.findByText("Choose the record this invoice is for")).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: /choose a record/i })).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it("must not fire in-app without a host: the click falls back to the navigateTo dialog for sprk_createinvoicewizard", async () => {
    mount(false);
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Invoice"));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0]).toMatchObject({
      pageType: "webresource",
      webresourceName: "sprk_createinvoicewizard",
    });
  });

  it("must not fire: the other cards do not open the invoice wizard", async () => {
    mount(true);
    const user = userEvent.setup();

    await user.click(screen.getByText("Find Similar"));

    expect(screen.queryByText("Choose the record this invoice is for")).toBeNull();
  });
});

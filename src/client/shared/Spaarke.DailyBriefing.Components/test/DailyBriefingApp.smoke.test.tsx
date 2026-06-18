/**
 * DailyBriefingApp smoke test — R2 task 019 / NFR-05.
 *
 * Asserts:
 *   1. `DailyBriefingApp` mounts cleanly with a mocked `Xrm` global.
 *   2. After channels load, the BFF `/narrate` fetch fires (`authenticatedFetch`
 *      is called with `/api/ai/daily-briefing/narrate` + a NON-empty JSON body
 *      containing `categories` / `channels`).
 *   3. At least one channel (the "Overdue Tasks" group) renders in the DOM.
 *
 * This is intentionally a SMOKE test — not a full integration test. It uses
 * Jest module mocks at the service boundary so the test doesn't depend on
 * Dataverse / MSAL / Fluent v9 internals.
 *
 * Independent mocks (per spec constraint): no module-level state is shared
 * across tests. Each `it` block resets its own mocks via `beforeEach`.
 */

import * as React from "react";
import { render, waitFor, screen } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

// ---- Mock the @spaarke/auth peer dep (routed by jest.config moduleNameMapper)
// ----  test/__mocks__/spaarke-auth.ts exports a jest.fn() authenticatedFetch.

// ---- Mock the data-layer services so the smoke test doesn't need Dataverse.
jest.mock("../src/services/notificationService", () => ({
  fetchAndGroupNotifications: jest.fn(),
  markNotificationRead: jest.fn(),
  markAllNotificationsRead: jest.fn(),
}));

jest.mock("../src/services/preferencesService", () => ({
  fetchDigestPreferences: jest.fn(() =>
    Promise.resolve({
      success: true,
      data: {
        preferences: {
          disabledChannels: [],
          dueWithinDays: 3,
          timeWindow: "24h",
          minConfidence: 75,
          autoPopup: true,
        },
        recordId: "rec-1",
      },
    })
  ),
  saveDigestPreferences: jest.fn(() =>
    Promise.resolve({ success: true, data: "rec-1" })
  ),
}));

import { DailyBriefingApp } from "../src/components/DailyBriefingApp";
import { fetchAndGroupNotifications } from "../src/services/notificationService";
// Imported AFTER the mock so we get the mocked authenticatedFetch jest.fn().
import { authenticatedFetch } from "@spaarke/auth";
import type { ChannelFetchResult } from "../src/types/notifications";

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

const fakeChannels: ChannelFetchResult[] = [
  {
    status: "success",
    group: {
      meta: {
        category: "tasks-overdue",
        label: "Overdue Tasks",
        iconName: "Warning",
        order: 1,
      },
      items: [
        {
          id: "n-1",
          title: "Review motion to dismiss",
          body: "Motion is overdue.",
          category: "tasks-overdue",
          priority: "high",
          actionUrl: "/main.aspx?etc=1&id=abc",
          regardingName: "Acme Matter",
          regardingEntityType: "sprk_matter",
          regardingId: "11111111-1111-1111-1111-111111111111",
          isRead: false,
          isAiGenerated: false,
          createdOn: new Date().toISOString(),
        },
      ],
      unreadCount: 1,
    },
  },
];

function installXrmGlobal(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Utility: {
      getGlobalContext: () => ({
        userSettings: {
          userId: "{00000000-0000-0000-0000-000000000001}",
        },
      }),
    },
  };
}

function uninstallXrmGlobal(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
}

function renderApp(): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DailyBriefingApp params={{}} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("DailyBriefingApp (smoke)", () => {
  beforeEach(() => {
    (fetchAndGroupNotifications as jest.Mock).mockReset();
    (fetchAndGroupNotifications as jest.Mock).mockResolvedValue(fakeChannels);
    (authenticatedFetch as jest.Mock).mockReset();
    (authenticatedFetch as jest.Mock).mockImplementation(() =>
      Promise.resolve(
        new Response(
          JSON.stringify({
            tldr: {
              highlights: ["You have 1 overdue motion."],
              confidence: 0.9,
            },
            channelNarratives: [
              {
                category: "tasks-overdue",
                bullets: [
                  {
                    narrative: "Review motion to dismiss for Acme Matter.",
                    itemIds: ["n-1"],
                    primaryEntityType: "sprk_matter",
                    primaryEntityId:
                      "11111111-1111-1111-1111-111111111111",
                    primaryEntityName: "Acme Matter",
                  },
                ],
              },
            ],
            generatedAtUtc: new Date().toISOString(),
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
    );
    installXrmGlobal();
  });

  afterEach(() => {
    uninstallXrmGlobal();
  });

  it("mounts and fires the /narrate fetch with a non-empty payload", async () => {
    renderApp();

    // Wait for the channels effect → narration effect chain to settle.
    await waitFor(
      () => {
        expect(authenticatedFetch).toHaveBeenCalled();
      },
      { timeout: 3000 }
    );

    // Assert /narrate was the endpoint
    const calls = (authenticatedFetch as jest.Mock).mock.calls;
    const narrateCall = calls.find((c) =>
      typeof c[0] === "string" && c[0].includes("/api/ai/daily-briefing/narrate")
    );
    expect(narrateCall).toBeDefined();

    // Assert non-empty JSON body with `categories` + `channels`
    const init = narrateCall![1] as RequestInit;
    expect(init.method).toBe("POST");
    expect(init.body).toBeTruthy();
    const parsed = JSON.parse(init.body as string);
    expect(parsed.categories).toBeDefined();
    expect(parsed.channels).toBeDefined();
    expect(Array.isArray(parsed.categories)).toBe(true);
    expect(Array.isArray(parsed.channels)).toBe(true);
    expect(parsed.categories.length + parsed.channels.length).toBeGreaterThan(0);
    expect(parsed.totalNotificationCount).toBeGreaterThan(0);
  });

  it("renders at least one channel after the digest loads", async () => {
    renderApp();

    // The Overdue Tasks channel meta should appear in the rendered DOM once
    // narratives resolve. We assert against the channel meta label rather
    // than channel-internal text so this test is resilient to bullet
    // re-arrangement.
    await waitFor(
      () => {
        const overdueMatches = screen.queryAllByText(/overdue tasks/i);
        expect(overdueMatches.length).toBeGreaterThan(0);
      },
      { timeout: 3000 }
    );
  });
});

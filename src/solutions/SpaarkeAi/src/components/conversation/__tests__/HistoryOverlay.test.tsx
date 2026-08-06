/**
 * HistoryOverlay.test.tsx — task 037 (FR-D6/D7/D8) unit tests.
 *
 * Verifies:
 *   1. The old "Promote…" up-arrow affordance is gone (FR-D6).
 *   2. Row body click opens the session (`onSelectSession`) and closes the menu (FR-D6).
 *   3. Each row's overflow (⋮) menu exposes exactly Open / Rename / Set related record / Delete
 *      (FR-D6), and "Set related record" is a no-op placeholder (does not call onSelectSession or
 *      throw — the slot task 034 wires).
 *   4. Delete opens a confirm dialog, calls the existing DELETE endpoint, and removes the row
 *      locally on success (FR-D6).
 *   5. Rename opens a dialog pre-filled with the current title, calls the existing PATCH endpoint
 *      with `{ title }`, and updates the row's displayed title on success (FR-D6).
 *   6. Rows are grouped under Today/Yesterday/This week/Older headers and search filters by
 *      title/preview (FR-D7/D8) — both the pure helpers and the rendered popover.
 *   7. Renders in light AND dark theme without crashing (ADR-021).
 *
 * @see HistoryOverlay.tsx — component under test
 * @see AssistantToolMenu.test.tsx — sibling Menu-dropdown test pattern this mirrors
 */

import "@testing-library/jest-dom";
import * as React from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from "@fluentui/react-components";
import type { AuthenticatedFetchFn } from "@spaarke/auth";

import {
  HistoryMenu,
  resolveGroupLabel,
  groupSessionsByRecency,
} from "../HistoryOverlay";

// ---------------------------------------------------------------------------
// Fixtures / helpers
// ---------------------------------------------------------------------------

const BFF_BASE_URL = "https://test-bff.example.com";

interface FetchScript {
  sessions?: unknown[];
  onDelete?: (url: string) => { ok: boolean; status: number };
  onPatch?: (url: string, body: string) => { ok: boolean; status: number };
}

function makeAuthenticatedFetch(script: FetchScript): jest.MockedFunction<AuthenticatedFetchFn> {
  return jest.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? "GET").toUpperCase();
    if (method === "GET") {
      return {
        ok: true,
        status: 200,
        json: async () => script.sessions ?? [],
      } as Response;
    }
    if (method === "DELETE") {
      const result = script.onDelete?.(url) ?? { ok: true, status: 204 };
      return { ok: result.ok, status: result.status, json: async () => ({}) } as Response;
    }
    if (method === "PATCH") {
      const body = typeof init?.body === "string" ? init.body : "";
      const result = script.onPatch?.(url, body) ?? { ok: true, status: 204 };
      return { ok: result.ok, status: result.status, json: async () => ({}) } as Response;
    }
    return { ok: false, status: 404, json: async () => ({}) } as Response;
  }) as unknown as jest.MockedFunction<AuthenticatedFetchFn>;
}

function renderMenu(options: {
  onSelectSession?: (sessionId: string) => void;
  fetchScript?: FetchScript;
  theme?: typeof webLightTheme;
}): { onSelectSession: jest.Mock; fetchMock: jest.MockedFunction<AuthenticatedFetchFn> } {
  const onSelectSession = jest.fn();
  const fetchMock = makeAuthenticatedFetch(options.fetchScript ?? { sessions: [] });
  render(
    <FluentProvider theme={options.theme ?? webLightTheme}>
      <HistoryMenu
        onSelectSession={options.onSelectSession ?? onSelectSession}
        bffBaseUrl={BFF_BASE_URL}
        authenticatedFetch={fetchMock}
      />
    </FluentProvider>
  );
  return { onSelectSession, fetchMock };
}

async function openHistoryMenu(): Promise<ReturnType<typeof userEvent.setup>> {
  const user = userEvent.setup();
  await user.click(screen.getByTestId("history-menu-trigger"));
  await screen.findByTestId("history-menu-popover");
  return user;
}

const nowIso = (): string => new Date().toISOString();
const daysAgoIso = (days: number): string =>
  new Date(Date.now() - days * 86_400_000).toISOString();

// ---------------------------------------------------------------------------
// Pure helper tests (FR-D8 grouping)
// ---------------------------------------------------------------------------

describe("resolveGroupLabel / groupSessionsByRecency (FR-D8)", () => {
  const fixedNow = new Date(2026, 6, 15, 12, 0, 0); // local: 15 Jul 2026, noon

  it("labels same-calendar-day timestamps as Today regardless of hour", () => {
    expect(resolveGroupLabel(new Date(2026, 6, 15, 0, 1).toISOString(), fixedNow)).toBe("Today");
    expect(resolveGroupLabel(new Date(2026, 6, 15, 23, 59).toISOString(), fixedNow)).toBe("Today");
  });

  it("labels the prior calendar day as Yesterday", () => {
    expect(resolveGroupLabel(new Date(2026, 6, 14, 8, 0).toISOString(), fixedNow)).toBe("Yesterday");
  });

  it("labels 2-7 calendar days back as This week", () => {
    expect(resolveGroupLabel(new Date(2026, 6, 13).toISOString(), fixedNow)).toBe("This week");
    expect(resolveGroupLabel(new Date(2026, 6, 8).toISOString(), fixedNow)).toBe("This week");
  });

  it("labels more than 7 days back as Older", () => {
    expect(resolveGroupLabel(new Date(2026, 6, 7).toISOString(), fixedNow)).toBe("Older");
  });

  it("falls back to Older for an unparseable timestamp", () => {
    expect(resolveGroupLabel("not-a-date", fixedNow)).toBe("Older");
  });

  it("groups rows into non-empty buckets in Today/Yesterday/This week/Older order", () => {
    const rows = [
      { sessionId: "1", title: "A", lastMessageAt: daysAgoIso(0) },
      { sessionId: "2", title: "B", lastMessageAt: daysAgoIso(10) },
    ];
    const groups = groupSessionsByRecency(rows);
    expect(groups.map((g) => g.label)).toEqual(["Today", "Older"]);
    expect(groups[0].rows.map((r) => r.sessionId)).toEqual(["1"]);
    expect(groups[1].rows.map((r) => r.sessionId)).toEqual(["2"]);
  });
});

// ---------------------------------------------------------------------------
// Component tests
// ---------------------------------------------------------------------------

describe("HistoryMenu (task 037, FR-D6/D7/D8)", () => {
  const sessions = [
    {
      sessionId: "s1",
      title: "Review the Acme NDA",
      lastMessageAt: nowIso(),
      conversationSummary: "Discussed indemnification carve-outs.",
      messageCount: 6,
      tabs: ["Email", "Compose"],
    },
    {
      sessionId: "s2",
      title: "Old matter question",
      lastMessageAt: daysAgoIso(10),
    },
  ];

  // -------------------------------------------------------------------------
  // FR-D6 — up-arrow removed
  // -------------------------------------------------------------------------

  it("does not render a Promote / up-arrow affordance anywhere", async () => {
    renderMenu({ fetchScript: { sessions } });
    await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    expect(screen.queryByLabelText(/promote/i)).not.toBeInTheDocument();
    expect(screen.queryByTestId(/history-menu-promote-/)).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // FR-D6 — row body opens
  // -------------------------------------------------------------------------

  it("opens the session and closes the menu when the row body is clicked", async () => {
    const { onSelectSession } = renderMenu({ fetchScript: { sessions } });
    const user = await openHistoryMenu();

    await user.click(await screen.findByText("Review the Acme NDA"));

    expect(onSelectSession).toHaveBeenCalledWith("s1");
    expect(screen.queryByTestId("history-menu-popover")).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // FR-D6 — overflow menu contents
  // -------------------------------------------------------------------------

  it("shows exactly Open / Rename / Set related record / Delete in the row overflow menu", async () => {
    renderMenu({ fetchScript: { sessions } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    await user.click(screen.getByTestId("history-menu-overflow-s1"));

    expect(await screen.findByTestId("history-menu-overflow-open-s1")).toBeInTheDocument();
    expect(screen.getByTestId("history-menu-overflow-rename-s1")).toBeInTheDocument();
    expect(screen.getByTestId("history-menu-overflow-set-related-s1")).toBeInTheDocument();
    expect(screen.getByTestId("history-menu-overflow-delete-s1")).toBeInTheDocument();
  });

  it("'Set related record' is a no-op placeholder — does not open the session or throw", async () => {
    const { onSelectSession } = renderMenu({ fetchScript: { sessions } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");
    await user.click(screen.getByTestId("history-menu-overflow-s1"));

    await expect(
      user.click(await screen.findByTestId("history-menu-overflow-set-related-s1"))
    ).resolves.not.toThrow();

    expect(onSelectSession).not.toHaveBeenCalled();
  });

  it("the overflow 'Open' item also opens the session", async () => {
    const { onSelectSession } = renderMenu({ fetchScript: { sessions } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");
    await user.click(screen.getByTestId("history-menu-overflow-s1"));

    await user.click(await screen.findByTestId("history-menu-overflow-open-s1"));

    expect(onSelectSession).toHaveBeenCalledWith("s1");
  });

  // -------------------------------------------------------------------------
  // FR-D6 — Delete wired to the existing DELETE endpoint
  // -------------------------------------------------------------------------

  it("deletes a session via the existing DELETE endpoint and removes the row", async () => {
    const onDelete = jest.fn().mockReturnValue({ ok: true, status: 204 });
    renderMenu({ fetchScript: { sessions, onDelete } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    await user.click(screen.getByTestId("history-menu-overflow-s1"));
    await user.click(await screen.findByTestId("history-menu-overflow-delete-s1"));

    // Confirm dialog appears.
    expect(await screen.findByTestId("delete-session-confirm")).toBeInTheDocument();
    await user.click(screen.getByTestId("delete-session-confirm"));

    await waitFor(() => expect(onDelete).toHaveBeenCalledTimes(1));
    expect(onDelete.mock.calls[0][0]).toContain("/api/ai/chat/sessions/s1");
    await waitFor(() =>
      expect(screen.queryByText("Review the Acme NDA")).not.toBeInTheDocument()
    );
  });

  it("shows an error and keeps the row when Delete fails", async () => {
    const onDelete = jest.fn().mockReturnValue({ ok: false, status: 500 });
    renderMenu({ fetchScript: { sessions, onDelete } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    await user.click(screen.getByTestId("history-menu-overflow-s1"));
    await user.click(await screen.findByTestId("history-menu-overflow-delete-s1"));
    await user.click(await screen.findByTestId("delete-session-confirm"));

    expect(await screen.findByRole("alert")).toHaveTextContent(/couldn't delete/i);
    expect(screen.getByText("Review the Acme NDA")).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // FR-D6 — Rename wired to the existing PATCH endpoint
  // -------------------------------------------------------------------------

  it("renames a session via the existing PATCH endpoint and updates the row title", async () => {
    const onPatch = jest.fn().mockReturnValue({ ok: true, status: 204 });
    renderMenu({ fetchScript: { sessions, onPatch } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    await user.click(screen.getByTestId("history-menu-overflow-s1"));
    await user.click(await screen.findByTestId("history-menu-overflow-rename-s1"));

    const input = await screen.findByTestId("rename-session-title-input");
    expect(input).toHaveValue("Review the Acme NDA");
    await user.clear(input);
    await user.type(input, "Acme NDA — final pass");
    await user.click(screen.getByTestId("rename-session-confirm"));

    await waitFor(() => expect(onPatch).toHaveBeenCalledTimes(1));
    const [url, body] = onPatch.mock.calls[0];
    expect(url).toContain("/api/ai/chat/sessions/s1");
    expect(JSON.parse(body)).toEqual({ title: "Acme NDA — final pass" });
    await screen.findByText("Acme NDA — final pass");
  });

  // -------------------------------------------------------------------------
  // FR-D7 — preview / message count / tab summary
  // -------------------------------------------------------------------------

  it("renders preview, message count, and tab summary when the BFF supplies them", async () => {
    renderMenu({ fetchScript: { sessions } });
    await openHistoryMenu();
    const row = await screen.findByTestId("history-menu-item-s1");

    expect(within(row).getByText(/6 messages/)).toBeInTheDocument();
    expect(within(row).getByText(/Email · Compose/)).toBeInTheDocument();
    expect(within(row).getByText(/indemnification carve-outs/)).toBeInTheDocument();
  });

  it("omits the meta/preview line gracefully when the BFF does not supply FR-D7 fields", async () => {
    renderMenu({ fetchScript: { sessions } });
    await openHistoryMenu();
    const row = await screen.findByTestId("history-menu-item-s2");

    expect(within(row).queryByText(/undefined/)).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // FR-D8 — grouping + search
  // -------------------------------------------------------------------------

  it("groups rows under Today/Older headers", async () => {
    renderMenu({ fetchScript: { sessions } });
    await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    expect(screen.getByText("Today")).toBeInTheDocument();
    expect(screen.getByText("Older")).toBeInTheDocument();
  });

  it("filters rows by title via the search box", async () => {
    renderMenu({ fetchScript: { sessions } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    await user.type(screen.getByTestId("history-menu-search"), "Acme");

    expect(screen.getByText("Review the Acme NDA")).toBeInTheDocument();
    expect(screen.queryByText("Old matter question")).not.toBeInTheDocument();
  });

  it("filters rows by preview text via the search box", async () => {
    renderMenu({ fetchScript: { sessions } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    await user.type(screen.getByTestId("history-menu-search"), "indemnification");

    expect(screen.getByText("Review the Acme NDA")).toBeInTheDocument();
    expect(screen.queryByText("Old matter question")).not.toBeInTheDocument();
  });

  it("shows a no-matches message when search excludes every row", async () => {
    renderMenu({ fetchScript: { sessions } });
    const user = await openHistoryMenu();
    await screen.findByText("Review the Acme NDA");

    await user.type(screen.getByTestId("history-menu-search"), "zzz-no-match-zzz");

    expect(await screen.findByText(/no conversations match/i)).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Dark-mode parity (ADR-021)
  // -------------------------------------------------------------------------

  it("renders in dark theme without crashing (ADR-021 token usage)", async () => {
    renderMenu({ fetchScript: { sessions }, theme: webDarkTheme });
    await openHistoryMenu();
    expect(await screen.findByText("Review the Acme NDA")).toBeInTheDocument();
  });

  it("renders in light theme as the baseline (ADR-021 parity check)", async () => {
    renderMenu({ fetchScript: { sessions }, theme: webLightTheme });
    await openHistoryMenu();
    expect(await screen.findByText("Review the Acme NDA")).toBeInTheDocument();
  });
});

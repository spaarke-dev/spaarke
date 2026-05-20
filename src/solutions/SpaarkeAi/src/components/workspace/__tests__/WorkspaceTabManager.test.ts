/**
 * WorkspaceTabManager — unit tests
 *
 * Covers tab lifecycle and FR-13 acceptance criteria:
 *   - Adding tabs 1..MAX_WORKSPACE_TABS works normally (no eviction).
 *   - Adding the (MAX_WORKSPACE_TABS + 1)th tab evicts the oldest non-Home tab (FIFO).
 *   - Home tab (kind === 'home') is exempt from the cap and from FIFO eviction.
 *   - Home tab cannot be closed via closeTab.
 *   - clearAllTabs preserves Home.
 *   - closeTab removes the correct non-Home tab.
 *   - closeTab on the active tab selects a sensible successor.
 *   - updateTab updates data without changing identity or order.
 *   - setActiveTab and getActiveTab work correctly.
 *   - getSnapshot returns a shallow copy (mutations to result don't affect manager).
 */

import { WorkspaceTabManager, MAX_WORKSPACE_TABS } from "../WorkspaceTabManager";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeManager(): WorkspaceTabManager {
  return new WorkspaceTabManager();
}

// ---------------------------------------------------------------------------
// MAX_WORKSPACE_TABS constant — single source of truth (FR-13, ADR-012)
// ---------------------------------------------------------------------------

describe("MAX_WORKSPACE_TABS", () => {
  it("is exported as a numeric constant equal to 8", () => {
    expect(MAX_WORKSPACE_TABS).toBe(8);
  });

  it("is a number type at runtime", () => {
    expect(typeof MAX_WORKSPACE_TABS).toBe("number");
  });
});

// ---------------------------------------------------------------------------
// addTab — normal cases (under the cap)
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — addTab (normal, under limit)", () => {
  it("adds the first tab and makes it active", () => {
    const mgr = makeManager();
    const id = mgr.addTab("document-summary", { foo: 1 });

    const { tabs, activeTabId } = mgr.getSnapshot();
    expect(tabs).toHaveLength(1);
    expect(tabs[0].id).toBe(id);
    expect(tabs[0].kind).toBe("widget");
    expect(tabs[0].widgetType).toBe("document-summary");
    expect(tabs[0].widgetData).toEqual({ foo: 1 });
    expect(tabs[0].isLoading).toBe(true);
    expect(activeTabId).toBe(id);
  });

  it("adds tabs up to MAX_WORKSPACE_TABS without eviction", () => {
    const mgr = makeManager();
    const ids: string[] = [];

    for (let i = 1; i <= MAX_WORKSPACE_TABS; i++) {
      ids.push(mgr.addTab(`widget-${i}`, { index: i }));
    }

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(MAX_WORKSPACE_TABS);

    // All added tabs must be present.
    for (const id of ids) {
      expect(tabs.some((t) => t.id === id)).toBe(true);
    }
  });

  it("stores the correct widgetType and displayName", () => {
    const mgr = makeManager();
    mgr.addTab("clause-list", { clauses: [] }, "Clause List");

    const { tabs } = mgr.getSnapshot();
    expect(tabs[0].widgetType).toBe("clause-list");
    expect(tabs[0].displayName).toBe("Clause List");
  });

  it("falls back to widgetType when displayName is omitted", () => {
    const mgr = makeManager();
    mgr.addTab("redline-viewer", null);

    const { tabs } = mgr.getSnapshot();
    expect(tabs[0].displayName).toBe("redline-viewer");
  });

  it("makes the newly added tab active each time", () => {
    const mgr = makeManager();
    mgr.addTab("widget-1", null);
    const id2 = mgr.addTab("widget-2", null);
    const id3 = mgr.addTab("widget-3", null);

    expect(mgr.getSnapshot().activeTabId).toBe(id3);
    // Verify id2 was also set active when it was added (by re-checking via close).
    mgr.closeTab(id3);
    // After closing the last one, next-to-last should be active.
    expect(mgr.getSnapshot().activeTabId).toBe(id2);
  });
});

// ---------------------------------------------------------------------------
// addTab — overflow (FIFO eviction at MAX_WORKSPACE_TABS + 1)
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — addTab overflow (FIFO at the cap)", () => {
  it("evicts the oldest non-Home tab when a (MAX_WORKSPACE_TABS + 1)th tab is added", () => {
    const mgr = makeManager();
    const oldestId = mgr.addTab("widget-1", null);
    // Fill to capacity.
    for (let i = 2; i <= MAX_WORKSPACE_TABS; i++) {
      mgr.addTab(`widget-${i}`, null);
    }
    // The overflow add — should evict oldestId.
    mgr.addTab("widget-overflow", null);

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(MAX_WORKSPACE_TABS);
    expect(tabs.some((t) => t.id === oldestId)).toBe(false);
  });

  it("FR-13 boundary: the 9th non-Home widget evicts the oldest non-Home tab", () => {
    // Direct boundary test for FR-13 acceptance using literal 9 to assert the
    // documented threshold — not the constant — so a future change to the
    // constant requires a deliberate test update.
    const mgr = makeManager();
    const widgetIds: string[] = [];

    for (let i = 1; i <= 8; i++) {
      widgetIds.push(mgr.addTab(`widget-${i}`, null));
    }
    expect(mgr.getSnapshot().tabs).toHaveLength(8);

    // 9th non-Home add — must evict widget-1 (oldest) and keep widgets 2..8 + new one.
    const ninthId = mgr.addTab("widget-9", null);

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(8);
    expect(tabs.some((t) => t.id === widgetIds[0])).toBe(false); // oldest evicted
    expect(tabs.some((t) => t.id === ninthId)).toBe(true);       // newest present

    // Verify FIFO order is preserved (widgets 2..8 + 9).
    expect(tabs.map((t) => t.widgetType)).toEqual([
      "widget-2",
      "widget-3",
      "widget-4",
      "widget-5",
      "widget-6",
      "widget-7",
      "widget-8",
      "widget-9",
    ]);
  });

  it("the new (overflow) tab becomes active after eviction", () => {
    const mgr = makeManager();
    for (let i = 1; i <= MAX_WORKSPACE_TABS; i++) {
      mgr.addTab(`widget-${i}`, null);
    }
    const newestId = mgr.addTab("widget-overflow", null);

    expect(mgr.getSnapshot().activeTabId).toBe(newestId);
  });

  it("maintains MAX_WORKSPACE_TABS after multiple overflows", () => {
    const mgr = makeManager();
    for (let i = 1; i <= MAX_WORKSPACE_TABS + 10; i++) {
      mgr.addTab(`widget-${i}`, null);
    }

    expect(mgr.getSnapshot().tabs).toHaveLength(MAX_WORKSPACE_TABS);
  });

  it("preserves insertion order after overflow", () => {
    const mgr = makeManager();
    // First one will be evicted.
    mgr.addTab("widget-evicted", null);
    const survivorIds: string[] = [];
    for (let i = 2; i <= MAX_WORKSPACE_TABS; i++) {
      survivorIds.push(mgr.addTab(`widget-${i}`, null));
    }
    const newestId = mgr.addTab("widget-newest", null);

    const { tabs } = mgr.getSnapshot();
    expect(tabs.map((t) => t.id)).toEqual([...survivorIds, newestId]);
  });
});

// ---------------------------------------------------------------------------
// FR-13 Home-tab exemption — Home is never evicted; cap counts only non-Home tabs
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — Home tab exemption (FR-13)", () => {
  it("ensureHomeTab adds Home at index 0 with kind 'home'", () => {
    const mgr = makeManager();
    const homeId = mgr.ensureHomeTab("Home Workspace");

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(1);
    expect(tabs[0].id).toBe(homeId);
    expect(tabs[0].kind).toBe("home");
    expect(tabs[0].displayName).toBe("Home Workspace");
  });

  it("ensureHomeTab is idempotent — second call updates rather than duplicates", () => {
    const mgr = makeManager();
    const firstId = mgr.ensureHomeTab("Home v1", { layout: "a" });
    const secondId = mgr.ensureHomeTab("Home v2", { layout: "b" });

    expect(firstId).toBe(secondId);
    const { tabs } = mgr.getSnapshot();
    expect(tabs.filter((t) => t.kind === "home")).toHaveLength(1);
    expect(tabs[0].displayName).toBe("Home v2");
    expect(tabs[0].widgetData).toEqual({ layout: "b" });
  });

  it("Home tab does not count against MAX_WORKSPACE_TABS — 8 non-Home tabs + Home = 9 total", () => {
    const mgr = makeManager();
    mgr.ensureHomeTab();
    for (let i = 1; i <= MAX_WORKSPACE_TABS; i++) {
      mgr.addTab(`widget-${i}`, null);
    }

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(MAX_WORKSPACE_TABS + 1); // 8 widgets + 1 Home
    expect(tabs.filter((t) => t.kind === "home")).toHaveLength(1);
    expect(tabs.filter((t) => t.kind === "widget")).toHaveLength(MAX_WORKSPACE_TABS);
  });

  it("FR-13 boundary: 9th non-Home widget evicts the oldest non-Home — Home is preserved", () => {
    const mgr = makeManager();
    const homeId = mgr.ensureHomeTab();
    const oldestWidgetId = mgr.addTab("widget-1", null);
    for (let i = 2; i <= 8; i++) {
      mgr.addTab(`widget-${i}`, null);
    }

    // 9th non-Home add — must evict widget-1 (oldest non-Home), NOT Home.
    const ninthId = mgr.addTab("widget-9", null);

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(9); // 8 widgets + Home
    expect(tabs.some((t) => t.id === oldestWidgetId)).toBe(false); // evicted
    expect(tabs.some((t) => t.id === ninthId)).toBe(true);         // newest present
    expect(tabs.some((t) => t.id === homeId)).toBe(true);          // Home preserved
    expect(tabs.find((t) => t.id === homeId)?.kind).toBe("home");
  });

  it("Home tab survives 20 sequential non-Home adds (NFR-09 stability)", () => {
    const mgr = makeManager();
    const homeId = mgr.ensureHomeTab();

    for (let i = 1; i <= 20; i++) {
      mgr.addTab(`widget-${i}`, null);
    }

    const { tabs } = mgr.getSnapshot();
    // After 20 non-Home adds: exactly MAX_WORKSPACE_TABS non-Home tabs + Home.
    expect(tabs.filter((t) => t.kind === "widget")).toHaveLength(MAX_WORKSPACE_TABS);
    expect(tabs.some((t) => t.id === homeId)).toBe(true);
    expect(tabs.find((t) => t.id === homeId)?.kind).toBe("home");
  });

  it("Home tab survives 50 adds and remains at index 0", () => {
    const mgr = makeManager();
    const homeId = mgr.ensureHomeTab();

    for (let i = 1; i <= 50; i++) {
      mgr.addTab(`widget-${i}`, null);
    }

    const { tabs } = mgr.getSnapshot();
    expect(tabs[0].id).toBe(homeId);
    expect(tabs[0].kind).toBe("home");
  });

  it("closeTab is a no-op for the Home tab", () => {
    const mgr = makeManager();
    const homeId = mgr.ensureHomeTab();
    mgr.addTab("widget-1", null);

    const result = mgr.closeTab(homeId);

    const { tabs } = mgr.getSnapshot();
    expect(tabs.some((t) => t.id === homeId)).toBe(true); // still present
    // closeTab returns the current active tab id when no change occurs.
    expect(result).toBe(mgr.getSnapshot().activeTabId);
  });

  it("clearAllTabs removes non-Home tabs but preserves Home", () => {
    const mgr = makeManager();
    const homeId = mgr.ensureHomeTab();
    mgr.addTab("widget-1", null);
    mgr.addTab("widget-2", null);

    const removed = mgr.clearAllTabs();

    const { tabs, activeTabId } = mgr.getSnapshot();
    expect(removed).toBe(2);
    expect(tabs).toHaveLength(1);
    expect(tabs[0].id).toBe(homeId);
    expect(activeTabId).toBe(homeId);
  });

  it("clearAllTabs returns 0 and leaves state empty when no Home and no widgets", () => {
    const mgr = makeManager();
    const removed = mgr.clearAllTabs();

    expect(removed).toBe(0);
    expect(mgr.getSnapshot().tabs).toHaveLength(0);
    expect(mgr.getSnapshot().activeTabId).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// closeTab
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — closeTab", () => {
  it("removes the correct tab by id", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);
    const id2 = mgr.addTab("widget-2", null);
    const id3 = mgr.addTab("widget-3", null);

    mgr.closeTab(id2);

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(2);
    expect(tabs.some((t) => t.id === id2)).toBe(false);
    expect(tabs.some((t) => t.id === id1)).toBe(true);
    expect(tabs.some((t) => t.id === id3)).toBe(true);
  });

  it("selects the right neighbour when the active tab is closed", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);
    const id2 = mgr.addTab("widget-2", null);
    const id3 = mgr.addTab("widget-3", null);

    // id3 is active. Close it → id3 has no right neighbour → selects previous (id2).
    void id1; // suppress unused warning
    mgr.closeTab(id3);

    expect(mgr.getSnapshot().activeTabId).toBe(id2);
  });

  it("selects the right neighbour when a non-last active tab is closed", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);
    const id2 = mgr.addTab("widget-2", null);
    const id3 = mgr.addTab("widget-3", null);

    // Activate the middle tab.
    mgr.setActiveTab(id2);
    mgr.closeTab(id2); // was at index 1 → right neighbour is now index 1 = id3

    expect(mgr.getSnapshot().activeTabId).toBe(id3);
    void id1;
  });

  it("sets activeTabId to null when the last tab is closed", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);

    mgr.closeTab(id1);

    const { tabs, activeTabId } = mgr.getSnapshot();
    expect(tabs).toHaveLength(0);
    expect(activeTabId).toBeNull();
  });

  it("does not change active tab when a non-active tab is closed", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);
    const id2 = mgr.addTab("widget-2", null);
    const id3 = mgr.addTab("widget-3", null);

    // id3 is active. Close id1 (not active).
    mgr.closeTab(id1);

    expect(mgr.getSnapshot().activeTabId).toBe(id3);
    void id2;
  });

  it("is a no-op for an unknown tab id", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);

    mgr.closeTab("non-existent-tab-id");

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(1);
    expect(tabs[0].id).toBe(id1);
  });
});

// ---------------------------------------------------------------------------
// updateTab
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — updateTab", () => {
  it("updates widgetData without changing the tab's id or order", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", { v: 1 });
    const id2 = mgr.addTab("widget-2", { v: 2 });

    mgr.updateTab(id1, { v: 99 });

    const { tabs } = mgr.getSnapshot();
    expect(tabs[0].id).toBe(id1);
    expect(tabs[0].widgetData).toEqual({ v: 99 });
    expect(tabs[1].id).toBe(id2); // unchanged
  });

  it("is a no-op for an unknown tab id", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", { v: 1 });

    mgr.updateTab("ghost-tab", { v: 99 });

    expect(mgr.getSnapshot().tabs[0].widgetData).toEqual({ v: 1 });
    void id1;
  });
});

// ---------------------------------------------------------------------------
// resolveTabComponent
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — resolveTabComponent", () => {
  it("clears isLoading and sets the Component reference", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const FakeComponent = (): null => null as any;
    mgr.resolveTabComponent(id1, FakeComponent as never);

    const { tabs } = mgr.getSnapshot();
    expect(tabs[0].isLoading).toBe(false);
    expect(tabs[0].Component).toBe(FakeComponent);
  });

  it("updates the displayName when provided", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null, "widget-1");

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const FakeComponent = (): null => null as any;
    mgr.resolveTabComponent(id1, FakeComponent as never, "Pretty Name");

    expect(mgr.getSnapshot().tabs[0].displayName).toBe("Pretty Name");
  });
});

// ---------------------------------------------------------------------------
// setActiveTab / getActiveTab
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — setActiveTab / getActiveTab", () => {
  it("sets the active tab to the given id", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);
    const id2 = mgr.addTab("widget-2", null);

    mgr.setActiveTab(id1);

    expect(mgr.getSnapshot().activeTabId).toBe(id1);
    void id2;
  });

  it("is a no-op for an unknown id", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);
    mgr.addTab("widget-2", null);

    mgr.setActiveTab(id1);        // set to id1
    mgr.setActiveTab("ghost");    // should not change

    expect(mgr.getSnapshot().activeTabId).toBe(id1);
  });

  it("getActiveTab returns the active tab record", () => {
    const mgr = makeManager();
    mgr.addTab("widget-1", null);
    const id2 = mgr.addTab("widget-2", { payload: true });

    const active = mgr.getActiveTab();
    expect(active?.id).toBe(id2);
    expect(active?.widgetData).toEqual({ payload: true });
  });

  it("getActiveTab returns null when no tabs exist", () => {
    const mgr = makeManager();
    expect(mgr.getActiveTab()).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// getSnapshot — immutability
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — getSnapshot immutability", () => {
  it("mutations to the snapshot array do not affect the manager", () => {
    const mgr = makeManager();
    mgr.addTab("widget-1", null);

    const snapshot = mgr.getSnapshot();
    // Mutate the snapshot array.
    snapshot.tabs.pop();

    // The manager's internal state must be unchanged.
    expect(mgr.getSnapshot().tabs).toHaveLength(1);
  });
});

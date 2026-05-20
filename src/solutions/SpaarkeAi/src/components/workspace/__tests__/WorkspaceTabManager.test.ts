/**
 * WorkspaceTabManager — unit tests
 *
 * Covers the acceptance criteria from task AIPU2-077:
 *   - Adding tabs 1, 2, 3 works normally (no eviction).
 *   - Adding tab 4 removes the oldest and adds the new one.
 *   - closeTab removes the correct tab.
 *   - closeTab on the active tab selects a sensible successor.
 *   - updateTab updates data without changing identity or order.
 *   - setActiveTab and getActiveTab work correctly.
 *   - getSnapshot returns a shallow copy (mutations to result don't affect manager).
 */

import { WorkspaceTabManager, MAX_TABS } from "../WorkspaceTabManager";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeManager(): WorkspaceTabManager {
  return new WorkspaceTabManager();
}

// ---------------------------------------------------------------------------
// addTab — normal cases (1–3 tabs)
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — addTab (normal, under limit)", () => {
  it("adds the first tab and makes it active", () => {
    const mgr = makeManager();
    const id = mgr.addTab("document-summary", { foo: 1 });

    const { tabs, activeTabId } = mgr.getSnapshot();
    expect(tabs).toHaveLength(1);
    expect(tabs[0].id).toBe(id);
    expect(tabs[0].widgetType).toBe("document-summary");
    expect(tabs[0].widgetData).toEqual({ foo: 1 });
    expect(tabs[0].isLoading).toBe(true);
    expect(activeTabId).toBe(id);
  });

  it("adds tabs up to MAX_TABS without eviction", () => {
    const mgr = makeManager();
    const ids: string[] = [];

    for (let i = 1; i <= MAX_TABS; i++) {
      ids.push(mgr.addTab(`widget-${i}`, { index: i }));
    }

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(MAX_TABS);

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
// addTab — overflow (4th tab evicts oldest)
// ---------------------------------------------------------------------------

describe("WorkspaceTabManager — addTab overflow (4th tab)", () => {
  it("evicts the oldest tab when a 4th tab is added", () => {
    const mgr = makeManager();
    const id1 = mgr.addTab("widget-1", null);
    mgr.addTab("widget-2", null);
    mgr.addTab("widget-3", null);
    mgr.addTab("widget-4", null); // should evict id1

    const { tabs } = mgr.getSnapshot();
    expect(tabs).toHaveLength(MAX_TABS);
    expect(tabs.some((t) => t.id === id1)).toBe(false); // oldest gone
  });

  it("the new (4th) tab becomes active after overflow eviction", () => {
    const mgr = makeManager();
    mgr.addTab("widget-1", null);
    mgr.addTab("widget-2", null);
    mgr.addTab("widget-3", null);
    const id4 = mgr.addTab("widget-4", null);

    expect(mgr.getSnapshot().activeTabId).toBe(id4);
  });

  it("maintains MAX_TABS after multiple overflows", () => {
    const mgr = makeManager();
    for (let i = 1; i <= 7; i++) {
      mgr.addTab(`widget-${i}`, null);
    }

    expect(mgr.getSnapshot().tabs).toHaveLength(MAX_TABS);
  });

  it("preserves insertion order after overflow", () => {
    const mgr = makeManager();
    mgr.addTab("widget-1", null); // will be evicted
    const id2 = mgr.addTab("widget-2", null);
    const id3 = mgr.addTab("widget-3", null);
    const id4 = mgr.addTab("widget-4", null);

    const { tabs } = mgr.getSnapshot();
    expect(tabs.map((t) => t.id)).toEqual([id2, id3, id4]);
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
    // Wait — actually id2 is at index 1, and after removing id3 (index 2),
    // idx (2) >= tabs.length (2) → so we go to last → id2.
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

// ---------------------------------------------------------------------------
// MAX_TABS constant
// ---------------------------------------------------------------------------

describe("MAX_TABS", () => {
  it("is 3", () => {
    expect(MAX_TABS).toBe(3);
  });
});

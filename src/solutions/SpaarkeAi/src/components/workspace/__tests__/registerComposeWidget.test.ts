/**
 * registerComposeWidget.test.ts — Wave 5 (Compose first-class Direct widget).
 *
 * Verifies the ADDITIVE registration:
 *   - `'compose'` is registered in the WorkspaceWidgetRegistry (metadata + factory).
 *
 * The factory itself is intentionally NOT resolved here (that would import the
 * TipTap/mammoth compose chain). The former client-side visibility derivation
 * (`composeWidgetVisibility`) was deleted 2026-10-03 (C-21, #1112) — the BFF
 * derives agent-visible tab state server-side.
 */

import { getWorkspaceWidgetMetadata, hasWorkspaceWidget } from "@spaarke/ai-widgets";
// Side-effect import registers 'compose' into the shared registry.
import "../registerComposeWidget";

describe("Wave 5 — Compose Direct-widget registration", () => {
  it("registers 'compose' with metadata + a lazy factory", () => {
    expect(hasWorkspaceWidget("compose")).toBe(true);
    const meta = getWorkspaceWidgetMetadata("compose");
    expect(meta).toBeDefined();
    expect(meta?.displayName).toBe("Compose");
    expect(meta?.category).toBe("document");
    // allowMultiple=false mirrors the DEF-08 single-tab-reuse intent.
    expect(meta?.allowMultiple).toBe(false);
  });

  // FR-08 enumeration (task 022) → FR-15 ENFORCEMENT (task 050): Compose IS
  // enumerated in the widget-type ↔ context-type map (contextType: 'compose-doc',
  // task 020) but is outside R3's overview/per-item scope (spec Out-of-Scope:
  // Compose write/read fidelity is governed separately by ADR-049) — so it
  // declares an EXPLICIT assistantContract opt-out marker (required post-050),
  // not a silent absence.
  it("declares the 'compose-doc' contextType and an EXPLICIT assistantContract opt-out (task 022 → task 050 FR-15)", () => {
    const meta = getWorkspaceWidgetMetadata("compose");
    expect(meta).toBeDefined();
    expect(meta?.contextType).toBe("compose-doc");
    const declared = meta?.assistantContract as { optOut?: boolean; reason?: string };
    expect(declared.optOut).toBe(true);
    expect(typeof declared.reason).toBe("string");
    expect(declared.reason!.length).toBeGreaterThan(0);
  });
});

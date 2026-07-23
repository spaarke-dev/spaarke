/**
 * registerComposeWidget.test.ts — Wave 5 (Compose first-class Direct widget).
 *
 * Verifies the ADDITIVE registration:
 *   - `'compose'` is registered in the WorkspaceWidgetRegistry (metadata + factory).
 *   - Its `getVisibleState` (Pillar 9) projects the active-document identity onto
 *     the `DocumentViewer` variant for a compose widgetData, and returns null
 *     when there is no compose payload / no document identity.
 *
 * The factory itself is intentionally NOT resolved here (that would import the
 * TipTap/mammoth compose chain); presence of a registration + metadata + the
 * visibility fn is the Wave-5 deliverable per plan §Wave 5 acceptance.
 */

import {
  getWorkspaceWidgetMetadata,
  getWorkspaceWidgetVisibleStateFn,
  hasWorkspaceWidget,
} from "@spaarke/ai-widgets";
// Side-effect import registers 'compose' into the shared registry.
import { composeWidgetVisibility } from "../registerComposeWidget";
import type { ComposeWidgetData } from "../composeWidgetData";

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

  it("wires composeWidgetVisibility as the registry getVisibleState for 'compose'", () => {
    const fn = getWorkspaceWidgetVisibleStateFn("compose");
    expect(fn).toBe(composeWidgetVisibility);
  });

  describe("composeWidgetVisibility (getVisibleState)", () => {
    it("projects a stored-document seed onto the DocumentViewer variant", () => {
      const widgetData: ComposeWidgetData = {
        compose: {
          speDriveItemId: "drive-item-123",
          sprkDocumentId: "doc-guid-1",
          speDriveId: "drive-1",
          fileName: "engagement-letter.docx",
        },
      };
      const state = composeWidgetVisibility(widgetData);
      expect(state).not.toBeNull();
      expect(state?.widgetType).toBe("DocumentViewer");
      expect(state).toMatchObject({
        widgetType: "DocumentViewer",
        filename: "engagement-letter.docx",
        hasSelection: false,
      });
    });

    it("surfaces the file name from an upload-door seed", () => {
      const widgetData: ComposeWidgetData = {
        compose: {
          upload: {
            sessionId: "sess-1",
            sessionFileId: "file-abc",
            fileName: "uploaded.docx",
          },
        },
      };
      const state = composeWidgetVisibility(widgetData);
      expect(state?.widgetType).toBe("DocumentViewer");
      // Narrow the closed union to the DocumentViewer variant before reading filename.
      if (state?.widgetType !== "DocumentViewer") throw new Error("expected DocumentViewer");
      expect(state.filename).toBe("uploaded.docx");
    });

    it("surfaces the file name from a draft-door seed", () => {
      const widgetData: ComposeWidgetData = {
        compose: {
          draft: { ledgerRef: "ledger-1", sessionId: "sess-2", fileName: "Draft memo.docx" },
        },
      };
      const state = composeWidgetVisibility(widgetData);
      expect(state?.widgetType).toBe("DocumentViewer");
      if (state?.widgetType !== "DocumentViewer") throw new Error("expected DocumentViewer");
      expect(state.filename).toBe("Draft memo.docx");
    });

    it("falls back to a generic label when a draft seed has an identity but no file name", () => {
      const widgetData: ComposeWidgetData = {
        compose: { draft: { html: "<p>drafted</p>" } },
      };
      const state = composeWidgetVisibility(widgetData);
      expect(state?.widgetType).toBe("DocumentViewer");
      if (state?.widgetType !== "DocumentViewer") throw new Error("expected DocumentViewer");
      expect(state.filename).toBe("Compose document");
    });

    it("returns null when there is no compose payload", () => {
      expect(composeWidgetVisibility({})).toBeNull();
      expect(composeWidgetVisibility({ compose: undefined })).toBeNull();
      expect(composeWidgetVisibility(null)).toBeNull();
      expect(composeWidgetVisibility("not-an-object")).toBeNull();
    });

    it("returns null when a compose payload carries no document identity", () => {
      expect(composeWidgetVisibility({ compose: {} })).toBeNull();
    });
  });
});

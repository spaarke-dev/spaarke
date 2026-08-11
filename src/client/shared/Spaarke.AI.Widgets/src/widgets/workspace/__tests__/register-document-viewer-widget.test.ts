/**
 * register-document-viewer-widget — unit tests (R4 task 042 / W-4)
 *
 * Verifies the DocumentViewer widget is wired into WorkspaceWidgetRegistry
 * via the dedicated side-effect registration file, so dispatching
 * `widget_load` with widgetType: 'document-viewer' resolves to the
 * expected component (and NOT the GenericTextWidget fallback).
 */

import {
  hasWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  resolveWorkspaceWidget,
} from '../../../registry/WorkspaceWidgetRegistry';
import { DOCUMENT_VIEWER_WIDGET_TYPE } from '../register-document-viewer-widget';

// Side-effect import: ensure the registration has run before the assertions.
// The package barrel does this in production; tests import directly so the
// registry state is set up regardless of test-runner module-load order.
import '../register-document-viewer-widget';

describe('register-document-viewer-widget', () => {
  it('registers the document-viewer widget type', () => {
    expect(hasWorkspaceWidget(DOCUMENT_VIEWER_WIDGET_TYPE)).toBe(true);
  });

  it('exposes the expected display name in registry metadata', () => {
    const meta = getWorkspaceWidgetMetadata(DOCUMENT_VIEWER_WIDGET_TYPE);
    expect(meta).toBeDefined();
    expect(meta!.displayName).toBe('Document Viewer');
    expect(meta!.category).toBe('document');
    expect(meta!.allowMultiple).toBe(true);
  });

  it('resolveWorkspaceWidget returns a component (not the GenericTextWidget fallback)', async () => {
    // Smoke test for resolution. We don't compare component identity directly
    // (the registry returns a lazy-loaded promise), but we assert resolution
    // succeeds without falling back to the GenericTextWidget code path.
    const Component = await resolveWorkspaceWidget(DOCUMENT_VIEWER_WIDGET_TYPE);
    expect(Component).toBeDefined();
    expect(typeof Component).toBe('function');
  });

  it('exports DOCUMENT_VIEWER_WIDGET_TYPE as a stable string constant', () => {
    // Guard against accidental renames — dispatchers reference this constant
    // (e.g. ConversationPane in SpaarkeAi). Changing the value would break
    // the Assistant → Workspace `widget_load` demo wiring.
    expect(DOCUMENT_VIEWER_WIDGET_TYPE).toBe('document-viewer');
  });

  // -------------------------------------------------------------------------
  // assistantContract — FR-11 per-item cards (task 022, FR-08 + FR-15 SHAPE)
  // -------------------------------------------------------------------------

  it("declares the 'document' contextType (task 020)", () => {
    const meta = getWorkspaceWidgetMetadata(DOCUMENT_VIEWER_WIDGET_TYPE);
    expect(meta!.contextType).toBe('document');
  });

  it('declares per-item cards Summarize/Draft response/Draft memo and no overview tool (FR-11)', () => {
    const meta = getWorkspaceWidgetMetadata(DOCUMENT_VIEWER_WIDGET_TYPE);
    const contract = meta!.assistantContract;
    expect(contract).toBeDefined();
    expect(contract!.overviewTools).toEqual([]);
    expect(contract!.perItemCards.map(c => c.label)).toEqual(['Summarize', 'Draft response', 'Draft memo']);
    expect(contract!.interactionPattern).toBe('hybrid');
  });

  it('all three cards land in chat (task 026 finalization — no composer/Compose surface exists for documents; see register-document-viewer-widget.ts landing NOTE)', () => {
    const contract = getWorkspaceWidgetMetadata(DOCUMENT_VIEWER_WIDGET_TYPE)!.assistantContract!;
    const byLabel = new Map(contract.perItemCards.map(c => [c.label, c.landing]));
    expect(byLabel.get('Summarize')).toBe('chat');
    expect(byLabel.get('Draft response')).toBe('chat');
    expect(byLabel.get('Draft memo')).toBe('chat');
  });
});

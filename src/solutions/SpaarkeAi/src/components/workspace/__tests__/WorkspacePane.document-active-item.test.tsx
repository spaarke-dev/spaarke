/**
 * WorkspacePane.document-active-item.test.tsx — spaarkeai-assistant-enhancements-r3 task 026 (FR-11).
 *
 * Unit-tests the `deriveActiveItemHandle` TAB-FOCUS branch for `document-viewer` tabs — the
 * generalization of the SAME effect condition Compose already uses (`WorkspacePane.tsx`'s
 * `active-doc-follows-tab` effect). No full WorkspacePane render harness needed — the helper is
 * exported + dependency-free, mirroring `WorkspacePane.email-active-item.test.tsx`'s approach for
 * the sibling in-widget-selection helper.
 *
 * D-3 verification (per task 026 notes): unlike Compose's `composeSessionId` (which back-fills
 * ASYNCHRONOUSLY after the editor mounts and registers via a later `onDataChange` patch — the
 * race the D-3 defer-issue flagged), `document-viewer`'s `documentId` is set SYNCHRONOUSLY in the
 * SAME `widget_load` payload that creates the tab at every current dispatch site (`WorkspacePane`'s
 * own read-only-preview fallback, `CreateAnalysisWizardWidget`'s created-file fallback) — i.e.
 * BEFORE the tab exists. There is no `handleTabDataChange` write-back path for `documentId` on this
 * widget, so the value present at tab-creation is final for the tab's lifetime. The D-3 race
 * therefore does NOT recur here; this suite proves the id is present on FIRST derivation (no
 * "second effect run" needed).
 *
 * Test category per ADR-038: Unit Test (KEEP — pure function, no I/O, asserts a binding contract:
 * the conduit id-handle NEVER carries content, and the fallback contract for legacy dispatch sites
 * that never populate `documentId`).
 */
import { deriveActiveItemHandle } from '../WorkspacePane';
import type { WorkspaceTab } from '../WorkspaceTabManager';

function makeTab(overrides: Partial<WorkspaceTab>): WorkspaceTab {
  return {
    id: 'tab-1',
    kind: 'widget',
    widgetType: 'document-viewer',
    displayName: 'Untitled',
    widgetData: null,
    Component: null,
    isLoading: false,
    visibleToAssistant: false,
    ...overrides,
  } as unknown as WorkspaceTab;
}

describe('deriveActiveItemHandle — document-viewer tab-focus → active-item conduit (task 026, FR-11)', () => {
  it('a focused document-viewer tab with a stable documentId publishes {id, type: "document", label: filename}', () => {
    const tab = makeTab({
      id: 'tab-doc-1',
      displayName: 'Contract.docx (tab title)',
      widgetData: { documentId: 'doc-abc-123', filename: 'Contract.docx', contentType: 'application/pdf', textContent: '' },
    });

    const handle = deriveActiveItemHandle(tab);

    expect(handle).toEqual({ id: 'doc-abc-123', type: 'document', label: 'Contract.docx' });
    // ADR-015: the conduit handle carries id/type/label ONLY — no stray content field.
    expect(Object.keys(handle as object).sort()).toEqual(['id', 'label', 'type']);
  });

  it('negative: the published handle never carries a textContent/body field even when present on widgetData', () => {
    const tab = makeTab({
      widgetData: {
        documentId: 'doc-abc-123',
        filename: 'Contract.docx',
        contentType: 'application/pdf',
        textContent: 'The full extracted body text should never reach the conduit.',
      },
    });

    const handle = deriveActiveItemHandle(tab);
    expect(Object.keys(handle as object).sort()).toEqual(['id', 'label', 'type']);
    expect((handle as unknown as { textContent?: unknown }).textContent).toBeUndefined();
  });

  it('D-3: documentId set synchronously at tab-creation dispatch time is stable on first derivation (no async backfill needed)', () => {
    // Mirrors the WorkspacePane preview-fallback / CreateAnalysisWizardWidget dispatch shape: the
    // SAME widget_load payload that creates the tab already carries documentId — there is no
    // later handleTabDataChange patch for this widget, so the FIRST call already sees the real id.
    const tab = makeTab({
      id: 'tab-fresh',
      widgetData: { documentId: 'doc-fresh-1', filename: 'Fresh.pdf', contentType: 'application/pdf', textContent: '' },
    });
    expect(deriveActiveItemHandle(tab)).toEqual({ id: 'doc-fresh-1', type: 'document', label: 'Fresh.pdf' });
  });

  it('falls back to the tab id when documentId is absent (legacy dispatch sites — upload wizard documentIds[], file-preview fileId payloads never set documentId)', () => {
    const tab = makeTab({
      id: 'tab-no-doc-id',
      displayName: 'Uploaded Document',
      widgetData: { documentIds: ['a', 'b'], sessionId: 'sess-1', title: 'Uploaded Document' },
    });

    const handle = deriveActiveItemHandle(tab);
    expect(handle).toEqual({ id: 'tab-no-doc-id', type: 'document', label: 'Uploaded Document' });
  });

  it('falls back to the tab id AND tab displayName when widgetData is null', () => {
    const tab = makeTab({ id: 'tab-empty', displayName: 'Document Viewer', widgetData: null });
    expect(deriveActiveItemHandle(tab)).toEqual({ id: 'tab-empty', type: 'document', label: 'Document Viewer' });
  });

  it('negative: no tab → null (clears the conduit)', () => {
    expect(deriveActiveItemHandle(null)).toBeNull();
  });

  it('negative: a multi-item / dashboard widget type → null (no single active item)', () => {
    expect(deriveActiveItemHandle(makeTab({ widgetType: 'documents-list' }))).toBeNull();
    expect(deriveActiveItemHandle(makeTab({ widgetType: 'daily-briefing' }))).toBeNull();
  });

  it('regression: the compose branch is unaffected by the new document-viewer branch', () => {
    const composeTab = makeTab({
      id: 'tab-compose-1',
      widgetType: 'compose',
      displayName: 'Untitled',
      widgetData: { compose: { composeSessionId: 'compose-sess-1', fileName: 'Draft.docx' } },
    });
    expect(deriveActiveItemHandle(composeTab)).toEqual({
      id: 'compose-sess-1',
      type: 'compose',
      label: 'Draft.docx',
    });
  });
});

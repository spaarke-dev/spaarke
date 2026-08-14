/**
 * WorkspacePane.email-active-item.test.tsx — spaarkeai-assistant-enhancements-r3 task 012 (FR-05).
 *
 * Unit-tests the pure `deriveEmailActiveItemFromPatch` helper WorkspacePane's `handleTabDataChange`
 * uses to bridge the email widget's `onDataChange` patch (produced by the shared
 * `EmailWorkspaceWidget` in `@spaarke/ai-widgets`, which CANNOT import the SpaarkeAi-solution
 * active-item conduit per ADR-012) into a published `ActiveItemHandle` on the task-001 conduit.
 *
 * No full WorkspacePane render harness needed — the helper is exported + dependency-free, mirroring
 * `deriveActiveItemHandle`'s own testability. This is the "(the SpaarkeAi bridge → conduit)" half of
 * the task's required test matrix; `EmailWorkspaceWidget.test.tsx` (Spaarke.AI.Widgets) covers the
 * shared widget's own patch-shaping half.
 *
 * Test category per ADR-038: Unit Test (KEEP — pure function, no I/O, asserts a binding contract:
 * the conduit id-handle NEVER carries content, and the persisted `EmailTabWidgetData` contract is
 * never polluted with the transient `communicationId` bridge field).
 */
import { deriveEmailActiveItemFromPatch } from '../WorkspacePane';

describe('deriveEmailActiveItemFromPatch — email in-widget-selection → active-item conduit bridge (task 012)', () => {
  it('selecting an email publishes {id: communicationId, type: "email", label: subject} and strips communicationId from the persistable patch', () => {
    const result = deriveEmailActiveItemFromPatch('email', {
      kind: 'Email',
      communicationId: 'comm-1',
      emlDocumentId: 'eml-doc-1',
      subject: 'Quarterly filing update',
      from: 'jane.doe@example.com',
      date: '2026-08-02T10:30:00Z',
    });

    expect(result).not.toBeNull();
    expect(result!.handle).toEqual({ id: 'comm-1', type: 'email', label: 'Quarterly filing update' });
    // The conduit handle carries id/type/label ONLY (ADR-015) — no stray fields.
    expect(Object.keys(result!.handle as object).sort()).toEqual(['id', 'label', 'type']);

    // Persistable patch keeps the EmailTabWidgetData-shaped fields but DROPS communicationId —
    // the transient bridge field never reaches WorkspaceTabManager.updateTab / the BFF contract.
    expect(result!.persistablePatch).toEqual({
      kind: 'Email',
      emlDocumentId: 'eml-doc-1',
      subject: 'Quarterly filing update',
      from: 'jane.doe@example.com',
      date: '2026-08-02T10:30:00Z',
    });
    expect('communicationId' in result!.persistablePatch).toBe(false);
  });

  it('negative: the published handle never carries a body/content/snippet field even if present on the source patch', () => {
    const result = deriveEmailActiveItemFromPatch('email', {
      kind: 'Email',
      communicationId: 'comm-1',
      subject: 'Quarterly filing update',
      from: 'jane.doe@example.com',
      date: '2026-08-02T10:30:00Z',
      // A stray content-bearing field a future caller might mistakenly still attach —
      // the handle MUST NOT surface it regardless (ADR-015 is enforced structurally: the
      // helper only ever reads communicationId/subject onto the handle).
      snippet: 'The full body text should never reach the conduit.',
    });

    expect(result).not.toBeNull();
    expect(Object.keys(result!.handle as object).sort()).toEqual(['id', 'label', 'type']);
    expect((result!.handle as unknown as { snippet?: unknown }).snippet).toBeUndefined();
  });

  it('deselect (communicationId: null) clears the handle and returns an EMPTY persistable patch — the last persisted carrier is left intact', () => {
    const result = deriveEmailActiveItemFromPatch('email', { communicationId: null });

    expect(result).not.toBeNull();
    expect(result!.handle).toBeNull();
    expect(result!.persistablePatch).toEqual({});
  });

  it('returns null for a non-email widget type — the patch is not an active-item signal, caller persists it unchanged', () => {
    expect(
      deriveEmailActiveItemFromPatch('documents-list', { communicationId: 'comm-1', subject: 'irrelevant' })
    ).toBeNull();
    expect(deriveEmailActiveItemFromPatch('compose', { communicationId: 'comm-1' })).toBeNull();
  });

  it('returns null for an email-widget patch with no communicationId field (not an active-item signal)', () => {
    expect(deriveEmailActiveItemFromPatch('email', { kind: 'Email', subject: 'no id here' })).toBeNull();
  });

  it('returns null for a non-object patch', () => {
    expect(deriveEmailActiveItemFromPatch('email', null)).toBeNull();
    expect(deriveEmailActiveItemFromPatch('email', 'not-an-object')).toBeNull();
    expect(deriveEmailActiveItemFromPatch('email', undefined)).toBeNull();
  });

  it('falls back to an empty label when subject is absent on a selection patch', () => {
    const result = deriveEmailActiveItemFromPatch('email', { communicationId: 'comm-1' });
    expect(result!.handle).toEqual({ id: 'comm-1', type: 'email', label: '' });
  });
});

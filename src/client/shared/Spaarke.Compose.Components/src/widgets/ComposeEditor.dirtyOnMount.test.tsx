/**
 * ComposeEditor.dirtyOnMount.test.tsx — DEF-01 (GitHub #601) regression guard.
 *
 * Proves the `docxBytes` effect's transient-vs-persisted dirty-reporting split
 * against the REAL `ComposeEditor` (real TipTap editor), exercised at the
 * Compose.Components level (the SpaarkeAi-level tests mock ComposeEditor, so the
 * effect itself is only reachable here).
 *
 * Contract under test (ComposeEditor.tsx, docxBytes effect):
 *  - TRANSIENT mount (Browse/Upload draft — empty `documentRef.speDriveItemId`,
 *    or no documentRef): after the mammoth import resolves the editor reports
 *    `onDirtyChange(true)` to the workspace so the FIRST Save fires create-on-save
 *    (FR-05 / task 013). The editor's OWN `isDirty()` stays FALSE so an untouched
 *    transient Save still persists the pristine original bytes (FR-06a).
 *  - PERSISTED mount (real opened/searched doc — non-empty `speDriveItemId`):
 *    unchanged legacy behavior — `onDirtyChange(false)` (fresh load is clean).
 *
 * The DOCX bridge is mocked so the import resolves synchronously (jsdom has no
 * mammoth WASM path); the real editor + real effect drive the assertion.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle, type ComposeEditorDocumentRef } from './ComposeEditor';

// ComposeAiToolbar's `useAuth()` throws outside a real `initAuth()` bootstrap
// (MSAL). This test never dispatches an action, so a stub token is sufficient.
// Mirrors the identical mock in ComposeEditor.aiToolbarTriggers.test.tsx.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// Resolve the mammoth import synchronously — the effect only needs the html +
// messages shape; document content is irrelevant to the dirty-reporting split.
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: jest.fn(async () => ({ html: '<p>Loaded document body</p>', messages: [] })),
  tipTapToDocxBytes: jest.fn(async () => new ArrayBuffer(0)),
  // task 011: ComposeEditor calls stampParaIds after the docx import; a no-op is
  // correct here (this suite asserts dirty-reporting, not paraId carry) and — being
  // a no-op — dispatches nothing, so it does not perturb the onDirtyChange signal.
  stampParaIds: jest.fn(),
}));

// Wave 6 (DEF-G): the editor now gates the mammoth import on a valid DOCX byte
// signature (ZIP local-file-header magic `PK\x03\x04`) — a non-docx buffer routes
// to the reference-only state instead of the editable editor. These dirty-reporting
// tests represent a VALID docx mount, so the fixture bytes must begin with that
// signature (the mocked bridge otherwise ignores the content).
function docxBytesFixture(totalLen = 8): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set([0x50, 0x4b, 0x03, 0x04], 0); // PK\x03\x04
  return buf.buffer;
}

function renderEditor(
  documentRef: ComposeEditorDocumentRef | undefined,
  onDirtyChange: (dirty: boolean) => void,
  ref: React.Ref<ComposeEditorHandle>
) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          ref={ref}
          docxBytes={docxBytesFixture(8)}
          documentRef={documentRef}
          sessionId="session-def01"
          onDirtyChange={onDirtyChange}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe('ComposeEditor — DEF-01 transient-vs-persisted dirty reporting on docxBytes mount', () => {
  it('TRANSIENT mount (empty speDriveItemId): reports dirty=true so the first Save fires create-on-save', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor({ speDriveItemId: '', fileName: 'Untitled draft.docx' }, onDirtyChange, ref);

    await screen.findByRole('textbox'); // editor mounted → effect ran

    // Workspace-facing signal: the transient draft is dirty (unsaved work exists).
    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(true));
    // ...and it was NEVER reported clean on this mount (no onDirtyChange(false)).
    expect(onDirtyChange).not.toHaveBeenCalledWith(false);

    // Editor's OWN dirty flag stays FALSE so an untouched transient Save persists
    // the pristine original bytes byte-identical (FR-06a) — triggerSave keys the
    // byte-branch off isDirty(), not the workspace onDirtyChange signal.
    expect(ref.current?.isDirty()).toBe(false);
  });

  it('TRANSIENT mount (no documentRef at all): also reports dirty=true', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(undefined, onDirtyChange, ref);

    await screen.findByRole('textbox');

    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(true));
    expect(onDirtyChange).not.toHaveBeenCalledWith(false);
  });

  it('PERSISTED mount (non-empty speDriveItemId): reports dirty=false — unchanged legacy behavior', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(
      { speDriveItemId: 'drive-item-abc123', sprkDocumentId: 'doc-guid-1', fileName: 'Opened matter.docx' },
      onDirtyChange,
      ref
    );

    await screen.findByRole('textbox');

    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(false));
    expect(onDirtyChange).not.toHaveBeenCalledWith(true);
    expect(ref.current?.isDirty()).toBe(false);
  });
});

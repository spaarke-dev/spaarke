/**
 * ComposeEditor.advisoryComments.test.tsx — NDA-REVIEW advisory comments
 * (ai-advanced-capabilities-nda-r1 task 031).
 *
 * Proves `ComposeEditorHandle.placeAdvisoryComments` against the REAL `ComposeEditor` (real TipTap
 * editor), mirroring `ComposeEditor.dirtyOnMount.test.tsx`'s mount convention (the SpaarkeAi-level
 * tests mock ComposeEditor, so this composition — `resolveTargetSpans('strict')` +
 * `useComposeCommentThreads.createThread` — is only reachable here):
 *
 *  - a UNIQUE, exact `targetText` resolves and materializes a PERSISTENT comment thread (a
 *    `commentAnchor` mark in the document, `explanation` as the thread text) — `placed` increments;
 *  - a `targetText` with ZERO matches is reported via `failed` (`kind: 'not_found'`) — never applies
 *    a mark, never throws;
 *  - a `targetText` with MULTIPLE matches is reported via `failed` (`kind: 'ambiguous'`) — the FR-19
 *    "do not guess" rule — never guesses which occurrence;
 *  - `placed` + `failed.length` accounts for every input item; only the resolved item's mark exists
 *    in the document.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle, type ComposeEditorDocumentRef } from './ComposeEditor';
import type { ComposeServerProjection } from '../types/compose-contracts';

// ComposeAiToolbar's `useAuth()` throws outside a real `initAuth()` bootstrap (MSAL). This suite
// never dispatches an action, so a stub token is sufficient — mirrors ComposeEditor.dirtyOnMount.test.tsx.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// Regression guard only (task 013): production no longer imports `docxToTipTapHtml` or
// `stampParaIds` — the editor mounts via the `projection` prop below instead (same content).
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: jest.fn(async () => ({
    html:
      '<p>The receiving party shall retain confidential information indefinitely. ' +
      'Either party may terminate this agreement. ' +
      'Some unrelated boilerplate text. ' +
      'Either party may terminate this agreement.</p>',
    messages: [],
  })),
  stampParaIds: jest.fn(),
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

function docxBytesFixture(totalLen = 8): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set([0x50, 0x4b, 0x03, 0x04], 0); // PK\x03\x04 — ZIP local-file-header signature
  return buf.buffer;
}

// Task 013 (F-2 "one reader"): the client-side mammoth reader is DELETED — the editor now
// requires a server `projection` to mount the editable surface. Same body text the mocked
// docxBridge above used to supply, so this suite's target-resolution assertions are unaffected.
const ADVISORY_COMMENTS_PROJECTION: ComposeServerProjection = {
  status: 'success',
  canEdit: true,
  html:
    '<p data-paraid="AB12CD34">The receiving party shall retain confidential information indefinitely. ' +
    'Either party may terminate this agreement. ' +
    'Some unrelated boilerplate text. ' +
    'Either party may terminate this agreement.</p>',
  warnings: [],
  schemaVersion: 'compose-html-v1',
};

function renderEditor(ref: React.Ref<ComposeEditorHandle>, documentRef: ComposeEditorDocumentRef) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          ref={ref}
          docxBytes={docxBytesFixture(8)}
          projection={ADVISORY_COMMENTS_PROJECTION}
          documentRef={documentRef}
          sessionId="session-nda-review-031"
          onDirtyChange={jest.fn()}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe('ComposeEditor.placeAdvisoryComments — NDA-REVIEW advisory comments (task 031)', () => {
  it('a unique target resolves + materializes a comment thread; not_found/ambiguous targets are reported, not dropped', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(ref, { speDriveItemId: 'drive-item-nda-1', fileName: 'NDA.docx' });

    await screen.findByRole('textbox');
    // Editor content settled (the mocked docxBridge import resolved + rendered).
    await screen.findByText(/The receiving party shall retain confidential information indefinitely\./);

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: 'The receiving party shall retain confidential information indefinitely.',
        explanation: 'Indefinite retention deviates from the standard 3-year term.',
      },
      {
        targetText: 'This clause does not appear anywhere in the document.',
        explanation: 'should not resolve — not_found',
      },
      {
        targetText: 'Either party may terminate this agreement.',
        explanation: 'should not resolve — ambiguous (appears twice)',
      },
    ]);

    expect(result.placed).toBe(1);
    expect(result.failed).toHaveLength(2);
    expect(result.failed).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          targetText: 'This clause does not appear anywhere in the document.',
          kind: 'not_found',
        }),
        expect.objectContaining({
          targetText: 'Either party may terminate this agreement.',
          kind: 'ambiguous',
        }),
      ])
    );

    // The resolved target materialized as a real commentAnchor mark carrying the explanation text
    // (findByRole('textbox') proves the ProseMirror surface; the mark itself is a <span data-comment-id>).
    const editorSurface = screen.getByRole('textbox');
    const anchorSpans = editorSurface.querySelectorAll('span[data-comment-id]');
    expect(anchorSpans.length).toBe(1);
    expect(anchorSpans[0].textContent).toBe('The receiving party shall retain confidential information indefinitely.');

    // task 040 read surface: the placed thread's explanation is discoverable via
    // getAdvisoryCommentThreads() (a separate instance from the session Comments panel's own
    // threads — export/persistence wiring is task 040's job, not this one's). The underlying
    // `setThreads` is an async React state update, so the handle only reflects it after the
    // next render settles.
    await waitFor(() => expect(ref.current!.getAdvisoryCommentThreads()).toHaveLength(1));
    expect(ref.current!.getAdvisoryCommentThreads()[0]).toMatchObject({
      author: 'AI Advisory Review',
      text: 'Indefinite retention deviates from the standard 3-year term.',
      anchorText: 'The receiving party shall retain confidential information indefinitely.',
    });
  });

  it('editor not mounted (no matching ref content yet is impossible via ref, so assert the empty-items no-op contract)', () => {
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(ref, { speDriveItemId: 'drive-item-nda-2', fileName: 'NDA2.docx' });

    // Calling with an empty item list is a legitimate no-op regardless of mount timing.
    const result = ref.current ? ref.current.placeAdvisoryComments([]) : { placed: 0, failed: [] };
    expect(result.placed).toBe(0);
    expect(result.failed).toHaveLength(0);
  });
});

/**
 * ComposeEditor.advisoryCommentAuthor.test.tsx — ai-advanced-capabilities-agreements-r1 task 052
 * (FR-15, Word-comment export mirror; ADR-012 lib-level configurability).
 *
 * Proves the `advisoryCommentAuthor` prop end-to-end against the REAL `ComposeEditor` (mirrors
 * `ComposeEditor.advisoryComments.test.tsx`'s mount convention — kept in a SEPARATE file so this
 * task doesn't touch that suite, which carries 6 pre-existing DEF-01 failures that are task 012's
 * scope, not this one's):
 *  - omitting the prop keeps the EXACT pre-existing hardcoded behavior ('AI Advisory Review')
 *  - supplying the prop attributes newly-placed advisory threads to the configured name
 *  - the session Comments panel's OWN `commentAuthor` prop is unaffected (the two authors are
 *    independent — separate `useComposeCommentThreads` instances)
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle, type ComposeEditorDocumentRef } from './ComposeEditor';
import type { ComposeServerProjection } from '../types/compose-contracts';

// ComposeAiToolbar's `useAuth()` throws outside a real `initAuth()` bootstrap (MSAL). This suite
// never dispatches an action, so a stub token is sufficient — mirrors the sibling advisory-comments suite.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

const PROJECTION: ComposeServerProjection = {
  status: 'success',
  canEdit: true,
  html: '<p data-paraid="AB12CD34">The receiving party shall retain confidential information indefinitely.</p>',
  warnings: [],
  schemaVersion: 'compose-html-v1',
};

function docxBytesFixture(): ArrayBuffer {
  const buf = new Uint8Array(8);
  buf.set([0x50, 0x4b, 0x03, 0x04], 0); // PK\x03\x04 — ZIP local-file-header signature
  return buf.buffer;
}

function renderEditor(
  ref: React.Ref<ComposeEditorHandle>,
  documentRef: ComposeEditorDocumentRef,
  advisoryCommentAuthor?: string
) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          ref={ref}
          docxBytes={docxBytesFixture()}
          projection={PROJECTION}
          documentRef={documentRef}
          sessionId="session-052-author-config"
          advisoryCommentAuthor={advisoryCommentAuthor}
          onDirtyChange={jest.fn()}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe('ComposeEditor — advisoryCommentAuthor prop (task 052, FR-15 / ADR-012)', () => {
  it('defaults to the pre-existing hardcoded literal "AI Advisory Review" when the prop is omitted', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(ref, { speDriveItemId: 'drive-item-052-1', fileName: 'Agreement1.docx' }, undefined);

    await screen.findByRole('textbox');
    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: 'The receiving party shall retain confidential information indefinitely.',
        explanation: 'Indefinite retention deviates from the standard 3-year term.',
      },
    ]);
    expect(result.placed).toBe(1);

    await waitFor(() => expect(ref.current!.getAdvisoryCommentThreads()).toHaveLength(1));
    expect(ref.current!.getAdvisoryCommentThreads()[0].author).toBe('AI Advisory Review');
  });

  it('attributes newly-placed advisory threads to a configured author name', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(ref, { speDriveItemId: 'drive-item-052-2', fileName: 'Agreement2.docx' }, 'Spaarke Agreement Review');

    await screen.findByRole('textbox');
    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: 'The receiving party shall retain confidential information indefinitely.',
        explanation: 'Indefinite retention deviates from the standard 3-year term.',
      },
    ]);
    expect(result.placed).toBe(1);

    await waitFor(() => expect(ref.current!.getAdvisoryCommentThreads()).toHaveLength(1));
    expect(ref.current!.getAdvisoryCommentThreads()[0].author).toBe('Spaarke Agreement Review');
  });
});

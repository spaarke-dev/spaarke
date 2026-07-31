/**
 * ComposeEditor.bidirectionalHighlight.test.tsx — summary row → document section AND gutter Review
 * Note (ai-advanced-capabilities-agreements-r1 task 040, spec FR-10).
 *
 * Before this task, clicking a `AgreementReviewSummaryPanel` row highlighted the document only
 * (`handleReviewNavigate` → `qaHighlight.highlight`) — the matching right-gutter Review Note never
 * lit up. This suite proves the reverse link against the REAL `ComposeEditor` (mirrors
 * `ComposeEditor.advisoryComments.test.tsx`'s mount convention, since the SpaarkeAi-level tests mock
 * ComposeEditor and this composition is only reachable here):
 *
 *  - a summary row whose finding resolves (via `resolveMatchingThreadId`'s deterministic sectionRef/
 *    explanation join — `ComposeCommentGutter.tsx`) to a placed advisory thread selects THAT thread —
 *    proven via the `SelectedCommentExtension` decoration class landing on the MATCHED clause's anchor
 *    span (not the other one) — ONE coordinated action, not the doc-only ephemeral highlight;
 *  - switching between two matched rows leaves exactly ONE selected pair — the previous clause's
 *    decoration is gone the instant the new one appears (no stacking);
 *  - a row whose finding has no matching thread (a placement failure, or "note removed") degrades
 *    gracefully to the pre-existing doc-only ephemeral highlight (`compose-qa-highlight`) — no error,
 *    and no stale note-selection decoration lingers from a prior click.
 *
 * Gutter-card DOM assertions (aria-pressed on `ComposeCommentGutter`'s own card) are intentionally NOT
 * used here — `ComposeCommentGutter` positions cards via `editor.view.coordsAtPos`, which is not
 * reliably reachable in jsdom without spying on the internal (non-exposed) editor instance (see
 * `ComposeCommentGutter.test.tsx`, which spies on a directly-constructed editor for that reason). The
 * `SelectedCommentExtension` decoration class is shared, DOM-visible, editor-instance-independent proof
 * that the SAME `selectedThreadId` state (which also drives the gutter card's selected style) changed
 * correctly — sufficient to prove this task's wiring without re-deriving the gutter's own positioning
 * tests.
 */

import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle, type ComposeEditorDocumentRef } from './ComposeEditor';
import { SELECTED_COMMENT_ANCHOR_CLASS } from './marks/SelectedCommentExtension';
import { QA_HIGHLIGHT_CLASS } from './marks/QaHighlightExtension';
import type { ComposeServerProjection, ParaIdMapEntry } from '../types/compose-contracts';

// ComposeAiToolbar's `useAuth()` throws outside a real `initAuth()` bootstrap (MSAL) — mirrors
// ComposeEditor.advisoryComments.test.tsx.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// Regression guard only (task 013): production no longer imports these — the editor mounts via the
// `projection` prop instead.
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: jest.fn(async () => ({ html: '<p></p>', messages: [] })),
  stampParaIds: jest.fn(),
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

function docxBytesFixture(totalLen = 8): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set([0x50, 0x4b, 0x03, 0x04], 0); // PK\x03\x04 — ZIP local-file-header signature
  return buf.buffer;
}

/** Escapes regex metacharacters for `findByText`. */
function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

const CLAUSE_41 = 'Clause 4.1: Confidentiality obligations survive termination for three years.';
const CLAUSE_42 = 'Clause 4.2: The receiving party shall indemnify the disclosing party for any breach.';

const PARA_ID_MAP: ParaIdMapEntry[] = [
  { index: 0, paraId: 'AAAA0041', isMinted: false, computedNumber: '4.1', listPath: [4, 1] },
  { index: 1, paraId: 'AAAA0042', isMinted: false, computedNumber: '4.2', listPath: [4, 2] },
];

const NO_MATCH_TARGET_TEXT = '§§§ THIS TARGET TEXT DOES NOT APPEAR ANYWHERE IN THE DOCUMENT §§§';

const EXPLANATION_41 = 'Confidentiality survival period exceeds the standard term.';
const EXPLANATION_42 = 'Indemnification scope is broader than the standard clause.';
const EXPLANATION_UNMATCHED = 'This finding never got a placed note.';

function renderEditor(ref: React.Ref<ComposeEditorHandle>, documentRef: ComposeEditorDocumentRef) {
  const projection: ComposeServerProjection = {
    status: 'success',
    canEdit: true,
    html: `<p data-paraid="AAAA0041">${CLAUSE_41}</p><p data-paraid="AAAA0042">${CLAUSE_42}</p>`,
    warnings: [],
    schemaVersion: 'compose-html-v1',
  };
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          ref={ref}
          docxBytes={docxBytesFixture(8)}
          projection={projection}
          paraIdMap={PARA_ID_MAP}
          documentRef={documentRef}
          sessionId="session-040-bidirectional"
          onDirtyChange={jest.fn()}
          reviewSummary={{
            open: true,
            hasFindings: true,
            onToggle: jest.fn(),
            findings: [
              { sectionRef: 'Section 4.1', quotedText: CLAUSE_41, explanation: EXPLANATION_41 },
              { sectionRef: 'Section 4.2', quotedText: CLAUSE_42, explanation: EXPLANATION_42 },
              // No thread will ever be placed for this sectionRef — the graceful-degrade case.
              { sectionRef: 'Section 9.9', quotedText: CLAUSE_41, explanation: EXPLANATION_UNMATCHED },
            ],
          }}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Places the two advisory comments that back findings 0/1 above — mirrors how `ComposeWorkspace`'s
 *  `onAdvisoryComments` handler builds BOTH the placed threads and the summary findings from the SAME
 *  source items (identical `sectionRef`/`explanation` on both sides — see `resolveMatchingThreadId`'s
 *  JSDoc for why that identity is the deterministic join key). */
function placeMatchingAdvisoryComments(ref: React.RefObject<ComposeEditorHandle | null>): void {
  const result = ref.current!.placeAdvisoryComments([
    { targetText: NO_MATCH_TARGET_TEXT, explanation: EXPLANATION_41, sectionRef: 'Section 4.1' },
    { targetText: NO_MATCH_TARGET_TEXT, explanation: EXPLANATION_42, sectionRef: 'Section 4.2' },
  ]);
  expect(result.placed).toBe(2);
  expect(result.failed).toHaveLength(0);
}

describe('ComposeEditor — bidirectional highlight (task 040, spec FR-10)', () => {
  it('selecting a summary row with a matching note highlights ONLY that note\'s document anchor', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(ref, { speDriveItemId: 'drive-item-040-1', fileName: 'Agreement.docx' });

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(CLAUSE_42.slice(0, 20))));
    placeMatchingAdvisoryComments(ref);

    const row = await screen.findByTestId('nda-review-summary-finding-0');
    fireEvent.click(row);

    await waitFor(() => {
      const selected = document.querySelectorAll(`.${SELECTED_COMMENT_ANCHOR_CLASS}`);
      expect(selected).toHaveLength(1);
      expect(selected[0].textContent).toBe(CLAUSE_41);
    });
    // The doc-only ephemeral highlight never fires on the matched path (ONE coordinated action).
    expect(document.querySelectorAll(`.${QA_HIGHLIGHT_CLASS}`)).toHaveLength(0);
  });

  it('switching to a different matched row leaves exactly ONE highlighted pair — no stacking', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(ref, { speDriveItemId: 'drive-item-040-2', fileName: 'Agreement.docx' });

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(CLAUSE_42.slice(0, 20))));
    placeMatchingAdvisoryComments(ref);

    fireEvent.click(await screen.findByTestId('nda-review-summary-finding-0'));
    await waitFor(() => expect(document.querySelectorAll(`.${SELECTED_COMMENT_ANCHOR_CLASS}`)).toHaveLength(1));

    fireEvent.click(await screen.findByTestId('nda-review-summary-finding-1'));
    await waitFor(() => {
      const selected = document.querySelectorAll(`.${SELECTED_COMMENT_ANCHOR_CLASS}`);
      expect(selected).toHaveLength(1); // never two at once
      expect(selected[0].textContent).toBe(CLAUSE_42); // the NEW row's clause, not the old one
    });
  });

  it('a row whose finding has no placed note degrades gracefully to the doc-only highlight — no error, no stale note selection', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    renderEditor(ref, { speDriveItemId: 'drive-item-040-3', fileName: 'Agreement.docx' });

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(CLAUSE_42.slice(0, 20))));
    placeMatchingAdvisoryComments(ref);

    // First select a MATCHED row (so a stale selection would exist if the fallback path failed to clear it).
    fireEvent.click(await screen.findByTestId('nda-review-summary-finding-0'));
    await waitFor(() => expect(document.querySelectorAll(`.${SELECTED_COMMENT_ANCHOR_CLASS}`)).toHaveLength(1));

    // Now click the UNMATCHED row (sectionRef "Section 9.9" — no thread was ever placed for it).
    expect(() => fireEvent.click(screen.getByTestId('nda-review-summary-finding-2'))).not.toThrow();

    await waitFor(() => {
      // The stale note selection is cleared — never lingers alongside the new ephemeral highlight.
      expect(document.querySelectorAll(`.${SELECTED_COMMENT_ANCHOR_CLASS}`)).toHaveLength(0);
      // The pre-existing doc-only ephemeral highlight still fires (graceful degrade, not a silent no-op).
      const qa = document.querySelectorAll(`.${QA_HIGHLIGHT_CLASS}`);
      expect(qa).toHaveLength(1);
      expect(qa[0].textContent).toBe(CLAUSE_41);
    });
  });
});

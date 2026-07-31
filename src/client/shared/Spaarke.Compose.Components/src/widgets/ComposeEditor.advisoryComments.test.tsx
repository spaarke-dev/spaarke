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
import type { ComposeServerProjection, ParaIdMapEntry } from '../types/compose-contracts';

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

/**
 * ComposeEditor.placeAdvisoryComments — deterministic sectionRef → paraId resolution
 * (ai-advanced-capabilities-agreements-r1 task 011, spec FR-03).
 *
 * Every item below sets `targetText` to text that DOES NOT appear anywhere in the mounted document —
 * `resolveAdvisoryAnchorSpan` (strict / first-occurrence / verbatim-prefix) is therefore guaranteed to
 * return `null` for it. If the finding STILL places a correctly-anchored comment, that anchoring came
 * from the deterministic `sectionRef` path, not a text search — the empirical proof for acceptance
 * criterion #1 ("assert no text-search used").
 */

/** Escapes regex metacharacters — some fixture clause text below contains literal `(`/`)` (e.g. the
 *  "4.2(b)(iii)" sub-item label), which would otherwise break `new RegExp(...)` used for `findByText`. */
function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function renderEditorWithReferenceMap(
  ref: React.Ref<ComposeEditorHandle>,
  documentRef: ComposeEditorDocumentRef,
  html: string,
  paraIdMap: ParaIdMapEntry[]
) {
  const projection: ComposeServerProjection = {
    status: 'success',
    canEdit: true,
    html,
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
          paraIdMap={paraIdMap}
          documentRef={documentRef}
          sessionId="session-agreements-011-citation"
          onDirtyChange={jest.fn()}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

const NO_MATCH_TARGET_TEXT = '§§§ THIS TARGET TEXT DOES NOT APPEAR ANYWHERE IN THE DOCUMENT §§§';

describe('ComposeEditor.placeAdvisoryComments — deterministic sectionRef→paraId resolution (task 011)', () => {
  it('a single-label sectionRef ("Section 4.2") resolves to the exact clause paraId via ComputedNumber/CitationResolver, not text search', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    const clause41 = 'Clause 4.1: Confidentiality obligations survive termination for three years.';
    const clause42 = 'Clause 4.2: The receiving party shall indemnify the disclosing party for any breach.';
    renderEditorWithReferenceMap(
      ref,
      { speDriveItemId: 'drive-item-citation-1', fileName: 'Agreement.docx' },
      `<p data-paraid="AAAA0041">${clause41}</p><p data-paraid="AAAA0042">${clause42}</p>`,
      [
        { index: 0, paraId: 'AAAA0041', isMinted: false, computedNumber: '4.1', listPath: [4, 1] },
        { index: 1, paraId: 'AAAA0042', isMinted: false, computedNumber: '4.2', listPath: [4, 2] },
      ]
    );

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(clause42.slice(0, 20))));

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: NO_MATCH_TARGET_TEXT,
        explanation: 'Indemnification scope is broader than the standard clause.',
        sectionRef: 'Section 4.2',
      },
    ]);

    expect(result.placed).toBe(1);
    expect(result.failed).toHaveLength(0);

    const editorSurface = screen.getByRole('textbox');
    const anchorSpans = editorSurface.querySelectorAll('span[data-comment-id]');
    expect(anchorSpans).toHaveLength(1);
    expect(anchorSpans[0].textContent).toBe(clause42);

    await waitFor(() => expect(ref.current!.getAdvisoryCommentThreads()).toHaveLength(1));
    expect(ref.current!.getAdvisoryCommentThreads()[0].anchorText).toBe(clause42);
  });

  it('a sub-item sectionRef ("4.2(b)(iii)") resolves to the exact sub-clause paraId, not its parent [4,2] clause', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    const clause42 = 'Clause 4.2: The receiving party shall indemnify the disclosing party for any breach.';
    const subItem = 'Clause 4.2(b)(iii): Liability under this subsection is capped at fees paid in the prior year.';
    renderEditorWithReferenceMap(
      ref,
      { speDriveItemId: 'drive-item-citation-2', fileName: 'Agreement.docx' },
      `<p data-paraid="AAAA0042">${clause42}</p><p data-paraid="AAAA0100">${subItem}</p>`,
      [
        { index: 0, paraId: 'AAAA0042', isMinted: false, computedNumber: '4.2', listPath: [4, 2] },
        {
          index: 1,
          paraId: 'AAAA0100',
          isMinted: false,
          computedNumber: '4.2(b)(iii)',
          listPath: [4, 2, 2, 3],
        },
      ]
    );

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(subItem.slice(0, 20))));

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: NO_MATCH_TARGET_TEXT,
        explanation: 'Liability cap is below the standard 12-month fee benchmark.',
        sectionRef: '4.2(b)(iii)',
      },
    ]);

    expect(result.placed).toBe(1);
    expect(result.failed).toHaveLength(0);

    const anchorSpans = screen.getByRole('textbox').querySelectorAll('span[data-comment-id]');
    expect(anchorSpans).toHaveLength(1);
    // Anchored to the SUB-ITEM paragraph, not its parent [4,2] clause.
    expect(anchorSpans[0].textContent).toBe(subItem);
  });

  it('a range sectionRef ("Sections 4–7") anchors a single comment spanning the first-to-last matched clause, in document order', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    const sec4 = 'Section four covers termination rights.';
    const sec5 = 'Section five covers renewal options.';
    const sec6 = 'Section six covers assignment restrictions.';
    const sec7 = 'Section seven covers dispute resolution.';
    renderEditorWithReferenceMap(
      ref,
      { speDriveItemId: 'drive-item-citation-3', fileName: 'Agreement.docx' },
      `<p data-paraid="R0004">${sec4}</p><p data-paraid="R0005">${sec5}</p>` +
        `<p data-paraid="R0006">${sec6}</p><p data-paraid="R0007">${sec7}</p>`,
      [
        { index: 0, paraId: 'R0004', isMinted: false, computedNumber: '4', listPath: [4] },
        { index: 1, paraId: 'R0005', isMinted: false, computedNumber: '5', listPath: [5] },
        { index: 2, paraId: 'R0006', isMinted: false, computedNumber: '6', listPath: [6] },
        { index: 3, paraId: 'R0007', isMinted: false, computedNumber: '7', listPath: [7] },
      ]
    );

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(sec4)));

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: NO_MATCH_TARGET_TEXT,
        explanation: 'Sections 4 through 7 form an inconsistent renewal/termination block.',
        sectionRef: 'Sections 4–7',
      },
    ]);

    expect(result.placed).toBe(1);
    expect(result.failed).toHaveLength(0);

    await waitFor(() => expect(ref.current!.getAdvisoryCommentThreads()).toHaveLength(1));
    const { anchorText } = ref.current!.getAdvisoryCommentThreads()[0];
    // Spans from the FIRST matched clause's start to the LAST matched clause's end — all four texts
    // present, in document order.
    expect(anchorText!.startsWith(sec4)).toBe(true);
    expect(anchorText!.endsWith(sec7)).toBe(true);
    expect(anchorText).toContain(sec5);
    expect(anchorText).toContain(sec6);
  });

  it('an unresolvable sectionRef falls back to the legacy text path — behavior unchanged for legacy inputs', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    const clause41 = 'Clause 4.1: Confidentiality obligations survive termination for three years.';
    const clause42 = 'Clause 4.2: The receiving party shall indemnify the disclosing party for any breach.';
    renderEditorWithReferenceMap(
      ref,
      { speDriveItemId: 'drive-item-citation-4', fileName: 'Agreement.docx' },
      `<p data-paraid="AAAA0041">${clause41}</p><p data-paraid="AAAA0042">${clause42}</p>`,
      [
        { index: 0, paraId: 'AAAA0041', isMinted: false, computedNumber: '4.1', listPath: [4, 1] },
        { index: 1, paraId: 'AAAA0042', isMinted: false, computedNumber: '4.2', listPath: [4, 2] },
      ]
    );

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(clause41.slice(0, 20))));

    // "Section 99" does not resolve against the reference map (deterministic path yields no match) —
    // the exact, unique verbatim targetText must still resolve via the legacy text path.
    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: clause41,
        explanation: 'Confidentiality survival period exceeds the standard term.',
        sectionRef: 'Section 99',
      },
    ]);

    expect(result.placed).toBe(1);
    expect(result.failed).toHaveLength(0);

    const anchorSpans = screen.getByRole('textbox').querySelectorAll('span[data-comment-id]');
    expect(anchorSpans).toHaveLength(1);
    expect(anchorSpans[0].textContent).toBe(clause41);
  });

  it('negative: a sectionRef matching nothing AND an unresolvable targetText places NO comment (feeds task 012\'s DEF-01 contract)', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    const clause41 = 'Clause 4.1: Confidentiality obligations survive termination for three years.';
    renderEditorWithReferenceMap(
      ref,
      { speDriveItemId: 'drive-item-citation-5', fileName: 'Agreement.docx' },
      `<p data-paraid="AAAA0041">${clause41}</p>`,
      [{ index: 0, paraId: 'AAAA0041', isMinted: false, computedNumber: '4.1', listPath: [4, 1] }]
    );

    await screen.findByRole('textbox');
    await screen.findByText(new RegExp(escapeRegExp(clause41.slice(0, 20))));

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: NO_MATCH_TARGET_TEXT,
        explanation: 'should not resolve — neither sectionRef nor targetText match',
        sectionRef: 'Section 99',
      },
    ]);

    expect(result.placed).toBe(0);
    expect(result.failed).toEqual([{ targetText: NO_MATCH_TARGET_TEXT, kind: 'not_found' }]);

    const anchorSpans = screen.getByRole('textbox').querySelectorAll('span[data-comment-id]');
    expect(anchorSpans).toHaveLength(0);
  });
});

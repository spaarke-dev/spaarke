/**
 * ComposeEditor.advisoryCommentParaId.test.tsx — the advisory path's paraId anchor (r8 task 055).
 *
 * `placeAdvisoryComments` already resolved a finding deterministically by CITATION (`sectionRef` →
 * `CitationResolver` mirror, agreements-r1 task 011). Task 055 adds the OTHER deterministic
 * vocabulary — an explicit `w14:paraId` — checked ABOVE `sectionRef`, so that the advisory path and
 * the AI-edit path share one precedence (`resolveAnchorParaIds`) instead of two that can drift.
 *
 * The additive half is the load-bearing half. `paraId` is a NEW field and no current caller sets it,
 * so every shipped NDA-REVIEW behaviour — the fixed deterministic-then-legacy ordering, the
 * range-citation spanning, the ambiguity reporting — must be byte-identical. The last describe block
 * asserts exactly that, which is why this file exists alongside (not inside) the task-011 suite.
 *
 * Every item below sets `targetText` to prose that appears NOWHERE in the mounted document, so the
 * legacy `resolveAdvisoryAnchorSpan` leg is guaranteed to fail. A comment that still lands on the
 * right clause got there deterministically.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle, type ComposeEditorDocumentRef } from './ComposeEditor';
import type { ComposeServerProjection, ParaIdMapEntry } from '../types/compose-contracts';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: jest.fn(async () => ({ html: '<p>unused</p>', messages: [] })),
  stampParaIds: jest.fn(),
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

function docxBytesFixture(totalLen = 8): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set([0x50, 0x4b, 0x03, 0x04], 0); // PK\x03\x04 — ZIP local-file-header signature
  return buf.buffer;
}

const CLAUSE_41 = 'Clause 4.1: Confidentiality obligations survive termination for three years.';
const CLAUSE_42 = 'Clause 4.2: The receiving party shall indemnify the disclosing party for any breach.';
const CLAUSE_43 = 'Clause 4.3: Notices under this Agreement shall be delivered in writing.';

const MAP: ParaIdMapEntry[] = [
  { index: 0, paraId: 'AAAA0041', isMinted: false, computedNumber: '4.1', listPath: [4, 1] },
  { index: 1, paraId: 'AAAA0042', isMinted: false, computedNumber: '4.2', listPath: [4, 2] },
  { index: 2, paraId: 'AAAA0043', isMinted: false, computedNumber: '4.3', listPath: [4, 3] },
];

const HTML =
  '<p data-paraid="AAAA0041">' +
  CLAUSE_41 +
  '</p><p data-paraid="AAAA0042">' +
  CLAUSE_42 +
  '</p><p data-paraid="AAAA0043">' +
  CLAUSE_43 +
  '</p>';

const NO_MATCH_TARGET_TEXT = 'THIS TARGET TEXT DOES NOT APPEAR ANYWHERE IN THE DOCUMENT';

function renderEditor(ref: React.Ref<ComposeEditorHandle>, documentRef: ComposeEditorDocumentRef) {
  const projection: ComposeServerProjection = {
    status: 'success',
    canEdit: true,
    html: HTML,
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
          paraIdMap={MAP}
          documentRef={documentRef}
          sessionId="session-055-advisory-paraid"
          onDirtyChange={jest.fn()}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** The anchored spans currently in the document, in document order. */
function anchoredTexts(): string[] {
  const surface = screen.getByRole('textbox');
  return Array.from(surface.querySelectorAll('span[data-comment-id]')).map(n => n.textContent ?? '');
}

async function mount(ref: React.RefObject<ComposeEditorHandle>, id: string): Promise<void> {
  renderEditor(ref, { speDriveItemId: id, fileName: 'Agreement.docx' });
  await screen.findByRole('textbox');
  await screen.findByText(new RegExp('Clause 4\\.2'));
}

describe('placeAdvisoryComments — an explicit paraId anchor (task 055)', () => {
  it('anchors the comment to the paragraph the paraId names, with prose that resolves nowhere', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-paraid-1');

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: NO_MATCH_TARGET_TEXT,
        explanation: 'Indemnification scope is broader than the standard clause.',
        paraId: 'AAAA0042',
      },
    ]);

    expect(result.placed).toBe(1);
    expect(result.failed).toHaveLength(0);
    expect(anchoredTexts()).toEqual([CLAUSE_42]);
    await waitFor(() => expect(ref.current!.getAdvisoryCommentThreads()).toHaveLength(1));
  });

  it('matches the reference map case-insensitively — paraId casing varies by producer', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-paraid-2');

    const result = ref.current!.placeAdvisoryComments([
      { targetText: NO_MATCH_TARGET_TEXT, explanation: 'Lower-cased anchor.', paraId: 'aaaa0043' },
    ]);

    expect(result.placed).toBe(1);
    expect(anchoredTexts()).toEqual([CLAUSE_43]);
  });

  it('the paraId OUTRANKS a disagreeing sectionRef by refusing — it never silently picks one', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-paraid-3');

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: NO_MATCH_TARGET_TEXT,
        explanation: 'Two anchors that disagree.',
        paraId: 'AAAA0041',
        sectionRef: 'Section 4.3',
      },
    ]);

    expect(result.placed).toBe(0);
    expect(result.failed).toEqual([{ targetText: NO_MATCH_TARGET_TEXT, kind: 'ambiguous' }]);
    expect(anchoredTexts()).toEqual([]);
  });

  it('an agreeing sectionRef corroborates the paraId and the comment still lands', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-paraid-4');

    const result = ref.current!.placeAdvisoryComments([
      {
        targetText: NO_MATCH_TARGET_TEXT,
        explanation: 'Both anchors agree.',
        paraId: 'AAAA0042',
        sectionRef: '4.2',
      },
    ]);

    expect(result.placed).toBe(1);
    expect(anchoredTexts()).toEqual([CLAUSE_42]);
  });

  it('a paraId that is not in the live document REFUSES — it does not retry as a text search (UAT-21)', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-paraid-5');

    // The prose here DOES appear in the document, so a fall-through to the legacy leg would place it.
    const result = ref.current!.placeAdvisoryComments([
      { targetText: CLAUSE_43, explanation: 'Dead anchor, live prose.', paraId: 'DEADBEEF' },
    ]);

    expect(result.placed).toBe(0);
    expect(result.failed).toEqual([{ targetText: CLAUSE_43, kind: 'not_found' }]);
    expect(anchoredTexts()).toEqual([]);
  });

  it('per-item isolation: one dead anchor does not stop the rest of the batch', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-paraid-6');

    const result = ref.current!.placeAdvisoryComments([
      { targetText: NO_MATCH_TARGET_TEXT, explanation: 'first', paraId: 'AAAA0041' },
      { targetText: NO_MATCH_TARGET_TEXT, explanation: 'dead', paraId: 'DEADBEEF' },
      { targetText: NO_MATCH_TARGET_TEXT, explanation: 'third', paraId: 'AAAA0043' },
    ]);

    expect(result.placed).toBe(2);
    expect(result.failed).toHaveLength(1);
    expect(anchoredTexts()).toEqual([CLAUSE_41, CLAUSE_43]);
  });
});

// ---------------------------------------------------------------------------
// The additive guarantee — no current caller supplies `paraId`, so nothing shipped may move.
// ---------------------------------------------------------------------------
describe('placeAdvisoryComments — the shipped sectionRef/legacy behaviour is unchanged (task 055 additive)', () => {
  it('a sectionRef-only finding still resolves deterministically', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-additive-1');

    const result = ref.current!.placeAdvisoryComments([
      { targetText: NO_MATCH_TARGET_TEXT, explanation: 'Citation only.', sectionRef: 'Section 4.2' },
    ]);

    expect(result.placed).toBe(1);
    expect(anchoredTexts()).toEqual([CLAUSE_42]);
  });

  it('a RANGE sectionRef still spans first→last clause (range citations are legal HERE, unlike an edit)', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-additive-2');

    const result = ref.current!.placeAdvisoryComments([
      { targetText: NO_MATCH_TARGET_TEXT, explanation: 'Spans the whole of clause 4.', sectionRef: 'Sections 4-4' },
    ]);

    expect(result.placed).toBe(1);
    // ONE thread, whose anchor range covers 4.1 through 4.3. The mark renders as one DOM span per
    // paragraph (a mark cannot cross a block boundary), all carrying the SAME comment id.
    expect(anchoredTexts()).toEqual([CLAUSE_41, CLAUSE_42, CLAUSE_43]);
    const ids = new Set(
      Array.from(screen.getByRole('textbox').querySelectorAll('span[data-comment-id]')).map(
        n => n.getAttribute('data-comment-id')
      )
    );
    expect(ids.size).toBe(1);
  });

  it('an UNRESOLVABLE sectionRef still falls through to the legacy text leg (the shipped ordering)', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-additive-3');

    // Citation names nothing in the map; the prose IS in the document → the legacy leg places it.
    const result = ref.current!.placeAdvisoryComments([
      { targetText: CLAUSE_43, explanation: 'Falls back to prose.', sectionRef: 'Section 99.9' },
    ]);

    expect(result.placed).toBe(1);
    expect(anchoredTexts()).toEqual([CLAUSE_43]);
  });

  it('a finding with NEITHER anchor still takes the legacy text leg unchanged', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-additive-4');

    const result = ref.current!.placeAdvisoryComments([
      { targetText: CLAUSE_41, explanation: 'Prose only.' },
    ]);

    expect(result.placed).toBe(1);
    expect(anchoredTexts()).toEqual([CLAUSE_41]);
  });

  it('a text-only finding whose prose is absent is still reported not_found', async () => {
    const ref = React.createRef<ComposeEditorHandle>();
    await mount(ref, 'advisory-additive-5');

    const result = ref.current!.placeAdvisoryComments([
      { targetText: NO_MATCH_TARGET_TEXT, explanation: 'Nothing to anchor to.' },
    ]);

    expect(result.placed).toBe(0);
    expect(result.failed).toEqual([{ targetText: NO_MATCH_TARGET_TEXT, kind: 'not_found' }]);
  });
});

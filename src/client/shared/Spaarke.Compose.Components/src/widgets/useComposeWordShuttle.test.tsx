/**
 * useComposeWordShuttle.test.tsx — coverage for the Word round-trip shuttle client wiring
 * (task 103, Cluster 3). Verifies the mappers + the pull / check-changes fetch hooks POST the right
 * routes/bodies.
 */

import { renderHook, act } from '@testing-library/react';

// @spaarke/auth is the fetch boundary — mock useAuth (the hooks call it unconditionally). The
// hooks also accept a fetchOverride escape hatch, used below to assert wire shapes.
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: jest.fn(),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

import {
  useComposePullAnnotations,
  useComposeCheckChanges,
  anchoredAnnotationsToDocxAnnotations,
  anchoredAnnotationsToPriorAnchors,
  redlineMarksToDocxAnnotations,
  selectSaveRedlineAnnotations,
  DocxTrackChangeKind,
} from './useComposeWordShuttle';
import type { AnchoredAnnotation } from '../types/compose-contracts';

function anchor(overrides: Partial<AnchoredAnnotation> & Pick<AnchoredAnnotation, 'id' | 'type'>): AnchoredAnnotation {
  return {
    id: overrides.id,
    type: overrides.type,
    anchor: overrides.anchor ?? { textPattern: 'the target clause', paragraphHint: 2, spanId: 's1' },
    body: overrides.body ?? 'a body',
    author: overrides.author ?? 'Alice',
    timestamp: overrides.timestamp ?? '2026-07-10T00:00:00Z',
    source: overrides.source ?? 'human',
  };
}

function okJson(payload: unknown): typeof fetch {
  return jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => payload,
  }) as unknown as typeof fetch;
}

describe('anchoredAnnotationsToDocxAnnotations (gap 3.1 mapping)', () => {
  it('maps each annotation kind to its native track-change kind + fields', () => {
    const result = anchoredAnnotationsToDocxAnnotations([
      anchor({ id: 'c1', type: 'comment', body: 'please revise' }),
      anchor({ id: 'i1', type: 'insertion-suggestion', body: 'inserted text' }),
      anchor({ id: 'd1', type: 'deletion-suggestion' }),
    ]);

    expect(result).toHaveLength(3);
    expect(result[0]).toMatchObject({
      kind: DocxTrackChangeKind.Comment,
      commentText: 'please revise',
      targetText: 'the target clause',
    });
    expect(result[1]).toMatchObject({ kind: DocxTrackChangeKind.Insertion, newText: 'inserted text' });
    expect(result[2]).toMatchObject({ kind: DocxTrackChangeKind.Deletion, targetText: 'the target clause' });
  });

  it('drops annotations missing the fields the server requires (no 400-inducing payloads)', () => {
    const result = anchoredAnnotationsToDocxAnnotations([
      anchor({ id: 'c-empty', type: 'comment', body: '' }), // comment with no body → dropped
      anchor({ id: 'i-empty', type: 'insertion-suggestion', body: '' }), // insertion with no text → dropped
    ]);
    expect(result).toHaveLength(0);
  });

  it('DEF-13: maps an AI edit-REASON comment annotation to a native w:comment push entry anchored to the change', () => {
    // The reason ComposeWorkspace registers on a redline (registerAiEditReasonComment): a Compose
    // AnchoredAnnotation of type 'comment', source 'ai', body = the model rationale, anchored to the
    // edit's target_text. It must map to a Comment DocxAnnotationInput so DocxAnnotationWriter emits a
    // real w:comment on Push/Save.
    const result = anchoredAnnotationsToDocxAnnotations([
      {
        id: 'ai-edit-reason:binding-1@t1',
        type: 'comment',
        anchor: { textPattern: 'the prior clause', paragraphHint: -1, spanId: 'binding-1@t1' },
        body: 'clearer indemnity language',
        author: 'Spaarke Assistant',
        timestamp: '2026-07-12T00:00:00.000Z',
        source: 'ai',
        provenance: { bindingId: 'binding-1', ledgerRef: 'binding-1@t1' },
      },
    ]);

    expect(result).toHaveLength(1);
    expect(result[0]).toMatchObject({
      kind: DocxTrackChangeKind.Comment,
      commentText: 'clearer indemnity language',
      targetText: 'the prior clause',
      author: 'Spaarke Assistant',
    });
  });
});

describe('anchoredAnnotationsToPriorAnchors (gap 3.5 mapping)', () => {
  it('maps anchor textPattern + paragraphHint and truncates the preview', () => {
    const result = anchoredAnnotationsToPriorAnchors([anchor({ id: 'a1', type: 'comment', body: 'x'.repeat(200) })]);
    expect(result).toEqual([
      { id: 'a1', type: 'comment', textPattern: 'the target clause', paragraphHint: 2, preview: 'x'.repeat(160) },
    ]);
  });
});

describe('useComposeCheckChanges (poll half of gap 3.5)', () => {
  it('POSTs check-changes with the containerId and returns the changed flag', async () => {
    const fetchMock = okJson({
      documentSpeId: 'spe-1',
      containerId: 'b!drive-1',
      changed: true,
      deleted: false,
      correlationId: 'c',
    });
    const { result } = renderHook(() =>
      useComposeCheckChanges({ bffBaseUrl: 'https://bff', fetchOverride: fetchMock })
    );

    let changed = false;
    await act(async () => {
      const res = await result.current.checkChanges({ documentSpeId: 'spe-1', containerId: 'b!drive-1' });
      changed = res.changed;
    });

    expect(changed).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://bff/api/compose/document/spe-1/check-changes',
      expect.objectContaining({ method: 'POST' })
    );
    expect(JSON.parse((fetchMock as jest.Mock).mock.calls[0][1].body)).toEqual({ containerId: 'b!drive-1' });
  });
});

describe('useComposePullAnnotations (gap 3.4)', () => {
  it('POSTs pull-annotations and returns comments + revisions', async () => {
    const fetchMock = okJson({ documentSpeId: 'spe-1', comments: [{}], revisions: [{}, {}], correlationId: 'c' });
    const { result } = renderHook(() =>
      useComposePullAnnotations({ bffBaseUrl: 'https://bff', fetchOverride: fetchMock })
    );

    let count = 0;
    await act(async () => {
      const res = await result.current.pull({ documentSpeId: 'spe-1', driveId: 'b!drive-1', tenantId: 't1' });
      count = res.comments.length + res.revisions.length;
    });

    expect(count).toBe(3);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://bff/api/compose/document/spe-1/pull-annotations',
      expect.objectContaining({ method: 'POST' })
    );
  });
});

// ---------------------------------------------------------------------------
// redlineMarksToDocxAnnotations — per-block split (Fix #1, UAT 2026-07-20)
// A redline whose marked text spans >1 editor paragraph must emit ONE annotation per <w:p>, because the
// server locates each target within a single paragraph — a concatenated cross-paragraph target never
// matches (→ 422 "a tracked change could not be located").
// ---------------------------------------------------------------------------
describe('redlineMarksToDocxAnnotations — per-block split (Fix #1)', () => {
  const del = (text: string, ledgerRef = 'b1@t1') => ({
    type: 'text',
    text,
    marks: [{ type: 'deletion', attrs: { ledgerRef } }],
  });
  const ins = (text: string, ledgerRef = 'b1@t1') => ({
    type: 'text',
    text,
    marks: [{ type: 'insertion', attrs: { ledgerRef } }],
  });
  const para = (...content: unknown[]) => ({ type: 'paragraph', content });
  const doc = (...content: unknown[]) => ({ type: 'doc', content });

  it('splits a deletion that spans two paragraphs into one annotation per paragraph', () => {
    const json = doc(para(del('End of paragraph one.')), para(del('Start of paragraph two.')));

    const out = redlineMarksToDocxAnnotations(json, 'Spaarke AI', '2026-07-20T00:00:00Z');

    const deletions = out.filter(a => a.kind === DocxTrackChangeKind.Deletion);
    expect(deletions.map(d => d.targetText)).toEqual(['End of paragraph one.', 'Start of paragraph two.']);
    // Critically, NO annotation concatenates the two paragraphs' text.
    expect(out.some(a => a.targetText === 'End of paragraph one.Start of paragraph two.')).toBe(false);
  });

  it('leaves a single-paragraph replace redline unchanged (insertion first, then deletion)', () => {
    const json = doc(para(ins('the new clause'), del('the old clause')));

    const out = redlineMarksToDocxAnnotations(json, 'Spaarke AI', '2026-07-20T00:00:00Z');

    expect(out).toEqual([
      {
        kind: DocxTrackChangeKind.Insertion,
        targetText: 'the old clause',
        newText: 'the new clause',
        author: 'Spaarke AI',
        date: '2026-07-20T00:00:00Z',
      },
      {
        kind: DocxTrackChangeKind.Deletion,
        targetText: 'the old clause',
        author: 'Spaarke AI',
        date: '2026-07-20T00:00:00Z',
      },
    ]);
  });

  it('skips imported: revisions (they ride the retained-original baseline)', () => {
    const json = doc(para(del('was-imported', 'imported:rev-1')));

    const out = redlineMarksToDocxAnnotations(json, 'Spaarke AI', '2026-07-20T00:00:00Z');

    expect(out).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// selectSaveRedlineAnnotations — Item 3 (UAT round-4): no 422 on a zero-edit save
// ---------------------------------------------------------------------------
describe('selectSaveRedlineAnnotations (item 3 — zero-edit save must not persist unverified suggestions)', () => {
  const REDLINES = [
    { kind: DocxTrackChangeKind.Deletion, targetText: 'old', author: 'AI', date: '2026-07-21T00:00:00Z' },
  ];

  it('returns [] for a CLEAN save even when the doc carries passively-materialized AI redline marks', () => {
    const getRedlineAnnotations = jest.fn(() => REDLINES);
    const out = selectSaveRedlineAnnotations({ editorIsDirty: false, hasRedlines: true, getRedlineAnnotations });
    expect(out).toEqual([]);
    // The gate short-circuits BEFORE reading annotations — nothing to locate server-side, so no 422.
    expect(getRedlineAnnotations).not.toHaveBeenCalled();
  });

  it('persists redline annotations once the user has ENGAGED the document (dirty + has redlines)', () => {
    const out = selectSaveRedlineAnnotations({
      editorIsDirty: true,
      hasRedlines: true,
      getRedlineAnnotations: () => REDLINES,
    });
    expect(out).toEqual(REDLINES);
  });

  it('returns [] when there are no redlines, regardless of dirty state', () => {
    const getRedlineAnnotations = jest.fn(() => REDLINES);
    expect(selectSaveRedlineAnnotations({ editorIsDirty: true, hasRedlines: false, getRedlineAnnotations })).toEqual(
      []
    );
    expect(getRedlineAnnotations).not.toHaveBeenCalled();
  });
});

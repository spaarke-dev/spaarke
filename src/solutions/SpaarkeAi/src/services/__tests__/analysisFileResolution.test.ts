/**
 * analysisFileResolution.test.ts — task 013 (`ai-advanced-capabilities-analysis-hub-r1`).
 *
 * Asserts the `sprk_documentid` → `sprk_document` SPE hop (ADR-007): the SPE
 * pointer for an Analysis's file comes from the LINKED `sprk_document` (via
 * the existing BFF `GET /api/documents/{id}/preview-url` surface — resolved
 * server-side through `SpeFileStore`), never from a pointer duplicated onto
 * `sprk_analysis` itself.
 *
 * Test category per ADR-038: Domain Logic (KEEP path, pure-function +
 * single sociable fetch-mock boundary — no `Mock<HttpMessageHandler>`, no
 * DI-registration test, no ctor null-check test).
 *
 * @see src/solutions/SpaarkeAi/src/services/analysisFileResolution.ts
 * @see projects/ai-advanced-capabilities-analysis-hub-r1/tasks/013-file-resolution-document-spe-hop.poml
 */

import '@testing-library/jest-dom';

import {
  resolveAnalysisDocumentId,
  resolveAnalysisFilePreview,
  type AnalysisFilePreviewResolved,
} from '../analysisFileResolution';
import type { ISprkAnalysisRecord } from '../../types/sprkAnalysis';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

type PartialAnalysis = Pick<
  ISprkAnalysisRecord,
  '_sprk_documentid_value' | '_sprk_documentid_value@OData.Community.Display.V1.FormattedValue' | 'sprk_name'
>;

function makeAnalysis(overrides: Partial<PartialAnalysis> = {}): PartialAnalysis {
  return {
    sprk_name: 'Test Analysis',
    _sprk_documentid_value: '11111111-1111-1111-1111-111111111111',
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// resolveAnalysisDocumentId — the raw document-hop read
// ---------------------------------------------------------------------------

describe('resolveAnalysisDocumentId', () => {
  test('reads the sprk_document id off _sprk_documentid_value (the hop)', () => {
    const analysis = makeAnalysis({ _sprk_documentid_value: '22222222-2222-2222-2222-222222222222' });
    expect(resolveAnalysisDocumentId(analysis)).toBe('22222222-2222-2222-2222-222222222222');
  });

  test('returns null when the Analysis has no linked document (negative case)', () => {
    const analysis = makeAnalysis({ _sprk_documentid_value: null });
    expect(resolveAnalysisDocumentId(analysis)).toBeNull();
  });

  test('returns null for an empty-string lookup value (defensive)', () => {
    const analysis = makeAnalysis({ _sprk_documentid_value: '' });
    expect(resolveAnalysisDocumentId(analysis)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// resolveAnalysisFilePreview — the document-hop wired to the BFF preview-url
// endpoint (the same call shape ConversationPane's fetchSavedPreviewUrl uses)
// ---------------------------------------------------------------------------

describe('resolveAnalysisFilePreview', () => {
  test('resolves via the document hop: the returned documentId is the LINKED sprk_document id, not a value fabricated on the Analysis', async () => {
    const linkedDocumentId = '33333333-3333-3333-3333-333333333333';
    const analysis = makeAnalysis({ _sprk_documentid_value: linkedDocumentId });

    const authenticatedFetch = jest.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ previewUrl: 'https://bff.example/preview/abc' }),
    });

    const result = resolveAnalysisFilePreview(analysis, {
      bffBaseUrl: 'https://bff.example',
      authenticatedFetch,
    });

    expect(result.status).toBe('resolved');
    const resolved = result as AnalysisFilePreviewResolved;
    expect(resolved.documentId).toBe(linkedDocumentId);

    const previewUrl = await resolved.fetchPreviewUrl();

    // The document hop: the BFF call targets the LINKED sprk_document id via
    // the existing /api/documents/{id}/preview-url surface (SpeFileStore
    // resolves the SPE pointer server-side) — not a pointer read off the
    // Analysis record.
    expect(authenticatedFetch).toHaveBeenCalledWith(
      `https://bff.example/api/documents/${linkedDocumentId}/preview-url`
    );
    expect(previewUrl).toBe('https://bff.example/preview/abc');
  });

  test('uses the OData formatted-value display name when present, else falls back to sprk_name', () => {
    const withFormatted = makeAnalysis({
      '_sprk_documentid_value@OData.Community.Display.V1.FormattedValue': 'Acme MSA.docx',
    });
    const authenticatedFetch = jest.fn();

    const resolved = resolveAnalysisFilePreview(withFormatted, {
      bffBaseUrl: 'https://bff.example',
      authenticatedFetch,
    }) as AnalysisFilePreviewResolved;
    expect(resolved.documentName).toBe('Acme MSA.docx');

    const withoutFormatted = makeAnalysis();
    const resolvedFallback = resolveAnalysisFilePreview(withoutFormatted, {
      bffBaseUrl: 'https://bff.example',
      authenticatedFetch,
    }) as AnalysisFilePreviewResolved;
    expect(resolvedFallback.documentName).toBe('Test Analysis');
  });

  test('negative: an Analysis with no sprk_documentid surfaces a clear no-document state (never a fabricated pointer)', () => {
    const analysis = makeAnalysis({ _sprk_documentid_value: null });
    const authenticatedFetch = jest.fn();

    const result = resolveAnalysisFilePreview(analysis, {
      bffBaseUrl: 'https://bff.example',
      authenticatedFetch,
    });

    expect(result).toEqual({ status: 'no-document' });
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  test('fetchPreviewUrl resolves to null (never throws) when the BFF call fails', async () => {
    const analysis = makeAnalysis();
    const authenticatedFetch = jest.fn().mockRejectedValue(new Error('network error'));

    const resolved = resolveAnalysisFilePreview(analysis, {
      bffBaseUrl: 'https://bff.example',
      authenticatedFetch,
    }) as AnalysisFilePreviewResolved;

    await expect(resolved.fetchPreviewUrl()).resolves.toBeNull();
  });

  test('fetchPreviewUrl resolves to null when the response is not ok', async () => {
    const analysis = makeAnalysis();
    const authenticatedFetch = jest.fn().mockResolvedValue({ ok: false, json: async () => ({}) });

    const resolved = resolveAnalysisFilePreview(analysis, {
      bffBaseUrl: 'https://bff.example',
      authenticatedFetch,
    }) as AnalysisFilePreviewResolved;

    await expect(resolved.fetchPreviewUrl()).resolves.toBeNull();
  });

  test('fetchPreviewUrl resolves to null when bffBaseUrl is absent (no BFF surface to call)', async () => {
    const analysis = makeAnalysis();
    const authenticatedFetch = jest.fn();

    const resolved = resolveAnalysisFilePreview(analysis, {
      bffBaseUrl: undefined,
      authenticatedFetch,
    }) as AnalysisFilePreviewResolved;

    await expect(resolved.fetchPreviewUrl()).resolves.toBeNull();
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });
});

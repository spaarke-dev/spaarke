import { fetchEnginePreSelection } from '../communicationSuggestionsService';
import { apiClient, ApiClientError } from '@shared/services';

// Mock the shared apiClient; keep a real ApiClientError class so the service's
// `instanceof` 404 branch works. The service imports the REAL shared
// `derivePrimaryReview` (jest maps it to the pure provenance source), so these
// tests exercise the actual candidate model — proving no fork (ADR-045).
jest.mock('@shared/services', () => {
  class ApiClientError extends Error {
    error: { type: string; title: string; status: number };
    constructor(error: { type: string; title: string; status: number }) {
      super('api error');
      this.name = 'ApiClientError';
      this.error = error;
    }
  }
  return {
    apiClient: { get: jest.fn() },
    ApiClientError,
  };
});

const mockGet = apiClient.get as jest.Mock;

function matterSuggestion(overrides?: Record<string, unknown>) {
  return {
    communicationId: 'comm-1',
    subject: 'Re: Smith matter',
    suggestions: {
      communicationId: 'comm-1',
      status: 'Suggested',
      autoFileEligible: false,
      candidates: [
        {
          field: 'sprk_regardingmatter',
          targetEntity: 'sprk_matter',
          targetId: '11111111-1111-1111-1111-111111111111',
          reinforcedConfidence: 0.92,
          deterministicConfidence: 0.92,
          written: false,
          conflict: false,
          contributors: [
            {
              rung: 'RecordNameMatch',
              confidence: 0.92,
              provenance:
                'record-name-match:sprk_matter:where=subject:matched=number:number="REAL-2026-123":reason="number in subject"',
            },
          ],
        },
      ],
      ...(overrides ?? {}),
    },
  };
}

describe('fetchEnginePreSelection', () => {
  it('returns null for an empty internetMessageId without a network call', async () => {
    const result = await fetchEnginePreSelection(undefined);
    expect(result).toBeNull();
    expect(mockGet).not.toHaveBeenCalled();
  });

  it('maps the engine-predicted matter to a picker EntitySearchResult', async () => {
    mockGet.mockResolvedValueOnce(matterSuggestion());

    const result = await fetchEnginePreSelection('<abc@contoso.com>');

    expect(result).not.toBeNull();
    expect(result!.predicted.entityType).toBe('Matter');
    expect(result!.predicted.logicalName).toBe('sprk_matter');
    expect(result!.predicted.id).toBe('11111111-1111-1111-1111-111111111111');
    // Record number (from the RecordNameMatch contributor) surfaces as displayInfo.
    expect(result!.predicted.displayInfo).toBe('REAL-2026-123');
    // Endpoint is the by-message-id/{id}/suggestions route with the id URL-encoded.
    expect(mockGet).toHaveBeenCalledWith(
      expect.stringContaining('/api/office/communications/by-message-id/')
    );
    expect(mockGet).toHaveBeenCalledWith(expect.stringContaining('/suggestions'));
  });

  it('returns null (no pre-selection) when the email is not captured (404)', async () => {
    mockGet.mockRejectedValueOnce(new ApiClientError({ type: 'about:blank', title: 'Not Found', status: 404 }));

    const result = await fetchEnginePreSelection('<not-captured@contoso.com>');

    expect(result).toBeNull();
  });

  it('returns null when the top candidate is a type the picker cannot represent', async () => {
    mockGet.mockResolvedValueOnce(
      matterSuggestion({
        candidates: [
          {
            field: 'sprk_regardingorganization',
            targetEntity: 'sprk_organization',
            targetId: '22222222-2222-2222-2222-222222222222',
            reinforcedConfidence: 0.95,
            deterministicConfidence: 0.95,
            written: false,
            conflict: false,
            contributors: [],
          },
        ],
      })
    );

    const result = await fetchEnginePreSelection('<org@contoso.com>');

    expect(result).toBeNull();
  });

  it('returns null when the engine has no candidate above the confidence floor', async () => {
    mockGet.mockResolvedValueOnce(
      matterSuggestion({
        candidates: [
          {
            field: 'sprk_regardingmatter',
            targetEntity: 'sprk_matter',
            targetId: '33333333-3333-3333-3333-333333333333',
            reinforcedConfidence: 0.4, // below PRIMARY_MATCH_MIN_CONFIDENCE (0.7)
            deterministicConfidence: 0.4,
            written: false,
            conflict: false,
            contributors: [],
          },
        ],
      })
    );

    const result = await fetchEnginePreSelection('<weak@contoso.com>');

    expect(result).toBeNull();
  });

  it('re-throws non-404 errors so the caller treats it as no pre-selection (best-effort)', async () => {
    mockGet.mockRejectedValueOnce(new ApiClientError({ type: 'about:blank', title: 'Server Error', status: 500 }));

    await expect(fetchEnginePreSelection('<err@contoso.com>')).rejects.toBeInstanceOf(ApiClientError);
  });
});

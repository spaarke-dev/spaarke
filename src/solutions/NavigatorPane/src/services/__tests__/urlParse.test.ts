/**
 * urlParse Tests (task 051, spec FR-08 / OQ-7)
 *
 * Verifies the closed decision order documented in `../urlParse.ts`:
 *   - An MDA record URL (etn + id) -> a labeled record target.
 *   - An MDA entitylist/view URL (viewid) -> a viewid target.
 *   - An external/non-Dataverse http(s) URL -> a raw weblink fallback.
 *   - A non-URL string -> a friendly rejection, no target constructed.
 *   - Edge cases: missing id, uppercase-GUID-with-braces normalization,
 *     hash-fragment param placement, query-wins-over-fragment precedence.
 *
 * Pure-function module — no Xrm/DOM dependency, no mocking required.
 *
 * @see ../urlParse.ts
 */

import { parseBookmarkInput } from '../urlParse';
import { NavItemPageType } from '@spaarke/ui-components/services/navigator/navItemRepository';

describe('parseBookmarkInput', () => {
  // ───────────────────────────────────────────────────────────────────────
  // Record target
  // ───────────────────────────────────────────────────────────────────────

  it('parse_MdaRecordUrl_ReturnsRecordTargetWithEtnIdPageType', () => {
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?appid=00000000-0000-0000-0000-000000000001' +
      '&pagetype=entityrecord&etn=sprk_matter&id=11111111-1111-1111-1111-111111111111';

    const result = parseBookmarkInput(url);

    expect(result).toEqual({
      kind: 'record',
      etn: 'sprk_matter',
      id: '11111111-1111-1111-1111-111111111111',
      pageType: NavItemPageType.EntityRecord,
    });
  });

  it('parse_MdaRecordUrl_UppercaseGuidWithBraces_NormalizesToLowercaseNoBraces', () => {
    // Build via URLSearchParams so encoding matches a real browser-copied link
    // (braces get percent-encoded; the GUID itself is uppercase).
    const params = new URLSearchParams();
    params.set('pagetype', 'entityrecord');
    params.set('etn', 'sprk_matter');
    params.set('id', '{1a2b3c4d-5e6f-7890-abcd-ef1234567890}'.toUpperCase());
    const url = `https://spaarkedev1.crm.dynamics.com/main.aspx?${params.toString()}`;

    const result = parseBookmarkInput(url);

    expect(result).toEqual({
      kind: 'record',
      etn: 'sprk_matter',
      id: '1a2b3c4d-5e6f-7890-abcd-ef1234567890',
      pageType: NavItemPageType.EntityRecord,
    });
  });

  it('parse_MdaRecordUrl_MissingId_FallsBackToWeblink', () => {
    const url = 'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter';

    const result = parseBookmarkInput(url);

    expect(result.kind).toBe('weblink');
    if (result.kind === 'weblink') {
      expect(result.url).toBe(url);
    }
  });

  // ───────────────────────────────────────────────────────────────────────
  // View target
  // ───────────────────────────────────────────────────────────────────────

  it('parse_MdaEntityListUrl_ReturnsViewTargetWithViewId', () => {
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?appid=00000000-0000-0000-0000-000000000001' +
      '&pagetype=entitylist&etn=sprk_matter&viewid=22222222-2222-2222-2222-222222222222&viewtype=1039';

    const result = parseBookmarkInput(url);

    expect(result).toEqual({
      kind: 'view',
      etn: 'sprk_matter',
      viewId: '22222222-2222-2222-2222-222222222222',
    });
  });

  it('parse_ViewUrl_ViewIdWinsOverRecordShapeEvenIfIdAlsoPresent', () => {
    // A URL that happens to carry both `id` and `viewid` — viewid takes
    // priority per the documented decision order (viewid checked first).
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?etn=sprk_matter' +
      '&id=11111111-1111-1111-1111-111111111111&viewid=22222222-2222-2222-2222-222222222222';

    const result = parseBookmarkInput(url);

    expect(result.kind).toBe('view');
    if (result.kind === 'view') {
      expect(result.viewId).toBe('22222222-2222-2222-2222-222222222222');
    }
  });

  // ───────────────────────────────────────────────────────────────────────
  // Weblink fallback
  // ───────────────────────────────────────────────────────────────────────

  it('parse_ExternalHttpsUrl_ReturnsWeblinkWithRawUrl', () => {
    const url = 'https://example.com/some/page?query=1';

    const result = parseBookmarkInput(url);

    expect(result).toEqual({ kind: 'weblink', url });
  });

  it('parse_HttpUrl_AlsoAcceptedAsWeblink', () => {
    const result = parseBookmarkInput('http://intranet.example.com/kb/123');
    expect(result.kind).toBe('weblink');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Rejection
  // ───────────────────────────────────────────────────────────────────────

  it('parse_NonUrlString_ReturnsFriendlyRejectionNoTarget', () => {
    const result = parseBookmarkInput('notes for later');

    expect(result.kind).toBe('reject');
    if (result.kind === 'reject') {
      expect(result.reason.length).toBeGreaterThan(0);
      expect(result.reason).not.toMatch(/error|exception|undefined/i);
    }
  });

  it('parse_EmptyString_ReturnsRejection', () => {
    expect(parseBookmarkInput('').kind).toBe('reject');
    expect(parseBookmarkInput('   ').kind).toBe('reject');
  });

  it('parse_NonHttpScheme_IsRejectedNotStoredAsWeblink', () => {
    const result = parseBookmarkInput('javascript:alert(1)');
    expect(result.kind).toBe('reject');
  });

  it('parse_MailtoScheme_IsRejected', () => {
    expect(parseBookmarkInput('mailto:someone@example.com').kind).toBe('reject');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Fragment / hash-based param placement (defensive)
  // ───────────────────────────────────────────────────────────────────────

  it('parse_ParamsInHashFragment_StillParsesAsRecordTarget', () => {
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx#pagetype=entityrecord&etn=sprk_document&id=33333333-3333-3333-3333-333333333333';

    const result = parseBookmarkInput(url);

    expect(result).toEqual({
      kind: 'record',
      etn: 'sprk_document',
      id: '33333333-3333-3333-3333-333333333333',
      pageType: NavItemPageType.EntityRecord,
    });
  });

  it('parse_ParamsInHashWithLeadingQuestionMark_StillParses', () => {
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx#/?pagetype=entitylist&etn=sprk_matter&viewid=44444444-4444-4444-4444-444444444444';

    const result = parseBookmarkInput(url);

    expect(result.kind).toBe('view');
    if (result.kind === 'view') {
      expect(result.viewId).toBe('44444444-4444-4444-4444-444444444444');
    }
  });

  it('parse_QueryStringWinsOverConflictingFragmentValue', () => {
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?etn=sprk_matter&id=11111111-1111-1111-1111-111111111111' +
      '#etn=sprk_document&id=99999999-9999-9999-9999-999999999999';

    const result = parseBookmarkInput(url);

    expect(result).toEqual({
      kind: 'record',
      etn: 'sprk_matter',
      id: '11111111-1111-1111-1111-111111111111',
      pageType: NavItemPageType.EntityRecord,
    });
  });
});

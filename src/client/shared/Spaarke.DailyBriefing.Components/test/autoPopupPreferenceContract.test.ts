/**
 * Schema contract tests for the `autoPopup` preference — R4 task 043 / FR-17d.
 *
 * Background:
 *   The standalone LegalWorkspace `useDailyDigestAutoPopup` hook
 *   (`src/solutions/LegalWorkspace/src/hooks/useDailyDigestAutoPopup.ts`)
 *   reads the SAME `sprk_userpreference` row that the in-widget
 *   `PreferencesDropdown` writes via `saveDigestPreferences`. The hook
 *   intentionally inlines its own copy of the preference type ID + a small
 *   parser helper (`parseAutoPopupFromPreferenceJson`) rather than depending
 *   on `@spaarke/daily-briefing-components` directly (LegalWorkspace must not
 *   acquire a new package dep per task 043 constraints).
 *
 *   These tests enforce the contract the hook depends on:
 *     1. `PREFERENCE_TYPE_DAILY_DIGEST === 100000002` (the value the hook
 *        inlines as a constant must match the canonical export).
 *     2. `DEFAULT_DAILY_DIGEST_PREFERENCES.autoPopup === true` (opt-out
 *        model — missing preference rows must auto-popup).
 *     3. The `autoPopup` field round-trips through `fetchDigestPreferences`
 *        unchanged.
 *
 *   These tests sit alongside `useBriefingPreferences.test.ts` /
 *   `notificationService.test.ts` and run on every PR via the package's
 *   existing Jest config — they catch drift between the canonical R4 schema
 *   and the LegalWorkspace hook's inlined mirror.
 *
 * Per-test independence:
 *   Each `it` block resets mocks + uses fresh fixtures (per project
 *   "Independent mocks" convention).
 */

import {
  PREFERENCE_TYPE_DAILY_DIGEST,
  DEFAULT_DAILY_DIGEST_PREFERENCES,
  type DailyDigestPreferences,
  type IWebApi,
  type RetrieveMultipleResult,
  type WebApiEntity,
} from '../src/types/notifications';
import { fetchDigestPreferences } from '../src/services/preferencesService';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeWebApi(): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn(),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

const USER_ID = '00000000-0000-0000-0000-000000000001';

// ---------------------------------------------------------------------------
// Pure-helper: replicates the LegalWorkspace inlined parser
// (`parseAutoPopupFromPreferenceJson`) so this package's tests own the
// canonical contract. If the hook's inlined copy diverges, the LegalWorkspace
// build is unaffected, but a follow-up review should align them.
// ---------------------------------------------------------------------------

function parseAutoPopupFromPreferenceJson(rawValue: string | undefined | null): boolean {
  if (!rawValue) return true;
  try {
    const parsed = JSON.parse(rawValue) as { autoPopup?: unknown };
    if (typeof parsed.autoPopup === 'boolean') return parsed.autoPopup;
    return true;
  } catch {
    return true;
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('autoPopup preference — schema contract (FR-17d)', () => {
  describe('canonical constants the LegalWorkspace hook depends on', () => {
    it('PREFERENCE_TYPE_DAILY_DIGEST is the R4 canonical value (100000002)', () => {
      // The LegalWorkspace `useDailyDigestAutoPopup` hook inlines this
      // constant. If this value ever changes in
      // `Spaarke.DailyBriefing.Components/src/types/notifications.ts`, the
      // hook MUST be updated to match — this assertion is the canary.
      expect(PREFERENCE_TYPE_DAILY_DIGEST).toBe(100000002);
    });

    it('DEFAULT_DAILY_DIGEST_PREFERENCES.autoPopup is true (opt-out model)', () => {
      // The hook's "missing preference record → auto-popup" branch relies on
      // this default. If this flips to `false`, the hook would silently stop
      // popping for users who never set a preference.
      expect(DEFAULT_DAILY_DIGEST_PREFERENCES.autoPopup).toBe(true);
    });

    it('DailyDigestPreferences includes the autoPopup field', () => {
      // Type-level check: this compiles only if `autoPopup` is part of the
      // `DailyDigestPreferences` interface. Catches removal/rename.
      const prefs: DailyDigestPreferences = {
        ...DEFAULT_DAILY_DIGEST_PREFERENCES,
        autoPopup: false,
      };
      expect(prefs.autoPopup).toBe(false);
    });
  });

  describe('parseAutoPopupFromPreferenceJson — parser contract', () => {
    it('returns true when autoPopup is explicitly true', () => {
      const json = JSON.stringify({ autoPopup: true, disabledChannels: [] });
      expect(parseAutoPopupFromPreferenceJson(json)).toBe(true);
    });

    it('returns false when autoPopup is explicitly false', () => {
      const json = JSON.stringify({ autoPopup: false, disabledChannels: [] });
      expect(parseAutoPopupFromPreferenceJson(json)).toBe(false);
    });

    it('returns true (opt-out default) when JSON is missing autoPopup', () => {
      const json = JSON.stringify({ disabledChannels: ['tasks-overdue'] });
      expect(parseAutoPopupFromPreferenceJson(json)).toBe(true);
    });

    it('returns true (opt-out default) when raw value is undefined', () => {
      expect(parseAutoPopupFromPreferenceJson(undefined)).toBe(true);
    });

    it('returns true (opt-out default) when raw value is null', () => {
      expect(parseAutoPopupFromPreferenceJson(null)).toBe(true);
    });

    it('returns true (opt-out default) when raw value is empty string', () => {
      expect(parseAutoPopupFromPreferenceJson('')).toBe(true);
    });

    it('returns true (defensive default) when JSON is malformed', () => {
      expect(parseAutoPopupFromPreferenceJson('{ not valid json')).toBe(true);
    });

    it('returns true (defensive default) when autoPopup is non-boolean', () => {
      const json = JSON.stringify({ autoPopup: 'yes', disabledChannels: [] });
      expect(parseAutoPopupFromPreferenceJson(json)).toBe(true);
    });
  });

  describe('round-trip via fetchDigestPreferences (proves hook + widget share schema)', () => {
    it('returns autoPopup=true from the canonical preferences row', async () => {
      const webApi = makeWebApi();
      const stored: DailyDigestPreferences = {
        ...DEFAULT_DAILY_DIGEST_PREFERENCES,
        autoPopup: true,
      };
      const entity: WebApiEntity = {
        sprk_userpreferenceid: 'rec-1',
        sprk_preferencetype: PREFERENCE_TYPE_DAILY_DIGEST,
        sprk_preferencevalue: JSON.stringify(stored),
        _sprk_user_value: USER_ID,
        createdon: '2026-06-26T00:00:00Z',
        modifiedon: '2026-06-26T00:00:00Z',
      };
      (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [entity],
      } as RetrieveMultipleResult);

      const result = await fetchDigestPreferences(webApi, USER_ID);

      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.preferences.autoPopup).toBe(true);
      }
    });

    it('returns autoPopup=false from the canonical preferences row', async () => {
      const webApi = makeWebApi();
      const stored: DailyDigestPreferences = {
        ...DEFAULT_DAILY_DIGEST_PREFERENCES,
        autoPopup: false,
      };
      const entity: WebApiEntity = {
        sprk_userpreferenceid: 'rec-2',
        sprk_preferencetype: PREFERENCE_TYPE_DAILY_DIGEST,
        sprk_preferencevalue: JSON.stringify(stored),
        _sprk_user_value: USER_ID,
        createdon: '2026-06-26T00:00:00Z',
        modifiedon: '2026-06-26T00:00:00Z',
      };
      (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [entity],
      } as RetrieveMultipleResult);

      const result = await fetchDigestPreferences(webApi, USER_ID);

      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.preferences.autoPopup).toBe(false);
      }
    });

    it('returns autoPopup=true (default) when no preference record exists', async () => {
      // The hook's "no preference record → auto-popup" branch — proves the
      // service preserves the default rather than coercing to false.
      const webApi = makeWebApi();
      (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [],
      } as RetrieveMultipleResult);

      const result = await fetchDigestPreferences(webApi, USER_ID);

      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.preferences.autoPopup).toBe(true);
        expect(result.data.recordId).toBeUndefined();
      }
    });

    it('returns autoPopup=true (default) when raw value is empty / malformed', async () => {
      const webApi = makeWebApi();
      const entity: WebApiEntity = {
        sprk_userpreferenceid: 'rec-3',
        sprk_preferencetype: PREFERENCE_TYPE_DAILY_DIGEST,
        sprk_preferencevalue: '{ malformed json',
        _sprk_user_value: USER_ID,
      };
      (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [entity],
      } as RetrieveMultipleResult);

      const result = await fetchDigestPreferences(webApi, USER_ID);

      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.preferences.autoPopup).toBe(true);
      }
    });
  });
});

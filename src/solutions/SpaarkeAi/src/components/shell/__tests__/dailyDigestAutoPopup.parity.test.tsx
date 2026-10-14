/**
 * dailyDigestAutoPopup.parity.test.tsx — the REAL LegalWorkspace `useDailyDigestAutoPopup` hook (and its exported
 * `parseAutoPopupFromPreferenceJson`) stays in step with the canonical Daily Briefing preferences schema.
 *
 * Why this lives in SpaarkeAi: the hook sits in LegalWorkspace (no Jest setup; its imports need React) and
 * deliberately inlines `PREFERENCE_TYPE_DAILY_DIGEST` instead of depending on `@spaarke/daily-briefing-components`.
 * The Console already embeds LegalWorkspace and its Jest config resolves React / `@spaarke/ui-components` for
 * relative imports into it (see personalizeBanner.launch.test.tsx). The canonical side is imported by relative path
 * from `Spaarke.DailyBriefing.Components/src` (type-only + a pure service), so no new package dependency is added.
 *
 * Classification (ADR-038 §7): MAINTAIN — guards drift between the hook's inlined mirror and the canonical schema.
 */
import { renderHook, waitFor } from "@testing-library/react";

jest.mock("../../../../../LegalWorkspace/src/config/runtimeConfig", () => ({ getBffBaseUrl: () => "https://bff.example" }));
jest.mock("../../../../../LegalWorkspace/src/services/authInit", () => ({ authenticatedFetch: jest.fn() }));

import {
  parseAutoPopupFromPreferenceJson,
  useDailyDigestAutoPopup,
} from "../../../../../LegalWorkspace/src/hooks/useDailyDigestAutoPopup";
import { fetchDigestPreferences } from "../../../../../../client/shared/Spaarke.DailyBriefing.Components/src/services/preferencesService";
import {
  DEFAULT_DAILY_DIGEST_PREFERENCES,
  PREFERENCE_TYPE_DAILY_DIGEST,
} from "../../../../../../client/shared/Spaarke.DailyBriefing.Components/src/types/notifications";

const USER_ID = "00000000-0000-0000-0000-000000000001";

/** Minimal Xrm.WebApi double whose preference query returns `raw` (or no row when `raw === undefined`). */
function makeWebApi(raw?: string) {
  const entities =
    raw === undefined
      ? []
      : [
          {
            sprk_userpreferenceid: "rec-1",
            sprk_preferencetype: PREFERENCE_TYPE_DAILY_DIGEST,
            sprk_preferencevalue: raw,
            _sprk_user_value: USER_ID,
          },
        ];
  return {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities }),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

describe("parseAutoPopupFromPreferenceJson — parity with the canonical preferences service", () => {
  const cases: Array<[string, string]> = [
    ["autoPopup true", JSON.stringify({ ...DEFAULT_DAILY_DIGEST_PREFERENCES, autoPopup: true })],
    ["autoPopup false", JSON.stringify({ ...DEFAULT_DAILY_DIGEST_PREFERENCES, autoPopup: false })],
    ["autoPopup field missing", JSON.stringify({ dueWithinDays: 3 })],
    ["autoPopup wrong type (string)", JSON.stringify({ autoPopup: "false" })],
    ["autoPopup wrong type (null)", JSON.stringify({ autoPopup: null })],
    ["malformed JSON", "{ malformed json"],
    ["empty string", ""],
  ];

  beforeEach(() => {
    // The canonical service warns on malformed JSON; expected here.
    jest.spyOn(console, "warn").mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it.each(cases)("matches fetchDigestPreferences for: %s", async (_label, raw) => {
    const canonical = await fetchDigestPreferences(makeWebApi(raw), USER_ID);

    expect(canonical.success).toBe(true);
    if (!canonical.success) return;
    expect(parseAutoPopupFromPreferenceJson(raw)).toBe(canonical.data.preferences.autoPopup);
  });

  it("returns the opt-out default (true) for null / undefined, like a missing preference row", async () => {
    const canonical = await fetchDigestPreferences(makeWebApi(undefined), USER_ID);

    expect(canonical.success).toBe(true);
    if (!canonical.success) return;
    expect(canonical.data.preferences.autoPopup).toBe(true);
    expect(parseAutoPopupFromPreferenceJson(null)).toBe(true);
    expect(parseAutoPopupFromPreferenceJson(undefined)).toBe(true);
  });
});

describe("useDailyDigestAutoPopup — reads the canonical preference type and honours the opt-out", () => {
  let navigateTo: jest.Mock;

  beforeEach(() => {
    sessionStorage.clear();
    navigateTo = jest.fn().mockResolvedValue(undefined);
    (window as unknown as { Xrm?: unknown }).Xrm = { Navigation: { navigateTo } };
  });

  afterEach(() => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    sessionStorage.clear();
  });

  it("queries the canonical PREFERENCE_TYPE_DAILY_DIGEST (inlined constant has not drifted)", async () => {
    const webApi = makeWebApi(JSON.stringify({ autoPopup: true }));

    renderHook(() => useDailyDigestAutoPopup({ webApi, userId: USER_ID }));

    await waitFor(() => expect(webApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1));
    const query = webApi.retrieveMultipleRecords.mock.calls[0][1] as string;
    expect(query).toContain(`sprk_preferencetype eq ${PREFERENCE_TYPE_DAILY_DIGEST}`);
  });

  it("opens the Daily Briefing dialog when autoPopup is true", async () => {
    const webApi = makeWebApi(JSON.stringify({ autoPopup: true }));

    renderHook(() => useDailyDigestAutoPopup({ webApi, userId: USER_ID }));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0]).toEqual({ pageType: "webresource", webresourceName: "sprk_dailyupdate" });
  });

  it("does not open the dialog when the stored preference has autoPopup false", async () => {
    const webApi = makeWebApi(JSON.stringify({ autoPopup: false }));

    renderHook(() => useDailyDigestAutoPopup({ webApi, userId: USER_ID }));

    await waitFor(() => expect(sessionStorage.getItem("spaarke_dailyDigestShown")).toBe("opted-out"));
    expect(navigateTo).not.toHaveBeenCalled();
  });
});

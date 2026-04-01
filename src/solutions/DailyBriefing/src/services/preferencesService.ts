/**
 * Preferences Service — fetch and save Daily Digest user preferences
 * from sprk_userpreference via Xrm.WebApi.
 *
 * Opt-out model: all channels are enabled by default. Only overrides
 * (disabled channels, parameter customizations) are stored.
 *
 * Follows the same pattern as LegalWorkspace's useUserPreferences hook
 * but extracted as a standalone service for reuse by the hook layer.
 */

import type {
  IWebApi,
  WebApiEntity,
  DailyDigestPreferences,
} from "../types/notifications";
import {
  DEFAULT_DAILY_DIGEST_PREFERENCES,
  PREFERENCE_TYPE_DAILY_DIGEST,
  tryCatch,
} from "../types/notifications";
import type { IResult } from "../types/notifications";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const PREFERENCE_SELECT = [
  "sprk_userpreferenceid",
  "sprk_preferencetype",
  "sprk_preferencevalue",
  "_sprk_user_value",
  "createdon",
  "modifiedon",
].join(",");

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Fetch Daily Digest preferences for the given user.
 * Returns the parsed preferences, or defaults if no record exists.
 *
 * @param webApi - Xrm.WebApi reference
 * @param userId - The current user's systemuser GUID
 * @returns IResult<{ preferences: DailyDigestPreferences; recordId: string | undefined }>
 */
export async function fetchDigestPreferences(
  webApi: IWebApi,
  userId: string
): Promise<IResult<{ preferences: DailyDigestPreferences; recordId: string | undefined }>> {
  const filter =
    `_sprk_user_value eq ${userId} and sprk_preferencetype eq ${PREFERENCE_TYPE_DAILY_DIGEST}`;
  const query = `?$select=${PREFERENCE_SELECT}&$filter=${filter}&$top=1`;

  return tryCatch(async () => {
    const result = await webApi.retrieveMultipleRecords(
      "sprk_userpreference",
      query,
      1
    );

    if (result.entities.length === 0) {
      return {
        preferences: { ...DEFAULT_DAILY_DIGEST_PREFERENCES },
        recordId: undefined,
      };
    }

    const record = result.entities[0];
    const recordId = record["sprk_userpreferenceid"] as string;
    const rawValue = record["sprk_preferencevalue"] as string | undefined;

    if (!rawValue) {
      return {
        preferences: { ...DEFAULT_DAILY_DIGEST_PREFERENCES },
        recordId,
      };
    }

    try {
      const parsed = JSON.parse(rawValue) as Partial<DailyDigestPreferences>;
      return {
        preferences: mergeWithDefaults(parsed),
        recordId,
      };
    } catch {
      console.warn("[DailyBriefing] Failed to parse preference JSON, using defaults");
      return {
        preferences: { ...DEFAULT_DAILY_DIGEST_PREFERENCES },
        recordId,
      };
    }
  }, "PREFERENCES_FETCH_ERROR");
}

/**
 * Save Daily Digest preferences for the given user.
 * Creates a new record if no existingRecordId is provided; otherwise updates.
 *
 * @param webApi - Xrm.WebApi reference
 * @param userId - The current user's systemuser GUID
 * @param preferences - The preferences to save
 * @param existingRecordId - Optional existing sprk_userpreferenceid for update
 * @returns IResult<string> — the preference record ID
 */
export async function saveDigestPreferences(
  webApi: IWebApi,
  userId: string,
  preferences: DailyDigestPreferences,
  existingRecordId?: string
): Promise<IResult<string>> {
  const jsonValue = JSON.stringify(preferences);

  // Update existing record
  if (existingRecordId) {
    return tryCatch(async () => {
      await webApi.updateRecord("sprk_userpreference", existingRecordId, {
        sprk_preferencevalue: jsonValue,
      });
      return existingRecordId;
    }, "PREFERENCES_SAVE_ERROR");
  }

  // Check if a record already exists (in case recordId was lost)
  const existing = await fetchDigestPreferences(webApi, userId);
  if (existing.success && existing.data.recordId) {
    return tryCatch(async () => {
      await webApi.updateRecord("sprk_userpreference", existing.data.recordId!, {
        sprk_preferencevalue: jsonValue,
      });
      return existing.data.recordId!;
    }, "PREFERENCES_SAVE_ERROR");
  }

  // Create new preference record
  return tryCatch(async () => {
    const record: WebApiEntity = {
      sprk_preferencetype: PREFERENCE_TYPE_DAILY_DIGEST,
      sprk_preferencevalue: jsonValue,
      "sprk_User@odata.bind": `/systemusers(${userId})`,
    };
    const result = await webApi.createRecord("sprk_userpreference", record);
    return result.id;
  }, "PREFERENCES_CREATE_ERROR");
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Merge partial preferences with defaults, ensuring all fields have valid values.
 */
function mergeWithDefaults(
  partial: Partial<DailyDigestPreferences>
): DailyDigestPreferences {
  return {
    disabledChannels: Array.isArray(partial.disabledChannels)
      ? partial.disabledChannels
      : DEFAULT_DAILY_DIGEST_PREFERENCES.disabledChannels,
    dueWithinDays: typeof partial.dueWithinDays === "number"
      ? partial.dueWithinDays
      : DEFAULT_DAILY_DIGEST_PREFERENCES.dueWithinDays,
    timeWindow: typeof partial.timeWindow === "string"
      ? partial.timeWindow
      : DEFAULT_DAILY_DIGEST_PREFERENCES.timeWindow,
    minConfidence: typeof partial.minConfidence === "number"
      ? partial.minConfidence
      : DEFAULT_DAILY_DIGEST_PREFERENCES.minConfidence,
    autoPopup: typeof partial.autoPopup === "boolean"
      ? partial.autoPopup
      : DEFAULT_DAILY_DIGEST_PREFERENCES.autoPopup,
  };
}

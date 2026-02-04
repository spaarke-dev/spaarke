/**
 * EventTypeService Unit Tests
 *
 * Tests for the EventTypeService class including:
 * - parseFieldConfigJson: JSON parsing and validation
 * - computeFieldStates: Configuration merging with defaults
 * - validateConfig: Configuration validation
 * - Cache management
 *
 * @see EventTypeService.ts
 * @see EventTypeConfig.ts
 */

import {
  EventTypeService,
  eventTypeService,
  DEFAULT_EVENT_FIELD_STATES,
  ALL_EVENT_FIELDS,
  DEFAULT_SECTION_STATES,
  getEventTypeFieldConfig,
} from "../EventTypeService";
import type { IEventTypeFieldConfig, ISectionDefaults } from "../../types/EventTypeConfig";
import type { IWebApiLike } from "../../types/WebApiLike";

describe("EventTypeService", () => {
  let service: EventTypeService;

  beforeEach(() => {
    service = new EventTypeService();
  });

  // ===========================================================================
  // parseFieldConfigJson Tests
  // ===========================================================================

  describe("parseFieldConfigJson", () => {
    describe("null/empty input handling", () => {
      it("returns null for null input", () => {
        const result = service.parseFieldConfigJson(null);
        expect(result).toBeNull();
      });

      it("returns null for undefined input", () => {
        const result = service.parseFieldConfigJson(undefined);
        expect(result).toBeNull();
      });

      it("returns null for empty string", () => {
        const result = service.parseFieldConfigJson("");
        expect(result).toBeNull();
      });

      it("returns null for whitespace-only string", () => {
        const result = service.parseFieldConfigJson("   ");
        expect(result).toBeNull();
      });
    });

    describe("invalid JSON handling", () => {
      it("returns null for invalid JSON syntax", () => {
        const consoleSpy = jest.spyOn(console, "warn").mockImplementation();
        const result = service.parseFieldConfigJson("{invalid json}");
        expect(result).toBeNull();
        expect(consoleSpy).toHaveBeenCalledWith(
          expect.stringContaining("[EventTypeService] Failed to parse config JSON"),
          expect.anything()
        );
        consoleSpy.mockRestore();
      });

      it("returns empty config for non-object JSON (array)", () => {
        // Note: Arrays are technically objects in JavaScript (typeof [] === 'object')
        // The parsing treats them as objects with no valid properties, returning {}
        const result = service.parseFieldConfigJson('["field1", "field2"]');
        expect(result).toEqual({});
      });

      it("returns null for non-object JSON (string)", () => {
        const consoleSpy = jest.spyOn(console, "warn").mockImplementation();
        const result = service.parseFieldConfigJson('"just a string"');
        expect(result).toBeNull();
        consoleSpy.mockRestore();
      });

      it("returns null for non-object JSON (number)", () => {
        const consoleSpy = jest.spyOn(console, "warn").mockImplementation();
        const result = service.parseFieldConfigJson("42");
        expect(result).toBeNull();
        consoleSpy.mockRestore();
      });

      it("returns null for JSON null", () => {
        const consoleSpy = jest.spyOn(console, "warn").mockImplementation();
        const result = service.parseFieldConfigJson("null");
        expect(result).toBeNull();
        consoleSpy.mockRestore();
      });
    });

    describe("valid JSON parsing", () => {
      it("parses empty object to empty config", () => {
        const result = service.parseFieldConfigJson("{}");
        expect(result).toEqual({});
      });

      it("parses visibleFields array", () => {
        const json = '{"visibleFields": ["sprk_duedate", "sprk_priority"]}';
        const result = service.parseFieldConfigJson(json);
        expect(result).toEqual({
          visibleFields: ["sprk_duedate", "sprk_priority"],
        });
      });

      it("parses hiddenFields array", () => {
        const json = '{"hiddenFields": ["sprk_completeddate"]}';
        const result = service.parseFieldConfigJson(json);
        expect(result).toEqual({
          hiddenFields: ["sprk_completeddate"],
        });
      });

      it("parses requiredFields array", () => {
        const json = '{"requiredFields": ["sprk_duedate", "sprk_eventname"]}';
        const result = service.parseFieldConfigJson(json);
        expect(result).toEqual({
          requiredFields: ["sprk_duedate", "sprk_eventname"],
        });
      });

      it("parses optionalFields array", () => {
        const json = '{"optionalFields": ["sprk_description"]}';
        const result = service.parseFieldConfigJson(json);
        expect(result).toEqual({
          optionalFields: ["sprk_description"],
        });
      });

      it("parses sectionDefaults", () => {
        const json = '{"sectionDefaults": {"dates": "expanded", "relatedEvent": "collapsed"}}';
        const result = service.parseFieldConfigJson(json);
        expect(result).toEqual({
          sectionDefaults: {
            dates: "expanded",
            relatedEvent: "collapsed",
          },
        });
      });

      it("parses complete configuration", () => {
        const config: IEventTypeFieldConfig = {
          visibleFields: ["sprk_duedate", "sprk_priority"],
          hiddenFields: ["sprk_completeddate"],
          requiredFields: ["sprk_duedate"],
          optionalFields: ["sprk_description"],
          sectionDefaults: {
            dates: "expanded",
            relatedEvent: "collapsed",
            description: "expanded",
            history: "collapsed",
          },
        };
        const json = JSON.stringify(config);
        const result = service.parseFieldConfigJson(json);
        expect(result).toEqual(config);
      });
    });

    describe("field filtering", () => {
      it("filters non-string values from visibleFields", () => {
        const json = '{"visibleFields": ["sprk_duedate", 123, null, "sprk_priority", true]}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.visibleFields).toEqual(["sprk_duedate", "sprk_priority"]);
      });

      it("filters non-string values from hiddenFields", () => {
        const json = '{"hiddenFields": [null, "sprk_completeddate", {}]}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.hiddenFields).toEqual(["sprk_completeddate"]);
      });

      it("filters non-string values from requiredFields", () => {
        const json = '{"requiredFields": ["sprk_duedate", [], "sprk_eventname"]}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.requiredFields).toEqual(["sprk_duedate", "sprk_eventname"]);
      });

      it("ignores non-array visibleFields", () => {
        const json = '{"visibleFields": "not an array"}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.visibleFields).toBeUndefined();
      });

      it("ignores non-array hiddenFields", () => {
        const json = '{"hiddenFields": {"key": "value"}}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.hiddenFields).toBeUndefined();
      });
    });

    describe("sectionDefaults validation", () => {
      it("ignores invalid section state values", () => {
        const json = '{"sectionDefaults": {"dates": "invalid", "relatedEvent": "collapsed"}}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.sectionDefaults).toEqual({
          relatedEvent: "collapsed",
        });
      });

      it("ignores non-object sectionDefaults", () => {
        const json = '{"sectionDefaults": "not an object"}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.sectionDefaults).toBeUndefined();
      });

      it("ignores unknown section keys", () => {
        const json = '{"sectionDefaults": {"dates": "expanded", "unknownSection": "collapsed"}}';
        const result = service.parseFieldConfigJson(json);
        expect(result?.sectionDefaults).toEqual({
          dates: "expanded",
        });
      });
    });
  });

  // ===========================================================================
  // computeFieldStates Tests
  // ===========================================================================

  describe("computeFieldStates", () => {
    it("returns default states when config is null", () => {
      const result = service.computeFieldStates(null);

      expect(result.sourceConfig).toBeNull();
      expect(result.sectionDefaults).toEqual(DEFAULT_SECTION_STATES);

      // Check a few default field states
      const eventNameState = result.fields.get("sprk_eventname");
      expect(eventNameState?.isVisible).toBe(true);
      expect(eventNameState?.requiredLevel).toBe("required");
      expect(eventNameState?.isOverridden).toBe(false);
    });

    it("applies hiddenFields to make fields invisible", () => {
      const config: IEventTypeFieldConfig = {
        hiddenFields: ["sprk_basedate", "sprk_completeddate"],
      };
      const result = service.computeFieldStates(config);

      const baseDateState = result.fields.get("sprk_basedate");
      expect(baseDateState?.isVisible).toBe(false);
      expect(baseDateState?.requiredLevel).toBe("none"); // Hidden fields can't be required
      expect(baseDateState?.isOverridden).toBe(true);
    });

    it("applies visibleFields to override hidden state", () => {
      const config: IEventTypeFieldConfig = {
        hiddenFields: ["sprk_basedate"],
        visibleFields: ["sprk_basedate"], // Override hidden
      };
      const result = service.computeFieldStates(config);

      const baseDateState = result.fields.get("sprk_basedate");
      expect(baseDateState?.isVisible).toBe(true);
      expect(baseDateState?.isOverridden).toBe(true);
    });

    it("applies requiredFields to make fields required and visible", () => {
      const config: IEventTypeFieldConfig = {
        hiddenFields: ["sprk_duedate"], // Try to hide
        requiredFields: ["sprk_duedate"], // But require (takes precedence)
      };
      const result = service.computeFieldStates(config);

      const dueDateState = result.fields.get("sprk_duedate");
      expect(dueDateState?.isVisible).toBe(true); // Required fields must be visible
      expect(dueDateState?.requiredLevel).toBe("required");
      expect(dueDateState?.isOverridden).toBe(true);
    });

    it("applies optionalFields to remove requirement", () => {
      const config: IEventTypeFieldConfig = {
        optionalFields: ["sprk_eventname"], // Override default requirement
      };
      const result = service.computeFieldStates(config);

      const eventNameState = result.fields.get("sprk_eventname");
      expect(eventNameState?.requiredLevel).toBe("none");
      expect(eventNameState?.isOverridden).toBe(true);
    });

    it("merges sectionDefaults with defaults", () => {
      const config: IEventTypeFieldConfig = {
        sectionDefaults: {
          dates: "collapsed",
          history: "expanded",
        },
      };
      const result = service.computeFieldStates(config);

      expect(result.sectionDefaults).toEqual({
        dates: "collapsed", // Overridden
        relatedEvent: "collapsed", // Default
        description: "expanded", // Default
        history: "expanded", // Overridden
      });
    });

    it("accepts custom defaults", () => {
      const customDefaults = {
        custom_field: { visible: true, requiredLevel: "recommended" as const },
      };
      const result = service.computeFieldStates(null, customDefaults);

      const customState = result.fields.get("custom_field");
      expect(customState?.isVisible).toBe(true);
      expect(customState?.requiredLevel).toBe("recommended");
    });
  });

  // ===========================================================================
  // validateConfig Tests
  // ===========================================================================

  describe("validateConfig", () => {
    it("validates empty config as valid", () => {
      const result = service.validateConfig({});
      expect(result.isValid).toBe(true);
      expect(result.warnings).toHaveLength(0);
      expect(result.errors).toHaveLength(0);
    });

    it("validates config with known fields as valid", () => {
      const config: IEventTypeFieldConfig = {
        visibleFields: ["sprk_duedate", "sprk_priority"],
        hiddenFields: ["sprk_completeddate"],
        requiredFields: ["sprk_duedate"],
      };
      const result = service.validateConfig(config);
      expect(result.isValid).toBe(true);
    });

    it("warns about unknown fields", () => {
      const config: IEventTypeFieldConfig = {
        visibleFields: ["unknown_field"],
      };
      const result = service.validateConfig(config);
      expect(result.isValid).toBe(true); // Warnings don't invalidate
      expect(result.warnings).toContain("Unknown field 'unknown_field' in visibleFields");
    });

    it("errors when field is in both hiddenFields and requiredFields", () => {
      const config: IEventTypeFieldConfig = {
        hiddenFields: ["sprk_duedate"],
        requiredFields: ["sprk_duedate"],
      };
      const result = service.validateConfig(config);
      expect(result.isValid).toBe(false);
      expect(result.errors).toContain(
        "Field 'sprk_duedate' is in both hiddenFields and requiredFields (required takes precedence)"
      );
    });

    it("checks all field lists for unknown fields", () => {
      const config: IEventTypeFieldConfig = {
        visibleFields: ["unknown_visible"],
        hiddenFields: ["unknown_hidden"],
        requiredFields: ["unknown_required"],
        optionalFields: ["unknown_optional"],
      };
      const result = service.validateConfig(config);
      expect(result.warnings).toHaveLength(4);
    });
  });

  // ===========================================================================
  // Helper Method Tests
  // ===========================================================================

  describe("helper methods", () => {
    it("getFieldState returns correct state for known field", () => {
      const config: IEventTypeFieldConfig = {
        hiddenFields: ["sprk_basedate"],
      };
      const state = service.getFieldState(config, "sprk_basedate");
      expect(state?.isVisible).toBe(false);
      expect(state?.isOverridden).toBe(true);
    });

    it("getFieldState returns null for unknown field", () => {
      const state = service.getFieldState(null, "nonexistent_field");
      expect(state).toBeNull();
    });

    it("isFieldVisible returns correct visibility", () => {
      const config: IEventTypeFieldConfig = {
        hiddenFields: ["sprk_basedate"],
      };
      expect(service.isFieldVisible(config, "sprk_basedate")).toBe(false);
      expect(service.isFieldVisible(config, "sprk_duedate")).toBe(true);
    });

    it("isFieldRequired returns correct requirement", () => {
      const config: IEventTypeFieldConfig = {
        requiredFields: ["sprk_duedate"],
      };
      expect(service.isFieldRequired(config, "sprk_duedate")).toBe(true);
      expect(service.isFieldRequired(config, "sprk_basedate")).toBe(false);
    });

    it("getFieldRequiredLevel returns correct level", () => {
      const config: IEventTypeFieldConfig = {
        requiredFields: ["sprk_duedate"],
      };
      expect(service.getFieldRequiredLevel(config, "sprk_duedate")).toBe("required");
      expect(service.getFieldRequiredLevel(config, "sprk_basedate")).toBe("none");
    });

    it("getDefaultFieldState returns default for known field", () => {
      const state = service.getDefaultFieldState("sprk_eventname");
      expect(state).toEqual({ visible: true, requiredLevel: "required" });
    });

    it("getDefaultFieldState returns null for unknown field", () => {
      const state = service.getDefaultFieldState("nonexistent_field");
      expect(state).toBeNull();
    });

    it("getAllFieldNames returns all known fields", () => {
      const fields = service.getAllFieldNames();
      expect(fields).toEqual(ALL_EVENT_FIELDS);
      expect(fields).toContain("sprk_eventname");
      expect(fields).toContain("sprk_duedate");
    });

    it("getDefaultSectionStates returns section defaults", () => {
      const sections = service.getDefaultSectionStates();
      expect(sections).toEqual(DEFAULT_SECTION_STATES);
    });
  });

  // ===========================================================================
  // Cache Management Tests
  // ===========================================================================

  describe("cache management", () => {
    it("getCachedConfig returns null when caching disabled", () => {
      const service = new EventTypeService({ enableCache: false });
      const result = service.getCachedConfig("some-id");
      expect(result).toBeNull();
    });

    it("setCachedConfig does nothing when caching disabled", () => {
      const service = new EventTypeService({ enableCache: false });
      const config: IEventTypeFieldConfig = { visibleFields: ["sprk_duedate"] };
      service.setCachedConfig("some-id", config);
      expect(service.getCachedConfig("some-id")).toBeNull();
    });

    it("caches and retrieves config when enabled", () => {
      const service = new EventTypeService({ enableCache: true });
      const config: IEventTypeFieldConfig = { visibleFields: ["sprk_duedate"] };
      service.setCachedConfig("some-id", config);
      const cached = service.getCachedConfig("some-id");
      expect(cached).toEqual(config);
    });

    it("expires cache after TTL", () => {
      const service = new EventTypeService({ enableCache: true, cacheTtlMs: 100 });
      const config: IEventTypeFieldConfig = { visibleFields: ["sprk_duedate"] };
      service.setCachedConfig("some-id", config);

      // Before expiry
      expect(service.getCachedConfig("some-id")).toEqual(config);

      // Mock time passing
      jest.useFakeTimers();
      jest.advanceTimersByTime(150);

      // After expiry
      expect(service.getCachedConfig("some-id")).toBeNull();

      jest.useRealTimers();
    });

    it("clearCache removes all cached entries", () => {
      const service = new EventTypeService({ enableCache: true });
      service.setCachedConfig("id-1", { visibleFields: ["field1"] });
      service.setCachedConfig("id-2", { visibleFields: ["field2"] });

      service.clearCache();

      expect(service.getCachedConfig("id-1")).toBeNull();
      expect(service.getCachedConfig("id-2")).toBeNull();
    });

    it("clearCacheForEventType removes specific entry", () => {
      const service = new EventTypeService({ enableCache: true });
      service.setCachedConfig("id-1", { visibleFields: ["field1"] });
      service.setCachedConfig("id-2", { visibleFields: ["field2"] });

      service.clearCacheForEventType("id-1");

      expect(service.getCachedConfig("id-1")).toBeNull();
      expect(service.getCachedConfig("id-2")).toEqual({ visibleFields: ["field2"] });
    });
  });

  // ===========================================================================
  // Singleton Instance Tests
  // ===========================================================================

  describe("singleton instance", () => {
    it("eventTypeService is an instance of EventTypeService", () => {
      expect(eventTypeService).toBeInstanceOf(EventTypeService);
    });

    it("singleton parseFieldConfigJson works correctly", () => {
      const result = eventTypeService.parseFieldConfigJson('{"visibleFields": ["sprk_duedate"]}');
      expect(result).toEqual({ visibleFields: ["sprk_duedate"] });
    });
  });

  // ===========================================================================
  // Default Exports Tests
  // ===========================================================================

  describe("default exports", () => {
    it("DEFAULT_EVENT_FIELD_STATES contains expected fields", () => {
      expect(DEFAULT_EVENT_FIELD_STATES.sprk_eventname).toEqual({
        visible: true,
        requiredLevel: "required",
      });
      expect(DEFAULT_EVENT_FIELD_STATES.sprk_duedate).toEqual({
        visible: true,
        requiredLevel: "none",
      });
    });

    it("ALL_EVENT_FIELDS contains all default field names", () => {
      expect(ALL_EVENT_FIELDS).toContain("sprk_eventname");
      expect(ALL_EVENT_FIELDS).toContain("sprk_duedate");
      expect(ALL_EVENT_FIELDS).toContain("sprk_basedate");
      expect(ALL_EVENT_FIELDS).toContain("sprk_completeddate");
      expect(ALL_EVENT_FIELDS).toContain("sprk_remindat");
      expect(ALL_EVENT_FIELDS).toContain("sprk_relatedevent");
      expect(ALL_EVENT_FIELDS).toContain("statecode");
      expect(ALL_EVENT_FIELDS).toContain("statuscode");
    });

    it("DEFAULT_SECTION_STATES contains all section defaults", () => {
      expect(DEFAULT_SECTION_STATES.dates).toBe("expanded");
      expect(DEFAULT_SECTION_STATES.relatedEvent).toBe("collapsed");
      expect(DEFAULT_SECTION_STATES.description).toBe("expanded");
      expect(DEFAULT_SECTION_STATES.history).toBe("collapsed");
    });
  });

  // ===========================================================================
  // getDefaultFieldStates Method Tests
  // ===========================================================================

  describe("getDefaultFieldStates", () => {
    it("returns a copy of default field states", () => {
      const states = service.getDefaultFieldStates();
      expect(states).toEqual(DEFAULT_EVENT_FIELD_STATES);
    });

    it("returns a new object on each call (not the original)", () => {
      const states1 = service.getDefaultFieldStates();
      const states2 = service.getDefaultFieldStates();
      expect(states1).not.toBe(states2);
      expect(states1).toEqual(states2);
    });

    it("modifications to returned object do not affect defaults", () => {
      const states = service.getDefaultFieldStates();
      states.sprk_eventname = { visible: false, requiredLevel: "none" };

      const freshStates = service.getDefaultFieldStates();
      expect(freshStates.sprk_eventname).toEqual({ visible: true, requiredLevel: "required" });
    });
  });

  // ===========================================================================
  // Helper Method Edge Cases
  // ===========================================================================

  describe("helper method edge cases", () => {
    it("isFieldVisible returns true for unknown field (safe default)", () => {
      expect(service.isFieldVisible(null, "completely_unknown_field")).toBe(true);
    });

    it("isFieldRequired returns false for unknown field", () => {
      expect(service.isFieldRequired(null, "completely_unknown_field")).toBe(false);
    });

    it("getFieldRequiredLevel returns 'none' for unknown field", () => {
      expect(service.getFieldRequiredLevel(null, "completely_unknown_field")).toBe("none");
    });

    it("getFieldState returns null for field not in custom defaults", () => {
      const customDefaults = {
        custom_only_field: { visible: true, requiredLevel: "required" as const },
      };
      const state = service.getFieldState(null, "sprk_eventname", customDefaults);
      expect(state).toBeNull(); // sprk_eventname is not in custom defaults
    });
  });
});

// =============================================================================
// getEventTypeFieldConfig Function Tests (Standalone Function)
// =============================================================================

describe("getEventTypeFieldConfig", () => {
  // Mock WebAPI
  const createMockWebApi = (overrides: Partial<IWebApiLike> = {}): IWebApiLike => ({
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    ...overrides,
  });

  describe("input validation", () => {
    it("returns error for null eventTypeId", async () => {
      const mockWebApi = createMockWebApi();
      const result = await getEventTypeFieldConfig(mockWebApi, null as unknown as string);

      expect(result.success).toBe(false);
      expect(result.error).toBe("Event Type ID is required");
      expect(result.notFound).toBe(false);
      expect(mockWebApi.retrieveRecord).not.toHaveBeenCalled();
    });

    it("returns error for empty eventTypeId", async () => {
      const mockWebApi = createMockWebApi();
      const result = await getEventTypeFieldConfig(mockWebApi, "");

      expect(result.success).toBe(false);
      expect(result.error).toBe("Event Type ID is required");
    });

    it("returns error for whitespace-only eventTypeId", async () => {
      const mockWebApi = createMockWebApi();
      const result = await getEventTypeFieldConfig(mockWebApi, "   ");

      expect(result.success).toBe(false);
      expect(result.error).toBe("Event Type ID is required");
    });
  });

  describe("GUID normalization", () => {
    it("normalizes GUID with braces", async () => {
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Test Type",
          sprk_fieldconfigjson: null,
        }),
      });

      await getEventTypeFieldConfig(mockWebApi, "{12345678-1234-1234-1234-123456789ABC}");

      expect(mockWebApi.retrieveRecord).toHaveBeenCalledWith(
        "sprk_eventtype",
        "12345678-1234-1234-1234-123456789abc",
        expect.any(String)
      );
    });

    it("normalizes GUID to lowercase", async () => {
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Test Type",
          sprk_fieldconfigjson: null,
        }),
      });

      await getEventTypeFieldConfig(mockWebApi, "ABCD1234-ABCD-ABCD-ABCD-ABCD12345678");

      expect(mockWebApi.retrieveRecord).toHaveBeenCalledWith(
        "sprk_eventtype",
        "abcd1234-abcd-abcd-abcd-abcd12345678",
        expect.any(String)
      );
    });
  });

  describe("successful retrieval", () => {
    it("returns success with parsed config", async () => {
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Meeting",
          sprk_fieldconfigjson: '{"visibleFields": ["sprk_duedate"]}',
        }),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(true);
      expect(result.eventTypeName).toBe("Meeting");
      expect(result.config).toEqual({ visibleFields: ["sprk_duedate"] });
      expect(result.eventTypeId).toBe("12345678-1234-1234-1234-123456789012");
    });

    it("returns success with null config when JSON is null", async () => {
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Simple Type",
          sprk_fieldconfigjson: null,
        }),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(true);
      expect(result.config).toBeNull();
    });

    it("returns success with null config when JSON is empty string", async () => {
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Empty Config Type",
          sprk_fieldconfigjson: "",
        }),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(true);
      expect(result.config).toBeNull();
    });

    it("returns full config with all fields", async () => {
      const fullConfig = {
        visibleFields: ["sprk_duedate", "sprk_priority"],
        hiddenFields: ["sprk_completeddate"],
        requiredFields: ["sprk_duedate"],
        optionalFields: ["sprk_description"],
        sectionDefaults: {
          dates: "expanded",
          history: "collapsed",
        },
      };

      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Full Config Type",
          sprk_fieldconfigjson: JSON.stringify(fullConfig),
        }),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(true);
      expect(result.config).toEqual(fullConfig);
    });
  });

  describe("error handling", () => {
    it("returns notFound for 404 error", async () => {
      const consoleSpy = jest.spyOn(console, "warn").mockImplementation();
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockRejectedValue(new Error("404 Not Found")),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(false);
      expect(result.notFound).toBe(true);
      expect(result.error).toContain("not found");
      consoleSpy.mockRestore();
    });

    it("returns notFound for 'does not exist' error", async () => {
      const consoleSpy = jest.spyOn(console, "warn").mockImplementation();
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockRejectedValue(new Error("Record does not exist")),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(false);
      expect(result.notFound).toBe(true);
      consoleSpy.mockRestore();
    });

    it("returns generic error for network failure", async () => {
      const consoleSpy = jest.spyOn(console, "error").mockImplementation();
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockRejectedValue(new Error("Network request failed")),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(false);
      expect(result.notFound).toBe(false);
      expect(result.error).toBe("Network request failed");
      consoleSpy.mockRestore();
    });

    it("handles non-Error thrown values", async () => {
      const consoleSpy = jest.spyOn(console, "error").mockImplementation();
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockRejectedValue("String error"),
      });

      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(false);
      expect(result.error).toBe("String error");
      consoleSpy.mockRestore();
    });
  });

  describe("caching behavior", () => {
    it("returns cached config on second call", async () => {
      const cacheService = new EventTypeService({ enableCache: true });
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Cached Type",
          sprk_fieldconfigjson: '{"visibleFields": ["sprk_duedate"]}',
        }),
      });

      // First call - hits WebAPI
      const result1 = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012", {
        service: cacheService,
      });
      expect(result1.success).toBe(true);
      expect(mockWebApi.retrieveRecord).toHaveBeenCalledTimes(1);

      // Second call - should use cache
      const result2 = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012", {
        service: cacheService,
      });
      expect(result2.success).toBe(true);
      expect(result2.config).toEqual({ visibleFields: ["sprk_duedate"] });
      expect(mockWebApi.retrieveRecord).toHaveBeenCalledTimes(1); // Still only 1 call
    });

    it("does not cache null config", async () => {
      const cacheService = new EventTypeService({ enableCache: true });
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "No Config Type",
          sprk_fieldconfigjson: null,
        }),
      });

      // First call
      await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012", {
        service: cacheService,
      });

      // Second call - should hit WebAPI again since null wasn't cached
      await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012", {
        service: cacheService,
      });

      expect(mockWebApi.retrieveRecord).toHaveBeenCalledTimes(2);
    });

    it("uses singleton service when no service provided", async () => {
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_name: "Test",
          sprk_fieldconfigjson: null,
        }),
      });

      // Call without service option - should use eventTypeService singleton
      const result = await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(result.success).toBe(true);
    });
  });

  describe("correct WebAPI query", () => {
    it("queries sprk_eventtype with correct select fields", async () => {
      const mockWebApi = createMockWebApi({
        retrieveRecord: jest.fn().mockResolvedValue({
          sprk_eventtypeid: "12345678-1234-1234-1234-123456789012",
          sprk_name: "Test Type",
          sprk_fieldconfigjson: null,
        }),
      });

      await getEventTypeFieldConfig(mockWebApi, "12345678-1234-1234-1234-123456789012");

      expect(mockWebApi.retrieveRecord).toHaveBeenCalledWith(
        "sprk_eventtype",
        "12345678-1234-1234-1234-123456789012",
        "?$select=sprk_eventtypeid,sprk_name,sprk_fieldconfigjson"
      );
    });
  });
});

/**
 * Unit tests for FieldMappingService
 *
 * Tests cover:
 * - Type compatibility validation
 * - Value conversion
 * - Mapping application with cascading
 * - Error handling for missing fields, type mismatches
 * - Cache management
 */

import {
  FieldMappingService,
} from "../FieldMappingService";

import {
  FieldType,
  CompatibilityMode,
  CompatibilityLevel,
  MappingDirection,
  SyncMode,
  IFieldMappingProfile,
  IFieldMappingRule,
  STRICT_TYPE_COMPATIBILITY,
  MappingErrorCode,
} from "../../types/FieldMappingTypes";

// Mock WebAPI
const createMockWebApi = (): ComponentFramework.WebApi => ({
  createRecord: jest.fn().mockResolvedValue({ id: "new-id" }),
  deleteRecord: jest.fn().mockResolvedValue({}),
  updateRecord: jest.fn().mockResolvedValue({}),
  retrieveRecord: jest.fn().mockResolvedValue({}),
  retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
});

// Helper to create a test profile
const createTestProfile = (overrides?: Partial<IFieldMappingProfile>): IFieldMappingProfile => ({
  id: "profile-001",
  name: "Test Profile",
  sourceEntity: "sprk_matter",
  targetEntity: "sprk_event",
  mappingDirection: MappingDirection.ParentToChild,
  syncMode: SyncMode.OneTime,
  isActive: true,
  rules: [],
  ...overrides,
});

// Helper to create a test rule
const createTestRule = (overrides?: Partial<IFieldMappingRule>): IFieldMappingRule => ({
  id: "rule-001",
  profileId: "profile-001",
  sourceField: "sprk_client",
  sourceFieldType: FieldType.Lookup,
  targetField: "sprk_regardingaccount",
  targetFieldType: FieldType.Lookup,
  compatibilityMode: CompatibilityMode.Strict,
  isRequired: false,
  isCascadingSource: false,
  executionOrder: 1,
  isActive: true,
  ...overrides,
});

describe("FieldMappingService", () => {
  let service: FieldMappingService;
  let mockWebApi: ComponentFramework.WebApi;

  beforeEach(() => {
    mockWebApi = createMockWebApi();
    service = new FieldMappingService({ webApi: mockWebApi });
  });

  // ===========================================================================
  // Type Compatibility Validation Tests
  // ===========================================================================

  describe("validateTypeCompatibility", () => {
    it("should return exact compatibility for same types", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Text,
        FieldType.Text,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
      expect(result.level).toBe(CompatibilityLevel.Exact);
      expect(result.errors).toHaveLength(0);
    });

    it("should allow Text to Memo conversion", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Text,
        FieldType.Memo,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
      expect(result.level).toBe(CompatibilityLevel.Exact);
    });

    it("should allow Memo to Text conversion", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Memo,
        FieldType.Text,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
    });

    it("should allow Lookup to Text conversion with warning", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Lookup,
        FieldType.Text,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
      expect(result.level).toBe(CompatibilityLevel.SafeConversion);
      expect(result.warnings.length).toBeGreaterThan(0);
    });

    it("should allow Number to Text conversion", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Number,
        FieldType.Text,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
    });

    it("should allow DateTime to Text conversion", () => {
      const result = service.validateTypeCompatibility(
        FieldType.DateTime,
        FieldType.Text,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
    });

    it("should allow Boolean to Text conversion", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Boolean,
        FieldType.Text,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
    });

    it("should allow OptionSet to Text conversion", () => {
      const result = service.validateTypeCompatibility(
        FieldType.OptionSet,
        FieldType.Text,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(true);
    });

    it("should block Text to Lookup conversion in Strict mode", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Text,
        FieldType.Lookup,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(false);
      expect(result.level).toBe(CompatibilityLevel.Incompatible);
      expect(result.errors.length).toBeGreaterThan(0);
    });

    it("should block Text to Number conversion in Strict mode", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Text,
        FieldType.Number,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(false);
    });

    it("should block Text to OptionSet conversion in Strict mode", () => {
      const result = service.validateTypeCompatibility(
        FieldType.Text,
        FieldType.OptionSet,
        CompatibilityMode.Strict
      );

      expect(result.isCompatible).toBe(false);
    });

    it("should validate all combinations in STRICT_TYPE_COMPATIBILITY matrix", () => {
      // Test that the matrix is correctly applied
      for (const sourceType of Object.keys(STRICT_TYPE_COMPATIBILITY)) {
        const sourceTypeNum = parseInt(sourceType) as FieldType;
        const compatibleTargets = STRICT_TYPE_COMPATIBILITY[sourceTypeNum];

        for (const targetType of compatibleTargets) {
          const result = service.validateTypeCompatibility(
            sourceTypeNum,
            targetType,
            CompatibilityMode.Strict
          );

          expect(result.isCompatible).toBe(true);
        }
      }
    });
  });

  describe("validateMappingRule", () => {
    it("should validate a compatible rule", () => {
      const rule = createTestRule({
        sourceFieldType: FieldType.Lookup,
        targetFieldType: FieldType.Lookup,
      });

      const result = service.validateMappingRule(rule);

      expect(result.isCompatible).toBe(true);
    });

    it("should reject an incompatible rule", () => {
      const rule = createTestRule({
        sourceFieldType: FieldType.Text,
        targetFieldType: FieldType.Lookup,
        compatibilityMode: CompatibilityMode.Strict,
      });

      const result = service.validateMappingRule(rule);

      expect(result.isCompatible).toBe(false);
    });
  });

  // ===========================================================================
  // isTypeCompatible Tests (Task 011)
  // ===========================================================================

  describe("isTypeCompatible", () => {
    it("should return true for exact type match", () => {
      expect(service.isTypeCompatible(FieldType.Text, FieldType.Text)).toBe(true);
      expect(service.isTypeCompatible(FieldType.Lookup, FieldType.Lookup)).toBe(true);
      expect(service.isTypeCompatible(FieldType.Number, FieldType.Number)).toBe(true);
    });

    it("should return true for Lookup to Text (widening)", () => {
      expect(service.isTypeCompatible(FieldType.Lookup, FieldType.Text)).toBe(true);
    });

    it("should return false for Text to Lookup (narrowing)", () => {
      expect(service.isTypeCompatible(FieldType.Text, FieldType.Lookup)).toBe(false);
    });

    it("should return true for Text to Memo", () => {
      expect(service.isTypeCompatible(FieldType.Text, FieldType.Memo)).toBe(true);
    });

    it("should return true for Memo to Text", () => {
      expect(service.isTypeCompatible(FieldType.Memo, FieldType.Text)).toBe(true);
    });

    it("should return true for all types to Text (widening)", () => {
      expect(service.isTypeCompatible(FieldType.Number, FieldType.Text)).toBe(true);
      expect(service.isTypeCompatible(FieldType.DateTime, FieldType.Text)).toBe(true);
      expect(service.isTypeCompatible(FieldType.Boolean, FieldType.Text)).toBe(true);
      expect(service.isTypeCompatible(FieldType.OptionSet, FieldType.Text)).toBe(true);
    });

    it("should return false for incompatible conversions", () => {
      expect(service.isTypeCompatible(FieldType.Text, FieldType.Number)).toBe(false);
      expect(service.isTypeCompatible(FieldType.Text, FieldType.Boolean)).toBe(false);
      expect(service.isTypeCompatible(FieldType.Text, FieldType.DateTime)).toBe(false);
      expect(service.isTypeCompatible(FieldType.Number, FieldType.Boolean)).toBe(false);
      expect(service.isTypeCompatible(FieldType.DateTime, FieldType.Number)).toBe(false);
    });
  });

  // ===========================================================================
  // getCompatibleTargetTypes Tests (Task 011)
  // ===========================================================================

  describe("getCompatibleTargetTypes", () => {
    it("should return Lookup and Text for Lookup source", () => {
      const compatible = service.getCompatibleTargetTypes(FieldType.Lookup);

      expect(compatible).toContain(FieldType.Lookup);
      expect(compatible).toContain(FieldType.Text);
      expect(compatible).toHaveLength(2);
    });

    it("should return Text and Memo for Text source", () => {
      const compatible = service.getCompatibleTargetTypes(FieldType.Text);

      expect(compatible).toContain(FieldType.Text);
      expect(compatible).toContain(FieldType.Memo);
      expect(compatible).toHaveLength(2);
    });

    it("should return Text and Memo for Memo source", () => {
      const compatible = service.getCompatibleTargetTypes(FieldType.Memo);

      expect(compatible).toContain(FieldType.Text);
      expect(compatible).toContain(FieldType.Memo);
      expect(compatible).toHaveLength(2);
    });

    it("should return OptionSet and Text for OptionSet source", () => {
      const compatible = service.getCompatibleTargetTypes(FieldType.OptionSet);

      expect(compatible).toContain(FieldType.OptionSet);
      expect(compatible).toContain(FieldType.Text);
      expect(compatible).toHaveLength(2);
    });

    it("should return Number and Text for Number source", () => {
      const compatible = service.getCompatibleTargetTypes(FieldType.Number);

      expect(compatible).toContain(FieldType.Number);
      expect(compatible).toContain(FieldType.Text);
      expect(compatible).toHaveLength(2);
    });

    it("should return DateTime and Text for DateTime source", () => {
      const compatible = service.getCompatibleTargetTypes(FieldType.DateTime);

      expect(compatible).toContain(FieldType.DateTime);
      expect(compatible).toContain(FieldType.Text);
      expect(compatible).toHaveLength(2);
    });

    it("should return Boolean and Text for Boolean source", () => {
      const compatible = service.getCompatibleTargetTypes(FieldType.Boolean);

      expect(compatible).toContain(FieldType.Boolean);
      expect(compatible).toContain(FieldType.Text);
      expect(compatible).toHaveLength(2);
    });
  });

  // ===========================================================================
  // validateProfile Tests (Task 011)
  // ===========================================================================

  describe("validateProfile", () => {
    it("should return valid result for profile with all compatible rules", () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            id: "rule-1",
            sourceFieldType: FieldType.Lookup,
            targetFieldType: FieldType.Lookup,
          }),
          createTestRule({
            id: "rule-2",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Text,
          }),
          createTestRule({
            id: "rule-3",
            sourceFieldType: FieldType.Number,
            targetFieldType: FieldType.Text, // Valid widening
          }),
        ],
      });

      const result = service.validateProfile(profile);

      expect(result.isValid).toBe(true);
      expect(result.totalRules).toBe(3);
      expect(result.validRules).toBe(3);
      expect(result.invalidRules).toBe(0);
      expect(result.ruleResults).toHaveLength(3);
      expect(result.ruleResults.every((r) => r.isCompatible)).toBe(true);
    });

    it("should return invalid result for profile with incompatible rule", () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            id: "rule-1",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Lookup, // Invalid: Text -> Lookup
          }),
        ],
      });

      const result = service.validateProfile(profile);

      expect(result.isValid).toBe(false);
      expect(result.totalRules).toBe(1);
      expect(result.validRules).toBe(0);
      expect(result.invalidRules).toBe(1);
      expect(result.ruleResults[0].isCompatible).toBe(false);
      expect(result.ruleResults[0].errors.length).toBeGreaterThan(0);
    });

    it("should identify multiple incompatible rules in profile", () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            id: "rule-1",
            sourceFieldType: FieldType.Lookup,
            targetFieldType: FieldType.Lookup, // Valid
          }),
          createTestRule({
            id: "rule-2",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Number, // Invalid
          }),
          createTestRule({
            id: "rule-3",
            sourceFieldType: FieldType.Boolean,
            targetFieldType: FieldType.DateTime, // Invalid
          }),
        ],
      });

      const result = service.validateProfile(profile);

      expect(result.isValid).toBe(false);
      expect(result.totalRules).toBe(3);
      expect(result.validRules).toBe(1);
      expect(result.invalidRules).toBe(2);

      // Verify specific rules
      expect(result.ruleResults[0].isCompatible).toBe(true); // Lookup -> Lookup
      expect(result.ruleResults[1].isCompatible).toBe(false); // Text -> Number
      expect(result.ruleResults[2].isCompatible).toBe(false); // Boolean -> DateTime
    });

    it("should accept external rules array parameter", () => {
      const profile = createTestProfile({ rules: [] }); // Empty rules in profile

      const externalRules = [
        createTestRule({
          id: "ext-rule-1",
          sourceFieldType: FieldType.DateTime,
          targetFieldType: FieldType.Text,
        }),
      ];

      const result = service.validateProfile(profile, externalRules);

      expect(result.isValid).toBe(true);
      expect(result.totalRules).toBe(1);
      expect(result.validRules).toBe(1);
    });

    it("should include rule metadata in results", () => {
      const profile = createTestProfile({
        id: "profile-test",
        name: "Test Profile",
        rules: [
          createTestRule({
            id: "rule-test",
            name: "Client to Account",
            sourceField: "sprk_client",
            targetField: "sprk_regardingaccount",
            sourceFieldType: FieldType.Lookup,
            targetFieldType: FieldType.Lookup,
          }),
        ],
      });

      const result = service.validateProfile(profile);

      expect(result.profileId).toBe("profile-test");
      expect(result.profileName).toBe("Test Profile");
      expect(result.ruleResults[0].ruleId).toBe("rule-test");
      expect(result.ruleResults[0].ruleName).toBe("Client to Account");
      expect(result.ruleResults[0].sourceField).toBe("sprk_client");
      expect(result.ruleResults[0].targetField).toBe("sprk_regardingaccount");
    });

    it("should return valid result for empty rules", () => {
      const profile = createTestProfile({ rules: [] });

      const result = service.validateProfile(profile);

      expect(result.isValid).toBe(true);
      expect(result.totalRules).toBe(0);
      expect(result.validRules).toBe(0);
      expect(result.invalidRules).toBe(0);
    });
  });

  // ===========================================================================
  // Mapping Application Tests
  // ===========================================================================

  describe("applyMappings", () => {
    it("should return empty result for profile with no rules", async () => {
      const profile = createTestProfile({ rules: [] });
      const targetRecord: Record<string, unknown> = {};

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile
      );

      expect(result.success).toBe(true);
      expect(result.appliedRules).toBe(0);
      expect(result.skippedRules).toBe(0);
      expect(result.totalRules).toBe(0);
    });

    it("should skip inactive rules", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({ isActive: false }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile
      );

      expect(result.skippedRules).toBe(1);
      expect(result.appliedRules).toBe(0);
    });

    it("should handle empty source value with default value", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "empty_field",
            targetField: "target_field",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Text,
            defaultValue: "default_value",
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      // Mock source values (empty for the field)
      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({});

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile
      );

      expect(result.appliedRules).toBe(1);
      expect(targetRecord.target_field).toBe("default_value");
    });

    it("should fail on required field with empty source and no default", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "empty_field",
            targetField: "target_field",
            isRequired: true,
            defaultValue: undefined,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({});

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile
      );

      expect(result.success).toBe(false);
      expect(result.errors).toHaveLength(1);
      expect(result.errors[0].code).toBe(MappingErrorCode.RequiredFieldEmpty);
    });

    it("should apply type-compatible mappings", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "source_text",
            targetField: "target_text",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Text,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        source_text: "Hello World",
      });

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile
      );

      expect(result.success).toBe(true);
      expect(result.appliedRules).toBe(1);
      expect(targetRecord.target_text).toBe("Hello World");
      expect(result.fieldsMapped).toContain("target_text");
    });

    it("should reject type-incompatible mappings", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "source_text",
            targetField: "target_lookup",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Lookup,
            compatibilityMode: CompatibilityMode.Strict,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        source_text: "some-text",
      });

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile
      );

      expect(result.errors).toHaveLength(1);
      expect(result.errors[0].code).toBe(MappingErrorCode.TypeMismatch);
      expect(result.skippedRules).toBe(1);
    });

    it("should not modify target in dry-run mode", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "source_text",
            targetField: "target_text",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Text,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        source_text: "Hello World",
      });

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile,
        { dryRun: true }
      );

      expect(result.success).toBe(true);
      expect(result.appliedRules).toBe(1);
      expect(targetRecord.target_text).toBeUndefined(); // Not modified in dry-run
    });

    it("should skip validation when skipValidation is true", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "source_text",
            targetField: "target_lookup",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Lookup, // Normally incompatible
            compatibilityMode: CompatibilityMode.Strict,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        source_text: "some-value",
      });

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile,
        { skipValidation: true }
      );

      // Should apply despite type mismatch when validation is skipped
      expect(result.appliedRules).toBe(1);
      expect(result.errors).toHaveLength(0);
    });
  });

  // ===========================================================================
  // Cascading Mapping Tests
  // ===========================================================================

  describe("cascading mappings", () => {
    it("should execute cascading rules in second pass", async () => {
      const profile = createTestProfile({
        rules: [
          // First rule populates target_a
          createTestRule({
            id: "rule-1",
            sourceField: "source_a",
            targetField: "target_a",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Text,
            executionOrder: 1,
            isCascadingSource: false,
          }),
          // Second rule uses target_a as source (cascading)
          createTestRule({
            id: "rule-2",
            sourceField: "target_a", // Uses output of rule-1
            targetField: "target_b",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Text,
            executionOrder: 2,
            isCascadingSource: true, // Marked as cascading source
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        source_a: "Value A",
        target_a: undefined, // Not in source, will be populated by rule-1
      });

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile
      );

      expect(result.success).toBe(true);
      expect(targetRecord.target_a).toBe("Value A");
      // Cascading should have used the mapped value
      expect(result.pass).toBe(2); // Second pass executed
    });

    it("should respect maxPasses option", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            id: "rule-1",
            sourceField: "source_a",
            targetField: "target_a",
            sourceFieldType: FieldType.Text,
            targetFieldType: FieldType.Text,
            isCascadingSource: true,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        source_a: "Value",
      });

      const result = await service.applyMappings(
        "source-id",
        targetRecord,
        profile,
        { maxPasses: 1 }
      );

      // With maxPasses: 1, no cascading pass should occur
      expect(result.pass).toBe(1);
    });
  });

  // ===========================================================================
  // Value Conversion Tests
  // ===========================================================================

  describe("value conversion", () => {
    it("should convert number to text", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "amount",
            targetField: "amount_text",
            sourceFieldType: FieldType.Number,
            targetFieldType: FieldType.Text,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        amount: 123.45,
      });

      await service.applyMappings("source-id", targetRecord, profile);

      expect(targetRecord.amount_text).toBe("123.45");
    });

    it("should convert boolean to text", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "is_active",
            targetField: "status_text",
            sourceFieldType: FieldType.Boolean,
            targetFieldType: FieldType.Text,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        is_active: true,
      });

      await service.applyMappings("source-id", targetRecord, profile);

      expect(targetRecord.status_text).toBe("Yes");
    });

    it("should convert date to text (ISO format)", async () => {
      const profile = createTestProfile({
        rules: [
          createTestRule({
            sourceField: "created_date",
            targetField: "date_text",
            sourceFieldType: FieldType.DateTime,
            targetFieldType: FieldType.Text,
          }),
        ],
      });
      const targetRecord: Record<string, unknown> = {};
      const testDate = new Date("2026-01-15T10:30:00Z");

      jest.spyOn(service as any, "getSourceValues").mockResolvedValue({
        created_date: testDate,
      });

      await service.applyMappings("source-id", targetRecord, profile);

      expect(targetRecord.date_text).toBe(testDate.toISOString());
    });
  });

  // ===========================================================================
  // Cache Management Tests
  // ===========================================================================

  describe("cache management", () => {
    it("should clear all caches", () => {
      const serviceWithCache = new FieldMappingService({
        webApi: mockWebApi,
        enableCache: true,
      });

      // Simulate adding cache entries (internal implementation)
      (serviceWithCache as any).profileCache.set("test", { data: [], cachedAt: Date.now(), expiresAt: Date.now() + 10000 });
      (serviceWithCache as any).ruleCache.set("test", { data: [], cachedAt: Date.now(), expiresAt: Date.now() + 10000 });

      serviceWithCache.clearCache();

      expect((serviceWithCache as any).profileCache.size).toBe(0);
      expect((serviceWithCache as any).ruleCache.size).toBe(0);
    });

    it("should clear specific profile cache", () => {
      const serviceWithCache = new FieldMappingService({
        webApi: mockWebApi,
        enableCache: true,
      });

      (serviceWithCache as any).ruleCache.set("rules:profile-001", { data: [], cachedAt: Date.now(), expiresAt: Date.now() + 10000 });
      (serviceWithCache as any).ruleCache.set("rules:profile-002", { data: [], cachedAt: Date.now(), expiresAt: Date.now() + 10000 });

      serviceWithCache.clearProfileCache("profile-001");

      expect((serviceWithCache as any).ruleCache.has("rules:profile-001")).toBe(false);
      expect((serviceWithCache as any).ruleCache.has("rules:profile-002")).toBe(true);
    });
  });

  // ===========================================================================
  // Profile Query Tests (with stubs)
  // ===========================================================================

  describe("getProfiles", () => {
    it("should return empty array when no profiles exist", async () => {
      const profiles = await service.getProfiles();

      // STUB implementation returns empty array
      expect(profiles).toEqual([]);
    });

    it("should accept filter options", async () => {
      const profiles = await service.getProfiles({
        activeOnly: true,
        sourceEntity: "sprk_matter",
        targetEntity: "sprk_event",
      });

      expect(profiles).toEqual([]);
    });
  });

  describe("getProfileForEntityPair", () => {
    it("should return null when no matching profile exists", async () => {
      const profile = await service.getProfileForEntityPair("sprk_matter", "sprk_event");

      expect(profile).toBeNull();
    });
  });

  describe("getRulesForProfile", () => {
    it("should return empty array when no rules exist", async () => {
      const rules = await service.getRulesForProfile("profile-001");

      // STUB implementation returns empty array
      expect(rules).toEqual([]);
    });
  });

  describe("getSourceValues", () => {
    it("should return empty object when no fields requested", async () => {
      const values = await service.getSourceValues("sprk_matter", "record-001", []);

      expect(values).toEqual({});
    });
  });
});

/**
 * ConfigurationLoader Service Tests
 * Task 021 - Visualization Module
 */

import {
  loadChartDefinition,
  loadChartDefinitions,
  queryChartDefinitions,
  getChartOptions,
  clearCache,
  ConfigurationNotFoundError,
  ConfigurationLoadError,
  IConfigContext,
  IConfigWebApi,
} from "../ConfigurationLoader";
import { VisualType, AggregationType } from "../../types";

// Mock the logger
jest.mock("../../utils/logger", () => ({
  logger: {
    debug: jest.fn(),
    info: jest.fn(),
    warn: jest.fn(),
    error: jest.fn(),
  },
}));

/**
 * Mock WebApi for testing - extends IConfigWebApi with jest mocks
 */
interface IMockWebApi extends IConfigWebApi {
  retrieveRecord: jest.Mock;
  retrieveMultipleRecords: jest.Mock;
}

/**
 * Mock PCF Context - implements IConfigContext
 */
interface IMockContext extends IConfigContext {
  webAPI: IMockWebApi;
}

/**
 * Create a mock PCF context with webAPI
 */
function createMockContext(overrides?: Partial<IMockWebApi>): IMockContext {
  return {
    webAPI: {
      retrieveRecord: jest.fn(),
      retrieveMultipleRecords: jest.fn(),
      ...overrides,
    },
  };
}

/**
 * Create a mock Dataverse record
 */
function createMockRecord(
  overrides?: Partial<Record<string, unknown>>
): Record<string, unknown> {
  return {
    sprk_chartdefinitionid: "12345678-1234-1234-1234-123456789012",
    sprk_name: "Test Chart",
    sprk_visualtype: 100000001, // BarChart
    sprk_entitylogicalname: "sprk_project",
    sprk_baseviewid: "00000000-0000-0000-0000-000000000001",
    sprk_aggregationfield: "sprk_estimatedhours",
    sprk_aggregationtype: 100000001, // Sum
    sprk_groupbyfield: "statuscode",
    sprk_optionsjson: '{"showLegend": true}',
    ...overrides,
  };
}

describe("ConfigurationLoader", () => {
  beforeEach(() => {
    // Clear cache before each test
    clearCache();
  });

  describe("loadChartDefinition", () => {
    it("should load a chart definition by ID", async () => {
      const mockRecord = createMockRecord();
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      const result = await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      expect(result).toEqual({
        sprk_chartdefinitionid: "12345678-1234-1234-1234-123456789012",
        sprk_name: "Test Chart",
        sprk_visualtype: VisualType.BarChart,
        sprk_entitylogicalname: "sprk_project",
        sprk_baseviewid: "00000000-0000-0000-0000-000000000001",
        sprk_aggregationfield: "sprk_estimatedhours",
        sprk_aggregationtype: AggregationType.Sum,
        sprk_groupbyfield: "statuscode",
        sprk_optionsjson: '{"showLegend": true}',
      });
    });

    it("should normalize GUID format (remove braces)", async () => {
      const mockRecord = createMockRecord();
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      await loadChartDefinition(
        mockContext,
        "{12345678-1234-1234-1234-123456789012}"
      );

      expect(mockContext.webAPI.retrieveRecord).toHaveBeenCalledWith(
        "sprk_chartdefinition",
        "12345678-1234-1234-1234-123456789012",
        expect.any(String)
      );
    });

    it("should return cached result on second call", async () => {
      const mockRecord = createMockRecord();
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      // First call
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      // Second call
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      // Should only be called once (cached)
      expect(mockContext.webAPI.retrieveRecord).toHaveBeenCalledTimes(1);
    });

    it("should bypass cache when skipCache is true", async () => {
      const mockRecord = createMockRecord();
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      // First call
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      // Second call with skipCache
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012",
        true
      );

      expect(mockContext.webAPI.retrieveRecord).toHaveBeenCalledTimes(2);
    });

    it("should throw ConfigurationNotFoundError when record not found", async () => {
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockRejectedValue(
          new Error("Record does not exist")
        ),
      });

      await expect(
        loadChartDefinition(
          mockContext,
          "12345678-1234-1234-1234-999999999999"
        )
      ).rejects.toThrow(ConfigurationNotFoundError);
    });

    it("should throw ConfigurationLoadError for other errors", async () => {
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockRejectedValue(
          new Error("Network error")
        ),
      });

      await expect(
        loadChartDefinition(
          mockContext,
          "12345678-1234-1234-1234-123456789012"
        )
      ).rejects.toThrow(ConfigurationLoadError);
    });

    it("should throw ConfigurationLoadError when ID is empty", async () => {
      const mockContext = createMockContext();

      await expect(loadChartDefinition(mockContext, "")).rejects.toThrow(
        ConfigurationLoadError
      );
    });

    it("should handle missing optional fields", async () => {
      const mockRecord = {
        sprk_chartdefinitionid: "12345678-1234-1234-1234-123456789012",
        sprk_name: "Minimal Chart",
        sprk_visualtype: 100000000, // MetricCard
      };
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      const result = await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      expect(result.sprk_name).toBe("Minimal Chart");
      expect(result.sprk_visualtype).toBe(VisualType.MetricCard);
      expect(result.sprk_entitylogicalname).toBeUndefined();
      expect(result.sprk_aggregationtype).toBeUndefined();
    });

    it("should default to MetricCard for unknown visual types", async () => {
      const mockRecord = createMockRecord({
        sprk_visualtype: 999999999, // Unknown type
      });
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      const result = await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      expect(result.sprk_visualtype).toBe(VisualType.MetricCard);
    });
  });

  describe("loadChartDefinitions", () => {
    it("should load multiple chart definitions", async () => {
      const mockRecord1 = createMockRecord({ sprk_name: "Chart 1" });
      const mockRecord2 = createMockRecord({
        sprk_chartdefinitionid: "22222222-2222-2222-2222-222222222222",
        sprk_name: "Chart 2",
      });

      const mockContext = createMockContext({
        retrieveRecord: jest
          .fn()
          .mockResolvedValueOnce(mockRecord1)
          .mockResolvedValueOnce(mockRecord2),
      });

      const results = await loadChartDefinitions(mockContext, [
        "12345678-1234-1234-1234-123456789012",
        "22222222-2222-2222-2222-222222222222",
      ]);

      expect(results).toHaveLength(2);
      expect((results[0] as any).sprk_name).toBe("Chart 1");
      expect((results[1] as any).sprk_name).toBe("Chart 2");
    });

    it("should return errors for failed loads", async () => {
      const mockRecord1 = createMockRecord({ sprk_name: "Chart 1" });

      const mockContext = createMockContext({
        retrieveRecord: jest
          .fn()
          .mockResolvedValueOnce(mockRecord1)
          .mockRejectedValueOnce(new Error("Record does not exist")),
      });

      const results = await loadChartDefinitions(mockContext, [
        "12345678-1234-1234-1234-123456789012",
        "22222222-2222-2222-2222-222222222222",
      ]);

      expect(results).toHaveLength(2);
      expect((results[0] as any).sprk_name).toBe("Chart 1");
      expect(results[1]).toBeInstanceOf(Error);
    });
  });

  describe("queryChartDefinitions", () => {
    it("should query chart definitions with filter", async () => {
      const mockResult = {
        entities: [
          createMockRecord({ sprk_name: "Bar Chart 1" }),
          createMockRecord({
            sprk_chartdefinitionid: "22222222-2222-2222-2222-222222222222",
            sprk_name: "Bar Chart 2",
          }),
        ],
      };

      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue(mockResult),
      });

      const results = await queryChartDefinitions(
        mockContext,
        "sprk_visualtype eq 100000001"
      );

      expect(results).toHaveLength(2);
      expect(results[0].sprk_name).toBe("Bar Chart 1");
      expect(results[1].sprk_name).toBe("Bar Chart 2");
    });

    it("should query all chart definitions when no filter provided", async () => {
      const mockResult = {
        entities: [createMockRecord()],
      };

      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue(mockResult),
      });

      await queryChartDefinitions(mockContext);

      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledWith(
        "sprk_chartdefinition",
        expect.not.stringContaining("$filter")
      );
    });

    it("should populate cache with query results", async () => {
      const mockRecord = createMockRecord();
      const mockResult = { entities: [mockRecord] };

      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue(mockResult),
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      await queryChartDefinitions(mockContext);

      // Now load by ID - should use cache
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      // retrieveRecord should not be called (cached from query)
      expect(mockContext.webAPI.retrieveRecord).not.toHaveBeenCalled();
    });
  });

  describe("getChartOptions", () => {
    it("should parse valid JSON options", () => {
      const definition = {
        sprk_chartdefinitionid: "test",
        sprk_name: "Test",
        sprk_visualtype: VisualType.BarChart,
        sprk_optionsjson: '{"showLegend": true, "orientation": "horizontal"}',
      };

      const options = getChartOptions(definition);

      expect(options).toEqual({
        showLegend: true,
        orientation: "horizontal",
      });
    });

    it("should return empty object for invalid JSON", () => {
      const definition = {
        sprk_chartdefinitionid: "test",
        sprk_name: "Test",
        sprk_visualtype: VisualType.BarChart,
        sprk_optionsjson: "not valid json",
      };

      const options = getChartOptions(definition);

      expect(options).toEqual({});
    });

    it("should return empty object for undefined optionsJson", () => {
      const definition = {
        sprk_chartdefinitionid: "test",
        sprk_name: "Test",
        sprk_visualtype: VisualType.BarChart,
      };

      const options = getChartOptions(definition);

      expect(options).toEqual({});
    });

    it("should return empty object for empty string optionsJson", () => {
      const definition = {
        sprk_chartdefinitionid: "test",
        sprk_name: "Test",
        sprk_visualtype: VisualType.BarChart,
        sprk_optionsjson: "",
      };

      const options = getChartOptions(definition);

      expect(options).toEqual({});
    });
  });

  describe("clearCache", () => {
    it("should clear specific cache entry", async () => {
      const mockRecord = createMockRecord();
      const mockContext = createMockContext({
        retrieveRecord: jest.fn().mockResolvedValue(mockRecord),
      });

      // Load to populate cache
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      // Clear specific entry
      clearCache("12345678-1234-1234-1234-123456789012");

      // Load again - should call API
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );

      expect(mockContext.webAPI.retrieveRecord).toHaveBeenCalledTimes(2);
    });

    it("should clear entire cache when no ID provided", async () => {
      const mockRecord1 = createMockRecord();
      const mockRecord2 = createMockRecord({
        sprk_chartdefinitionid: "22222222-2222-2222-2222-222222222222",
      });

      const mockContext = createMockContext({
        retrieveRecord: jest
          .fn()
          .mockResolvedValueOnce(mockRecord1)
          .mockResolvedValueOnce(mockRecord2)
          .mockResolvedValueOnce(mockRecord1)
          .mockResolvedValueOnce(mockRecord2),
      });

      // Load two records
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );
      await loadChartDefinition(
        mockContext,
        "22222222-2222-2222-2222-222222222222"
      );

      // Clear all
      clearCache();

      // Load again - should call API for both
      await loadChartDefinition(
        mockContext,
        "12345678-1234-1234-1234-123456789012"
      );
      await loadChartDefinition(
        mockContext,
        "22222222-2222-2222-2222-222222222222"
      );

      expect(mockContext.webAPI.retrieveRecord).toHaveBeenCalledTimes(4);
    });
  });
});

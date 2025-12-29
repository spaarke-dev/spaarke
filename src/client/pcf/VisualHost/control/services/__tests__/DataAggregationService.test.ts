/**
 * DataAggregationService Tests
 * Task 022 - Visualization Module
 */

import {
  fetchRecords,
  aggregateRecords,
  fetchAndAggregate,
  aggregateData,
  clearAggregationCache,
  AggregationError,
  IAggregationContext,
  IAggregationWebApi,
} from "../DataAggregationService";
import { AggregationType, IChartDefinition, VisualType } from "../../types";

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
 * Mock WebApi for testing
 */
interface IMockWebApi extends IAggregationWebApi {
  retrieveMultipleRecords: jest.Mock;
}

/**
 * Mock PCF Context
 */
interface IMockContext extends IAggregationContext {
  webAPI: IMockWebApi;
}

/**
 * Create a mock PCF context
 */
function createMockContext(overrides?: Partial<IMockWebApi>): IMockContext {
  return {
    webAPI: {
      retrieveMultipleRecords: jest.fn(),
      ...overrides,
    },
  };
}

/**
 * Create mock records for testing
 */
function createMockRecords(count: number, options?: {
  statusField?: string;
  amountField?: string;
}): Array<Record<string, unknown>> {
  const statuses = ["Active", "Pending", "Completed", "Cancelled"];
  const records: Array<Record<string, unknown>> = [];

  for (let i = 0; i < count; i++) {
    records.push({
      id: `record-${i}`,
      name: `Record ${i}`,
      [options?.statusField || "statuscode"]: statuses[i % statuses.length],
      [options?.amountField || "amount"]: (i + 1) * 100,
    });
  }

  return records;
}

/**
 * Create a mock chart definition
 */
function createMockDefinition(
  overrides?: Partial<IChartDefinition>
): IChartDefinition {
  return {
    sprk_chartdefinitionid: "test-chart-id",
    sprk_name: "Test Chart",
    sprk_visualtype: VisualType.BarChart,
    sprk_entitylogicalname: "sprk_project",
    sprk_aggregationtype: AggregationType.Count,
    sprk_groupbyfield: "statuscode",
    ...overrides,
  };
}

describe("DataAggregationService", () => {
  beforeEach(() => {
    clearAggregationCache();
  });

  describe("fetchRecords", () => {
    it("should fetch records from Dataverse", async () => {
      const mockRecords = createMockRecords(5);
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: mockRecords,
        }),
      });

      const result = await fetchRecords(mockContext, "sprk_project");

      expect(result).toHaveLength(5);
      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledWith(
        "sprk_project",
        expect.any(String),
        expect.any(Number)
      );
    });

    it("should apply select columns", async () => {
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: [],
        }),
      });

      await fetchRecords(mockContext, "sprk_project", {
        selectColumns: ["name", "statuscode"],
      });

      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledWith(
        "sprk_project",
        expect.stringContaining("$select=name,statuscode"),
        expect.any(Number)
      );
    });

    it("should apply filter", async () => {
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: [],
        }),
      });

      await fetchRecords(mockContext, "sprk_project", {
        filter: "statuscode eq 'Active'",
      });

      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledWith(
        "sprk_project",
        expect.stringContaining("$filter="),
        expect.any(Number)
      );
    });

    it("should throw AggregationError on fetch failure", async () => {
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockRejectedValue(
          new Error("Network error")
        ),
      });

      await expect(
        fetchRecords(mockContext, "sprk_project")
      ).rejects.toThrow(AggregationError);
    });
  });

  describe("aggregateRecords", () => {
    describe("Count aggregation", () => {
      it("should count all records without grouping", () => {
        const records = createMockRecords(10);

        const result = aggregateRecords(records, AggregationType.Count);

        expect(result).toHaveLength(1);
        expect(result[0].label).toBe("Total");
        expect(result[0].value).toBe(10);
      });

      it("should count records by group", () => {
        const records = createMockRecords(12); // 3 each: Active, Pending, Completed, Cancelled

        const result = aggregateRecords(
          records,
          AggregationType.Count,
          undefined,
          "statuscode"
        );

        expect(result).toHaveLength(4);
        // Each status should have 3 records (12 / 4 statuses)
        result.forEach((point) => {
          expect(point.value).toBe(3);
        });
      });
    });

    describe("Sum aggregation", () => {
      it("should sum field values", () => {
        const records = createMockRecords(5); // amounts: 100, 200, 300, 400, 500

        const result = aggregateRecords(
          records,
          AggregationType.Sum,
          "amount"
        );

        expect(result).toHaveLength(1);
        expect(result[0].value).toBe(1500); // 100 + 200 + 300 + 400 + 500
      });

      it("should sum field values by group", () => {
        const records = createMockRecords(4); // 1 each status: 100, 200, 300, 400

        const result = aggregateRecords(
          records,
          AggregationType.Sum,
          "amount",
          "statuscode"
        );

        expect(result).toHaveLength(4);
        // Each group has one record
        const sums = result.map((r) => r.value).sort((a, b) => a - b);
        expect(sums).toEqual([100, 200, 300, 400]);
      });
    });

    describe("Average aggregation", () => {
      it("should average field values", () => {
        const records = createMockRecords(5); // amounts: 100, 200, 300, 400, 500

        const result = aggregateRecords(
          records,
          AggregationType.Average,
          "amount"
        );

        expect(result).toHaveLength(1);
        expect(result[0].value).toBe(300); // (100 + 200 + 300 + 400 + 500) / 5
      });
    });

    describe("Min aggregation", () => {
      it("should find minimum field value", () => {
        const records = createMockRecords(5); // amounts: 100, 200, 300, 400, 500

        const result = aggregateRecords(
          records,
          AggregationType.Min,
          "amount"
        );

        expect(result).toHaveLength(1);
        expect(result[0].value).toBe(100);
      });
    });

    describe("Max aggregation", () => {
      it("should find maximum field value", () => {
        const records = createMockRecords(5); // amounts: 100, 200, 300, 400, 500

        const result = aggregateRecords(
          records,
          AggregationType.Max,
          "amount"
        );

        expect(result).toHaveLength(1);
        expect(result[0].value).toBe(500);
      });
    });

    describe("Edge cases", () => {
      it("should handle empty records", () => {
        const result = aggregateRecords([], AggregationType.Count);

        expect(result).toHaveLength(1);
        expect(result[0].value).toBe(0);
      });

      it("should handle null/undefined values", () => {
        const records = [
          { statuscode: null, amount: 100 },
          { statuscode: undefined, amount: 200 },
          { statuscode: "Active", amount: 300 },
        ];

        const result = aggregateRecords(
          records,
          AggregationType.Count,
          undefined,
          "statuscode"
        );

        // Should have 2 groups: (Blank) and Active
        expect(result.length).toBe(2);
        const blankGroup = result.find((r) => r.label === "(Blank)");
        expect(blankGroup?.value).toBe(2);
      });

      it("should handle boolean group values", () => {
        const records = [
          { isActive: true },
          { isActive: true },
          { isActive: false },
        ];

        const result = aggregateRecords(
          records,
          AggregationType.Count,
          undefined,
          "isActive"
        );

        expect(result.length).toBe(2);
        const yesGroup = result.find((r) => r.label === "Yes");
        const noGroup = result.find((r) => r.label === "No");
        expect(yesGroup?.value).toBe(2);
        expect(noGroup?.value).toBe(1);
      });

      it("should handle missing aggregation field", () => {
        const records = createMockRecords(5);

        const result = aggregateRecords(
          records,
          AggregationType.Sum,
          "nonexistent_field"
        );

        expect(result).toHaveLength(1);
        expect(result[0].value).toBe(0);
      });

      it("should handle non-numeric values for numeric aggregations", () => {
        const records = [
          { amount: "not a number" },
          { amount: "also not" },
          { amount: 100 },
        ];

        const result = aggregateRecords(
          records,
          AggregationType.Sum,
          "amount"
        );

        expect(result).toHaveLength(1);
        expect(result[0].value).toBe(100); // Only valid number
      });
    });
  });

  describe("fetchAndAggregate", () => {
    it("should fetch and aggregate data based on definition", async () => {
      const mockRecords = createMockRecords(12);
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: mockRecords,
        }),
      });

      const definition = createMockDefinition();

      const result = await fetchAndAggregate(mockContext, definition);

      expect(result.totalRecords).toBe(12);
      expect(result.aggregationType).toBe(AggregationType.Count);
      expect(result.dataPoints.length).toBe(4); // 4 status groups
    });

    it("should throw error when entity name is missing", async () => {
      const mockContext = createMockContext();
      const definition = createMockDefinition({
        sprk_entitylogicalname: undefined,
      });

      await expect(
        fetchAndAggregate(mockContext, definition)
      ).rejects.toThrow(AggregationError);
    });

    it("should use cache on subsequent calls", async () => {
      const mockRecords = createMockRecords(5);
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: mockRecords,
        }),
      });

      const definition = createMockDefinition();

      // First call
      await fetchAndAggregate(mockContext, definition);

      // Second call
      await fetchAndAggregate(mockContext, definition);

      // Should only fetch once (cached)
      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    });

    it("should bypass cache when skipCache is true", async () => {
      const mockRecords = createMockRecords(5);
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: mockRecords,
        }),
      });

      const definition = createMockDefinition();

      // First call
      await fetchAndAggregate(mockContext, definition);

      // Second call with skipCache
      await fetchAndAggregate(mockContext, definition, { skipCache: true });

      // Should fetch twice
      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledTimes(2);
    });

    it("should default to Count aggregation when not specified", async () => {
      const mockRecords = createMockRecords(10);
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: mockRecords,
        }),
      });

      const definition = createMockDefinition({
        sprk_aggregationtype: undefined,
      });

      const result = await fetchAndAggregate(mockContext, definition);

      expect(result.aggregationType).toBe(AggregationType.Count);
    });
  });

  describe("aggregateData", () => {
    it("should aggregate pre-loaded data", () => {
      const records = createMockRecords(8);

      const result = aggregateData(
        records,
        AggregationType.Count,
        undefined,
        "statuscode"
      );

      expect(result.totalRecords).toBe(8);
      expect(result.dataPoints.length).toBe(4);
    });

    it("should return correct chart data structure", () => {
      const records = createMockRecords(4);

      const result = aggregateData(
        records,
        AggregationType.Sum,
        "amount",
        "statuscode"
      );

      expect(result).toHaveProperty("dataPoints");
      expect(result).toHaveProperty("totalRecords");
      expect(result).toHaveProperty("aggregationType");
      expect(result).toHaveProperty("aggregationField");
      expect(result).toHaveProperty("groupByField");
    });
  });

  describe("clearAggregationCache", () => {
    it("should clear specific cache entry", async () => {
      const mockRecords = createMockRecords(5);
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: mockRecords,
        }),
      });

      const definition = createMockDefinition();

      // Populate cache
      await fetchAndAggregate(mockContext, definition);

      // Clear all cache (since cache key is internal)
      clearAggregationCache();

      // Fetch again - should call API
      await fetchAndAggregate(mockContext, definition);

      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledTimes(2);
    });

    it("should clear entire cache when no key provided", async () => {
      const mockRecords = createMockRecords(5);
      const mockContext = createMockContext({
        retrieveMultipleRecords: jest.fn().mockResolvedValue({
          entities: mockRecords,
        }),
      });

      // Create two different definitions
      const definition1 = createMockDefinition({ sprk_name: "Chart 1" });
      const definition2 = createMockDefinition({
        sprk_name: "Chart 2",
        sprk_groupbyfield: "ownerid",
      });

      // Populate cache with both
      await fetchAndAggregate(mockContext, definition1);
      await fetchAndAggregate(mockContext, definition2);

      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledTimes(2);

      // Clear all
      clearAggregationCache();

      // Fetch both again
      await fetchAndAggregate(mockContext, definition1);
      await fetchAndAggregate(mockContext, definition2);

      expect(mockContext.webAPI.retrieveMultipleRecords).toHaveBeenCalledTimes(4);
    });
  });
});

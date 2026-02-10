/**
 * FetchXmlService Unit Tests
 *
 * @see services/FetchXmlService.ts
 */

import { FetchXmlService } from "../FetchXmlService";
import type { XrmContext } from "../../utils/xrmContext";
import type { IFilterGroup } from "../../types/FetchXmlTypes";

// Mock XrmContext
const createMockXrm = (overrides?: Partial<XrmContext>): XrmContext => ({
  WebApi: {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({
      entities: [],
      "@Microsoft.Dynamics.CRM.totalrecordcount": 0,
      "@Microsoft.Dynamics.CRM.morerecords": false,
    }),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    createRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
    updateRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
    deleteRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
  },
  ...overrides,
});

describe("FetchXmlService", () => {
  let service: FetchXmlService;
  let mockXrm: XrmContext;

  beforeEach(() => {
    mockXrm = createMockXrm();
    service = new FetchXmlService(mockXrm);
  });

  describe("executeFetchXml", () => {
    const sampleFetchXml = `
      <fetch>
        <entity name="account">
          <attribute name="accountid"/>
          <attribute name="name"/>
        </entity>
      </fetch>
    `;

    it("should execute FetchXML and return results", async () => {
      const mockEntities = [
        { accountid: "1", name: "Account 1" },
        { accountid: "2", name: "Account 2" },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: mockEntities,
        "@Microsoft.Dynamics.CRM.totalrecordcount": 2,
        "@Microsoft.Dynamics.CRM.morerecords": false,
      });

      const result = await service.executeFetchXml<{ accountid: string; name: string }>(
        "account",
        sampleFetchXml
      );

      expect(result.entities).toHaveLength(2);
      expect(result.entities[0].name).toBe("Account 1");
      expect(result.moreRecords).toBe(false);
    });

    it("should apply pagination options", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [],
        "@Microsoft.Dynamics.CRM.morerecords": false,
      });

      await service.executeFetchXml("account", sampleFetchXml, {
        pageSize: 25,
        pageNumber: 2,
      });

      const callArgs = (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
      const fetchXmlParam = decodeURIComponent(callArgs[1].replace("?fetchXml=", ""));

      expect(fetchXmlParam).toContain('count="25"');
      expect(fetchXmlParam).toContain('page="2"');
    });

    it("should apply paging cookie when provided", async () => {
      const pagingCookie = "test-paging-cookie";

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [],
        "@Microsoft.Dynamics.CRM.morerecords": false,
      });

      await service.executeFetchXml("account", sampleFetchXml, {
        pagingCookie,
      });

      const callArgs = (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
      const fetchXmlParam = decodeURIComponent(callArgs[1].replace("?fetchXml=", ""));

      expect(fetchXmlParam).toContain(`paging-cookie="${pagingCookie}"`);
    });

    it("should apply returnTotalRecordCount when requested", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [],
        "@Microsoft.Dynamics.CRM.totalrecordcount": 100,
        "@Microsoft.Dynamics.CRM.morerecords": true,
      });

      const result = await service.executeFetchXml("account", sampleFetchXml, {
        returnTotalRecordCount: true,
      });

      const callArgs = (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
      const fetchXmlParam = decodeURIComponent(callArgs[1].replace("?fetchXml=", ""));

      expect(fetchXmlParam).toContain('returntotalrecordcount="true"');
      expect(result.totalRecordCount).toBe(100);
    });

    it("should respect maxPageSize option", async () => {
      const mockEntities = Array.from({ length: 50 }, (_, i) => ({
        accountid: String(i),
        name: `Account ${i}`,
      }));

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: mockEntities,
        "@Microsoft.Dynamics.CRM.morerecords": false,
      });

      const result = await service.executeFetchXml("account", sampleFetchXml, {
        maxPageSize: 10,
      });

      expect(result.entities).toHaveLength(10);
    });

    it("should handle API errors gracefully", async () => {
      const error = new Error("API Error");
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockRejectedValue(error);

      await expect(
        service.executeFetchXml("account", sampleFetchXml)
      ).rejects.toThrow("API Error");
    });
  });

  describe("parseLayoutXml", () => {
    it("should parse layout XML into column definitions", () => {
      const layoutXml = `
        <grid>
          <row>
            <cell name="name" width="200"/>
            <cell name="accountnumber" width="100"/>
            <cell name="primarycontactid" width="150"/>
          </row>
        </grid>
      `;

      const columns = service.parseLayoutXml(layoutXml);

      expect(columns).toHaveLength(3);
      expect(columns[0]).toEqual({
        name: "name",
        width: 200,
        isPrimary: true,
        index: 0,
      });
      expect(columns[1]).toEqual({
        name: "accountnumber",
        width: 100,
        isPrimary: false,
        index: 1,
      });
    });

    it("should handle isfirstcell attribute", () => {
      const layoutXml = `
        <grid>
          <row>
            <cell name="field1" width="100"/>
            <cell name="field2" width="100" isfirstcell="true"/>
          </row>
        </grid>
      `;

      const columns = service.parseLayoutXml(layoutXml);

      expect(columns[0].isPrimary).toBe(true); // First cell is primary by index
      expect(columns[1].isPrimary).toBe(true); // Also marked as primary by attribute
    });

    it("should use default width when not specified", () => {
      const layoutXml = `
        <grid>
          <row>
            <cell name="field1"/>
          </row>
        </grid>
      `;

      const columns = service.parseLayoutXml(layoutXml);

      expect(columns[0].width).toBe(100);
    });

    it("should return empty array for empty input", () => {
      expect(service.parseLayoutXml("")).toEqual([]);
      expect(service.parseLayoutXml(null as unknown as string)).toEqual([]);
    });

    it("should handle malformed XML gracefully", () => {
      const malformedXml = "<grid><row><cell name='test'>";

      const columns = service.parseLayoutXml(malformedXml);

      // Should not throw, may return partial results or empty
      expect(Array.isArray(columns)).toBe(true);
    });
  });

  describe("mergeFetchXmlFilter", () => {
    const baseFetchXml = `
      <fetch>
        <entity name="account">
          <attribute name="accountid"/>
          <attribute name="name"/>
        </entity>
      </fetch>
    `;

    it("should add filter to FetchXML without existing filter", () => {
      const filterGroup: IFilterGroup = {
        type: "and",
        conditions: [
          { attribute: "statecode", operator: "eq", value: 0 },
        ],
      };

      const result = service.mergeFetchXmlFilter(baseFetchXml, filterGroup);

      expect(result).toContain('<filter type="and">');
      expect(result).toContain('attribute="statecode"');
      expect(result).toContain('operator="eq"');
      expect(result).toContain('value="0"');
    });

    it("should merge filter with existing filter", () => {
      const fetchXmlWithFilter = `
        <fetch>
          <entity name="account">
            <attribute name="accountid"/>
            <filter type="and">
              <condition attribute="name" operator="like" value="%test%"/>
            </filter>
          </entity>
        </fetch>
      `;

      const filterGroup: IFilterGroup = {
        type: "and",
        conditions: [
          { attribute: "statecode", operator: "eq", value: 0 },
        ],
      };

      const result = service.mergeFetchXmlFilter(fetchXmlWithFilter, filterGroup);

      // Should have both conditions wrapped in AND
      expect(result).toContain('attribute="name"');
      expect(result).toContain('attribute="statecode"');
    });

    it("should handle OR filter groups", () => {
      const filterGroup: IFilterGroup = {
        type: "or",
        conditions: [
          { attribute: "statecode", operator: "eq", value: 0 },
          { attribute: "statecode", operator: "eq", value: 1 },
        ],
      };

      const result = service.mergeFetchXmlFilter(baseFetchXml, filterGroup);

      expect(result).toContain('<filter type="or">');
    });

    it("should handle nested filter groups", () => {
      const filterGroup: IFilterGroup = {
        type: "and",
        conditions: [
          { attribute: "statecode", operator: "eq", value: 0 },
        ],
        filters: [
          {
            type: "or",
            conditions: [
              { attribute: "name", operator: "like", value: "%A%" },
              { attribute: "name", operator: "like", value: "%B%" },
            ],
          },
        ],
      };

      const result = service.mergeFetchXmlFilter(baseFetchXml, filterGroup);

      expect(result).toContain('<filter type="and">');
      expect(result).toContain('<filter type="or">');
    });

    it("should handle 'in' operator with multiple values", () => {
      const filterGroup: IFilterGroup = {
        type: "and",
        conditions: [
          { attribute: "statecode", operator: "in", values: [0, 1, 2] },
        ],
      };

      const result = service.mergeFetchXmlFilter(baseFetchXml, filterGroup);

      expect(result).toContain('operator="in"');
      expect(result).toContain("<value>0</value>");
      expect(result).toContain("<value>1</value>");
      expect(result).toContain("<value>2</value>");
    });

    it("should format Date values as ISO strings", () => {
      const testDate = new Date("2026-01-15T10:30:00.000Z");
      const filterGroup: IFilterGroup = {
        type: "and",
        conditions: [
          { attribute: "createdon", operator: "ge", value: testDate },
        ],
      };

      const result = service.mergeFetchXmlFilter(baseFetchXml, filterGroup);

      expect(result).toContain("2026-01-15T10:30:00.000Z");
    });

    it("should return original FetchXML for empty filter", () => {
      const filterGroup: IFilterGroup = {
        type: "and",
        conditions: [],
      };

      const result = service.mergeFetchXmlFilter(baseFetchXml, filterGroup);

      // Should be essentially unchanged (whitespace may differ)
      expect(result).toContain("<entity name");
      expect(result).not.toContain('<filter type="and">');
    });
  });

  describe("getEntityFromFetchXml", () => {
    it("should extract entity name from FetchXML", () => {
      const fetchXml = '<fetch><entity name="contact"><attribute name="fullname"/></entity></fetch>';

      const entity = service.getEntityFromFetchXml(fetchXml);

      expect(entity).toBe("contact");
    });

    it("should return undefined for invalid FetchXML", () => {
      expect(service.getEntityFromFetchXml("invalid")).toBeUndefined();
      expect(service.getEntityFromFetchXml("<fetch></fetch>")).toBeUndefined();
    });
  });

  describe("getAttributesFromFetchXml", () => {
    it("should extract attribute names from FetchXML", () => {
      const fetchXml = `
        <fetch>
          <entity name="account">
            <attribute name="accountid"/>
            <attribute name="name"/>
            <attribute name="primarycontactid"/>
          </entity>
        </fetch>
      `;

      const attributes = service.getAttributesFromFetchXml(fetchXml);

      expect(attributes).toEqual(["accountid", "name", "primarycontactid"]);
    });

    it("should return empty array for FetchXML without attributes", () => {
      const fetchXml = '<fetch><entity name="account"></entity></fetch>';

      const attributes = service.getAttributesFromFetchXml(fetchXml);

      expect(attributes).toEqual([]);
    });
  });
});

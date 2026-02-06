/**
 * ConfigurationService Unit Tests
 *
 * @see services/ConfigurationService.ts
 */

import { ConfigurationService } from "../ConfigurationService";
import { GridConfigViewType } from "../../types/ConfigurationTypes";
import type { XrmContext } from "../../utils/xrmContext";

// Mock XrmContext
const createMockXrm = (): XrmContext => ({
  WebApi: {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    createRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
    updateRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
    deleteRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
  },
});

describe("ConfigurationService", () => {
  let service: ConfigurationService;
  let mockXrm: XrmContext;

  beforeEach(() => {
    mockXrm = createMockXrm();
    service = new ConfigurationService(mockXrm);
  });

  afterEach(() => {
    service.clearCache();
  });

  describe("getConfigurations", () => {
    const mockConfigurations = [
      {
        sprk_gridconfigurationid: "config-1",
        sprk_name: "Custom Events View",
        sprk_entitylogicalname: "sprk_event",
        sprk_viewtype: GridConfigViewType.CustomFetchXML,
        sprk_fetchxml: "<fetch><entity name='sprk_event'/></fetch>",
        sprk_layoutxml: "<grid><row><cell name='sprk_eventname' width='200'/></row></grid>",
        sprk_configjson: '{"features":{"enableSelection":true}}',
        sprk_isdefault: true,
        sprk_sortorder: 10,
        sprk_iconname: "CalendarAgenda",
        statecode: 0,
      },
      {
        sprk_gridconfigurationid: "config-2",
        sprk_name: "Overdue Events",
        sprk_entitylogicalname: "sprk_event",
        sprk_viewtype: GridConfigViewType.CustomFetchXML,
        sprk_fetchxml: "<fetch><entity name='sprk_event'/></fetch>",
        sprk_isdefault: false,
        sprk_sortorder: 20,
        statecode: 0,
      },
    ];

    it("should fetch configurations for an entity", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockConfigurations,
      });

      const configs = await service.getConfigurations("sprk_event");

      expect(configs).toHaveLength(2);
      expect(configs[0].name).toBe("Custom Events View");
      expect(configs[0].viewType).toBe(GridConfigViewType.CustomFetchXML);
      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledWith(
        "sprk_gridconfiguration",
        expect.stringContaining("sprk_entitylogicalname eq 'sprk_event'")
      );
    });

    it("should parse configJson correctly", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockConfigurations,
      });

      const configs = await service.getConfigurations("sprk_event");

      expect(configs[0].configJson).toBeDefined();
      expect(configs[0].configJson?.features?.enableSelection).toBe(true);
    });

    it("should handle missing configJson gracefully", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockConfigurations,
      });

      const configs = await service.getConfigurations("sprk_event");

      // Second config has no configJson
      expect(configs[1].configJson).toBeUndefined();
    });

    it("should cache results", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: mockConfigurations,
      });

      await service.getConfigurations("sprk_event");
      await service.getConfigurations("sprk_event");

      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    });

    it("should return empty array when entity doesn't exist", async () => {
      const entityNotFoundError = new Error("The entity 'sprk_gridconfiguration' doesn't exist");
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockRejectedValueOnce(
        entityNotFoundError
      );

      const configs = await service.getConfigurations("sprk_event");

      expect(configs).toEqual([]);
    });

    it("should remember entity doesn't exist and not retry", async () => {
      const entityNotFoundError = new Error("The entity 'sprk_gridconfiguration' doesn't exist");
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockRejectedValue(
        entityNotFoundError
      );

      await service.getConfigurations("sprk_event");
      await service.getConfigurations("account");

      // Should only try once after discovering entity doesn't exist
      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    });
  });

  describe("getDefaultConfiguration", () => {
    it("should return the default configuration", async () => {
      const mockConfigs = [
        {
          sprk_gridconfigurationid: "config-1",
          sprk_name: "View 1",
          sprk_entitylogicalname: "sprk_event",
          sprk_viewtype: 2,
          sprk_isdefault: false,
          sprk_sortorder: 10,
          statecode: 0,
        },
        {
          sprk_gridconfigurationid: "config-2",
          sprk_name: "Default View",
          sprk_entitylogicalname: "sprk_event",
          sprk_viewtype: 2,
          sprk_isdefault: true,
          sprk_sortorder: 20,
          statecode: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockConfigs,
      });

      const defaultConfig = await service.getDefaultConfiguration("sprk_event");

      expect(defaultConfig).toBeDefined();
      expect(defaultConfig?.name).toBe("Default View");
      expect(defaultConfig?.isDefault).toBe(true);
    });

    it("should return first configuration when no default set", async () => {
      const mockConfigs = [
        {
          sprk_gridconfigurationid: "config-1",
          sprk_name: "First View",
          sprk_entitylogicalname: "sprk_event",
          sprk_viewtype: 2,
          sprk_isdefault: false,
          sprk_sortorder: 10,
          statecode: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockConfigs,
      });

      const defaultConfig = await service.getDefaultConfiguration("sprk_event");

      expect(defaultConfig).toBeDefined();
      expect(defaultConfig?.name).toBe("First View");
    });

    it("should return undefined when no configurations exist", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: [],
      });

      const defaultConfig = await service.getDefaultConfiguration("sprk_event");

      expect(defaultConfig).toBeUndefined();
    });
  });

  describe("getConfigurationById", () => {
    it("should return configuration from cache when available", async () => {
      const mockConfigs = [
        {
          sprk_gridconfigurationid: "config-1",
          sprk_name: "Cached View",
          sprk_entitylogicalname: "sprk_event",
          sprk_viewtype: 2,
          sprk_isdefault: false,
          sprk_sortorder: 10,
          statecode: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockConfigs,
      });

      // Populate cache
      await service.getConfigurations("sprk_event");

      const config = await service.getConfigurationById("config-1");

      expect(config).toBeDefined();
      expect(config?.name).toBe("Cached View");
      expect(mockXrm.WebApi.retrieveRecord).not.toHaveBeenCalled();
    });

    it("should fetch directly when not in cache", async () => {
      const mockConfig = {
        sprk_gridconfigurationid: "config-1",
        sprk_name: "Direct View",
        sprk_entitylogicalname: "sprk_event",
        sprk_viewtype: 2,
        sprk_isdefault: false,
        sprk_sortorder: 10,
        statecode: 0,
      };

      (mockXrm.WebApi.retrieveRecord as jest.Mock).mockResolvedValueOnce(mockConfig);

      const config = await service.getConfigurationById("config-1");

      expect(config).toBeDefined();
      expect(config?.name).toBe("Direct View");
      expect(mockXrm.WebApi.retrieveRecord).toHaveBeenCalledWith(
        "sprk_gridconfiguration",
        "config-1",
        expect.any(String)
      );
    });
  });

  describe("toViewDefinition", () => {
    it("should convert configuration to view definition", async () => {
      const mockConfigs = [
        {
          sprk_gridconfigurationid: "config-1",
          sprk_name: "My View",
          sprk_entitylogicalname: "sprk_event",
          sprk_viewtype: 2,
          sprk_fetchxml: "<fetch/>",
          sprk_layoutxml: "<grid/>",
          sprk_isdefault: true,
          sprk_sortorder: 10,
          sprk_iconname: "Calendar",
          statecode: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockConfigs,
      });

      const configs = await service.getConfigurations("sprk_event");
      const viewDef = service.toViewDefinition(configs[0]);

      expect(viewDef.id).toBe("config-1");
      expect(viewDef.name).toBe("My View");
      expect(viewDef.entityLogicalName).toBe("sprk_event");
      expect(viewDef.viewType).toBe("custom");
      expect(viewDef.isDefault).toBe(true);
      expect(viewDef.sortOrder).toBe(10);
      expect(viewDef.iconName).toBe("Calendar");
    });
  });

  describe("checkEntityExists", () => {
    it("should return true when entity exists", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: [],
      });

      const exists = await service.checkEntityExists();

      expect(exists).toBe(true);
    });

    it("should return false when entity doesn't exist", async () => {
      const entityNotFoundError = new Error("The entity 'sprk_gridconfiguration' doesn't exist");
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockRejectedValueOnce(
        entityNotFoundError
      );

      const exists = await service.checkEntityExists();

      expect(exists).toBe(false);
    });

    it("should cache the entity existence check", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: [],
      });

      await service.checkEntityExists();
      await service.checkEntityExists();

      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    });
  });
});

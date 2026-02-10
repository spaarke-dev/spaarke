/**
 * ViewService Unit Tests
 *
 * @see services/ViewService.ts
 */

import { ViewService } from "../ViewService";
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

describe("ViewService", () => {
  let service: ViewService;
  let mockXrm: XrmContext;

  beforeEach(() => {
    mockXrm = createMockXrm();
    service = new ViewService(mockXrm);
  });

  afterEach(() => {
    service.clearCache();
  });

  describe("getViews", () => {
    const mockSavedQueries = [
      {
        savedqueryid: "view-1",
        name: "Active Accounts",
        returnedtypecode: "account",
        fetchxml: "<fetch><entity name='account'/></fetch>",
        layoutxml: "<grid><row><cell name='name' width='200'/></row></grid>",
        isdefault: true,
        querytype: 0,
      },
      {
        savedqueryid: "view-2",
        name: "Inactive Accounts",
        returnedtypecode: "account",
        fetchxml: "<fetch><entity name='account'/></fetch>",
        layoutxml: "<grid><row><cell name='name' width='200'/></row></grid>",
        isdefault: false,
        querytype: 0,
      },
    ];

    it("should fetch saved queries for an entity", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockSavedQueries,
      });

      const views = await service.getViews("account");

      expect(views).toHaveLength(2);
      expect(views[0].name).toBe("Active Accounts");
      expect(views[0].viewType).toBe("savedquery");
      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledWith(
        "savedquery",
        expect.stringContaining("returnedtypecode eq 'account'")
      );
    });

    it("should sort views by sortOrder", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockSavedQueries,
      });

      const views = await service.getViews("account");

      // Default view (sortOrder: 0) should come first
      expect(views[0].isDefault).toBe(true);
    });

    it("should parse layout XML into columns", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockSavedQueries,
      });

      const views = await service.getViews("account");

      expect(views[0].columns).toBeDefined();
      expect(views[0].columns).toHaveLength(1);
      expect(views[0].columns![0].name).toBe("name");
    });

    it("should include custom configurations when requested", async () => {
      const mockCustomConfigs = [
        {
          sprk_gridconfigurationid: "custom-1",
          sprk_name: "Custom View",
          sprk_entitylogicalname: "account",
          sprk_viewtype: 2,
          sprk_fetchxml: "<fetch><entity name='account'/></fetch>",
          sprk_layoutxml: "",
          sprk_isdefault: false,
          sprk_sortorder: 50,
          statecode: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock)
        .mockResolvedValueOnce({ entities: mockSavedQueries })
        .mockResolvedValueOnce({ entities: mockCustomConfigs });

      const views = await service.getViews("account", { includeCustom: true });

      // Custom view should be between default (0) and other saved queries (100)
      expect(views.some((v) => v.name === "Custom View")).toBe(true);
    });

    it("should cache results", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: mockSavedQueries,
      });

      await service.getViews("account");
      await service.getViews("account");

      // Should only call API once due to caching
      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    });

    it("should handle API errors gracefully", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockRejectedValueOnce(
        new Error("API Error")
      );

      const views = await service.getViews("account");

      expect(views).toEqual([]);
    });
  });

  describe("getDefaultView", () => {
    it("should return the default view", async () => {
      const mockViews = [
        {
          savedqueryid: "view-1",
          name: "All Accounts",
          returnedtypecode: "account",
          fetchxml: "<fetch/>",
          layoutxml: "",
          isdefault: false,
          querytype: 0,
        },
        {
          savedqueryid: "view-2",
          name: "Active Accounts",
          returnedtypecode: "account",
          fetchxml: "<fetch/>",
          layoutxml: "",
          isdefault: true,
          querytype: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockViews,
      });

      const defaultView = await service.getDefaultView("account");

      expect(defaultView).toBeDefined();
      expect(defaultView?.name).toBe("Active Accounts");
      expect(defaultView?.isDefault).toBe(true);
    });

    it("should return first view when no default is set", async () => {
      const mockViews = [
        {
          savedqueryid: "view-1",
          name: "All Accounts",
          returnedtypecode: "account",
          fetchxml: "<fetch/>",
          layoutxml: "",
          isdefault: false,
          querytype: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockViews,
      });

      const defaultView = await service.getDefaultView("account");

      expect(defaultView).toBeDefined();
      expect(defaultView?.name).toBe("All Accounts");
    });
  });

  describe("getViewById", () => {
    it("should return view from cache when available", async () => {
      const mockViews = [
        {
          savedqueryid: "view-1",
          name: "My View",
          returnedtypecode: "account",
          fetchxml: "<fetch/>",
          layoutxml: "",
          isdefault: true,
          querytype: 0,
        },
      ];

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: mockViews,
      });

      // Populate cache
      await service.getViews("account");

      // Get by ID should use cache
      const view = await service.getViewById("view-1", "account");

      expect(view).toBeDefined();
      expect(view?.id).toBe("view-1");
      // retrieveRecord should not be called since it's in cache
      expect(mockXrm.WebApi.retrieveRecord).not.toHaveBeenCalled();
    });

    it("should fetch directly when not in cache", async () => {
      const mockView = {
        savedqueryid: "view-1",
        name: "Direct View",
        returnedtypecode: "account",
        fetchxml: "<fetch/>",
        layoutxml: "",
        isdefault: true,
        querytype: 0,
      };

      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValueOnce({
        entities: [],
      });
      (mockXrm.WebApi.retrieveRecord as jest.Mock).mockResolvedValueOnce(mockView);

      const view = await service.getViewById("view-1", "account");

      expect(view).toBeDefined();
      expect(view?.name).toBe("Direct View");
      expect(mockXrm.WebApi.retrieveRecord).toHaveBeenCalledWith(
        "savedquery",
        "view-1",
        expect.any(String)
      );
    });
  });

  describe("clearCache", () => {
    it("should clear cache for specific entity", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [],
      });

      await service.getViews("account");
      await service.getViews("contact");

      service.clearCache("account");

      // Account should refetch, contact should use cache
      await service.getViews("account");
      await service.getViews("contact");

      // account: 2 calls, contact: 1 call = 3 total
      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(3);
    });

    it("should clear all cache when no entity specified", async () => {
      (mockXrm.WebApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
        entities: [],
      });

      await service.getViews("account");
      await service.getViews("contact");

      service.clearCache();

      await service.getViews("account");
      await service.getViews("contact");

      // 4 calls: 2 initial + 2 after clear
      expect(mockXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(4);
    });
  });
});

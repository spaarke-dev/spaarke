/**
 * CustomCommandFactory Unit Tests
 */

import { CustomCommandFactory } from '../CustomCommandFactory';
import { ICustomCommandConfiguration } from '../../types/EntityConfigurationTypes';
import { createMockCommandContext, createMockWebAPI } from '../../__mocks__/pcfMocks';

describe('CustomCommandFactory', () => {
  describe('createCommand', () => {
    it('should create command from JSON configuration', () => {
      const config: ICustomCommandConfiguration = {
        label: "Upload Document",
        icon: "ArrowUpload",
        actionType: "customapi",
        actionName: "sprk_UploadDocument",
        requiresSelection: false,
        group: "primary",
        description: "Upload document to SharePoint",
        keyboardShortcut: "Ctrl+U"
      };

      const command = CustomCommandFactory.createCommand("upload", config);

      expect(command.key).toBe("upload");
      expect(command.label).toBe("Upload Document");
      expect(command.requiresSelection).toBe(false);
      expect(command.group).toBe("primary");
      expect(command.description).toBe("Upload document to SharePoint");
      expect(command.keyboardShortcut).toBe("Ctrl+U");
      expect(command.handler).toBeDefined();
    });

    it('should handle missing optional properties', () => {
      const minimalConfig: ICustomCommandConfiguration = {
        label: "Test Command",
        actionType: "action",
        actionName: "TestAction"
      };

      const command = CustomCommandFactory.createCommand("test", minimalConfig);

      expect(command.key).toBe("test");
      expect(command.label).toBe("Test Command");
      expect(command.requiresSelection).toBe(false); // Default
      expect(command.group).toBe("overflow"); // Default
      expect(command.description).toBeUndefined();
      expect(command.keyboardShortcut).toBeUndefined();
    });

    it('should map icon names to Fluent UI icons', () => {
      const configs = [
        { label: "Upload", actionType: "customapi" as const, actionName: "Upload", icon: "ArrowUpload" },
        { label: "Download", actionType: "customapi" as const, actionName: "Download", icon: "ArrowDownload" },
        { label: "Send", actionType: "action" as const, actionName: "Send", icon: "Mail" }
      ];

      configs.forEach(config => {
        const command = CustomCommandFactory.createCommand(config.label.toLowerCase(), config);
        expect(command.icon).toBeDefined();
      });
    });

    it('should include confirmation message if provided', () => {
      const config: ICustomCommandConfiguration = {
        label: "Delete All",
        actionType: "action",
        actionName: "DeleteAll",
        confirmationMessage: "Are you sure?"
      };

      const command = CustomCommandFactory.createCommand("deleteAll", config);

      expect(command.confirmationMessage).toBe("Are you sure?");
    });

    it('should include refresh flag if provided', () => {
      const config: ICustomCommandConfiguration = {
        label: "Process Records",
        actionType: "customapi",
        actionName: "ProcessRecords",
        refresh: true
      };

      const command = CustomCommandFactory.createCommand("process", config);

      expect(command.refresh).toBe(true);
    });

    it('should include success message if provided', () => {
      const config: ICustomCommandConfiguration = {
        label: "Export Data",
        actionType: "function",
        actionName: "ExportData",
        successMessage: "Export completed successfully"
      };

      const command = CustomCommandFactory.createCommand("export", config);

      expect(command.successMessage).toBe("Export completed successfully");
    });
  });

  describe('Token Interpolation', () => {
    it('should interpolate {selectedCount} token', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Process",
        actionType: "customapi",
        actionName: "ProcessRecords",
        parameters: {
          Count: "{selectedCount}"
        }
      };

      const command = CustomCommandFactory.createCommand("process", config);
      const context = createMockCommandContext({
        selectedRecords: [
          { id: "1", entityName: "account" },
          { id: "2", entityName: "account" },
          { id: "3", entityName: "account" }
        ]
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          Count: "3"
        })
      );
    });

    it('should interpolate {entityName} token', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Test",
        actionType: "customapi",
        actionName: "TestAction",
        parameters: {
          Entity: "{entityName}"
        }
      };

      const command = CustomCommandFactory.createCommand("test", config);
      const context = createMockCommandContext({
        entityName: "contact"
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          Entity: "contact"
        })
      );
    });

    it('should interpolate {parentRecordId} token', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Link",
        actionType: "customapi",
        actionName: "LinkRecord",
        parameters: {
          ParentId: "{parentRecordId}"
        }
      };

      const command = CustomCommandFactory.createCommand("link", config);
      const context = createMockCommandContext({
        parentRecord: {
          id: { guid: "parent-123" },
          entityType: "account"
        }
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          ParentId: "parent-123"
        })
      );
    });

    it('should interpolate {parentTable} token', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Link",
        actionType: "customapi",
        actionName: "LinkRecord",
        parameters: {
          ParentTable: "{parentTable}"
        }
      };

      const command = CustomCommandFactory.createCommand("link", config);
      const context = createMockCommandContext({
        parentRecord: {
          id: { guid: "parent-123" },
          entityType: "account"
        } as any
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          ParentTable: "account"
        })
      );
    });

    it('should handle missing parent record gracefully', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Test",
        actionType: "customapi",
        actionName: "TestAction",
        parameters: {
          ParentId: "{parentRecordId}",
          ParentTable: "{parentTable}"
        }
      };

      const command = CustomCommandFactory.createCommand("test", config);
      const context = createMockCommandContext({
        parentRecord: undefined
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          ParentId: "",
          ParentTable: ""
        })
      );
    });
  });

  describe('Custom API Execution', () => {
    it('should execute custom API with correct request structure', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Upload",
        actionType: "customapi",
        actionName: "sprk_UploadDocument",
        parameters: {
          DocumentId: "doc-123",
          FolderPath: "/documents"
        }
      };

      const command = CustomCommandFactory.createCommand("upload", config);
      const context = createMockCommandContext();

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          DocumentId: "doc-123",
          FolderPath: "/documents",
          getMetadata: expect.any(Function)
        })
      );

      const call = (context.webAPI.execute as jest.Mock).mock.calls[0][0];
      const metadata = call.getMetadata();

      expect(metadata.operationName).toBe("sprk_UploadDocument");
      expect(metadata.boundParameter).toBeNull();
      expect(metadata.operationType).toBe(0);
    });
  });

  describe('Action Execution', () => {
    it('should execute bound action on selected records', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Send Email",
        actionType: "action",
        actionName: "SendEmail",
        requiresSelection: true
      };

      const command = CustomCommandFactory.createCommand("sendEmail", config);
      const context = createMockCommandContext({
        selectedRecords: [
          { id: "contact-1", entityName: "contact" },
          { id: "contact-2", entityName: "contact" }
        ],
        entityName: "contact"
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledTimes(2);

      // Check first call
      const firstCall = (context.webAPI.execute as jest.Mock).mock.calls[0][0];
      expect(firstCall.entity.id).toBe("contact-1");

      const metadata = firstCall.getMetadata();
      expect(metadata.operationName).toBe("SendEmail");
      expect(metadata.boundParameter).toBe("entity");
    });

    it('should execute unbound action when no selection required', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Global Process",
        actionType: "action",
        actionName: "GlobalProcess",
        requiresSelection: false,
        parameters: {
          ProcessType: "full"
        }
      };

      const command = CustomCommandFactory.createCommand("globalProcess", config);
      const context = createMockCommandContext({
        selectedRecords: []
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledTimes(1);

      const call = (context.webAPI.execute as jest.Mock).mock.calls[0][0];
      expect(call.entity).toBeUndefined();
      expect(call.ProcessType).toBe("full");

      const metadata = call.getMetadata();
      expect(metadata.boundParameter).toBeNull();
    });
  });

  describe('Function Execution', () => {
    it('should execute OData function with parameters', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Get Hierarchy",
        actionType: "function",
        actionName: "GetAccountHierarchy",
        parameters: {
          AccountId: "account-123",
          Depth: "3"
        }
      };

      const command = CustomCommandFactory.createCommand("getHierarchy", config);
      const context = createMockCommandContext({
        entityName: "account"
      });

      await command.handler(context);

      // Functions use retrieveMultipleRecords, not execute
      expect(context.webAPI.retrieveMultipleRecords).toHaveBeenCalledWith(
        "account",
        expect.stringContaining("GetAccountHierarchy")
      );
    });
  });

  describe('Workflow Execution', () => {
    it('should execute workflow on each selected record', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Approval Workflow",
        actionType: "workflow",
        actionName: "ApprovalProcess",
        requiresSelection: true
      };

      const command = CustomCommandFactory.createCommand("approval", config);
      const context = createMockCommandContext({
        selectedRecords: [
          { id: "record-1", entityName: "opportunity" },
          { id: "record-2", entityName: "opportunity" }
        ],
        entityName: "opportunity"
      });

      await command.handler(context);

      expect(context.webAPI.execute).toHaveBeenCalledTimes(2);

      // Verify workflow execution format
      const firstCall = (context.webAPI.execute as jest.Mock).mock.calls[0][0];
      expect(firstCall.EntityId).toBe("record-1");

      const metadata = firstCall.getMetadata();
      expect(metadata.operationName).toBe("ExecuteWorkflow");
      expect(metadata.operationType).toBe(0);
    });
  });

  describe('Selection Validation', () => {
    it('should validate minimum selection', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Bulk Update",
        actionType: "action",
        actionName: "BulkUpdate",
        minSelection: 2
      };

      const command = CustomCommandFactory.createCommand("bulkUpdate", config);
      const context = createMockCommandContext({
        selectedRecords: [{ id: "1", entityName: "account" }] // Only 1 selected
      });

      await expect(command.handler(context)).rejects.toThrow(
        "Select at least 2 record(s)"
      );
    });

    it('should validate maximum selection', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Limited Process",
        actionType: "action",
        actionName: "LimitedProcess",
        maxSelection: 2
      };

      const command = CustomCommandFactory.createCommand("limited", config);
      const context = createMockCommandContext({
        selectedRecords: [
          { id: "1", entityName: "account" },
          { id: "2", entityName: "account" },
          { id: "3", entityName: "account" }
        ]
      });

      await expect(command.handler(context)).rejects.toThrow(
        "Select no more than 2 record(s)"
      );
    });

    it('should pass validation when selection is within range', async () => {
      const config: ICustomCommandConfiguration = {
        label: "Process",
        actionType: "customapi",
        actionName: "Process",
        minSelection: 1,
        maxSelection: 3
      };

      const command = CustomCommandFactory.createCommand("process", config);
      const context = createMockCommandContext({
        selectedRecords: [
          { id: "1", entityName: "account" },
          { id: "2", entityName: "account" }
        ]
      });

      await expect(command.handler(context)).resolves.not.toThrow();
    });
  });
});

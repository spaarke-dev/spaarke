/**
 * CommandRegistry Unit Tests
 */

import { CommandRegistry } from '../CommandRegistry';
import { EntityConfigurationService } from '../EntityConfigurationService';
import { createMockEntityPrivileges } from '../../__mocks__/pcfMocks';

// Mock EntityConfigurationService
jest.mock('../EntityConfigurationService');

describe('CommandRegistry', () => {
  describe('getCommand', () => {
    it('should get built-in create command', () => {
      const command = CommandRegistry.getCommand('create');

      expect(command).toBeDefined();
      expect(command?.key).toBe('create');
      expect(command?.label).toBe('New');
      expect(command?.requiresSelection).toBe(false);
      expect(command?.group).toBe('primary');
      expect(command?.keyboardShortcut).toBe('Ctrl+N');
    });

    it('should get built-in open command', () => {
      const command = CommandRegistry.getCommand('open');

      expect(command).toBeDefined();
      expect(command?.key).toBe('open');
      expect(command?.label).toBe('Open');
      expect(command?.requiresSelection).toBe(true);
      expect(command?.multiSelectSupport).toBe(false);
      expect(command?.keyboardShortcut).toBe('Ctrl+O');
    });

    it('should get built-in delete command', () => {
      const command = CommandRegistry.getCommand('delete');

      expect(command).toBeDefined();
      expect(command?.key).toBe('delete');
      expect(command?.label).toBe('Delete');
      expect(command?.requiresSelection).toBe(true);
      expect(command?.multiSelectSupport).toBe(true);
      expect(command?.confirmationMessage).toBeDefined();
      expect(command?.keyboardShortcut).toBe('Delete');
      expect(command?.dividerAfter).toBe(true);
      expect(command?.refresh).toBe(true);
    });

    it('should get built-in refresh command', () => {
      const command = CommandRegistry.getCommand('refresh');

      expect(command).toBeDefined();
      expect(command?.key).toBe('refresh');
      expect(command?.label).toBe('Refresh');
      expect(command?.requiresSelection).toBe(false);
      expect(command?.keyboardShortcut).toBe('F5');
    });

    it('should get built-in upload command', () => {
      const command = CommandRegistry.getCommand('upload');

      expect(command).toBeDefined();
      expect(command?.key).toBe('upload');
      expect(command?.label).toBe('Upload');
      expect(command?.requiresSelection).toBe(false);
      expect(command?.keyboardShortcut).toBe('Ctrl+U');
    });

    it('should return undefined for unknown command', () => {
      const command = CommandRegistry.getCommand('unknown');
      expect(command).toBeUndefined();
    });

    it('should be case-insensitive', () => {
      const lowerCommand = CommandRegistry.getCommand('create');
      const upperCommand = CommandRegistry.getCommand('CREATE');
      const mixedCommand = CommandRegistry.getCommand('Create');

      expect(lowerCommand?.key).toBe('create');
      expect(upperCommand?.key).toBe('create');
      expect(mixedCommand?.key).toBe('create');
    });
  });

  describe('getCommands', () => {
    it('should return multiple commands by keys', () => {
      const commands = CommandRegistry.getCommands(['create', 'open', 'delete']);

      expect(commands).toHaveLength(3);
      expect(commands[0].key).toBe('create');
      expect(commands[1].key).toBe('open');
      expect(commands[2].key).toBe('delete');
    });

    it('should filter out unknown commands', () => {
      const commands = CommandRegistry.getCommands(['create', 'unknown', 'open']);

      expect(commands).toHaveLength(2);
      expect(commands[0].key).toBe('create');
      expect(commands[1].key).toBe('open');
    });

    it('should return all commands when no privileges provided', () => {
      const commands = CommandRegistry.getCommands(['create', 'open', 'delete', 'refresh']);

      expect(commands).toHaveLength(4);
    });

    it('should filter by canCreate privilege', () => {
      const privileges = createMockEntityPrivileges({
        canCreate: false,
        canRead: true,
        canDelete: true
      });

      const commands = CommandRegistry.getCommands(['create', 'open', 'delete'], privileges);

      expect(commands).toHaveLength(2); // Only open and delete
      expect(commands.find(c => c.key === 'create')).toBeUndefined();
    });

    it('should filter by canDelete privilege', () => {
      const privileges = createMockEntityPrivileges({
        canCreate: true,
        canRead: true,
        canDelete: false
      });

      const commands = CommandRegistry.getCommands(['create', 'open', 'delete'], privileges);

      expect(commands).toHaveLength(2); // Only create and open
      expect(commands.find(c => c.key === 'delete')).toBeUndefined();
    });

    it('should filter by canRead privilege for open command', () => {
      const privileges = createMockEntityPrivileges({
        canCreate: true,
        canRead: false,
        canDelete: true
      });

      const commands = CommandRegistry.getCommands(['create', 'open', 'refresh'], privileges);

      expect(commands).toHaveLength(1); // Only create (open and refresh require read)
      expect(commands.find(c => c.key === 'open')).toBeUndefined();
      expect(commands.find(c => c.key === 'refresh')).toBeUndefined();
    });

    it('should allow upload command with canCreate privilege', () => {
      const privileges = createMockEntityPrivileges({
        canCreate: true,
        canAppend: false
      });

      const commands = CommandRegistry.getCommands(['upload'], privileges);

      expect(commands).toHaveLength(1);
      expect(commands[0].key).toBe('upload');
    });

    it('should allow upload command with canAppend privilege', () => {
      const privileges = createMockEntityPrivileges({
        canCreate: false,
        canAppend: true
      });

      const commands = CommandRegistry.getCommands(['upload'], privileges);

      expect(commands).toHaveLength(1);
      expect(commands[0].key).toBe('upload');
    });
  });

  describe('getCommandsWithCustom', () => {
    beforeEach(() => {
      jest.clearAllMocks();
    });

    it('should include custom commands from entity configuration', () => {
      const mockCustomCommand = {
        label: "Upload Document",
        actionType: "customapi" as const,
        actionName: "sprk_UploadDocument"
      };

      (EntityConfigurationService.getCustomCommand as jest.Mock)
        .mockReturnValue(mockCustomCommand);

      const commands = CommandRegistry.getCommandsWithCustom(
        ['create', 'open', 'customUpload'],
        'sprk_document'
      );

      expect(commands).toHaveLength(3);
      expect(commands[0].key).toBe('create');
      expect(commands[1].key).toBe('open');
      expect(commands[2].key).toBe('customUpload');
    });

    it('should prioritize built-in commands over custom commands', () => {
      const mockCustomCommand = {
        label: "Custom Create",
        actionType: "action" as const,
        actionName: "CustomCreate"
      };

      (EntityConfigurationService.getCustomCommand as jest.Mock)
        .mockReturnValue(mockCustomCommand);

      const commands = CommandRegistry.getCommandsWithCustom(
        ['create'], // Built-in command
        'account'
      );

      expect(commands).toHaveLength(1);
      expect(commands[0].label).toBe('New'); // Built-in label, not "Custom Create"
    });

    it('should filter custom commands by privilege (canWrite)', () => {
      const mockCustomCommand = {
        label: "Custom Action",
        actionType: "action" as const,
        actionName: "CustomAction"
      };

      (EntityConfigurationService.getCustomCommand as jest.Mock)
        .mockReturnValue(mockCustomCommand);

      const privileges = createMockEntityPrivileges({
        canWrite: false
      });

      const commands = CommandRegistry.getCommandsWithCustom(
        ['customAction'],
        'account',
        privileges
      );

      expect(commands).toHaveLength(0); // Filtered out due to no write privilege
    });

    it('should return only built-in when custom command not found', () => {
      (EntityConfigurationService.getCustomCommand as jest.Mock)
        .mockReturnValue(undefined);

      const commands = CommandRegistry.getCommandsWithCustom(
        ['create', 'nonExistentCustom'],
        'account'
      );

      expect(commands).toHaveLength(1);
      expect(commands[0].key).toBe('create');
    });

    it('should mix built-in and custom commands', () => {
      const mockUploadCommand = {
        label: "Upload to SPE",
        actionType: "customapi" as const,
        actionName: "sprk_UploadDocument"
      };

      const mockDownloadCommand = {
        label: "Download",
        actionType: "customapi" as const,
        actionName: "sprk_DownloadDocument"
      };

      (EntityConfigurationService.getCustomCommand as jest.Mock)
        .mockImplementation((entity: string, key: string) => {
          if (key === 'uploadCustom') return mockUploadCommand;
          if (key === 'downloadCustom') return mockDownloadCommand;
          return undefined;
        });

      const commands = CommandRegistry.getCommandsWithCustom(
        ['open', 'uploadCustom', 'delete', 'downloadCustom'],
        'sprk_document'
      );

      expect(commands).toHaveLength(4);
      expect(commands[0].key).toBe('open');
      expect(commands[1].key).toBe('uploadCustom');
      expect(commands[2].key).toBe('delete');
      expect(commands[3].key).toBe('downloadCustom');
    });

    it('should return all commands when no privileges provided', () => {
      const mockCustomCommand = {
        label: "Custom Command",
        actionType: "action" as const,
        actionName: "CustomAction"
      };

      (EntityConfigurationService.getCustomCommand as jest.Mock)
        .mockReturnValue(mockCustomCommand);

      const commands = CommandRegistry.getCommandsWithCustom(
        ['create', 'customCmd'],
        'account'
      );

      expect(commands).toHaveLength(2);
    });
  });
});

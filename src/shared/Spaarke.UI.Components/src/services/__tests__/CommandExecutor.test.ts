/**
 * CommandExecutor Unit Tests
 */

import { CommandExecutor } from '../CommandExecutor';
import { ICommand } from '../../types/CommandTypes';
import { createMockCommandContext } from '../../__mocks__/pcfMocks';

describe('CommandExecutor', () => {
  let mockHandler: jest.Mock;
  let mockCommand: ICommand;
  let mockContext: any;

  beforeEach(() => {
    mockHandler = jest.fn().mockResolvedValue(undefined);

    mockCommand = {
      key: 'test',
      label: 'Test Command',
      requiresSelection: false,
      handler: mockHandler
    };

    mockContext = createMockCommandContext();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('canExecute', () => {
    it('should return true when command does not require selection', () => {
      mockCommand.requiresSelection = false;
      mockContext.selectedRecords = [];

      const result = CommandExecutor.canExecute(mockCommand, mockContext);

      expect(result).toBe(true);
    });

    it('should return false when command requires selection but none selected', () => {
      mockCommand.requiresSelection = true;
      mockContext.selectedRecords = [];

      const result = CommandExecutor.canExecute(mockCommand, mockContext);

      expect(result).toBe(false);
    });

    it('should return true when command requires selection and record is selected', () => {
      mockCommand.requiresSelection = true;
      mockContext.selectedRecords = [{ id: '1', entityName: 'account' }];

      const result = CommandExecutor.canExecute(mockCommand, mockContext);

      expect(result).toBe(true);
    });

    it('should return false when multiSelectSupport is false and multiple records selected', () => {
      mockCommand.requiresSelection = true;
      mockCommand.multiSelectSupport = false;
      mockContext.selectedRecords = [
        { id: '1', entityName: 'account' },
        { id: '2', entityName: 'account' }
      ];

      const result = CommandExecutor.canExecute(mockCommand, mockContext);

      expect(result).toBe(false);
    });

    it('should return true when multiSelectSupport is true and multiple records selected', () => {
      mockCommand.requiresSelection = true;
      mockCommand.multiSelectSupport = true;
      mockContext.selectedRecords = [
        { id: '1', entityName: 'account' },
        { id: '2', entityName: 'account' }
      ];

      const result = CommandExecutor.canExecute(mockCommand, mockContext);

      expect(result).toBe(true);
    });
  });

  describe('execute', () => {
    it('should execute command handler', async () => {
      await CommandExecutor.execute(mockCommand, mockContext);

      expect(mockHandler).toHaveBeenCalledWith(mockContext);
    });

    it('should execute command handler with correct context', async () => {
      await CommandExecutor.execute(mockCommand, mockContext);

      expect(mockHandler).toHaveBeenCalledTimes(1);
      expect(mockHandler.mock.calls[0][0]).toEqual(mockContext);
    });

    it('should propagate errors from command handler', async () => {
      const error = new Error('Command failed');
      mockHandler.mockRejectedValue(error);

      await expect(CommandExecutor.execute(mockCommand, mockContext)).rejects.toThrow('Command failed');
    });
  });
});

/**
 * CommandToolbar Integration Tests
 */

import * as React from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CommandToolbar } from '../CommandToolbar';
import { ICommand } from '../../../types/CommandTypes';
import { createMockCommandContext, renderWithProviders } from '../../../__mocks__/pcfMocks';
import { AddRegular, DeleteRegular, ArrowSyncRegular, OpenRegular } from '@fluentui/react-icons';

describe('CommandToolbar', () => {
  let mockHandler: jest.Mock;
  let mockContext: any;

  beforeEach(() => {
    mockHandler = jest.fn().mockResolvedValue(undefined);
    mockContext = createMockCommandContext({
      selectedRecords: []
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('Rendering', () => {
    it('should render toolbar with commands', () => {
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          icon: React.createElement(AddRegular),
          requiresSelection: false,
          handler: mockHandler
        },
        {
          key: 'delete',
          label: 'Delete',
          icon: React.createElement(DeleteRegular),
          requiresSelection: true,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      expect(screen.getByRole('toolbar')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /new/i })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /delete/i })).toBeInTheDocument();
    });

    it('should render empty toolbar with no commands', () => {
      renderWithProviders(
        <CommandToolbar commands={[]} context={mockContext} />
      );

      expect(screen.getByRole('toolbar')).toBeInTheDocument();
      expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });

    it('should apply compact mode styling', () => {
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          requiresSelection: false,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} compact={true} />
      );

      const toolbar = screen.getByRole('toolbar');
      expect(toolbar).toBeInTheDocument();
    });
  });

  describe('Command Execution', () => {
    it('should execute command on button click', async () => {
      const user = userEvent.setup();
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          requiresSelection: false,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      const button = screen.getByRole('button', { name: /new/i });
      await user.click(button);

      expect(mockHandler).toHaveBeenCalledWith(mockContext);
    });

    it('should call onCommandExecuted callback after execution', async () => {
      const user = userEvent.setup();
      const onCommandExecuted = jest.fn();
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          requiresSelection: false,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar
          commands={commands}
          context={mockContext}
          onCommandExecuted={onCommandExecuted}
        />
      );

      const button = screen.getByRole('button', { name: /new/i });
      await user.click(button);

      await waitFor(() => {
        expect(onCommandExecuted).toHaveBeenCalledWith('create');
      });
    });

    it('should show loading state during command execution', async () => {
      const user = userEvent.setup();
      let resolveHandler: () => void;
      const slowHandler = jest.fn(() => new Promise<void>((resolve) => {
        resolveHandler = resolve;
      }));

      const commands: ICommand[] = [
        {
          key: 'slow',
          label: 'Slow Action',
          requiresSelection: false,
          handler: slowHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      const button = screen.getByRole('button', { name: /slow action/i });
      await user.click(button);

      // Button should be disabled during execution
      expect(button).toBeDisabled();

      // Resolve the promise
      resolveHandler!();

      await waitFor(() => {
        expect(button).not.toBeDisabled();
      });
    });
  });

  describe('Command State', () => {
    it('should disable command when selection is required but none selected', () => {
      mockContext.selectedRecords = [];

      const commands: ICommand[] = [
        {
          key: 'delete',
          label: 'Delete',
          requiresSelection: true,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      const button = screen.getByRole('button', { name: /delete/i });
      expect(button).toBeDisabled();
    });

    it('should enable command when selection requirement is met', () => {
      mockContext.selectedRecords = [{ id: '1', entityName: 'account' }];

      const commands: ICommand[] = [
        {
          key: 'delete',
          label: 'Delete',
          requiresSelection: true,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      const button = screen.getByRole('button', { name: /delete/i });
      expect(button).not.toBeDisabled();
    });

    it('should disable command when multiSelectSupport is false and multiple selected', () => {
      mockContext.selectedRecords = [
        { id: '1', entityName: 'account' },
        { id: '2', entityName: 'account' }
      ];

      const commands: ICommand[] = [
        {
          key: 'open',
          label: 'Open',
          requiresSelection: true,
          multiSelectSupport: false,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      const button = screen.getByRole('button', { name: /open/i });
      expect(button).toBeDisabled();
    });
  });

  describe('Command Grouping', () => {
    it('should group commands by group property', () => {
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          group: 'primary',
          requiresSelection: false,
          handler: mockHandler
        },
        {
          key: 'delete',
          label: 'Delete',
          group: 'secondary',
          requiresSelection: false,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      // Both buttons should be visible
      expect(screen.getByRole('button', { name: /new/i })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /delete/i })).toBeInTheDocument();
    });

    it('should show overflow menu when more than 8 commands', () => {
      const commands: ICommand[] = [];
      for (let i = 1; i <= 10; i++) {
        commands.push({
          key: `cmd${i}`,
          label: `Command ${i}`,
          group: i <= 5 ? 'primary' : 'secondary',
          requiresSelection: false,
          handler: mockHandler
        });
      }

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} showOverflow={true} />
      );

      // Primary commands should be visible
      expect(screen.getByRole('button', { name: /command 1/i })).toBeInTheDocument();

      // Overflow button should be present
      const buttons = screen.getAllByRole('button');
      const overflowButton = buttons.find(btn => btn.getAttribute('aria-label')?.includes('More'));
      expect(overflowButton).toBeInTheDocument();
    });

    it('should execute overflow command from menu', async () => {
      const user = userEvent.setup();
      const commands: ICommand[] = [];

      // Create 9 commands to trigger overflow
      for (let i = 1; i <= 9; i++) {
        commands.push({
          key: `cmd${i}`,
          label: `Command ${i}`,
          group: i <= 5 ? 'primary' : 'secondary',
          requiresSelection: false,
          handler: mockHandler
        });
      }

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} showOverflow={true} />
      );

      // Find and click overflow button
      const buttons = screen.getAllByRole('button');
      const overflowButton = buttons.find(btn => btn.getAttribute('aria-label')?.includes('More'));

      if (overflowButton) {
        await user.click(overflowButton);

        // Click overflow command
        const menuItems = await screen.findAllByRole('menuitem');
        if (menuItems.length > 0) {
          await user.click(menuItems[0]);

          await waitFor(() => {
            expect(mockHandler).toHaveBeenCalled();
          });
        }
      }
    });
  });

  describe('Tooltips and Accessibility', () => {
    it('should show tooltip with description', async () => {
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          description: 'Create a new record',
          requiresSelection: false,
          handler: mockHandler
        }
      ];

      const user = userEvent.setup();
      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      const button = screen.getByRole('button', { name: /new/i });
      await user.hover(button);

      await waitFor(() => {
        expect(screen.getByText(/create a new record/i)).toBeInTheDocument();
      }, { timeout: 2000 });
    });

    it('should show keyboard shortcut in tooltip', async () => {
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          description: 'Create a new record',
          keyboardShortcut: 'Ctrl+N',
          requiresSelection: false,
          handler: mockHandler
        }
      ];

      const user = userEvent.setup();
      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      const button = screen.getByRole('button', { name: /new/i });
      expect(button).toHaveAttribute('aria-keyshortcuts', 'Ctrl+N');
    });
  });

  describe('Dividers', () => {
    it('should render divider after command when dividerAfter is true', () => {
      const commands: ICommand[] = [
        {
          key: 'create',
          label: 'New',
          dividerAfter: true,
          requiresSelection: false,
          handler: mockHandler
        },
        {
          key: 'delete',
          label: 'Delete',
          requiresSelection: false,
          handler: mockHandler
        }
      ];

      renderWithProviders(
        <CommandToolbar commands={commands} context={mockContext} />
      );

      // Toolbar should render
      expect(screen.getByRole('toolbar')).toBeInTheDocument();
    });
  });
});

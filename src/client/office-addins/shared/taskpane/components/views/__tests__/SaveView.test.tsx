/**
 * Unit tests for SaveView component
 *
 * Tests the save flow view for saving documents/emails to Spaarke DMS.
 */

import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveView, type SaveOptions } from '../SaveView';
import type { IHostContext } from '@shared/adapters';

// Helper to render with FluentProvider
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

// Mock host contexts
const mockEmailContext: IHostContext = {
  hostType: 'outlook',
  itemType: 'email',
  itemId: 'email-123',
  displayName: 'Important Email',
  metadata: {
    sender: 'sender@example.com',
    receivedDate: '2026-01-15T10:00:00Z',
    attachments: [
      { id: 'att-1', name: 'document.pdf' },
      { id: 'att-2', name: 'image.png' },
    ],
  },
};

const mockDocumentContext: IHostContext = {
  hostType: 'word',
  itemType: 'document',
  itemId: 'doc-123',
  displayName: 'My Document.docx',
  metadata: {},
};

describe('SaveView', () => {
  describe('initial render', () => {
    it('renders document information card', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      expect(screen.getByText('Document Information')).toBeInTheDocument();
      expect(screen.getByText('Important Email')).toBeInTheDocument();
    });

    it('renders email-specific metadata', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      expect(screen.getByText(/sender@example.com/)).toBeInTheDocument();
    });

    it('renders save button', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      expect(screen.getByRole('button', { name: /save to spaarke/i })).toBeInTheDocument();
    });

    it('handles null hostContext gracefully', () => {
      renderWithProvider(<SaveView hostContext={null} />);

      expect(screen.getByText('Unknown')).toBeInTheDocument();
    });
  });

  describe('email with attachments', () => {
    it('renders attachments card', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      expect(screen.getByText(/Attachments \(2\)/)).toBeInTheDocument();
    });

    it('renders include attachments checkbox', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      expect(screen.getByRole('checkbox', { name: /include attachments/i })).toBeInTheDocument();
    });

    it('shows attachment list when include attachments is checked', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      // Checkbox is checked by default
      expect(screen.getByText('document.pdf')).toBeInTheDocument();
      expect(screen.getByText('image.png')).toBeInTheDocument();
    });

    it('hides attachment list when include attachments is unchecked', async () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      const checkbox = screen.getByRole('checkbox', { name: /include attachments/i });
      await userEvent.click(checkbox);

      expect(screen.queryByText('document.pdf')).not.toBeInTheDocument();
    });

    it('allows selecting individual attachments', async () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      const pdfCheckbox = screen.getByRole('checkbox', { name: /document\.pdf/i });
      expect(pdfCheckbox).not.toBeChecked();

      await userEvent.click(pdfCheckbox);
      expect(pdfCheckbox).toBeChecked();
    });
  });

  describe('document context', () => {
    it('does not render attachments card for documents', () => {
      renderWithProvider(<SaveView hostContext={mockDocumentContext} />);

      expect(screen.queryByText('Attachments')).not.toBeInTheDocument();
    });

    it('does not render email-specific fields for documents', () => {
      renderWithProvider(<SaveView hostContext={mockDocumentContext} />);

      expect(screen.queryByText(/From:/)).not.toBeInTheDocument();
      expect(screen.queryByText(/Date:/)).not.toBeInTheDocument();
    });
  });

  describe('save action', () => {
    it('calls onSave with correct options', async () => {
      const handleSave = jest.fn().mockResolvedValue(undefined);
      renderWithProvider(<SaveView hostContext={mockEmailContext} onSave={handleSave} />);

      // Select an attachment
      const pdfCheckbox = screen.getByRole('checkbox', { name: /document\.pdf/i });
      await userEvent.click(pdfCheckbox);

      // Click save
      const saveButton = screen.getByRole('button', { name: /save to spaarke/i });
      await userEvent.click(saveButton);

      expect(handleSave).toHaveBeenCalledWith({
        includeAttachments: true,
        attachmentIds: ['att-1'],
      });
    });

    it('passes empty attachmentIds when include attachments is unchecked', async () => {
      const handleSave = jest.fn().mockResolvedValue(undefined);
      renderWithProvider(<SaveView hostContext={mockEmailContext} onSave={handleSave} />);

      // Uncheck include attachments
      const includeCheckbox = screen.getByRole('checkbox', { name: /include attachments/i });
      await userEvent.click(includeCheckbox);

      // Click save
      const saveButton = screen.getByRole('button', { name: /save to spaarke/i });
      await userEvent.click(saveButton);

      expect(handleSave).toHaveBeenCalledWith({
        includeAttachments: false,
        attachmentIds: [],
      });
    });

    it('does not call onSave when not provided', async () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} />);

      const saveButton = screen.getByRole('button', { name: /save to spaarke/i });
      await userEvent.click(saveButton);

      // Should not throw
      expect(true).toBe(true);
    });
  });

  describe('saving state', () => {
    it('disables save button when saving', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} isSaving />);

      const saveButton = screen.getByRole('button', { name: /saving/i });
      expect(saveButton).toBeDisabled();
    });

    it('shows spinner in button when saving', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} isSaving />);

      expect(screen.getByText('Saving...')).toBeInTheDocument();
    });

    it('shows progress bar when saving', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} isSaving progress={50} />);

      expect(screen.getByText('Saving to Spaarke...')).toBeInTheDocument();
      expect(screen.getByRole('progressbar')).toBeInTheDocument();
    });

    it('updates progress bar value', () => {
      renderWithProvider(<SaveView hostContext={mockEmailContext} isSaving progress={75} />);

      const progressBar = screen.getByRole('progressbar');
      expect(progressBar).toHaveAttribute('aria-valuenow', '0.75');
    });
  });

  describe('status messages', () => {
    it('shows error message when error prop is set', () => {
      renderWithProvider(
        <SaveView hostContext={mockEmailContext} error="Failed to save document" />
      );

      expect(screen.getByText('Failed to save document')).toBeInTheDocument();
    });

    it('shows success message when success prop is set', () => {
      renderWithProvider(
        <SaveView hostContext={mockEmailContext} success="Document saved successfully!" />
      );

      expect(screen.getByText('Document saved successfully!')).toBeInTheDocument();
    });

    it('can show both error and success messages', () => {
      renderWithProvider(
        <SaveView
          hostContext={mockEmailContext}
          error="Some warning"
          success="Partial success"
        />
      );

      expect(screen.getByText('Some warning')).toBeInTheDocument();
      expect(screen.getByText('Partial success')).toBeInTheDocument();
    });
  });

  describe('edge cases', () => {
    it('handles email without attachments', () => {
      const contextWithoutAttachments: IHostContext = {
        ...mockEmailContext,
        metadata: {
          ...mockEmailContext.metadata,
          attachments: [],
        },
      };

      renderWithProvider(<SaveView hostContext={contextWithoutAttachments} />);

      expect(screen.queryByText('Attachments')).not.toBeInTheDocument();
    });

    it('handles email with undefined attachments', () => {
      const contextWithUndefinedAttachments: IHostContext = {
        ...mockEmailContext,
        metadata: {
          sender: 'sender@example.com',
        },
      };

      renderWithProvider(<SaveView hostContext={contextWithUndefinedAttachments} />);

      expect(screen.queryByText('Attachments')).not.toBeInTheDocument();
    });

    it('handles missing display name', () => {
      const contextWithoutName: IHostContext = {
        ...mockEmailContext,
        displayName: undefined as unknown as string,
      };

      renderWithProvider(<SaveView hostContext={contextWithoutName} />);

      expect(screen.getByText('Unknown')).toBeInTheDocument();
    });

    it('handles missing sender', () => {
      const contextWithoutSender: IHostContext = {
        ...mockEmailContext,
        metadata: {
          receivedDate: '2026-01-15T10:00:00Z',
        },
      };

      renderWithProvider(<SaveView hostContext={contextWithoutSender} />);

      expect(screen.getByText(/Unknown/)).toBeInTheDocument();
    });

    it('handles missing received date', () => {
      const contextWithoutDate: IHostContext = {
        ...mockEmailContext,
        metadata: {
          sender: 'sender@example.com',
        },
      };

      renderWithProvider(<SaveView hostContext={contextWithoutDate} />);

      // Date should show as Unknown
      const dateLabels = screen.getAllByText(/Unknown/);
      expect(dateLabels.length).toBeGreaterThan(0);
    });
  });
});

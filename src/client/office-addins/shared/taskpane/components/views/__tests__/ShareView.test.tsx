/**
 * Unit tests for ShareView component
 *
 * Tests the share flow view for generating and inserting document links.
 */

import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ShareView, type DocumentSearchResult, type SharePermissions } from '../ShareView';

// Helper to render with FluentProvider
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

// Mock search results
const mockSearchResults: DocumentSearchResult[] = [
  {
    id: 'doc-1',
    name: 'Important Contract.docx',
    path: '/Legal/Contracts/',
    modifiedDate: '2026-01-15T10:00:00Z',
  },
  {
    id: 'doc-2',
    name: 'Project Proposal.pdf',
    path: '/Projects/2026/',
    modifiedDate: '2026-01-14T15:30:00Z',
  },
];

// Mock clipboard
const mockClipboard = {
  writeText: jest.fn().mockResolvedValue(undefined),
};
Object.assign(navigator, { clipboard: mockClipboard });

describe('ShareView', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe('initial render', () => {
    it('renders search section', () => {
      renderWithProvider(<ShareView />);

      expect(screen.getByText('Find Document')).toBeInTheDocument();
      expect(screen.getByPlaceholderText('Search by name or path...')).toBeInTheDocument();
    });

    it('renders search button', () => {
      renderWithProvider(<ShareView />);

      expect(screen.getByRole('button', { name: /search/i })).toBeInTheDocument();
    });

    it('disables search button when search query is empty', () => {
      renderWithProvider(<ShareView />);

      const searchButton = screen.getByRole('button', { name: /search/i });
      expect(searchButton).toBeDisabled();
    });
  });

  describe('search functionality', () => {
    it('enables search button when query is entered', async () => {
      renderWithProvider(<ShareView />);

      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'contract');

      const searchButton = screen.getByRole('button', { name: /search/i });
      expect(searchButton).not.toBeDisabled();
    });

    it('calls onSearch when search button is clicked', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      renderWithProvider(<ShareView onSearch={handleSearch} />);

      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'contract');

      const searchButton = screen.getByRole('button', { name: /search/i });
      await userEvent.click(searchButton);

      expect(handleSearch).toHaveBeenCalledWith('contract');
    });

    it('calls onSearch when Enter is pressed', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      renderWithProvider(<ShareView onSearch={handleSearch} />);

      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'contract{Enter}');

      expect(handleSearch).toHaveBeenCalledWith('contract');
    });

    it('displays search results', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      renderWithProvider(<ShareView onSearch={handleSearch} />);

      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
        expect(screen.getByText('Project Proposal.pdf')).toBeInTheDocument();
      });
    });

    it('displays document paths in search results', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      renderWithProvider(<ShareView onSearch={handleSearch} />);

      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('/Legal/Contracts/')).toBeInTheDocument();
        expect(screen.getByText('/Projects/2026/')).toBeInTheDocument();
      });
    });

    it('shows loading state during search', async () => {
      const handleSearch = jest.fn().mockImplementation(
        () => new Promise((resolve) => setTimeout(() => resolve([]), 1000))
      );

      renderWithProvider(<ShareView onSearch={handleSearch} isLoading />);

      const searchButton = screen.getByRole('button', { name: /search/i });
      expect(searchButton).toBeDisabled();
    });
  });

  describe('document selection', () => {
    it('selects document when clicked', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      renderWithProvider(<ShareView onSearch={handleSearch} />);

      // Perform search
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      // Select document
      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      // Should show link generation section
      await waitFor(() => {
        expect(screen.getByText('Generate Sharing Link')).toBeInTheDocument();
        expect(screen.getByText(/Selected: Important Contract.docx/)).toBeInTheDocument();
      });
    });

    it('clears generated link when new document is selected', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      const handleGenerateLink = jest.fn().mockResolvedValue('https://share.spaarke.com/abc123');

      renderWithProvider(
        <ShareView onSearch={handleSearch} onGenerateLink={handleGenerateLink} />
      );

      // Perform search
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      // Select first document
      const firstDoc = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(firstDoc!);

      // Generate link
      await waitFor(() => {
        expect(screen.getByText('Generate Sharing Link')).toBeInTheDocument();
      });
      const generateButton = screen.getByRole('button', { name: /generate link/i });
      await userEvent.click(generateButton);

      await waitFor(() => {
        expect(screen.getByDisplayValue('https://share.spaarke.com/abc123')).toBeInTheDocument();
      });

      // Select different document
      const secondDoc = screen.getByText('Project Proposal.pdf').closest('div');
      await userEvent.click(secondDoc!);

      // Generated link should be cleared
      await waitFor(() => {
        expect(screen.queryByDisplayValue('https://share.spaarke.com/abc123')).not.toBeInTheDocument();
      });
    });
  });

  describe('link generation', () => {
    it('calls onGenerateLink with correct parameters', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      const handleGenerateLink = jest.fn().mockResolvedValue('https://share.spaarke.com/abc123');

      renderWithProvider(
        <ShareView onSearch={handleSearch} onGenerateLink={handleGenerateLink} />
      );

      // Search and select
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      // Generate link
      await waitFor(() => {
        expect(screen.getByRole('button', { name: /generate link/i })).toBeInTheDocument();
      });

      const generateButton = screen.getByRole('button', { name: /generate link/i });
      await userEvent.click(generateButton);

      expect(handleGenerateLink).toHaveBeenCalledWith('doc-1', { type: 'view' });
    });

    it('allows changing permission type before generating', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      const handleGenerateLink = jest.fn().mockResolvedValue('https://share.spaarke.com/edit123');

      renderWithProvider(
        <ShareView onSearch={handleSearch} onGenerateLink={handleGenerateLink} />
      );

      // Search and select
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      // Change permission type
      await waitFor(() => {
        expect(screen.getByText('Permission')).toBeInTheDocument();
      });

      const dropdown = screen.getByRole('combobox');
      await userEvent.click(dropdown);

      const editOption = screen.getByRole('option', { name: /can edit/i });
      await userEvent.click(editOption);

      // Generate link
      const generateButton = screen.getByRole('button', { name: /generate link/i });
      await userEvent.click(generateButton);

      expect(handleGenerateLink).toHaveBeenCalledWith('doc-1', { type: 'edit' });
    });

    it('displays generated link', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      const handleGenerateLink = jest.fn().mockResolvedValue('https://share.spaarke.com/xyz789');

      renderWithProvider(
        <ShareView onSearch={handleSearch} onGenerateLink={handleGenerateLink} />
      );

      // Search and select
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      // Generate link
      await waitFor(() => {
        expect(screen.getByRole('button', { name: /generate link/i })).toBeInTheDocument();
      });

      const generateButton = screen.getByRole('button', { name: /generate link/i });
      await userEvent.click(generateButton);

      await waitFor(() => {
        expect(screen.getByDisplayValue('https://share.spaarke.com/xyz789')).toBeInTheDocument();
      });
    });
  });

  describe('copy link', () => {
    it('copies link to clipboard', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      const handleGenerateLink = jest.fn().mockResolvedValue('https://share.spaarke.com/copy123');

      renderWithProvider(
        <ShareView onSearch={handleSearch} onGenerateLink={handleGenerateLink} />
      );

      // Search, select, and generate
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /generate link/i })).toBeInTheDocument();
      });

      const generateButton = screen.getByRole('button', { name: /generate link/i });
      await userEvent.click(generateButton);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /copy/i })).toBeInTheDocument();
      });

      // Copy link
      const copyButton = screen.getByRole('button', { name: /copy/i });
      await userEvent.click(copyButton);

      expect(mockClipboard.writeText).toHaveBeenCalledWith('https://share.spaarke.com/copy123');
    });

    it('shows "Copied!" feedback after copying', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      const handleGenerateLink = jest.fn().mockResolvedValue('https://share.spaarke.com/feedback');

      renderWithProvider(
        <ShareView onSearch={handleSearch} onGenerateLink={handleGenerateLink} />
      );

      // Search, select, and generate
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /generate link/i })).toBeInTheDocument();
      });

      const generateButton = screen.getByRole('button', { name: /generate link/i });
      await userEvent.click(generateButton);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /copy/i })).toBeInTheDocument();
      });

      // Copy link
      const copyButton = screen.getByRole('button', { name: /copy/i });
      await userEvent.click(copyButton);

      await waitFor(() => {
        expect(screen.getByText('Copied!')).toBeInTheDocument();
      });

      // After timeout, should revert
      jest.advanceTimersByTime(2500);

      await waitFor(() => {
        expect(screen.getByText('Copy')).toBeInTheDocument();
      });
    });
  });

  describe('insert link', () => {
    it('calls onInsertLink with generated link', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);
      const handleGenerateLink = jest.fn().mockResolvedValue('https://share.spaarke.com/insert123');
      const handleInsertLink = jest.fn().mockResolvedValue(undefined);

      renderWithProvider(
        <ShareView
          onSearch={handleSearch}
          onGenerateLink={handleGenerateLink}
          onInsertLink={handleInsertLink}
        />
      );

      // Search, select, and generate
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /generate link/i })).toBeInTheDocument();
      });

      const generateButton = screen.getByRole('button', { name: /generate link/i });
      await userEvent.click(generateButton);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /insert link/i })).toBeInTheDocument();
      });

      // Insert link
      const insertButton = screen.getByRole('button', { name: /insert link/i });
      await userEvent.click(insertButton);

      expect(handleInsertLink).toHaveBeenCalledWith('https://share.spaarke.com/insert123');
    });

    it('only shows insert button when link is generated', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);

      renderWithProvider(<ShareView onSearch={handleSearch} />);

      // Search and select (but don't generate)
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /generate link/i })).toBeInTheDocument();
      });

      // Insert button should not be visible
      expect(screen.queryByRole('button', { name: /insert link/i })).not.toBeInTheDocument();
    });
  });

  describe('error handling', () => {
    it('displays error message when error prop is set', () => {
      renderWithProvider(<ShareView error="Failed to generate link" />);

      expect(screen.getByText('Failed to generate link')).toBeInTheDocument();
    });
  });

  describe('loading state', () => {
    it('disables generate button when loading', async () => {
      const handleSearch = jest.fn().mockResolvedValue(mockSearchResults);

      renderWithProvider(<ShareView onSearch={handleSearch} isLoading />);

      // Search and select
      const input = screen.getByPlaceholderText('Search by name or path...');
      await userEvent.type(input, 'doc{Enter}');

      await waitFor(() => {
        expect(screen.getByText('Important Contract.docx')).toBeInTheDocument();
      });

      const docResult = screen.getByText('Important Contract.docx').closest('div');
      await userEvent.click(docResult!);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /generate link/i })).toBeInTheDocument();
      });

      const generateButton = screen.getByRole('button', { name: /generate link/i });
      expect(generateButton).toBeDisabled();
    });
  });
});

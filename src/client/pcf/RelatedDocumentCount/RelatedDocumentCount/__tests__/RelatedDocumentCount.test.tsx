/**
 * Unit tests for RelatedDocumentCount component.
 *
 * Tests rendering of RelationshipCountCard and FindSimilarDialog,
 * including loading/error states and dialog open/close interactions.
 *
 * Uses @testing-library/react v12 for React 16 compatibility (ADR-022).
 */
import * as React from 'react';
import { render, fireEvent } from '@testing-library/react';
import { RelatedDocumentCount } from '../RelatedDocumentCount';
import { IRelatedDocumentCountProps } from '../types';

// ── Mocks ────────────────────────────────────────────────────────────────────

// Mock the hook so we can control its return values
const mockHookReturn = {
  count: 0,
  isLoading: false,
  error: null as string | null,
  lastUpdated: null as Date | null,
  refetch: jest.fn(),
};

jest.mock('../hooks/useRelatedDocumentCount', () => ({
  useRelatedDocumentCount: jest.fn(() => mockHookReturn),
}));

// Mock shared UI components
let capturedCountCardProps: Record<string, unknown> = {};
let capturedDialogProps: Record<string, unknown> = {};

jest.mock('@spaarke/ui-components/dist/components/RelationshipCountCard', () => ({
  RelationshipCountCard: (props: Record<string, unknown>) => {
    capturedCountCardProps = props;
    return (
      <div data-testid="relationship-count-card">
        <span data-testid="card-title">{props.title as string}</span>
        <span data-testid="card-count">{String(props.count)}</span>
        {props.isLoading && <span data-testid="card-loading">Loading</span>}
        {props.error && <span data-testid="card-error">{props.error as string}</span>}
        <button data-testid="card-open-button" onClick={props.onOpen as () => void}>
          Open
        </button>
      </div>
    );
  },
}));

jest.mock('@spaarke/ui-components/dist/components/FindSimilarDialog', () => ({
  FindSimilarDialog: (props: Record<string, unknown>) => {
    capturedDialogProps = props;
    return props.open ? (
      <div data-testid="find-similar-dialog">
        <span data-testid="dialog-url">{props.url as string}</span>
        <button data-testid="dialog-close-button" onClick={props.onClose as () => void}>
          Close
        </button>
      </div>
    ) : null;
  },
}));

// ── Helpers ──────────────────────────────────────────────────────────────────

const DEFAULT_PROPS: IRelatedDocumentCountProps = {
  context: {} as ComponentFramework.Context<never>,
  documentId: 'doc-123',
  tenantId: 'tenant-001',
  apiBaseUrl: 'https://spe-api-dev.azurewebsites.net',
  isDarkMode: false,
};

function renderComponent(overrides?: Partial<IRelatedDocumentCountProps>) {
  return render(<RelatedDocumentCount {...DEFAULT_PROPS} {...overrides} />);
}

// ── Setup / Teardown ─────────────────────────────────────────────────────────

beforeEach(() => {
  // Reset hook mock to defaults
  mockHookReturn.count = 0;
  mockHookReturn.isLoading = false;
  mockHookReturn.error = null;
  mockHookReturn.lastUpdated = null;
  mockHookReturn.refetch = jest.fn();
  capturedCountCardProps = {};
  capturedDialogProps = {};
});

// ── Tests ────────────────────────────────────────────────────────────────────

describe('RelatedDocumentCount', () => {
  describe('rendering', () => {
    it('renders RelationshipCountCard with correct props', () => {
      mockHookReturn.count = 12;
      mockHookReturn.lastUpdated = new Date('2026-01-15T10:00:00Z');

      const { getByTestId } = renderComponent();

      expect(getByTestId('relationship-count-card')).toBeInTheDocument();
      expect(capturedCountCardProps.title).toBe('RELATED DOCUMENTS');
      expect(capturedCountCardProps.count).toBe(12);
      expect(capturedCountCardProps.isLoading).toBe(false);
      expect(capturedCountCardProps.error).toBeNull();
    });

    it('uses custom cardTitle when provided', () => {
      renderComponent({ cardTitle: 'SIMILAR ITEMS' });

      expect(capturedCountCardProps.title).toBe('SIMILAR ITEMS');
    });

    it("defaults cardTitle to 'RELATED DOCUMENTS'", () => {
      renderComponent();

      expect(capturedCountCardProps.title).toBe('RELATED DOCUMENTS');
    });
  });

  describe('loading state', () => {
    it('passes isLoading=true to card when hook is loading', () => {
      mockHookReturn.isLoading = true;

      const { getByTestId } = renderComponent();

      expect(getByTestId('card-loading')).toBeInTheDocument();
      expect(capturedCountCardProps.isLoading).toBe(true);
    });
  });

  describe('error state', () => {
    it('passes error to card when hook returns error', () => {
      mockHookReturn.error = 'Failed to load';

      const { getByTestId } = renderComponent();

      expect(getByTestId('card-error')).toBeInTheDocument();
      expect(capturedCountCardProps.error).toBe('Failed to load');
    });
  });

  describe('FindSimilarDialog', () => {
    it('dialog is closed initially', () => {
      renderComponent();

      expect(capturedDialogProps.open).toBe(false);
    });

    it('opens dialog when card onOpen is triggered', () => {
      renderComponent();

      fireEvent.click(document.querySelector("[data-testid='card-open-button']"));

      expect(capturedDialogProps.open).toBe(true);
    });

    it('passes viewer URL to dialog when open', () => {
      const { getByTestId } = renderComponent({
        documentId: 'doc-xyz',
        tenantId: 't-1',
        isDarkMode: false,
      });

      fireEvent.click(getByTestId('card-open-button'));

      const dialogUrl = capturedDialogProps.url as string;
      expect(dialogUrl).toContain('sprk_documentrelationshipviewer');
      // The data param is URL-encoded, so decode it for readable assertions
      const decodedUrl = decodeURIComponent(dialogUrl);
      expect(decodedUrl).toContain('documentId=doc-xyz');
      expect(decodedUrl).toContain('theme=light');
    });

    it('passes dark theme to viewer URL when isDarkMode=true', () => {
      const { getByTestId } = renderComponent({ isDarkMode: true });

      fireEvent.click(getByTestId('card-open-button'));

      const decodedUrl = decodeURIComponent(capturedDialogProps.url as string);
      expect(decodedUrl).toContain('theme=dark');
    });

    it('closes dialog when onClose is triggered', () => {
      const { getByTestId } = renderComponent();

      // Open dialog
      fireEvent.click(getByTestId('card-open-button'));
      expect(capturedDialogProps.open).toBe(true);

      // Close dialog
      fireEvent.click(getByTestId('dialog-close-button'));
      expect(capturedDialogProps.open).toBe(false);
    });

    it('passes null URL to dialog when closed', () => {
      renderComponent();

      // Dialog should have null URL when closed
      expect(capturedDialogProps.url).toBeNull();
    });

    it('does not open dialog when documentId is empty', () => {
      renderComponent({ documentId: '' });

      fireEvent.click(document.querySelector("[data-testid='card-open-button']"));

      // viewerUrl is null because documentId is empty, so handleOpen returns early
      expect(capturedDialogProps.open).toBe(false);
    });
  });
});

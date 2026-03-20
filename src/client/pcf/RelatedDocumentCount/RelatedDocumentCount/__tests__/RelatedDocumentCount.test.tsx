/**
 * Unit tests for RelatedDocumentCount component.
 *
 * Tests rendering of RelationshipCountCard and FindSimilarDialog,
 * including loading/error states and dialog open/close interactions.
 *
 * Uses @testing-library/react v12 for React 16 compatibility (ADR-022).
 */
import * as React from 'react';
import { render, fireEvent, act } from '@testing-library/react';
import { RelatedDocumentCount } from '../RelatedDocumentCount';
import { IRelatedDocumentCountProps } from '../types';

// ── Mocks ────────────────────────────────────────────────────────────────────

// Mock authInit to resolve immediately with fake config
jest.mock('../authInit', () => ({
  initializeAuth: jest.fn(() =>
    Promise.resolve({
      bffApiUrl: 'https://spe-api-test.azurewebsites.net',
      tenantId: 'test-tenant-id',
    })
  ),
}));

// Mock the graph data hook so we can control its return values
const mockGraphHookReturn = {
  count: 0,
  nodes: [] as unknown[],
  edges: [] as unknown[],
  isLoading: false,
  error: null as string | null,
  lastUpdated: null as Date | null,
  refetch: jest.fn(),
};

jest.mock('../hooks/useRelatedDocumentGraphData', () => ({
  useRelatedDocumentGraphData: jest.fn(() => mockGraphHookReturn),
}));

// Mock MiniGraph component
jest.mock('@spaarke/ui-components/dist/components/MiniGraph', () => ({
  MiniGraph: () => <div data-testid="mini-graph" />,
}));

// Mock shared UI components
let capturedCountCardProps: Record<string, unknown> = {};
let capturedDialogProps: Record<string, unknown> = {};

jest.mock('@spaarke/ui-components/dist/components/RelationshipCountCard', () => ({
  RelationshipCountCard: (props: Record<string, unknown>) => {
    capturedCountCardProps = props;
    const countStr = props.count !== null && props.count !== undefined ? String(props.count as number) : '0';
    const errorStr = props.error ? String(props.error as string) : '';
    return (
      <div data-testid="relationship-count-card">
        <span data-testid="card-count">{countStr}</span>
        {props.isLoading && <span data-testid="card-loading">Loading</span>}
        {errorStr && <span data-testid="card-error">{errorStr}</span>}
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
    const urlStr = props.url ? String(props.url as string) : '';
    return props.open ? (
      <div data-testid="find-similar-dialog">
        <span data-testid="dialog-url">{urlStr}</span>
        <button data-testid="dialog-close-button" onClick={props.onClose as () => void}>
          Close
        </button>
      </div>
    ) : null;
  },
}));

// ── Helpers ──────────────────────────────────────────────────────────────────

const mockWebApi = {
  retrieveMultipleRecords: jest.fn(),
  retrieveRecord: jest.fn(),
  createRecord: jest.fn(),
  deleteRecord: jest.fn(),
  updateRecord: jest.fn(),
} as unknown as ComponentFramework.WebApi;

const DEFAULT_PROPS: IRelatedDocumentCountProps = {
  context: { webAPI: mockWebApi } as unknown as ComponentFramework.Context<never>,
  documentId: 'doc-123',
  isDarkMode: false,
};

function renderComponent(overrides?: Partial<IRelatedDocumentCountProps>) {
  return render(<RelatedDocumentCount {...DEFAULT_PROPS} {...overrides} />);
}

// ── Setup / Teardown ─────────────────────────────────────────────────────────

beforeEach(() => {
  // Reset hook mock to defaults
  mockGraphHookReturn.count = 0;
  mockGraphHookReturn.nodes = [];
  mockGraphHookReturn.edges = [];
  mockGraphHookReturn.isLoading = false;
  mockGraphHookReturn.error = null;
  mockGraphHookReturn.lastUpdated = null;
  mockGraphHookReturn.refetch = jest.fn();
  capturedCountCardProps = {};
  capturedDialogProps = {};
});

// ── Tests ────────────────────────────────────────────────────────────────────

describe('RelatedDocumentCount', () => {
  describe('rendering', () => {
    it('renders RelationshipCountCard', async () => {
      mockGraphHookReturn.count = 12;
      mockGraphHookReturn.lastUpdated = new Date('2026-01-15T10:00:00Z');

      await act(async () => {
        renderComponent();
      });

      expect(document.querySelector("[data-testid='relationship-count-card']")).toBeInTheDocument();
    });
  });

  describe('loading state', () => {
    it('passes isLoading=true to card when hook is loading', async () => {
      mockGraphHookReturn.isLoading = true;

      await act(async () => {
        renderComponent();
      });

      expect(document.querySelector("[data-testid='card-loading']")).toBeInTheDocument();
    });
  });

  describe('error state', () => {
    it('passes error to card when hook returns error', async () => {
      mockGraphHookReturn.error = 'Failed to load';

      await act(async () => {
        renderComponent();
      });

      expect(document.querySelector("[data-testid='card-error']")).toBeInTheDocument();
    });
  });

  describe('FindSimilarDialog', () => {
    it('dialog is closed initially', async () => {
      await act(async () => {
        renderComponent();
      });

      expect(capturedDialogProps.open).toBe(false);
    });

    it('opens dialog when card onOpen is triggered', async () => {
      await act(async () => {
        renderComponent();
      });

      act(() => {
        const btn = document.querySelector("[data-testid='card-open-button']");
        if (btn) fireEvent.click(btn);
      });

      expect(capturedDialogProps.open).toBe(true);
    });

    it('closes dialog when onClose is triggered', async () => {
      await act(async () => {
        renderComponent();
      });

      // Open dialog
      act(() => {
        const btn = document.querySelector("[data-testid='card-open-button']");
        if (btn) fireEvent.click(btn);
      });
      expect(capturedDialogProps.open).toBe(true);

      // Close dialog
      act(() => {
        const btn = document.querySelector("[data-testid='dialog-close-button']");
        if (btn) fireEvent.click(btn);
      });
      expect(capturedDialogProps.open).toBe(false);
    });

    it('does not open dialog when documentId is empty', async () => {
      await act(async () => {
        renderComponent({ documentId: '' });
      });

      act(() => {
        const btn = document.querySelector("[data-testid='card-open-button']");
        if (btn) fireEvent.click(btn);
      });

      // viewerUrl is null because documentId is empty, so handleOpen returns early
      expect(capturedDialogProps.open).toBe(false);
    });
  });
});

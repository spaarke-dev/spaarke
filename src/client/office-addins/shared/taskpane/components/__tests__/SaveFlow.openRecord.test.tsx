/**
 * Unit tests for SaveFlow's task 027 (FR-10) open-record wiring: the related-record card's click
 * seam, the Document-record affordance, capability gating (NFR-10), and the focus/visibility
 * return-path re-read (Spike-2 §d).
 *
 * Kept in a SEPARATE file from `SaveFlow.test.tsx` (which pre-dates task 027 and already carries
 * unrelated typecheck errors in its fixtures — the 2026-09-09 "consciously accepted" test-file
 * bucket, project CLAUDE.md Decisions Made) so this task's own tests stay independently clean.
 */

import React from 'react';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import type { DocumentIdentityState } from '../../services/documentIdentityService';
import { openRecord } from '../../services/openRecordLauncher';

jest.mock('../../services/openRecordLauncher');

// SaveFlow mounts DocumentProfileSection (task 021/022/033's useDocumentProfile,
// GET /api/v1/documents/{id}) AND fires the "Related to" suggestions lookup
// (communicationSuggestionsService, GET /api/office/communications/by-message-id/...) on mount
// regardless of props here. Mock apiClient so both resolve deterministically to a benign,
// non-error, non-MessageBar-rendering state (jsdom has no ResizeObserver, which Fluent's
// MessageBar needs) rather than exercising the real network/BFF.
jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn((url: string) => {
        if (url.startsWith('/api/v1/documents/')) {
          // No summaryStatus → summaryStatusFromCode(undefined) === 'None' → a plain <Text>,
          // never the MessageBar branch.
          return Promise.resolve({ data: {} });
        }
        return Promise.reject(
          new actual.ApiClientError({ type: 'about:blank', title: 'Not Found', status: 404, detail: '' })
        );
      }),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockOpenRecord = openRecord as jest.MockedFunction<typeof openRecord>;

const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

const mockGetAccessToken = jest.fn().mockResolvedValue('test-access-token');

function resolvedWithRelatedRecord(): DocumentIdentityState {
  return {
    kind: 'resolved',
    documentId: '11111111-1111-1111-1111-111111111111',
    documentName: 'Examiner report draft',
    fileName: 'Examiner report draft.docx',
    relatedRecord: {
      entityType: 'sprk_matter',
      id: '22222222-2222-2222-2222-222222222222',
      name: 'PAT-191111',
      displayName: 'Gamma Merger',
      number: 'PAT-191111',
    },
  };
}

describe('SaveFlow — task 027 / FR-10 open-record wiring', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockOpenRecord.mockReturnValue({ opened: true });
  });

  describe('capability gating (NFR-10) — no-capability rendering', () => {
    it('renders neither open affordance when canOpenRecord is false (default)', () => {
      render(
        <TestWrapper>
          <SaveFlow
            hostType="word"
            itemId="word-doc-1"
            itemName="Examiner report draft.docx"
            resolvedDocumentId="11111111-1111-1111-1111-111111111111"
            documentIdentity={resolvedWithRelatedRecord()}
            getAccessToken={mockGetAccessToken}
          />
        </TestWrapper>
      );

      expect(screen.queryByRole('button', { name: /open.*gamma merger/i })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /open this document's record/i })).not.toBeInTheDocument();
    });

    it('renders both open affordances when canOpenRecord is true and a record/document are resolved', () => {
      render(
        <TestWrapper>
          <SaveFlow
            hostType="word"
            itemId="word-doc-1"
            itemName="Examiner report draft.docx"
            resolvedDocumentId="11111111-1111-1111-1111-111111111111"
            documentIdentity={resolvedWithRelatedRecord()}
            canOpenRecord
            getAccessToken={mockGetAccessToken}
          />
        </TestWrapper>
      );

      expect(screen.getByRole('button', { name: /open.*gamma merger/i })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /open this document's record/i })).toBeInTheDocument();
    });
  });

  describe('launcher wiring', () => {
    it('clicking the related-record card Open button calls openRecord with the resolved entity and id', async () => {
      const user = userEvent.setup();
      render(
        <TestWrapper>
          <SaveFlow
            hostType="word"
            itemId="word-doc-1"
            itemName="Examiner report draft.docx"
            resolvedDocumentId="11111111-1111-1111-1111-111111111111"
            documentIdentity={resolvedWithRelatedRecord()}
            canOpenRecord
            getAccessToken={mockGetAccessToken}
          />
        </TestWrapper>
      );

      await user.click(screen.getByRole('button', { name: /open.*gamma merger/i }));

      expect(mockOpenRecord).toHaveBeenCalledTimes(1);
      expect(mockOpenRecord).toHaveBeenCalledWith({
        orgUrl: undefined,
        entityType: 'sprk_matter',
        recordId: '22222222-2222-2222-2222-222222222222',
      });
    });

    it('clicking the Document-record affordance calls openRecord for sprk_document with resolvedDocumentId', async () => {
      const user = userEvent.setup();
      render(
        <TestWrapper>
          <SaveFlow
            hostType="word"
            itemId="word-doc-1"
            itemName="Examiner report draft.docx"
            resolvedDocumentId="11111111-1111-1111-1111-111111111111"
            documentIdentity={resolvedWithRelatedRecord()}
            canOpenRecord
            getAccessToken={mockGetAccessToken}
          />
        </TestWrapper>
      );

      await user.click(screen.getByRole('button', { name: /open this document's record/i }));

      expect(mockOpenRecord).toHaveBeenCalledTimes(1);
      expect(mockOpenRecord).toHaveBeenCalledWith({
        orgUrl: undefined,
        entityType: 'sprk_document',
        recordId: '11111111-1111-1111-1111-111111111111',
      });
    });
  });

  describe('focus/visibility return path (Spike-2 §d)', () => {
    it('does NOT re-read on focus before any record has been opened', () => {
      const onRetryDocumentIdentity = jest.fn();
      render(
        <TestWrapper>
          <SaveFlow
            hostType="word"
            itemId="word-doc-1"
            itemName="Examiner report draft.docx"
            resolvedDocumentId="11111111-1111-1111-1111-111111111111"
            documentIdentity={resolvedWithRelatedRecord()}
            canOpenRecord
            onRetryDocumentIdentity={onRetryDocumentIdentity}
            getAccessToken={mockGetAccessToken}
          />
        </TestWrapper>
      );

      act(() => {
        window.dispatchEvent(new Event('focus'));
      });

      expect(onRetryDocumentIdentity).not.toHaveBeenCalled();
    });

    it('re-reads identity on window focus after the user opened a record via the escape hatch', async () => {
      const user = userEvent.setup();
      const onRetryDocumentIdentity = jest.fn();
      render(
        <TestWrapper>
          <SaveFlow
            hostType="word"
            itemId="word-doc-1"
            itemName="Examiner report draft.docx"
            resolvedDocumentId="11111111-1111-1111-1111-111111111111"
            documentIdentity={resolvedWithRelatedRecord()}
            canOpenRecord
            onRetryDocumentIdentity={onRetryDocumentIdentity}
            getAccessToken={mockGetAccessToken}
          />
        </TestWrapper>
      );

      await user.click(screen.getByRole('button', { name: /open this document's record/i }));

      act(() => {
        window.dispatchEvent(new Event('focus'));
      });

      expect(onRetryDocumentIdentity).toHaveBeenCalledTimes(1);
    });

    it('re-reads identity again on document visibilitychange back to visible', async () => {
      const user = userEvent.setup();
      const onRetryDocumentIdentity = jest.fn();
      render(
        <TestWrapper>
          <SaveFlow
            hostType="word"
            itemId="word-doc-1"
            itemName="Examiner report draft.docx"
            resolvedDocumentId="11111111-1111-1111-1111-111111111111"
            documentIdentity={resolvedWithRelatedRecord()}
            canOpenRecord
            onRetryDocumentIdentity={onRetryDocumentIdentity}
            getAccessToken={mockGetAccessToken}
          />
        </TestWrapper>
      );

      await user.click(screen.getByRole('button', { name: /open.*gamma merger/i }));

      act(() => {
        document.dispatchEvent(new Event('visibilitychange'));
      });

      expect(onRetryDocumentIdentity).toHaveBeenCalledTimes(1);
    });
  });
});

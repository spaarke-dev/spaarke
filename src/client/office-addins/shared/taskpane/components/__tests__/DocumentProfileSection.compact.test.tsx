/**
 * Task 141 (owner points 5-6): the Profile section in the compact saved view.
 *
 * - "Document type" is hidden for an email (`showDocumentType={false}`): it is a fixed field of the profile, not part of
 *   the AI output text. Kept by default (a Word document).
 * - Generate Profile / Refresh is an icon-only button with a tooltip, beside the "Profile" label, in both modes. Its
 *   accessible name is the label it used to show; busy and disabled states keep working.
 */
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DocumentProfileSection } from '../DocumentProfileSection';
import { apiClient } from '@shared/services';

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockGet = apiClient.get as jest.Mock;
const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111';

const completed = {
  data: {
    summary: 'A request to move the discovery deadline.',
    tldr: 'Deadline moves.',
    keywords: 'discovery, schedule',
    documentType: 'Email',
    summaryStatus: 100000002,
  },
};

const renderSection = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

/** The "Profile" label's row: the label and the action beside it. */
const titleRow = () => screen.getByText('Profile').parentElement as HTMLElement;

beforeEach(() => {
  mockGet.mockReset();
  mockGet.mockResolvedValue(completed);
});

describe('DocumentProfileSection — compact saved view (task 141)', () => {
  it('an email hides the "Document type" row; the rest of the profile shows', async () => {
    renderSection(<DocumentProfileSection documentId={DOCUMENT_ID} showDocumentType={false} />);

    expect(await screen.findByText('Deadline moves.')).toBeInTheDocument();
    expect(screen.queryByText('Document type')).toBeNull();
    expect(screen.queryByText('Email')).toBeNull();
    expect(screen.getByText('A request to move the discovery deadline.')).toBeInTheDocument();
  });

  it('by default (a Word document) the "Document type" row stays', async () => {
    renderSection(<DocumentProfileSection documentId={DOCUMENT_ID} />);

    expect(await screen.findByText('Document type')).toBeInTheDocument();
    expect(screen.getByText('Email')).toBeInTheDocument();
  });

  it('Generate Profile is an icon-only button beside the "Profile" label', async () => {
    renderSection(<DocumentProfileSection documentId={DOCUMENT_ID} />);
    await screen.findByText('Deadline moves.');

    const button = screen.getByRole('button', { name: 'Generate Profile' });
    expect(titleRow()).toContainElement(button);
    expect(button).not.toHaveTextContent('Generate Profile');
    expect(button).toBeEnabled();
  });

  it('Refresh is an icon-only button beside the "Profile" label', async () => {
    renderSection(<DocumentProfileSection documentId={DOCUMENT_ID} mode="refresh" />);
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Refresh' })).not.toHaveAttribute('aria-disabled', 'true')
    );

    const button = screen.getByRole('button', { name: 'Refresh' });
    expect(titleRow()).toContainElement(button);
    expect(button).not.toHaveTextContent('Refresh');
    expect(screen.queryByRole('button', { name: 'Generate Profile' })).toBeNull();
  });

  it('negative: without a document Generate Profile stays disabled, with its reason', () => {
    renderSection(<DocumentProfileSection />);

    expect(screen.getByRole('button', { name: 'Generate Profile' })).toBeDisabled();
    expect(
      screen.getByText('This document is not yet in Spaarke. Save it first to generate a profile.')
    ).toBeInTheDocument();
  });
});

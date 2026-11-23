/**
 * App-level test: ontology task 197 fix round 1 (F2-b) - the per-item email opens the composer in rich text with the record
 * URL as a real link. It used to be plain text (`Open the record: https://...`), which mail clients auto-link; in rich text the
 * URL must be an anchor or it goes out as dead text. Mounts the real `DailyBriefingApp`, opens a document preview and uses its
 * "Email" action, then reads the props the (captured) `SendEmailDialog` received.
 *
 * Classification (ADR-038 section 7): MAINTAIN - the contract of the Email Item action.
 */
import * as React from 'react';
import { act, render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { DailyBriefingApp } from '../src/components/DailyBriefingApp';
import { authenticatedFetch } from '@spaarke/auth';

const DOCUMENT_ID = '22222222-2222-2222-2222-222222222222';
const CLIENT_URL = 'https://org.crm.dynamics.com';

// Captured props of the two dialogs the flow goes through (the shared mock renders neither).
const mockPreviewProps: Array<Record<string, unknown>> = [];
const mockEmailProps: Array<Record<string, unknown>> = [];

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    RichFilePreviewDialog: (props: Record<string, unknown>) => {
      mockPreviewProps.push(props);
      return null;
    },
    SendEmailDialog: (props: Record<string, unknown>) => {
      mockEmailProps.push(props);
      return null;
    },
  };
});

// The documents row that opens the preview is the section's own concern: a stub button stands in for it.
jest.mock('../src/components/ActivityNotesSection', () => {
  const react = require('react') as typeof import('react');
  return {
    ActivityNotesSection: (props: { onPreviewDocument?: (id: string, name: string) => void }) =>
      react.createElement(
        'button',
        {
          onClick: () => props.onPreviewDocument?.('22222222-2222-2222-2222-222222222222', 'Q&A <draft> "terms".docx'),
        },
        'preview document'
      ),
  };
});

class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
(window as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;

function renderResponse(): { json: () => Promise<unknown> } {
  return {
    json: async () => ({
      tldr: { summary: '', keyTakeaways: [], categoryCount: 1, priorityItemCount: 0, itemRefs: [] },
      // A non-empty briefing, so the activity section (stubbed above) renders.
      channelNarratives: [
        {
          category: 'matters',
          bullets: [
            {
              narrative: 'Acme v. Globex',
              itemIds: ['item-1'],
              primaryEntityType: 'sprk_matter',
              primaryEntityId: '11111111-1111-1111-1111-111111111111',
              primaryEntityName: 'Acme v. Globex',
            },
          ],
        },
      ],
      generatedAtUtc: '2026-10-12T08:00:00Z',
    }),
  };
}

beforeEach(() => {
  mockPreviewProps.length = 0;
  mockEmailProps.length = 0;
  (authenticatedFetch as jest.Mock).mockReset();
  (authenticatedFetch as jest.Mock).mockImplementation(() => Promise.resolve(renderResponse()));
  (window as unknown as { Xrm?: unknown }).Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Utility: {
      getGlobalContext: () => ({
        userSettings: { userId: '{00000000-0000-0000-0000-000000000001}' },
        getClientUrl: () => CLIENT_URL,
      }),
    },
    Navigation: {},
  };
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

describe('DailyBriefingApp - Email Item (task 197 fix round 1)', () => {
  it('opens the composer in rich text with the record URL as a real link and every field escaped', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <DailyBriefingApp params={{}} />
      </FluentProvider>
    );
    fireEvent.click(await screen.findByRole('button', { name: 'preview document' }));
    await waitFor(() => expect(mockPreviewProps.length).toBeGreaterThan(0));

    act(() => {
      (mockPreviewProps[mockPreviewProps.length - 1].onEmailDocument as () => void)();
    });

    await waitFor(() => expect(mockEmailProps.length).toBeGreaterThan(0));
    const props = mockEmailProps[mockEmailProps.length - 1];
    expect(props.initialBodyFormat).toBe('HTML');
    const body = props.initialBody as string;
    const url = `${CLIENT_URL}/main.aspx?pagetype=entityrecord&amp;etn=sprk_document&amp;id=${DOCUMENT_ID}`;
    expect(body).toContain(`<a href="${url}">`);
    // The item's text is escaped: shown as the characters it is, never parsed as markup.
    expect(body).toContain('<p>Q&amp;A &lt;draft&gt; &quot;terms&quot;.docx</p>');
    expect(body).not.toContain('<draft>');
  });
});

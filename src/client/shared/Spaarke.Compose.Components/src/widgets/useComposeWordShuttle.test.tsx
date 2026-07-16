/**
 * useComposeWordShuttle.test.tsx — coverage for the Word round-trip shuttle client wiring
 * (task 103, Cluster 3). Verifies the mappers + the three fetch hooks POST the right routes/bodies,
 * and that the "Push to Word" toolbar action (gap 3.1) is rendered + wired.
 */

import * as React from 'react';
import { render, screen, renderHook, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// @spaarke/auth is the fetch boundary — mock useAuth (the hooks call it unconditionally). The
// hooks also accept a fetchOverride escape hatch, used below to assert wire shapes.
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: jest.fn(),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// ComposeToolbar depends on useDocumentActions (Open-in-Word) — mock to no-op handlers.
jest.mock('@spaarke/document-operations', () => ({
  useDocumentActions: () => ({
    openInWeb: jest.fn(),
    openInDesktop: jest.fn(),
    isActing: false,
  }),
}));

import {
  useComposePushAnnotations,
  useComposePullAnnotations,
  useComposeCheckChanges,
  anchoredAnnotationsToDocxAnnotations,
  anchoredAnnotationsToPriorAnchors,
  DocxTrackChangeKind,
} from './useComposeWordShuttle';
import { ComposeToolbar } from './ComposeToolbar';
import type { AnchoredAnnotation } from '../types/compose-contracts';

function anchor(overrides: Partial<AnchoredAnnotation> & Pick<AnchoredAnnotation, 'id' | 'type'>): AnchoredAnnotation {
  return {
    id: overrides.id,
    type: overrides.type,
    anchor: overrides.anchor ?? { textPattern: 'the target clause', paragraphHint: 2, spanId: 's1' },
    body: overrides.body ?? 'a body',
    author: overrides.author ?? 'Alice',
    timestamp: overrides.timestamp ?? '2026-07-10T00:00:00Z',
    source: overrides.source ?? 'human',
  };
}

function okJson(payload: unknown): typeof fetch {
  return jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => payload,
  }) as unknown as typeof fetch;
}

describe('anchoredAnnotationsToDocxAnnotations (gap 3.1 mapping)', () => {
  it('maps each annotation kind to its native track-change kind + fields', () => {
    const result = anchoredAnnotationsToDocxAnnotations([
      anchor({ id: 'c1', type: 'comment', body: 'please revise' }),
      anchor({ id: 'i1', type: 'insertion-suggestion', body: 'inserted text' }),
      anchor({ id: 'd1', type: 'deletion-suggestion' }),
    ]);

    expect(result).toHaveLength(3);
    expect(result[0]).toMatchObject({
      kind: DocxTrackChangeKind.Comment,
      commentText: 'please revise',
      targetText: 'the target clause',
    });
    expect(result[1]).toMatchObject({ kind: DocxTrackChangeKind.Insertion, newText: 'inserted text' });
    expect(result[2]).toMatchObject({ kind: DocxTrackChangeKind.Deletion, targetText: 'the target clause' });
  });

  it('drops annotations missing the fields the server requires (no 400-inducing payloads)', () => {
    const result = anchoredAnnotationsToDocxAnnotations([
      anchor({ id: 'c-empty', type: 'comment', body: '' }), // comment with no body → dropped
      anchor({ id: 'i-empty', type: 'insertion-suggestion', body: '' }), // insertion with no text → dropped
    ]);
    expect(result).toHaveLength(0);
  });

  it('DEF-13: maps an AI edit-REASON comment annotation to a native w:comment push entry anchored to the change', () => {
    // The reason ComposeWorkspace registers on a redline (registerAiEditReasonComment): a Compose
    // AnchoredAnnotation of type 'comment', source 'ai', body = the model rationale, anchored to the
    // edit's target_text. It must map to a Comment DocxAnnotationInput so DocxAnnotationWriter emits a
    // real w:comment on Push/Save.
    const result = anchoredAnnotationsToDocxAnnotations([
      {
        id: 'ai-edit-reason:binding-1@t1',
        type: 'comment',
        anchor: { textPattern: 'the prior clause', paragraphHint: -1, spanId: 'binding-1@t1' },
        body: 'clearer indemnity language',
        author: 'Spaarke Assistant',
        timestamp: '2026-07-12T00:00:00.000Z',
        source: 'ai',
        provenance: { bindingId: 'binding-1', ledgerRef: 'binding-1@t1' },
      },
    ]);

    expect(result).toHaveLength(1);
    expect(result[0]).toMatchObject({
      kind: DocxTrackChangeKind.Comment,
      commentText: 'clearer indemnity language',
      targetText: 'the prior clause',
      author: 'Spaarke Assistant',
    });
  });
});

describe('anchoredAnnotationsToPriorAnchors (gap 3.5 mapping)', () => {
  it('maps anchor textPattern + paragraphHint and truncates the preview', () => {
    const result = anchoredAnnotationsToPriorAnchors([anchor({ id: 'a1', type: 'comment', body: 'x'.repeat(200) })]);
    expect(result).toEqual([
      { id: 'a1', type: 'comment', textPattern: 'the target clause', paragraphHint: 2, preview: 'x'.repeat(160) },
    ]);
  });
});

describe('useComposePushAnnotations (gap 3.1)', () => {
  it('POSTs push-annotations with driveId/tenantId/ifMatch/annotations', async () => {
    const fetchMock = okJson({});
    const { result } = renderHook(() =>
      useComposePushAnnotations({ bffBaseUrl: 'https://bff', fetchOverride: fetchMock })
    );

    await act(async () => {
      await result.current.push({
        documentSpeId: 'spe-1',
        driveId: 'b!drive-1',
        tenantId: 't1',
        ifMatch: '"etag-1"',
        annotations: [
          {
            kind: DocxTrackChangeKind.Comment,
            targetText: 'x',
            commentText: 'y',
            author: 'A',
            date: '2026-07-10T00:00:00Z',
          },
        ],
      });
    });

    expect(fetchMock).toHaveBeenCalledWith(
      'https://bff/api/compose/document/spe-1/push-annotations',
      expect.objectContaining({ method: 'POST' })
    );
    const body = JSON.parse((fetchMock as jest.Mock).mock.calls[0][1].body);
    expect(body).toMatchObject({ driveId: 'b!drive-1', tenantId: 't1', ifMatch: '"etag-1"' });
    expect(body.annotations).toHaveLength(1);
  });
});

describe('useComposeCheckChanges (poll half of gap 3.5)', () => {
  it('POSTs check-changes with the containerId and returns the changed flag', async () => {
    const fetchMock = okJson({
      documentSpeId: 'spe-1',
      containerId: 'b!drive-1',
      changed: true,
      deleted: false,
      correlationId: 'c',
    });
    const { result } = renderHook(() =>
      useComposeCheckChanges({ bffBaseUrl: 'https://bff', fetchOverride: fetchMock })
    );

    let changed = false;
    await act(async () => {
      const res = await result.current.checkChanges({ documentSpeId: 'spe-1', containerId: 'b!drive-1' });
      changed = res.changed;
    });

    expect(changed).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://bff/api/compose/document/spe-1/check-changes',
      expect.objectContaining({ method: 'POST' })
    );
    expect(JSON.parse((fetchMock as jest.Mock).mock.calls[0][1].body)).toEqual({ containerId: 'b!drive-1' });
  });
});

describe('useComposePullAnnotations (gap 3.4)', () => {
  it('POSTs pull-annotations and returns comments + revisions', async () => {
    const fetchMock = okJson({ documentSpeId: 'spe-1', comments: [{}], revisions: [{}, {}], correlationId: 'c' });
    const { result } = renderHook(() =>
      useComposePullAnnotations({ bffBaseUrl: 'https://bff', fetchOverride: fetchMock })
    );

    let count = 0;
    await act(async () => {
      const res = await result.current.pull({ documentSpeId: 'spe-1', driveId: 'b!drive-1', tenantId: 't1' });
      count = res.comments.length + res.revisions.length;
    });

    expect(count).toBe(3);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://bff/api/compose/document/spe-1/pull-annotations',
      expect.objectContaining({ method: 'POST' })
    );
  });
});

describe('ComposeToolbar — Push to Word action (gap 3.1)', () => {
  const renderTb = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

  it('renders the Push to Word button and fires the handler when enabled', async () => {
    const onPush = jest.fn();
    renderTb(
      <ComposeToolbar documentId="spe-1" bffBaseUrl="https://bff" onPushToWordRequested={onPush} canPushToWord />
    );

    const button = screen.getByTestId('compose-toolbar-push-to-word');
    expect(button).toBeEnabled();
    await userEvent.click(button);
    expect(onPush).toHaveBeenCalledTimes(1);
  });

  it('disables the button when there is nothing to push', () => {
    renderTb(
      <ComposeToolbar
        documentId="spe-1"
        bffBaseUrl="https://bff"
        onPushToWordRequested={jest.fn()}
        canPushToWord={false}
      />
    );
    expect(screen.getByTestId('compose-toolbar-push-to-word')).toBeDisabled();
  });

  it('does not render the button when no push handler is provided', () => {
    renderTb(<ComposeToolbar documentId="spe-1" bffBaseUrl="https://bff" />);
    expect(screen.queryByTestId('compose-toolbar-push-to-word')).toBeNull();
  });
});

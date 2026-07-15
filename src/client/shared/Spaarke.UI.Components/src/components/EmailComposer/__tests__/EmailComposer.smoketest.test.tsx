/**
 * EmailComposer.smoketest.test.tsx
 *
 * Acceptance criterion #1 (task 020): "EmailComposer.tsx compiles; renders
 * without runtime error for all 15 mode×mount combinations (smoke-test via a
 * temporary story/inline harness)." This IS that harness — a render-only
 * smoke test, not a behavioral test suite (task 023 owns full unit tests for
 * validation/reducer/derive-state helpers).
 *
 * Per ADR-038 KEEP-path categories, this is a component-render smoke test
 * (not a DI/mock-boundary/ctor test the ADR bans) — asserts the composer
 * mounts + unmounts cleanly across the full mode×mount matrix without
 * throwing, which is exactly the risk task 020's `<EmailComposer />` engine
 * introduces (15 render paths through one shared reducer + layout switch).
 */
import * as React from 'react';
import { cleanup } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type {
  EmailComposerMode,
  EmailComposerMount,
  IEmailComposerProps,
  ISourceCommunicationRecord,
} from '../EmailComposer.types';

const MODES: EmailComposerMode[] = ['compose', 'view', 'reply', 'forward', 'draft'];
const MOUNTS: EmailComposerMount[] = ['inline', 'dialog', 'page'];

const mockAuthenticatedFetch: IEmailComposerProps['authenticatedFetch'] = jest.fn().mockResolvedValue({
  ok: true,
  json: async () => ({ communicationId: 'mock-communication-id' }),
} as Response);

const sourceRecord: ISourceCommunicationRecord = {
  communicationId: 'comm-1',
  from: 'sender@example.com',
  to: ['recipient@example.com'],
  cc: [],
  subject: 'Original subject',
  body: '<p>Original body</p>',
  bodyFormat: 'HTML',
  sentAt: '2026-07-01T10:00:00Z',
  attachments: [{ id: 'att-1', source: 'related', fileName: 'contract.pdf', sizeBytes: 1024, documentId: 'doc-1' }],
  associations: [{ entityType: 'sprk_matter', entityId: 'matter-1', entityName: 'Smith v. Jones' }],
};

function buildProps(mode: EmailComposerMode, mount: EmailComposerMount): IEmailComposerProps {
  const base: IEmailComposerProps = {
    mode,
    mount,
    authenticatedFetch: mockAuthenticatedFetch,
    bffBaseUrl: 'https://bff.example.com',
    onSearchRecipients: jest.fn().mockResolvedValue([]),
  };
  if (mode === 'compose') {
    return { ...base, initialTo: ['a@example.com'], initialSubject: 'Hello', associations: sourceRecord.associations };
  }
  return { ...base, communicationId: sourceRecord.communicationId, sourceRecord };
}

describe('EmailComposer — 15 mode×mount smoke matrix (AC#1)', () => {
  afterEach(() => {
    cleanup();
    jest.clearAllMocks();
  });

  for (const mode of MODES) {
    for (const mount of MOUNTS) {
      it(`renders mode="${mode}" mount="${mount}" without throwing`, () => {
        const composerRef = React.createRef<import('../EmailComposer.types').IEmailComposerHandle>();
        expect(() => {
          renderWithProviders(<EmailComposer ref={composerRef} {...buildProps(mode, mount)} />);
        }).not.toThrow();
        // The imperative handle should always be populated post-render.
        expect(composerRef.current).not.toBeNull();
        expect(typeof composerRef.current?.validate).toBe('function');
        expect(typeof composerRef.current?.send).toBe('function');
        expect(typeof composerRef.current?.saveDraft).toBe('function');
        expect(typeof composerRef.current?.getState).toBe('function');
      });
    }
  }
});

describe('EmailComposer — mount-variant chrome (design §5.10)', () => {
  afterEach(cleanup);

  it('ComposerActionBar is absent on inline mount (wizard frame owns navigation)', () => {
    const { queryByRole } = renderWithProviders(<EmailComposer {...buildProps('compose', 'inline')} />);
    expect(queryByRole('region', { name: 'Composer actions' })).toBeNull();
  });

  it('ComposerActionBar is present on dialog + page mounts', () => {
    const dialogRender = renderWithProviders(<EmailComposer {...buildProps('compose', 'dialog')} />);
    expect(dialogRender.queryByRole('region', { name: 'Composer actions' })).not.toBeNull();
    cleanup();

    const pageRender = renderWithProviders(<EmailComposer {...buildProps('compose', 'page')} />);
    expect(pageRender.queryByRole('region', { name: 'Composer actions' })).not.toBeNull();
  });
});

describe('EmailComposer — no @spaarke/auth dependency (ADR-028)', () => {
  it('the engine module does not import from @spaarke/auth', () => {
    // Static grep-equivalent guard so a future edit can't silently
    // reintroduce a direct @spaarke/auth import (task 020 acceptance
    // criterion: "No @spaarke/auth import anywhere under EmailComposer/**").
    const fs = require('fs');
    const path = require('path');
    const dir = path.resolve(__dirname, '..');
    const walk = (d: string): string[] =>
      fs
        .readdirSync(d, { withFileTypes: true })
        .flatMap((entry: { name: string; isDirectory: () => boolean }) =>
          entry.isDirectory() ? walk(path.join(d, entry.name)) : [path.join(d, entry.name)]
        );
    // Exclude this test file itself — it necessarily contains the literal
    // guarded-against string as the needle being searched for.
    const files = walk(dir).filter(
      f => (f.endsWith('.ts') || f.endsWith('.tsx')) && !f.includes(`__tests__${path.sep}`)
    );
    const forbidden = '@spaarke' + '/auth'; // split to avoid this file self-matching its own guard
    for (const file of files) {
      const content = fs.readFileSync(file, 'utf-8');
      expect(content.includes(`from '${forbidden}'`)).toBe(false);
    }
  });
});

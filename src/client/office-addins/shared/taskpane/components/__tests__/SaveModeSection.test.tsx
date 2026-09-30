/**
 * FR-11 save-mode affordance (spaarkeai-word-add-in-r1 task 024): `resolveSaveMode` decides what Save sends
 * for every document-identity outcome, and `SaveModeSection` renders a way forward for each — no dead ends,
 * and no silent create for an identity that is not known to be new.
 *
 * Plain DOM assertions (`.checked`, `toBeNull`) rather than jest-dom matchers: the matchers are registered at
 * runtime by jest.setup.js, but their TYPES are not in this package's tsconfig, and importing them here would
 * augment the whole program's types as a side effect.
 */
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme, type Theme } from '@fluentui/react-components';
import { SaveModeSection, resolveSaveMode, type SaveModeChoice } from '../SaveModeSection';
import type { DocumentIdentityOutcome, DocumentIdentityState } from '../../services/documentIdentityService';

const DOCUMENT_ID = '8c135b45-5da8-f111-aaab-7ced8ddc4a05';

const RESOLVED: DocumentIdentityOutcome = {
  kind: 'resolved',
  documentId: DOCUMENT_ID,
  documentName: 'Engagement Letter',
  fileName: 'Engagement Letter.docx',
  relatedRecord: null,
};
const NEW: DocumentIdentityOutcome = { kind: 'new', reason: 'not_spaarke_document' };
const CONFLICT: DocumentIdentityOutcome = { kind: 'conflict' };
const UNAVAILABLE: DocumentIdentityOutcome = { kind: 'indeterminate', reason: 'unavailable' };
const SYSTEM_FAILURE: DocumentIdentityOutcome = { kind: 'indeterminate', reason: 'system_failure' };
const DENIED: DocumentIdentityOutcome = { kind: 'denied' };
const ERROR: DocumentIdentityOutcome = { kind: 'error', message: 'network down' };

const CREATE = { mode: 'create' };
const VERSION = { mode: 'version', existingDocumentId: DOCUMENT_ID };

const isChecked = (element: HTMLElement): boolean => (element as HTMLInputElement).checked;

describe('resolveSaveMode (task 024)', () => {
  it.each<[string, DocumentIdentityState | undefined, SaveModeChoice | null, object | null, string]>([
    ['identity not applicable (Outlook)', undefined, null, CREATE, 'none'],
    ['checking', 'checking', null, null, 'checking'],
    ['checking, even with a choice', 'checking', 'new', null, 'checking'],
    ['resolved → defaults to a version', RESOLVED, null, VERSION, 'version'],
    ['resolved + explicit version', RESOLVED, 'version', VERSION, 'version'],
    ['resolved + "a new document" override → create', RESOLVED, 'new', CREATE, 'version'],
    ['new → plain create', NEW, null, CREATE, 'none'],
    ['conflict → nothing, and no save-as-new', CONFLICT, null, null, 'conflict'],
    ['conflict ignores a "new" choice', CONFLICT, 'new', null, 'conflict'],
    ['indeterminate (unavailable) → nothing until chosen', UNAVAILABLE, null, null, 'undetermined'],
    ['indeterminate (unavailable) + explicit new → create', UNAVAILABLE, 'new', CREATE, 'undetermined'],
    ['indeterminate (system failure) → nothing until chosen', SYSTEM_FAILURE, null, null, 'undetermined'],
    ['error → never treated as new', ERROR, null, null, 'undetermined'],
    ['error + explicit new → create', ERROR, 'new', CREATE, 'undetermined'],
    ['denied → nothing until chosen', DENIED, null, null, 'denied'],
    ['denied + explicit new → create', DENIED, 'new', CREATE, 'denied'],
  ])('%s', (_label, identity, choice, expectedTarget, expectedView) => {
    const resolution = resolveSaveMode(identity, choice);
    expect(resolution.view).toBe(expectedView);
    expect(resolution.target).toEqual(expectedTarget);
  });

  it('only a resolved identity ever produces a version target', () => {
    const others: Array<DocumentIdentityState | undefined> = [
      undefined,
      'checking',
      NEW,
      CONFLICT,
      UNAVAILABLE,
      SYSTEM_FAILURE,
      DENIED,
      ERROR,
    ];
    for (const identity of others) {
      for (const choice of [null, 'new', 'version'] as const) {
        expect(resolveSaveMode(identity, choice).target?.mode).not.toBe('version');
      }
    }
  });

  it('labels the resolved document by name, falling back to its file name', () => {
    expect(resolveSaveMode(RESOLVED, null).documentLabel).toBe('Engagement Letter');
    expect(resolveSaveMode({ ...RESOLVED, documentName: '' }, null).documentLabel).toBe('Engagement Letter.docx');
  });
});

/** Stateful host: owns the choice exactly as SaveFlow does, and exposes the resulting target. */
function Harness({
  identity,
  onRetry,
  theme = webLightTheme,
}: {
  identity: DocumentIdentityState | undefined;
  onRetry?: () => void;
  theme?: Theme;
}): React.ReactElement {
  const [choice, setChoice] = React.useState<SaveModeChoice | null>(null);
  const resolution = resolveSaveMode(identity, choice);
  return (
    <FluentProvider theme={theme}>
      <SaveModeSection
        identity={identity}
        resolution={resolution}
        onChoiceChange={setChoice}
        {...(onRetry ? { onRetryIdentity: onRetry } : {})}
      />
      <output data-testid="target">{resolution.target ? JSON.stringify(resolution.target) : 'none'}</output>
    </FluentProvider>
  );
}

const target = () => screen.getByTestId('target').textContent;

describe('SaveModeSection (task 024)', () => {
  it('resolved: states that Save adds a new version of the named document, and offers an explicit override', () => {
    render(<Harness identity={RESOLVED} />);

    const versionRadio = screen.getByRole('radio', { name: 'A new version of “Engagement Letter”' });
    expect(isChecked(versionRadio)).toBe(true);
    expect(screen.getByText(/Save will add a new version to “Engagement Letter” in Spaarke/)).toBeTruthy();
    expect(target()).toBe(JSON.stringify(VERSION));

    fireEvent.click(screen.getByRole('radio', { name: 'A new document' }));

    expect(isChecked(screen.getByRole('radio', { name: 'A new document' }))).toBe(true);
    expect(screen.getByText(/The existing document is not changed/)).toBeTruthy();
    expect(target()).toBe(JSON.stringify(CREATE));

    fireEvent.click(versionRadio);
    expect(target()).toBe(JSON.stringify(VERSION));
  });

  it.each<[string, DocumentIdentityState | undefined]>([
    ['not applicable', undefined],
    ['new', NEW],
  ])('%s: renders no version affordance at all', (_label, identity) => {
    render(<Harness identity={identity} />);
    expect(screen.queryByRole('radio')).toBeNull();
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(target()).toBe(JSON.stringify(CREATE));
  });

  it('checking: says so, and nothing can be saved yet', () => {
    render(<Harness identity="checking" />);
    expect(screen.getByText('Checking whether this document is already in Spaarke…')).toBeTruthy();
    expect(target()).toBe('none');
  });

  it('conflict: explains, offers "Check again", and never offers save-as-new', () => {
    const onRetry = jest.fn();
    render(<Harness identity={CONFLICT} onRetry={onRetry} />);

    expect(screen.getByText('This document can’t be saved from here')).toBeTruthy();
    expect(screen.queryByRole('radio')).toBeNull();
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(target()).toBe('none');

    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it.each<[string, DocumentIdentityOutcome, RegExp]>([
    ['indeterminate', UNAVAILABLE, /the service is unavailable right now/],
    ['error', ERROR, /Something went wrong while checking/],
  ])('%s: offers "Try again" or an explicit "save as new" — never a silent create', (_label, identity, copy) => {
    const onRetry = jest.fn();
    render(<Harness identity={identity} onRetry={onRetry} />);

    expect(screen.getByText(copy)).toBeTruthy();
    expect(target()).toBe('none');

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);

    const choose = screen.getByRole('checkbox', { name: 'Save it as a new document anyway' });
    expect(isChecked(choose)).toBe(false);
    fireEvent.click(choose);
    expect(target()).toBe(JSON.stringify(CREATE));
    fireEvent.click(choose);
    expect(target()).toBe('none');
  });

  it('denied: explains and offers an explicit "save my copy as new"', () => {
    render(<Harness identity={DENIED} />);

    expect(screen.getByText('You can’t add a version to this document')).toBeTruthy();
    expect(target()).toBe('none');
    fireEvent.click(screen.getByRole('checkbox', { name: 'Save my copy as a new document' }));
    expect(target()).toBe(JSON.stringify(CREATE));
  });

  it('renders under the dark theme (ADR-021 — colors come from theme tokens)', () => {
    render(<Harness identity={RESOLVED} theme={webDarkTheme} />);
    expect(isChecked(screen.getByRole('radio', { name: 'A new version of “Engagement Letter”' }))).toBe(true);
  });
});

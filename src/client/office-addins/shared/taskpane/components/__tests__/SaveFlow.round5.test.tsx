/**
 * Task 099 (spaarkeai-word-add-in-r1, owner UAT round 5, items 2 + 5; 2026-10-05) — the Save tab shows only what
 * the current step needs:
 *
 * - item 5: the name is edited in the "Document" header; there is no "Document Details" header or name card;
 * - item 2: while the Related-to picker's "+ New" create form is open, the document name, the Profile and
 *   Cancel / Save are hidden (the picker stays); they come back when the create ends;
 * - focus management (ADR-021): "Save as new document" removes the locked name — focus lands on the now-editable
 *   name control, not on <body>.
 *
 * The Related-to picker and the Profile section are doubled (they have their own suites).
 */
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import type { DocumentIdentityState } from '../../services/documentIdentityService';

jest.mock('../DocumentProfileSection', () => {
  const ReactModule = jest.requireActual('react');
  return { DocumentProfileSection: () => ReactModule.createElement('div', { 'data-testid': 'profile-section' }) };
});
jest.mock('../RelatedToPicker', () => {
  const ReactModule = jest.requireActual('react');
  return {
    RelatedToPicker: (props: { onCreatingChange?: (creating: boolean) => void }) =>
      ReactModule.createElement(
        'div',
        { 'data-testid': 'related-to-picker' },
        ReactModule.createElement(
          'button',
          { type: 'button', onClick: () => props.onCreatingChange?.(true) },
          'stub: open create'
        ),
        ReactModule.createElement(
          'button',
          { type: 'button', onClick: () => props.onCreatingChange?.(false) },
          'stub: close create'
        )
      ),
  };
});
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

function renderWord(props: Partial<React.ComponentProps<typeof SaveFlow>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief.docx"
        canProvideDocumentName
        captureDocumentContent={jest.fn().mockResolvedValue('UEsDBBQ=')}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl=""
        {...props}
      />
    </FluentProvider>
  );
}

describe('SaveFlow — the Document header owns the name (item 5)', () => {
  it('shows a "Document" header with the name and its pencil; there is no "Document Details" section', () => {
    renderWord();

    expect(screen.getByText('Document')).toBeInTheDocument();
    expect(screen.queryByText('Document Details')).not.toBeInTheDocument();
    expect(screen.getByText('Brief')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit document name' })).toBeInTheDocument();
  });

  it('a document with no name shows "Untitled Document" in the header', () => {
    renderWord({ itemName: '' });

    expect(screen.getByText('Untitled Document')).toBeInTheDocument();
  });

  it('editing in the header changes the name the pane holds', () => {
    renderWord();

    fireEvent.click(screen.getByRole('button', { name: 'Edit document name' }));
    const input = screen.getByRole('textbox', { name: 'Document name' });
    fireEvent.change(input, { target: { value: 'Renamed' } });
    fireEvent.keyDown(input, { key: 'Enter' });

    expect(screen.getByText('Renamed')).toBeInTheDocument();
  });
});

describe('SaveFlow — "+ New" shows only the create form (item 2)', () => {
  it('hides the name, Profile and Cancel/Save while the create form is open, and restores them after', () => {
    renderWord();
    expect(screen.getByRole('button', { name: 'Edit document name' })).toBeInTheDocument();
    expect(screen.getByTestId('profile-section')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'stub: open create' }));

    expect(screen.getByTestId('related-to-picker')).toBeInTheDocument();
    expect(screen.queryByText('Document')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit document name' })).not.toBeInTheDocument();
    expect(screen.queryByTestId('profile-section')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'stub: close create' }));

    expect(screen.getByRole('button', { name: 'Edit document name' })).toBeInTheDocument();
    expect(screen.getByTestId('profile-section')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });
});

describe('SaveFlow — focus when the locked name goes away (ADR-021)', () => {
  it('"Save as new document" moves focus to the now-editable name control', () => {
    const resolved: DocumentIdentityState = {
      kind: 'resolved',
      documentId: 'aaaa1111-0000-4000-8000-000000000099',
      documentName: 'Brief',
      fileName: 'Brief.docx',
      relatedRecord: null,
    };
    renderWord({ documentIdentity: resolved, resolvedDocumentId: resolved.documentId });
    expect(screen.queryByRole('button', { name: 'Edit document name' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save as new document' }));

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit document name' }));
  });
});

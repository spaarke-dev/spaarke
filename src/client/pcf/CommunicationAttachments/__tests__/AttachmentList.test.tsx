/**
 * Unit tests for the presentational AttachmentList — rendering (name + type),
 * inline-image expectations (filtered upstream), .eml badge, row-click, and
 * read-only suppression.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AttachmentList } from '../CommunicationAttachments/AttachmentList';
import { IAttachmentItem } from '../CommunicationAttachments/types';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const items: IAttachmentItem[] = [
  { attachmentId: '1', name: 'Report.pdf', attachmentType: 100000000, documentId: 'doc-1', documentName: null },
  { attachmentId: '2', name: 'Thread.eml', attachmentType: 100000000, documentId: 'doc-2', documentName: null },
  { attachmentId: '3', name: 'Sheet.xlsx', attachmentType: 100000000, documentId: 'doc-3', documentName: null },
];

describe('AttachmentList', () => {
  it('renders each attachment name and a type badge', () => {
    renderWithProvider(<AttachmentList items={items} onActivate={jest.fn()} />);
    expect(screen.getByText('Report.pdf')).toBeInTheDocument();
    expect(screen.getByText('Sheet.xlsx')).toBeInTheDocument();
    // Type labels
    expect(screen.getByText('PDF')).toBeInTheDocument();
    expect(screen.getByText('XLSX')).toBeInTheDocument();
  });

  it('badges .eml rows as Email (routed to open/download, not inline preview)', () => {
    renderWithProvider(<AttachmentList items={items} onActivate={jest.fn()} />);
    expect(screen.getByText('Thread.eml')).toBeInTheDocument();
    expect(screen.getByText('Email')).toBeInTheDocument();
  });

  it('fires onActivate with the clicked attachment', () => {
    const onActivate = jest.fn();
    renderWithProvider(<AttachmentList items={items} onActivate={onActivate} />);
    fireEvent.click(screen.getByText('Report.pdf'));
    expect(onActivate).toHaveBeenCalledTimes(1);
    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ attachmentId: '1', documentId: 'doc-1' }));
  });

  it('always renders document-bearing rows as openable (reads are never gated by form lock state)', () => {
    const onActivate = jest.fn();
    // No read-only concept exists on this viewer — every row with a document opens.
    renderWithProvider(<AttachmentList items={items} onActivate={onActivate} />);
    const name = screen.getByText('Report.pdf');
    expect(name.tagName.toLowerCase()).toBe('button');
    fireEvent.click(name);
    expect(onActivate).toHaveBeenCalledTimes(1);
  });

  it('renders a green "uploaded" upload-status icon when the document has an SPE file', () => {
    const uploaded: IAttachmentItem[] = [
      {
        attachmentId: '1',
        name: 'Report.pdf',
        attachmentType: 100000000,
        documentId: 'doc-1',
        documentName: null,
        uploaded: true,
      },
    ];
    renderWithProvider(<AttachmentList items={uploaded} onActivate={jest.fn()} />);
    expect(screen.getByLabelText('File uploaded to SharePoint')).toBeInTheDocument();
    expect(screen.queryByLabelText('File not uploaded to SharePoint')).not.toBeInTheDocument();
  });

  it('renders a red "not uploaded" upload-status icon when the document has no SPE file (default)', () => {
    // `uploaded` omitted → treated as not uploaded.
    renderWithProvider(<AttachmentList items={items} onActivate={jest.fn()} />);
    expect(screen.getAllByLabelText('File not uploaded to SharePoint').length).toBe(items.length);
    expect(screen.queryByLabelText('File uploaded to SharePoint')).not.toBeInTheDocument();
  });

  it('keeps the right-side type pill (PDF/XLSX/Email) alongside the upload-status icon', () => {
    renderWithProvider(<AttachmentList items={items} onActivate={jest.fn()} />);
    // Type pills unchanged by A11-2/A11-3.
    expect(screen.getByText('PDF')).toBeInTheDocument();
    expect(screen.getByText('XLSX')).toBeInTheDocument();
    expect(screen.getByText('Email')).toBeInTheDocument();
  });

  it('does not enable a row whose document lookup is missing', () => {
    const onActivate = jest.fn();
    const orphan: IAttachmentItem[] = [
      { attachmentId: '9', name: 'Orphan.txt', attachmentType: 100000000, documentId: null, documentName: null },
    ];
    renderWithProvider(<AttachmentList items={orphan} onActivate={onActivate} />);
    const name = screen.getByText('Orphan.txt');
    expect(name.tagName.toLowerCase()).not.toBe('button');
  });
});

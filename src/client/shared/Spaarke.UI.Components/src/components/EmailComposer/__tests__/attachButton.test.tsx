/**
 * attachButton.test.tsx - task 197 (owner dev recheck 2026-11-18).
 *
 * The attach menu is a button beside the "Attachments" heading (styled and placed like the "Related to" section's "Link a
 * record" button), present in rich AND plain-text modes, with the same menu items and handlers the toolbar paperclip had.
 * The rich-text toolbar no longer has a paperclip. A host with nothing to attach with shows no button.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import { ATTACHMENT_MAX_FILE_BYTES } from '../EmailComposer.reducer';
import type { IEmailComposerHandle, IEmailComposerProps } from '../EmailComposer.types';

const noopFetch = jest.fn() as unknown as IEmailComposerProps['authenticatedFetch'];

function fileOfSize(name: string, sizeBytes: number, type: string): File {
  const f = new File(['x'], name, { type });
  Object.defineProperty(f, 'size', { value: sizeBytes });
  return f;
}

function renderComposer(overrides: Partial<IEmailComposerProps> = {}) {
  const ref = React.createRef<IEmailComposerHandle>();
  const utils = renderWithProviders(
    <EmailComposer ref={ref} mode="compose" mount="page" authenticatedFetch={noopFetch} {...overrides} />
  );
  return { ref, ...utils };
}

// The composer's section wrapper and the list's own wrapper are both regions named Attachments; the outer one holds both.
const attachments = () => screen.getAllByRole('region', { name: 'Attachments' })[0];
const attachButton = () => within(attachments()).queryByRole('button', { name: 'Attach' });
const toolbar = () => document.querySelector('[role="toolbar"]') as HTMLElement | null;

describe('EmailComposer: the attach button beside the Attachments heading (task 197)', () => {
  it.each([
    ['rich text', 'HTML'],
    ['plain text', 'PlainText'],
  ] as const)('is in the Attachments section in %s mode, next to the heading', (_label, format) => {
    renderComposer({ initialBodyFormat: format, attachmentSources: [{ kind: 'local' }], onAddRelationship: jest.fn() });

    const button = attachButton();
    expect(button).toBeInTheDocument();
    // Same row as the heading toggle (a sibling of it), the way "Link a record" sits beside "Related to".
    const heading = within(attachments()).getByRole('button', { name: /^Attachments/ });
    expect(button!.parentElement).toBe(heading.parentElement);
    // Styled as the "Link a record" tile: the same class.
    const link = screen.getByRole('button', { name: /link a record/i });
    expect(button!.className).toBe(link.className);
  });

  it('is present with the Attachments list collapsed (the default) and does not toggle it', () => {
    renderComposer({ attachmentSources: [{ kind: 'local' }] });
    const heading = within(attachments()).getByRole('button', { name: /^Attachments/ });
    expect(heading).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(attachButton()!);

    expect(heading).toHaveAttribute('aria-expanded', 'false');
  });

  it('opens the same menu items as the old paperclip: Add files and Link documents', async () => {
    renderComposer({
      attachmentSources: [{ kind: 'local' }],
      onLookupRecord: jest.fn().mockResolvedValue(null),
    });

    fireEvent.click(attachButton()!);

    expect(await screen.findByRole('menuitem', { name: 'Add files' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Link documents' })).toBeInTheDocument();
  });

  it('shows only the items the host supports', async () => {
    renderComposer({ attachmentSources: [{ kind: 'local' }] });
    fireEvent.click(attachButton()!);
    expect(await screen.findByRole('menuitem', { name: 'Add files' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Link documents' })).toBeNull();
  });

  it('Add files opens the file picker; a picked file is validated and added, a rejected one is shown and never added', async () => {
    const { ref, container } = renderComposer({
      initialBodyFormat: 'PlainText',
      attachmentSources: [{ kind: 'local' }],
    });
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    const picker = jest.spyOn(input, 'click').mockImplementation(() => undefined);

    fireEvent.click(attachButton()!);
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Add files' }));
    expect(picker).toHaveBeenCalledTimes(1);

    fireEvent.change(input, {
      target: { files: [fileOfSize('big.pdf', ATTACHMENT_MAX_FILE_BYTES + 1, 'application/pdf')] },
    });
    expect(screen.getByRole('alert')).toHaveTextContent(/exceeds the 25 MB per-file limit/);
    expect(ref.current?.getState().attachments ?? []).toHaveLength(0);

    fireEvent.change(input, { target: { files: [fileOfSize('ok.pdf', 1024, 'application/pdf')] } });
    await waitFor(() => expect(ref.current?.getState().attachments ?? []).toHaveLength(1));
    expect(ref.current?.getState().attachments[0]).toMatchObject({ source: 'local', fileName: 'ok.pdf' });
  });

  it('Link documents runs the host document lookup and attaches the pick', async () => {
    const onLookupRecord = jest.fn().mockResolvedValue({
      entityType: 'sprk_document',
      id: 'doc-1',
      name: 'Brief.pdf',
    });
    const { ref } = renderComposer({ onLookupRecord, attachmentSources: [] });

    fireEvent.click(attachButton()!);
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Link documents' }));

    await waitFor(() => expect(onLookupRecord).toHaveBeenCalledWith('sprk_document'));
    await waitFor(() => expect((ref.current?.getState().attachments ?? []).length).toBeGreaterThan(0));
  });

  it('the rich-text toolbar has no paperclip (it has the other tools)', async () => {
    renderComposer({
      initialBodyFormat: 'HTML',
      attachmentSources: [{ kind: 'local' }],
      onLookupRecord: jest.fn(),
      recordLookupCatalog: [{ logicalName: 'sprk_matter', displayName: 'Matter' }],
    });

    const bar = toolbar();
    expect(bar).not.toBeNull();
    expect(within(bar!).queryByRole('button', { name: 'Attach' })).toBeNull();
    expect(within(bar!).queryByRole('button', { name: /attach/i })).toBeNull();
    // The record search stays in the toolbar: only the attach trigger moved.
    expect(within(bar!).getByRole('button', { name: 'Insert record link' })).toBeInTheDocument();
  });

  it('must-not-fire: a host with no attachment sources and no upload or document lookup shows no attach button', () => {
    renderComposer({ attachmentSources: [] });
    expect(attachButton()).toBeNull();
    expect(screen.queryByRole('button', { name: 'Attach' })).toBeNull();
  });

  it('must-not-fire: a read-only composer shows no attach button', () => {
    renderComposer({ attachmentSources: [{ kind: 'local' }], mode: 'view' });
    expect(screen.queryByRole('button', { name: 'Attach' })).toBeNull();
  });
});

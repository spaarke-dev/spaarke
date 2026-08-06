/**
 * templatePicker.test.tsx — Wave E (owner UAT 2026-07-30): compose template picker.
 *
 * The compose toolbar exposes a template button ONLY when the host supplies BOTH
 * `onListEmailTemplates` + `onRenderEmailTemplate`. Opening the menu lists templates; picking
 * one renders it via the host callback — passing the PRIMARY regarding (`associations[0]`) so
 * `{!entity.field}` codes merge server-side — and fills subject + body. When applying would
 * overwrite existing content, an "Apply template?" confirm gates the replace.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { ICommunicationAssociation } from '../../../services/communicationApi';
import type {
  IEmailComposerProps,
  IEmailComposerHandle,
  IEmailTemplateSummary,
  IEmailTemplateRenderResult,
} from '../EmailComposer.types';

const noopFetch = jest.fn();

const TEMPLATES: IEmailTemplateSummary[] = [
  { id: 't-1', name: 'Welcome Letter' },
  { id: 't-2', name: 'Status Update' },
];

const RENDERED: IEmailTemplateRenderResult = {
  subject: 'Welcome, Acme',
  body: '<p>Dear Acme,</p>',
  isHtml: true,
};

function renderComposer(overrides: Partial<IEmailComposerProps>) {
  const ref = React.createRef<IEmailComposerHandle>();
  const utils = renderWithProviders(
    <EmailComposer
      ref={ref}
      mode="compose"
      mount="page"
      authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      {...overrides}
    />
  );
  return { ref, ...utils };
}

describe('EmailComposer — compose template picker (Wave E)', () => {
  const primary: ICommunicationAssociation = {
    entityType: 'sprk_matter',
    entityId: 'm-1',
    entityName: 'Acme Matter',
  };

  it('is hidden unless BOTH list + render callbacks are supplied', () => {
    renderComposer({ onListEmailTemplates: jest.fn().mockResolvedValue(TEMPLATES) });
    expect(screen.queryByRole('button', { name: /apply template/i })).not.toBeInTheDocument();
  });

  it('lists templates on open and applies the picked one — passing the primary regarding', async () => {
    const onListEmailTemplates = jest.fn().mockResolvedValue(TEMPLATES);
    const onRenderEmailTemplate = jest.fn().mockResolvedValue(RENDERED);
    const { ref } = renderComposer({
      associations: [primary],
      onListEmailTemplates,
      onRenderEmailTemplate,
    });

    // Open the template menu → list loads.
    fireEvent.click(screen.getByRole('button', { name: /apply template/i }));
    const item = await screen.findByRole('menuitem', { name: 'Welcome Letter' });
    expect(onListEmailTemplates).toHaveBeenCalledTimes(1);

    // Pick it — empty body/subject → applies directly (no confirm).
    fireEvent.click(item);

    await waitFor(() => {
      expect(onRenderEmailTemplate).toHaveBeenCalledWith({
        templateId: 't-1',
        regardingEntityType: 'sprk_matter',
        regardingRecordId: 'm-1',
      });
    });
    await waitFor(() => {
      expect(ref.current?.getState().subject).toBe('Welcome, Acme');
    });
    expect(ref.current?.getState().body).toContain('Dear Acme');
    expect(ref.current?.getState().bodyFormat).toBe('HTML');
  });

  it('omits the regarding when there is no association', async () => {
    const onRenderEmailTemplate = jest.fn().mockResolvedValue(RENDERED);
    renderComposer({
      associations: [],
      onListEmailTemplates: jest.fn().mockResolvedValue(TEMPLATES),
      onRenderEmailTemplate,
    });

    fireEvent.click(screen.getByRole('button', { name: /apply template/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Status Update' }));

    await waitFor(() => {
      expect(onRenderEmailTemplate).toHaveBeenCalledWith({
        templateId: 't-2',
        regardingEntityType: undefined,
        regardingRecordId: undefined,
      });
    });
  });

  it('prompts a confirm before overwriting an existing subject; cancel keeps it', async () => {
    const onRenderEmailTemplate = jest.fn().mockResolvedValue(RENDERED);
    const { ref } = renderComposer({
      initialSubject: 'Draft in progress',
      onListEmailTemplates: jest.fn().mockResolvedValue(TEMPLATES),
      onRenderEmailTemplate,
    });

    fireEvent.click(screen.getByRole('button', { name: /apply template/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Welcome Letter' }));

    // Existing subject → confirm gate, render NOT yet called.
    await screen.findByText('Apply template?');
    expect(onRenderEmailTemplate).not.toHaveBeenCalled();

    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByText('Apply template?')).not.toBeInTheDocument();
    });
    expect(onRenderEmailTemplate).not.toHaveBeenCalled();
    expect(ref.current?.getState().subject).toBe('Draft in progress'); // unchanged
  });

  it('confirming the overwrite applies the template', async () => {
    const onRenderEmailTemplate = jest.fn().mockResolvedValue(RENDERED);
    const { ref } = renderComposer({
      initialSubject: 'Draft in progress',
      onListEmailTemplates: jest.fn().mockResolvedValue(TEMPLATES),
      onRenderEmailTemplate,
    });

    fireEvent.click(screen.getByRole('button', { name: /apply template/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Welcome Letter' }));
    await screen.findByText('Apply template?');
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Apply' }));

    await waitFor(() => {
      expect(onRenderEmailTemplate).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(ref.current?.getState().subject).toBe('Welcome, Acme');
    });
  });
});

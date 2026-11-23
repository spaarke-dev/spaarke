/**
 * SummarizeFilesDialog.sendEmail.test.tsx - spaarke-ontology-platform-r1 task 182, fix round 2.
 *
 * - The "Include only short summary" toggle rewrites the body as plain text. If the user had switched the composer to
 *   rich text, the old code kept `bodyFormat: 'HTML'`, so the plain summary was read as HTML and its line breaks
 *   collapsed.
 * - Finish sends the composed email through `sendComposedEmail`: To, Bcc and the body format reach the request, with no
 *   association and no archive (a summary is filed to no record).
 *
 * The wizard shell, the follow-on grid and the Send Email step are stubs that hand over their props; the summarize call
 * is substituted. Assertions are on what the dialog gives the step and on the request it sends.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IWizardShellProps } from '../../Wizard/wizardShellTypes';
import type { IFollowOnGridProps } from '../../WizardFollowOns';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { IComposedEmail } from '../../EmailComposer/composeOnlySend';
import type { ISendEmailFollowOnStepProps } from '../../WizardFollowOns/steps/SendEmailFollowOnStep';
import type { IUploadedFile } from '../../FileUpload/fileUploadTypes';
import { SummarizeFilesDialog } from '../SummarizeFilesDialog';

const mockShell: { props?: IWizardShellProps } = {};
jest.mock('../../Wizard/WizardShell', () => {
  const ReactActual = jest.requireActual('react');
  return {
    WizardShell: ReactActual.forwardRef((props: IWizardShellProps, ref: unknown) => {
      mockShell.props = props;
      ReactActual.useImperativeHandle(ref, () => ({ addDynamicStep: () => {}, removeDynamicStep: () => {} }));
      // Run Analysis (auto-runs the summary) and Next Steps.
      return ReactActual.createElement(
        'div',
        null,
        props.steps[1].renderContent({} as never),
        props.steps[2].renderContent({} as never)
      );
    }),
  };
});

const mockStep: { props?: ISendEmailFollowOnStepProps } = {};
jest.mock('../../WizardFollowOns', () => {
  const ReactActual = jest.requireActual('react');
  return {
    SendEmailFollowOnStep: (props: ISendEmailFollowOnStepProps) => {
      mockStep.props = props;
      return ReactActual.createElement('div', null, props.headerContent);
    },
    FollowOnGrid: (props: IFollowOnGridProps) =>
      ReactActual.createElement(
        'div',
        null,
        ReactActual.createElement(
          'button',
          { onClick: () => props.onSelectionChange(['send-email']) },
          'select send-email'
        ),
        props.selected.includes('send-email') ? props.cards.find(c => c.id === 'send-email')!.renderStep() : null
      ),
  };
});

jest.mock('../summarizeService', () => ({
  streamSummarize: jest.fn().mockResolvedValue({
    tldr: 'Short.',
    summary: 'First line of the summary.\nSecond line of the summary.',
    shortSummary: 'Short summary line one.\nShort summary line two.',
  }),
}));

const FILES: IUploadedFile[] = [
  { id: 'f-1', name: 'brief.pdf', sizeBytes: 1, fileType: 'pdf', file: new File(['x'], 'brief.pdf') } as IUploadedFile,
];
jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({ useHandoffFileLeg: () => FILES }));

const BFF = 'https://bff.example.test';

async function openOnSendEmail(authFetch: jest.Mock = jest.fn()) {
  renderWithProviders(
    <SummarizeFilesDialog
      open
      onClose={jest.fn()}
      dataService={{} as IDataService}
      authenticatedFetch={authFetch as never}
      bffBaseUrl={BFF}
    />
  );
  fireEvent.click(screen.getByRole('button', { name: 'select send-email' }));
  // The summary arrived and pre-filled the email.
  await waitFor(() => expect(mockStep.props?.value.body).toContain('First line of the summary.'));
}

const lift = (next: IComposedEmail) => act(() => mockStep.props!.onChange(next));

describe('SummarizeFilesDialog — Send Email (task 182 fix round 2)', () => {
  beforeEach(() => {
    mockShell.props = undefined;
    mockStep.props = undefined;
  });

  it('the short-summary toggle writes a plain-text body even after the user switched to rich text', async () => {
    await openOnSendEmail();
    lift({ ...mockStep.props!.value, body: '<p>An edited body</p>', bodyFormat: 'HTML' });
    expect(mockStep.props!.value.bodyFormat).toBe('HTML');

    fireEvent.click(screen.getByRole('checkbox', { name: 'Include only short summary' }));

    await waitFor(() =>
      expect(mockStep.props!.value.body).toContain('Short summary line one.\nShort summary line two.')
    );
    expect(mockStep.props!.value.bodyFormat).toBe('PlainText');
  });

  it('Finish sends the composed email: To, Bcc, the body format, no association and no archive', async () => {
    const authFetch = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ communicationId: 'c-1' }),
    });
    await openOnSendEmail(authFetch);
    lift({
      ...mockStep.props!.value,
      to: [{ email: 'client@acme.example', resolved: false }],
      bcc: [{ email: 'records@firm.example', resolved: false }],
    });

    await act(async () => {
      await mockShell.props!.onFinish();
    });

    const send = authFetch.mock.calls.find(([url]: [string]) => url.includes('/api/communications/send'));
    expect(send).toBeDefined();
    const body = JSON.parse(send![1].body as string);
    expect(body.to).toEqual(['client@acme.example']);
    expect(body.bcc).toEqual(['records@firm.example']);
    expect(body.bodyFormat).toBe('PlainText');
    expect(body.body).toContain('Second line of the summary.');
    expect(body.archiveToSpe).toBe(false);
    expect(body.associations ?? []).toEqual([]);
  });
});

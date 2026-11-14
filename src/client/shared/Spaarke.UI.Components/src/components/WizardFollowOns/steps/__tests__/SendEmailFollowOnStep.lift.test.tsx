/**
 * SendEmailFollowOnStep.lift.test.tsx - spaarke-ontology-platform-r1 task 182 (review findings on the step's lift logic).
 *
 * The composer is replaced by a probe that records its props, so each test states exactly what the composer reports:
 * - a plain-text pre-fill the rich-text editor re-serialised with its spacing collapsed is not an edit (it stays the
 *   wizard's plain text);
 * - formatting added to the same text IS an edit (lifted as HTML, never dropped);
 * - a local file whose upload had not finished when the step closed is uploaded again when it opens, and a failure
 *   removes it with an error.
 */
import * as React from 'react';
import { act, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import type { EmailComposerState, IEmailComposerProps } from '../../../EmailComposer/EmailComposer.types';
import { EMPTY_COMPOSED_EMAIL } from '../../../EmailComposer/composeOnlySend';
import type { IComposedEmail } from '../../../EmailComposer/composeOnlySend';

const probe: { props: IEmailComposerProps[] } = { props: [] };
jest.mock('../../../EmailComposer/EmailComposer', () => ({
  EmailComposer: (props: IEmailComposerProps) => {
    probe.props.push(props);
    return null;
  },
}));

// Imported after the mock so the step renders the probe.
import { SendEmailFollowOnStep } from '../SendEmailFollowOnStep';

const lastProps = () => probe.props[probe.props.length - 1];

/** Composer state as the composer would report it: the value's fields with `overrides`. */
function reported(value: IComposedEmail, overrides: Partial<EmailComposerState>): EmailComposerState {
  return {
    to: value.to,
    cc: value.cc,
    bcc: value.bcc,
    subject: value.subject,
    body: value.body,
    bodyFormat: value.bodyFormat,
    attachments: value.attachments,
    sendMode: value.sendMode,
    fromMailbox: value.fromMailbox,
    ...overrides,
  } as unknown as EmailComposerState;
}

const PREFILL: IComposedEmail = {
  ...EMPTY_COMPOSED_EMAIL,
  to: [{ email: 'client@acme.example', resolved: false }],
  subject: 'New project: Acme',
  body: 'Name:  Acme   Corp\n  - first item\tand a tab\nHello \nWorld',
  bodyFormat: 'PlainText',
};

/** A wizard-like host: keeps what the step lifts (as every wizard does) and reports it to `onChange`. */
const Host: React.FC<{ initial: IComposedEmail; onChange: jest.Mock; onUpload?: jest.Mock }> = ({
  initial,
  onChange,
  onUpload,
}) => {
  const [value, setValue] = React.useState(initial);
  return (
    <SendEmailFollowOnStep
      value={value}
      onChange={next => {
        setValue(next);
        onChange(next);
      }}
      composeHandlers={onUpload ? { onUploadLocalAttachment: onUpload } : undefined}
    />
  );
};

function render(value: IComposedEmail, onChange = jest.fn(), onUpload?: jest.Mock) {
  renderWithProviders(<Host initial={value} onChange={onChange} onUpload={onUpload} />);
  return onChange;
}

beforeEach(() => {
  probe.props = [];
});

describe('SendEmailFollowOnStep lift rules (task 182 review)', () => {
  it('opens a plain-text pre-fill in rich text', () => {
    render(PREFILL);
    expect(lastProps().initialBodyFormat).toBe('HTML');
    expect(lastProps().initialBody).toContain('<p>');
  });

  it('an untouched pre-fill with repeated, leading or trailing spaces and tabs is not an edit', () => {
    const onChange = render(PREFILL);
    // What the rich-text editor reports after loading the paragraphs: spacing collapsed, its own markup.
    const editorHtml =
      '<p class="p" dir="ltr"><span style="white-space: pre-wrap;">Name: Acme Corp</span><br>' +
      '<span style="white-space: pre-wrap;">- first item and a tab</span><br>' +
      '<span style="white-space: pre-wrap;">Hello</span><br><span style="white-space: pre-wrap;">World</span></p>';
    act(() => lastProps().onStateChange!(reported(PREFILL, { body: editorHtml, bodyFormat: 'HTML' })));
    expect(onChange).not.toHaveBeenCalled();

    // A later change elsewhere keeps the wizard's plain text, spacing intact.
    act(() =>
      lastProps().onStateChange!(reported(PREFILL, { body: editorHtml, bodyFormat: 'HTML', subject: 'Edited' }))
    );
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0]).toEqual(
      expect.objectContaining({ subject: 'Edited', body: PREFILL.body, bodyFormat: 'PlainText' })
    );
  });

  it('formatting added to the same text is an edit: lifted as HTML, never dropped', () => {
    const onChange = render({ ...PREFILL, body: 'Dear Client' });
    const bold = '<p dir="ltr"><b><strong class="bold" style="white-space: pre-wrap;">Dear Client</strong></b></p>';
    act(() =>
      lastProps().onStateChange!(reported({ ...PREFILL, body: 'Dear Client' }, { body: bold, bodyFormat: 'HTML' }))
    );
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0]).toEqual(expect.objectContaining({ body: bold, bodyFormat: 'HTML' }));
  });

  it('a styled span (a colour) on the same text is an edit too', () => {
    const onChange = render({ ...PREFILL, body: 'Dear Client' });
    const coloured = '<p><span style="color: rgb(255, 0, 0); white-space: pre-wrap;">Dear Client</span></p>';
    act(() =>
      lastProps().onStateChange!(reported({ ...PREFILL, body: 'Dear Client' }, { body: coloured, bodyFormat: 'HTML' }))
    );
    expect(onChange.mock.calls[0][0]).toEqual(expect.objectContaining({ body: coloured, bodyFormat: 'HTML' }));
  });
});

describe('SendEmailFollowOnStep: an upload unfinished when the step closed (task 182 review)', () => {
  const file = new File(['x'], 'brief.pdf', { type: 'application/pdf' });
  const STRANDED: IComposedEmail = {
    ...PREFILL,
    attachments: [{ id: 'local:1', source: 'local', fileName: 'brief.pdf', sizeBytes: 1, file, selected: true }],
  };

  it('is uploaded again when the step opens, and the composer restarts with its document', async () => {
    const onUpload = jest.fn().mockResolvedValue({ documentId: 'd-9', driveItemId: 'di-9' });
    const onChange = render(STRANDED, jest.fn(), onUpload);

    await waitFor(() => expect(onChange).toHaveBeenCalled());
    expect(onUpload).toHaveBeenCalledTimes(1);
    expect(onUpload).toHaveBeenCalledWith(file);
    const lifted = onChange.mock.calls[onChange.mock.calls.length - 1][0] as IComposedEmail;
    expect(lifted.attachments).toEqual([
      expect.objectContaining({ id: 'local:1', documentId: 'd-9', driveItemId: 'di-9' }),
    ]);
    await waitFor(() =>
      expect(lastProps().initialAttachments).toEqual([expect.objectContaining({ id: 'local:1', documentId: 'd-9' })])
    );
    expect(onUpload).toHaveBeenCalledTimes(1); // the restart does not upload it a second time
  });

  it('a failed upload removes the file and says so', async () => {
    const onUpload = jest.fn().mockRejectedValue(new Error('quota exceeded'));
    const onChange = render(STRANDED, jest.fn(), onUpload);

    await waitFor(() => expect(onChange).toHaveBeenCalled());
    expect((onChange.mock.calls[0][0] as IComposedEmail).attachments).toEqual([]);
    expect(await screen.findByTestId('attachment-upload-error')).toHaveTextContent(
      'Could not attach brief.pdf: quota exceeded.'
    );
  });

  it('must-not-fire: an uploaded attachment is not uploaded again', async () => {
    const onUpload = jest.fn();
    render(
      { ...STRANDED, attachments: [{ ...STRANDED.attachments[0], documentId: 'd-1', file: undefined }] },
      jest.fn(),
      onUpload
    );
    await act(async () => Promise.resolve());
    expect(onUpload).not.toHaveBeenCalled();
  });
});

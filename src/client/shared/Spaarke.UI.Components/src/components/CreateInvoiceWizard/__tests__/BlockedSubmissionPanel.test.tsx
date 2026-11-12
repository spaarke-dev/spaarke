/**
 * BlockedSubmissionPanel tests (ebilling-r1 task 052; FR-19, D-24, D-73, D-76).
 *
 * The panel, the canonical FormModal and Fluent run for real; only the two server calls are fakes. The server's own answer
 * (`canOverride`, `canReturn`) is the only thing that decides which actions exist: the client reads no role.
 */
import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionError } from '../../InvoiceSubmission';
import { BlockedSubmissionPanel } from '../BlockedSubmissionPanel';
import type { BlockedFinding, BlockedSubmissionView } from '../internalInvoiceSubmission';

const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';

const finding = (over: Partial<BlockedFinding> = {}): BlockedFinding => ({
  index: 0,
  ruleType: 'RateCap',
  policyCode: 'OCG-2026',
  clause: 'Partner rate may not exceed 650 per hour',
  clauseLocation: 'Section 4.2',
  subject: 'Line 3',
  line: 3,
  reasonCode: 'rate_cap_exceeded',
  blocking: true,
  ...over,
});

const heldView = (over: Partial<BlockedSubmissionView> = {}): BlockedSubmissionView => ({
  submissionId: SUBMISSION_ID,
  state: 'Blocked',
  canOverride: true,
  canReturn: true,
  findings: [finding(), finding({ index: 1, clause: 'Block billing', blocking: false })],
  ...over,
});

function mount(view: BlockedSubmissionView | Error, decide?: jest.Mock, onDecided?: jest.Mock) {
  const getBlocked = jest
    .fn()
    .mockImplementation(() => (view instanceof Error ? Promise.reject(view) : Promise.resolve(view)));
  const client = {
    getBlocked,
    decide:
      decide ??
      jest.fn().mockResolvedValue({ submissionId: SUBMISSION_ID, outcome: 'Overridden', alreadyDecided: false }),
  };
  const utils = renderWithProviders(
    <BlockedSubmissionPanel submissionId={SUBMISSION_ID} client={client} onDecided={onDecided} />
  );
  return { ...utils, ...client };
}

const dialog = () => screen.getByRole('dialog');
const reasonBox = () => within(dialog()).getByLabelText(/reason/i);

describe('BlockedSubmissionPanel — findings', () => {
  it('lists the blocking findings with their rule and clause, and counts the others', async () => {
    mount(heldView());
    const list = await screen.findByTestId('blocked-findings');
    expect(within(list).getByText('Partner rate may not exceed 650 per hour')).toBeInTheDocument();
    expect(within(list).getByText(/OCG-2026 · Section 4.2 · Line 3 · line 3/)).toBeInTheDocument();
    expect(within(list).queryByText('Block billing')).not.toBeInTheDocument(); // not blocking: counted, not listed
    expect(screen.getByText(/Why this invoice is held \(1 blocking finding\)/)).toBeInTheDocument();
    expect(screen.getByText(/1 other finding does not hold the invoice/)).toBeInTheDocument();
  });

  it('a finding whose detail is withheld says so and shows no clause', async () => {
    mount(heldView({ findings: [finding({ detailWithheld: true, clause: 'secret clause' })] }));
    expect(await screen.findByText(/Duplicate of an invoice you cannot open/)).toBeInTheDocument();
    expect(screen.queryByText('secret clause')).not.toBeInTheDocument();
  });

  it('a failed read is shown with a retry, and no action is offered', async () => {
    const t = mount(new InvoiceSubmissionError({ statusCode: 503 }));
    expect(await screen.findByTestId('blocked-load-error')).toHaveTextContent(/unavailable/i);
    expect(screen.queryByRole('button', { name: /^override$/i })).not.toBeInTheDocument();
    t.getBlocked.mockResolvedValue(heldView());
    fireEvent.click(screen.getByRole('button', { name: /try again/i }));
    expect(await screen.findByTestId('blocked-findings')).toBeInTheDocument();
  });
});

describe('BlockedSubmissionPanel — which actions exist (FR-19: the server signal, nothing else)', () => {
  it('no Override option without the server signal; Return to firm is still there (D-73)', async () => {
    mount(heldView({ canOverride: false, canReturn: true }));
    await screen.findByTestId('blocked-findings');
    expect(screen.queryByRole('button', { name: /override/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/override/i)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /return to firm/i })).toBeInTheDocument();
  });

  it('both actions with the signal', async () => {
    mount(heldView());
    expect(await screen.findByRole('button', { name: /^override$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /return to firm/i })).toBeInTheDocument();
  });

  it('neither action when the server allows neither, and the panel says so', async () => {
    mount(heldView({ canOverride: false, canReturn: false }));
    expect(await screen.findByText(/You cannot decide this submission/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /override|return/i })).not.toBeInTheDocument();
  });

  it('no action for a submission that is not held (the server says Overridden / Returned / NotBlocked)', async () => {
    for (const state of ['Overridden', 'Returned', 'NotBlocked']) {
      const { unmount } = mount(heldView({ state, canOverride: true, canReturn: true }));
      await screen.findByTestId('blocked-findings');
      expect(screen.queryByRole('button', { name: /^override$|return to firm/i })).not.toBeInTheDocument();
      unmount();
    }
  });

  it('shows who decided and why once the server says Returned or Overridden', async () => {
    const stamp = { recordId: 'r', bySystemUserId: 'u', on: '2026-11-03T10:00:00Z' };
    const { unmount } = mount(
      heldView({
        state: 'Returned',
        canReturn: false,
        canOverride: false,
        return: { ...stamp, reason: 'Wrong matter' },
      })
    );
    expect(await screen.findByTestId('blocked-state-returned')).toHaveTextContent('Reason: Wrong matter');
    unmount();
    mount(
      heldView({
        state: 'Overridden',
        canReturn: false,
        canOverride: false,
        override: { ...stamp, reason: 'Agreed rate' },
      })
    );
    expect(await screen.findByTestId('blocked-state-overridden')).toHaveTextContent('Reason: Agreed rate');
  });
});

describe('BlockedSubmissionPanel — Override', () => {
  it('requires a non-empty reason BEFORE the call: blank (or spaces) cannot be submitted and calls nothing', async () => {
    const t = mount(heldView());
    fireEvent.click(await screen.findByRole('button', { name: /^override$/i }));
    const submit = within(dialog()).getByRole('button', { name: /override and accept/i });
    expect(submit).toBeDisabled();
    fireEvent.change(reasonBox(), { target: { value: '    ' } });
    expect(submit).toBeDisabled();
    fireEvent.click(submit);
    expect(t.decide).not.toHaveBeenCalled();
  });

  it('with a reason, calls the server with the trimmed reason, closes the form and re-reads the submission', async () => {
    const onDecided = jest.fn();
    const decide = jest
      .fn()
      .mockResolvedValue({ submissionId: SUBMISSION_ID, outcome: 'Overridden', alreadyDecided: false });
    const t = mount(heldView(), decide, onDecided);
    fireEvent.click(await screen.findByRole('button', { name: /^override$/i }));
    fireEvent.change(reasonBox(), { target: { value: '  Negotiated rate, approved by the GC  ' } });
    t.getBlocked.mockResolvedValue(heldView({ state: 'Overridden', canOverride: false, canReturn: false }));
    fireEvent.click(within(dialog()).getByRole('button', { name: /override and accept/i }));

    await waitFor(() =>
      expect(decide).toHaveBeenCalledWith('override', SUBMISSION_ID, 'Negotiated rate, approved by the GC')
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(onDecided).toHaveBeenCalledWith('override');
    await waitFor(() => expect(t.getBlocked).toHaveBeenCalledTimes(2));
    expect(await screen.findByTestId('blocked-state-overridden')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^override$/i })).not.toBeInTheDocument();
  });

  it('a 403 from the server is shown in the form and nothing changes: the form stays, onDecided is not called, the submission is not re-read', async () => {
    const onDecided = jest.fn();
    const decide = jest
      .fn()
      .mockRejectedValue(new InvoiceSubmissionError({ statusCode: 403, errorCode: 'ebilling.override.not_permitted' }));
    const t = mount(heldView(), decide, onDecided);
    fireEvent.click(await screen.findByRole('button', { name: /^override$/i }));
    fireEvent.change(reasonBox(), { target: { value: 'because' } });
    fireEvent.click(within(dialog()).getByRole('button', { name: /override and accept/i }));

    expect(await screen.findByTestId('blocked-decision-error')).toHaveTextContent(
      /do not hold the eBilling Override privilege/i
    );
    expect(dialog()).toBeInTheDocument();
    expect(reasonBox()).toHaveValue('because');
    expect(onDecided).not.toHaveBeenCalled();
    expect(t.getBlocked).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('blocked-done')).not.toBeInTheDocument();
  });

  it('Cancel closes the form without a call and forgets the reason', async () => {
    const t = mount(heldView());
    fireEvent.click(await screen.findByRole('button', { name: /^override$/i }));
    fireEvent.change(reasonBox(), { target: { value: 'half typed' } });
    fireEvent.click(within(dialog()).getByRole('button', { name: /cancel/i }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(t.decide).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: /^override$/i }));
    expect(reasonBox()).toHaveValue('');
  });

  it('cannot be sent twice while the call is in flight', async () => {
    let finish: (v: unknown) => void = () => undefined;
    const decide = jest.fn().mockReturnValue(new Promise(r => (finish = r)));
    mount(heldView(), decide);
    fireEvent.click(await screen.findByRole('button', { name: /^override$/i }));
    fireEvent.change(reasonBox(), { target: { value: 'r' } });
    const submit = within(dialog()).getByRole('button', { name: /override and accept/i });
    fireEvent.click(submit);
    await waitFor(() => expect(submit).toBeDisabled());
    fireEvent.click(submit);
    expect(decide).toHaveBeenCalledTimes(1);
    await act(async () => finish({ submissionId: SUBMISSION_ID, outcome: 'Overridden', alreadyDecided: false }));
  });
});

describe('BlockedSubmissionPanel — Return to firm', () => {
  it('requires a reason, then calls the return route; no privilege is involved', async () => {
    const decide = jest
      .fn()
      .mockResolvedValue({ submissionId: SUBMISSION_ID, outcome: 'Returned', alreadyDecided: false });
    const onDecided = jest.fn();
    mount(heldView({ canOverride: false }), decide, onDecided);
    fireEvent.click(await screen.findByRole('button', { name: /return to firm/i }));
    const submit = within(dialog()).getByRole('button', { name: /^return to firm$/i });
    expect(submit).toBeDisabled();
    fireEvent.change(reasonBox(), { target: { value: 'Invoice is for another matter' } });
    fireEvent.click(submit);
    await waitFor(() => expect(decide).toHaveBeenCalledWith('return', SUBMISSION_ID, 'Invoice is for another matter'));
    await waitFor(() => expect(onDecided).toHaveBeenCalledWith('return'));
  });

  it('a refusal (409, no longer held) is shown in the form', async () => {
    const decide = jest
      .fn()
      .mockRejectedValue(
        new InvoiceSubmissionError({ statusCode: 409, errorCode: 'ebilling.blocked.already_overridden' })
      );
    mount(heldView(), decide);
    fireEvent.click(await screen.findByRole('button', { name: /return to firm/i }));
    fireEvent.change(reasonBox(), { target: { value: 'r' } });
    fireEvent.click(within(dialog()).getByRole('button', { name: /^return to firm$/i }));
    expect(await screen.findByTestId('blocked-decision-error')).toHaveTextContent(/already accepted by an override/i);
  });
});

describe('BlockedSubmissionPanel — ADR-021 / ADR-050', () => {
  it('uses theme tokens only: no colour literal in the new UI files', () => {
    const dir = path.resolve(__dirname, '..');
    for (const f of ['BlockedSubmissionPanel.tsx', 'CreateInvoiceWizard.tsx']) {
      const text = fs.readFileSync(path.join(dir, f), 'utf8');
      expect(text).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(/);
    }
  });

  it('the reason prompt is the canonical FormModal, not a hand-built overlay', () => {
    const text = fs.readFileSync(path.resolve(__dirname, '../BlockedSubmissionPanel.tsx'), 'utf8');
    expect(text).toMatch(/import \{ FormModal \} from '..\/SprkModal\/presets\/FormModal'/);
    expect(text).not.toMatch(/position:\s*['"]fixed['"]|createElement\(/);
  });
});

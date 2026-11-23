/**
 * Task 084d-5b review (F1): the frame never lets Finish run a plan that makes no call. Confirm holds Finish off while the plan is empty, so a
 * wizard whose choice was lost cannot report a success that wrote nothing. Every wizard's `canChoose` already needs a non-empty plan; this is
 * the backstop under them.
 */

import '@testing-library/jest-dom';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { WizardFrame } from '../components/wizards/WizardFrame';
import { NO_CHANGE, planAssign, type Plan } from '../components/wizards/plan';
import type { OverridesApi } from '../services/overridesApi';
import type { DryRunApi } from '../services/dryRunApi';
import { button, next } from './wizardHarness';

const notCalled = () => {
  throw new Error('a call was made');
};

function renderFrame(plan: Plan) {
  const api = {
    assignProcedure: jest.fn(notCalled),
    excludeRule: jest.fn(notCalled),
    changeRule: jest.fn(notCalled),
    removalPreview: jest.fn(notCalled),
    removeOverride: jest.fn(notCalled),
    procedureVersions: jest.fn(notCalled),
  } as unknown as OverridesApi;
  const refused = {
    ok: false,
    failure: {
      status: 503,
      errorCode: null,
      title: null,
      detail: null,
      problems: [],
      fieldErrors: [],
      signInFailed: false,
    },
  };
  const dryRunApi = { dryRun: jest.fn(() => Promise.resolve(refused)) } as unknown as DryRunApi;
  render(
    <FluentProvider theme={webLightTheme}>
      <WizardFrame
        title="Frame"
        resetKey="k"
        api={api}
        dryRunApi={dryRunApi}
        query={{ date: '2026-07-01' }}
        core="matters"
        recordId="r1"
        readOnly={false}
        reviewLabel="Procedure"
        reviewStep={<div data-testid="review-step" />}
        modeStep={<div data-testid="mode-step" />}
        canChoose
        plan={plan}
        before="a"
        after="b"
        confirmRows={[['Record', 'x']]}
        finishLabel="Finish now"
        successTitle="Done"
        successBody="Done."
        partialNote="Partial."
        onChanged={jest.fn()}
        onClose={jest.fn()}
      />
    </FluentProvider>
  );
  return api;
}

describe('the frame refuses to finish a plan with no call', () => {
  it('an empty plan reaches Confirm with Finish off, and nothing is sent', async () => {
    const api = renderFrame(NO_CHANGE);
    const d = await screen.findByRole('dialog', { hidden: true });
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    fireEvent.click(next(d));
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    expect(button(d, 'Finish now')).toBeDisabled();
    fireEvent.click(button(d, 'Finish now'));
    expect(screen.queryByTestId('success-body')).not.toBeInTheDocument();
    expect(api.assignProcedure).not.toHaveBeenCalled();
  });

  it('control: a plan with one call reaches Confirm with Finish on', async () => {
    renderFrame(planAssign('p1', 'follow', {}));
    const d = await screen.findByRole('dialog', { hidden: true });
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(next(d)).toBeEnabled()); // the dry run was refused: advisory, Next is open
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    expect(button(d, 'Finish now')).toBeEnabled();
  });
});

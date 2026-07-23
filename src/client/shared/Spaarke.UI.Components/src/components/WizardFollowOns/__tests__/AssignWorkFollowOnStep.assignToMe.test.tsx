/**
 * AssignWorkFollowOnStep — "Assign to me" (Assigned Attorney) tests
 * (spaarkeai-assistant-enhancements-r1 task 014 / FR-A4)
 *
 * `onAssignAttorneyToMe` is optional and purely additive: omitting it hides
 * the button entirely (pre-existing consumers unaffected). When supplied,
 * clicking it resolves the current user and applies the result via
 * `onAttorneyChange` — the component itself never touches identity/Dataverse
 * (pure controlled form, per the file's own header doc).
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

import { AssignWorkFollowOnStep, WORK_ASSIGNMENT_PRIORITY } from '../steps/AssignWorkFollowOnStep';
import type { IAssignWorkFollowOnStepProps } from '../steps/AssignWorkFollowOnStep';

function baseProps(overrides?: Partial<IAssignWorkFollowOnStepProps>): IAssignWorkFollowOnStepProps {
  return {
    nameValue: '',
    onNameChange: jest.fn(),
    descriptionValue: '',
    onDescriptionChange: jest.fn(),
    matterTypeValue: null,
    onMatterTypeChange: jest.fn(),
    onSearchMatterTypes: jest.fn(async () => []),
    practiceAreaValue: null,
    onPracticeAreaChange: jest.fn(),
    onSearchPracticeAreas: jest.fn(async () => []),
    priorityValue: WORK_ASSIGNMENT_PRIORITY.Normal,
    onPriorityChange: jest.fn(),
    responseDueDateValue: '',
    onResponseDueDateChange: jest.fn(),
    attorneyValue: null,
    onAttorneyChange: jest.fn(),
    onSearchAttorneys: jest.fn(async () => []),
    paralegalValue: null,
    onParalegalChange: jest.fn(),
    onSearchParalegals: jest.fn(async () => []),
    outsideCounselValue: null,
    onOutsideCounselChange: jest.fn(),
    onSearchOutsideCounsel: jest.fn(async () => []),
    ...overrides,
  };
}

function renderStep(props: IAssignWorkFollowOnStepProps, theme = webLightTheme) {
  render(
    <FluentProvider theme={theme}>
      <AssignWorkFollowOnStep {...props} />
    </FluentProvider>
  );
}

describe('AssignWorkFollowOnStep — Assign to me', () => {
  it('hides the "Assign to me" button when onAssignAttorneyToMe is not supplied (default, backward compatible)', () => {
    renderStep(baseProps());
    expect(screen.queryByRole('button', { name: 'Assign attorney to me' })).not.toBeInTheDocument();
  });

  it('renders the button when onAssignAttorneyToMe is supplied, and applies the resolved attorney on click', async () => {
    const onAttorneyChange = jest.fn();
    const onAssignAttorneyToMe = jest.fn().mockResolvedValue({ id: 'contact-guid-1', name: 'Jane Attorney' });
    renderStep(baseProps({ onAttorneyChange, onAssignAttorneyToMe }));

    const button = screen.getByRole('button', { name: 'Assign attorney to me' });
    fireEvent.click(button);

    await waitFor(() => {
      expect(onAttorneyChange).toHaveBeenCalledWith({ id: 'contact-guid-1', name: 'Jane Attorney' });
    });
  });

  it('does not call onAttorneyChange when resolution fails (null) — graceful no-op', async () => {
    const onAttorneyChange = jest.fn();
    const onAssignAttorneyToMe = jest.fn().mockResolvedValue(null);
    renderStep(baseProps({ onAttorneyChange, onAssignAttorneyToMe }));

    fireEvent.click(screen.getByRole('button', { name: 'Assign attorney to me' }));

    await waitFor(() => {
      expect(onAssignAttorneyToMe).toHaveBeenCalled();
    });
    expect(onAttorneyChange).not.toHaveBeenCalled();
  });

  it('renders without error in dark theme (ADR-021)', () => {
    renderStep(baseProps({ onAssignAttorneyToMe: jest.fn().mockResolvedValue(null) }), webDarkTheme);
    expect(screen.getByRole('button', { name: 'Assign attorney to me' })).toBeInTheDocument();
  });
});

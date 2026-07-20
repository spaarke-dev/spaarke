/**
 * MyAssistantDialog.test.tsx — task 042 questionnaire UI (FR-F3 / FR-E1), UAT 2026-07-19 MA cluster.
 *
 * Component tests (ADR-038): rendering, cold-start banner, chip-based submit payload (MA-4), work-office
 * dropdown (MA-3), removed "Clear my profile" affordance (MA-2), and dark-mode render. No DI/ctor/null
 * tests; no HttpMessageHandler mocks (frontend component).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  type Theme,
} from '@fluentui/react-components';

import { MyAssistantDialog, type MyAssistantDialogProps } from '../MyAssistantDialog';
import {
  encodeChipSelection,
  PREFERENCE_CHIPS,
  FOCUS_AREA_CHIPS,
  type PracticeArea,
  type WorkOffice,
} from '../userProfileService';

const PRACTICE_AREAS: PracticeArea[] = [
  { id: 'pa-1', name: 'Appellate', code: 'APPL' },
  { id: 'pa-2', name: 'Mergers & Acquisitions', code: 'MA' },
];

const WORK_OFFICES: WorkOffice[] = [
  { id: 'wo-chi', name: 'Chicago' },
  { id: 'wo-ny', name: 'New York' },
];

const CONCISE_PHRASE = PREFERENCE_CHIPS.find((c) => c.id === 'concise')!.phrase;
const LITIGATION_PHRASE = FOCUS_AREA_CHIPS.find((c) => c.id === 'litigation')!.phrase;

beforeAll(() => {
  // Fluent v9 Dropdown/MessageBar reflow need ResizeObserver, which jsdom lacks.
  if (typeof globalThis.ResizeObserver === 'undefined') {
    class ResizeObserverStub {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    }
    (globalThis as { ResizeObserver?: unknown }).ResizeObserver = ResizeObserverStub;
  }
});

function renderDialog(overrides: Partial<MyAssistantDialogProps> = {}, theme: Theme = webLightTheme) {
  const onSubmit = overrides.onSubmit ?? jest.fn().mockResolvedValue(undefined);
  const onClose = overrides.onClose ?? jest.fn();
  const props: MyAssistantDialogProps = {
    open: true,
    onClose,
    coldStart: false,
    practiceAreas: PRACTICE_AREAS,
    workOffices: WORK_OFFICES,
    initialValues: {},
    onSubmit,
    loading: false,
    ...overrides,
  };
  const utils = render(
    <FluentProvider theme={theme}>
      <MyAssistantDialog {...props} />
    </FluentProvider>
  );
  return { ...utils, onSubmit, onClose };
}

describe('MyAssistantDialog', () => {
  // The questionnaire is a 3-step wizard — advance through steps via "Next".
  const goNext = async (user: ReturnType<typeof userEvent.setup>) =>
    user.click(screen.getByTestId('my-assistant-next'));

  it('renders the wizard fields across its three steps (via Next)', async () => {
    const user = userEvent.setup();
    renderDialog();
    // Step 1 — role + primary work location (now a dropdown).
    expect(screen.getByTestId('my-assistant-dialog')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-role')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-office')).toBeInTheDocument();
    // Step 2 — practice areas + focus-area chips (MA-4).
    await goNext(user);
    expect(screen.getByTestId('my-assistant-practice-areas')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-focus-chips')).toBeInTheDocument();
    // Step 3 — preference chips + Save.
    await goNext(user);
    expect(screen.getByTestId('my-assistant-preferences-chips')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-save')).toBeInTheDocument();
  });

  it('is not rendered when closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByTestId('my-assistant-dialog')).not.toBeInTheDocument();
  });

  it('shows the cold-start banner on first run', () => {
    renderDialog({ coldStart: true });
    expect(screen.getByTestId('my-assistant-coldstart')).toBeInTheDocument();
  });

  it('does not show the cold-start banner once complete', () => {
    renderDialog({ coldStart: false });
    expect(screen.queryByTestId('my-assistant-coldstart')).not.toBeInTheDocument();
  });

  it('submits the prefilled values unchanged — chips + office round-trip (MA-3/MA-4)', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn().mockResolvedValue(undefined);
    const focusEncoded = encodeChipSelection(['ma'], FOCUS_AREA_CHIPS);
    const prefEncoded = encodeChipSelection(['concise'], PREFERENCE_CHIPS);
    const { onClose } = renderDialog({
      onSubmit,
      initialValues: {
        primaryRole: 100000001,
        practiceAreaIds: ['pa-2'],
        focusAreas: focusEncoded,
        officeLocation: 'Chicago',
        assistantPreferences: prefEncoded,
      },
    });

    // Save lives on the final step — advance through the wizard first.
    await goNext(user);
    await goNext(user);
    await user.click(screen.getByTestId('my-assistant-save'));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit).toHaveBeenCalledWith({
      primaryRole: 100000001,
      practiceAreaIds: ['pa-2'],
      focusAreas: focusEncoded,
      officeLocation: 'Chicago',
      assistantPreferences: prefEncoded,
    });
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('toggling a preference chip flows its directive phrase into the submit payload (MA-4)', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn().mockResolvedValue(undefined);
    renderDialog({ onSubmit, initialValues: {} });

    // Preferences chips are on step 3.
    await goNext(user);
    await goNext(user);
    await user.click(screen.getByTestId('my-assistant-preferences-concise'));
    await user.click(screen.getByTestId('my-assistant-save'));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].assistantPreferences).toBe(CONCISE_PHRASE);
  });

  it('toggling a focus chip flows its phrase into the submit payload (MA-4)', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn().mockResolvedValue(undefined);
    renderDialog({ onSubmit, initialValues: {} });

    // Focus chips are on step 2.
    await goNext(user);
    await user.click(screen.getByTestId('my-assistant-focus-litigation'));
    await goNext(user);
    await user.click(screen.getByTestId('my-assistant-save'));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].focusAreas).toBe(LITIGATION_PHRASE);
  });

  it('renders the primary work location as a combobox (MA-3), not a free-text input', () => {
    renderDialog();
    const office = screen.getByTestId('my-assistant-office');
    // Fluent v9 Dropdown renders a combobox role (the old field was a textbox Input).
    expect(office.getAttribute('role')).toBe('combobox');
  });

  it('no longer renders the "Clear my profile" erase affordance (MA-2)', async () => {
    const user = userEvent.setup();
    renderDialog();
    await goNext(user);
    await goNext(user);
    expect(screen.queryByTestId('my-assistant-erase')).not.toBeInTheDocument();
  });

  it('surfaces an inline error when the save rejects (does not close)', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn().mockRejectedValue(new Error('Dataverse 500'));
    const { onClose } = renderDialog({ onSubmit });
    await goNext(user);
    await goNext(user);
    await user.click(screen.getByTestId('my-assistant-save'));
    await waitFor(() => expect(screen.getByTestId('my-assistant-error')).toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();
  });

  it('renders under the dark theme (ADR-021 semantic tokens adapt)', async () => {
    const user = userEvent.setup();
    renderDialog({ coldStart: true }, webDarkTheme);
    expect(screen.getByTestId('my-assistant-dialog')).toBeInTheDocument();
    await goNext(user);
    await goNext(user);
    expect(screen.getByTestId('my-assistant-save')).toBeInTheDocument();
  });
});

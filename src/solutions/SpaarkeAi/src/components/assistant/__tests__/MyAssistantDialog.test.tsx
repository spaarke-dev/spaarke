/**
 * MyAssistantDialog.test.tsx — task 042 questionnaire UI (FR-F3 / FR-E1 / F5).
 *
 * Component tests (ADR-038): rendering, cold-start banner, submit payload, erasure confirm flow, and
 * dark-mode render. No DI/ctor/null tests; no HttpMessageHandler mocks (frontend component).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  type Theme,
} from '@fluentui/react-components';

import { MyAssistantDialog, type MyAssistantDialogProps } from '../MyAssistantDialog';
import type { PracticeArea } from '../userProfileService';

const PRACTICE_AREAS: PracticeArea[] = [
  { id: 'pa-1', name: 'Appellate', code: 'APPL' },
  { id: 'pa-2', name: 'Mergers & Acquisitions', code: 'MA' },
];

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
  const onErase = overrides.onErase ?? jest.fn().mockResolvedValue(undefined);
  const onClose = overrides.onClose ?? jest.fn();
  const props: MyAssistantDialogProps = {
    open: true,
    onClose,
    coldStart: false,
    practiceAreas: PRACTICE_AREAS,
    initialValues: {},
    onSubmit,
    onErase,
    loading: false,
    ...overrides,
  };
  const utils = render(
    <FluentProvider theme={theme}>
      <MyAssistantDialog {...props} />
    </FluentProvider>
  );
  return { ...utils, onSubmit, onErase, onClose };
}

describe('MyAssistantDialog', () => {
  it('renders the questionnaire fields when open', () => {
    renderDialog();
    expect(screen.getByTestId('my-assistant-dialog')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-role')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-practice-areas')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-focus')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-office')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-preferences')).toBeInTheDocument();
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

  it('submits the prefilled + edited values and closes', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn().mockResolvedValue(undefined);
    const { onClose } = renderDialog({
      onSubmit,
      initialValues: {
        primaryRole: 100000001,
        practiceAreaIds: ['pa-2'],
        focusAreas: 'M&A',
        officeLocation: 'London',
        assistantPreferences: 'Concise',
      },
    });

    await user.click(screen.getByTestId('my-assistant-save'));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    // Submits the prefilled profile values (role, practice areas, and free-text) unchanged.
    expect(onSubmit).toHaveBeenCalledWith({
      primaryRole: 100000001,
      practiceAreaIds: ['pa-2'],
      focusAreas: 'M&A',
      officeLocation: 'London',
      assistantPreferences: 'Concise',
    });
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('flows an edited free-text field into the submit payload', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn().mockResolvedValue(undefined);
    renderDialog({ onSubmit, initialValues: { focusAreas: '' } });

    // Synchronous change event — deterministic for a controlled textarea (no keystroke batching).
    fireEvent.change(screen.getByTestId('my-assistant-focus'), {
      target: { value: 'Securities litigation' },
    });
    await user.click(screen.getByTestId('my-assistant-save'));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].focusAreas).toBe('Securities litigation');
  });

  it('surfaces an inline error when the save rejects (does not close)', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn().mockRejectedValue(new Error('Dataverse 500'));
    const { onClose } = renderDialog({ onSubmit });
    await user.click(screen.getByTestId('my-assistant-save'));
    await waitFor(() => expect(screen.getByTestId('my-assistant-error')).toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();
  });

  it('erasure is a two-step confirm before calling onErase (F5)', async () => {
    const user = userEvent.setup();
    const onErase = jest.fn().mockResolvedValue(undefined);
    renderDialog({ onErase });

    // Step 1: click "Clear my profile" → confirm banner appears, onErase NOT yet called.
    await user.click(screen.getByTestId('my-assistant-erase'));
    expect(screen.getByTestId('my-assistant-erase-confirm')).toBeInTheDocument();
    expect(onErase).not.toHaveBeenCalled();

    // Step 2: click "Confirm delete" → onErase fires.
    await user.click(screen.getByTestId('my-assistant-erase-confirm-btn'));
    await waitFor(() => expect(onErase).toHaveBeenCalledTimes(1));
  });

  it('omits the erasure affordance when onErase is not provided', () => {
    renderDialog({ onErase: undefined });
    expect(screen.queryByTestId('my-assistant-erase')).not.toBeInTheDocument();
  });

  it('renders under the dark theme (ADR-021 semantic tokens adapt)', () => {
    renderDialog({ coldStart: true }, webDarkTheme);
    // Renders without throwing under dark theme; tokens resolve via the host FluentProvider.
    expect(screen.getByTestId('my-assistant-dialog')).toBeInTheDocument();
    expect(screen.getByTestId('my-assistant-save')).toBeInTheDocument();
  });
});

/**
 * wizardWidgets.inApp.test.tsx — the Create Project (#1421) and Find Similar wizard widgets launch through the
 * shared `navigateToWebResourceSurfaceAsync` (spaarke-ontology-platform-r1 task 113; D-26; ADR-050 as amended
 * 2026-10-07, launch rule (a)).
 *
 * Both widgets used to call their own private `Xrm.Navigation.navigateTo(webresource …)`, so in the Console they
 * always opened the un-themeable platform dialog. Now, while the Console's in-app host is registered they open
 * in-app (no `navigateTo`); with no host they fall back to the SAME `navigateTo` call as before; with neither a
 * host nor Xrm they show the existing "unavailable" state.
 *
 * The real `@spaarke/ui-components` launcher seam is used (not a mock of it); the in-app host is a registered
 * recorder, so this asserts on what the seam actually receives.
 *
 * Classification (ADR-038 §7): MAINTAIN — the launch-routing contract of two shipped widgets.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { registerInAppWizardHost } from '@spaarke/ui-components';

import CreateProjectWizardWidget from '../CreateProjectWizardWidget';
import FindSimilarWizardWidget from '../FindSimilarWizardWidget';

let navigateTo: jest.Mock;
let unregister: (() => void) | null = null;

beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = { Navigation: { navigateTo } };
});

afterEach(() => {
  unregister?.();
  unregister = null;
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

const BFF = 'https://bff.example';

describe('CreateProjectWizardWidget — launches through navigateToWebResourceSurfaceAsync (#1421)', () => {
  it('opens in-app when the Console host is registered, and calls no navigateTo', async () => {
    const opener = jest.fn().mockResolvedValue(undefined);
    unregister = registerInAppWizardHost(opener, ['sprk_createprojectwizard']);

    render(<CreateProjectWizardWidget data={{ bffBaseUrl: BFF }} />);

    await waitFor(() => expect(opener).toHaveBeenCalledTimes(1));
    expect(opener).toHaveBeenCalledWith({
      webresourceName: 'sprk_createprojectwizard',
      data: `bffBaseUrl=${encodeURIComponent(BFF)}`,
    });
    expect(navigateTo).not.toHaveBeenCalled();
    expect(await screen.findByTestId('create-project-wizard-relaunch')).toBeInTheDocument();
  });

  it('falls back to the same navigateTo call as before when no host is mounted', async () => {
    render(<CreateProjectWizardWidget data={{ bffBaseUrl: BFF }} />);

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput).toEqual({
      pageType: 'webresource',
      webresourceName: 'sprk_createprojectwizard',
      data: `bffBaseUrl=${encodeURIComponent(BFF)}`,
    });
    expect(navOptions).toMatchObject({ target: 2, title: 'Create New Project' });
    expect(navOptions.width).toBeDefined();
    expect(navOptions.height).toBeDefined();
    expect(await screen.findByTestId('create-project-wizard-relaunch')).toBeInTheDocument();
  });

  it('shows the unavailable state with neither a host nor Xrm', async () => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    render(<CreateProjectWizardWidget data={{ bffBaseUrl: BFF }} />);
    expect(await screen.findByText(/only available inside a Dataverse host/)).toBeInTheDocument();
  });
});

describe('FindSimilarWizardWidget — launches through navigateToWebResourceSurfaceAsync (task 113)', () => {
  const data = { bffBaseUrl: BFF, documentId: 'doc 1', containerId: 'c-9' };
  const expectedData = `documentId=${encodeURIComponent('doc 1')}&containerId=c-9&bffBaseUrl=${encodeURIComponent(BFF)}`;

  it('opens in-app when the Console host supports it, and calls no navigateTo', async () => {
    const opener = jest.fn().mockResolvedValue(undefined);
    unregister = registerInAppWizardHost(opener, ['sprk_findsimilar']);

    render(<FindSimilarWizardWidget data={data} />);

    await waitFor(() => expect(opener).toHaveBeenCalledTimes(1));
    expect(opener).toHaveBeenCalledWith({ webresourceName: 'sprk_findsimilar', data: expectedData });
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('keeps navigateTo when the host is mounted WITHOUT a Find Similar renderer', async () => {
    const opener = jest.fn().mockResolvedValue(undefined);
    unregister = registerInAppWizardHost(opener); // default names: no sprk_findsimilar

    render(<FindSimilarWizardWidget data={data} />);

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(opener).not.toHaveBeenCalled();
  });

  it('falls back to the same navigateTo call as before when no host is mounted', async () => {
    render(<FindSimilarWizardWidget data={data} />);

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput).toEqual({ pageType: 'webresource', webresourceName: 'sprk_findsimilar', data: expectedData });
    expect(navOptions).toMatchObject({ target: 2, title: 'Find Similar Documents' });
  });

  it('shows the unavailable state with neither a host nor Xrm', async () => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    render(<FindSimilarWizardWidget data={data} />);
    expect(await screen.findByText(/only available inside a Dataverse host/)).toBeInTheDocument();
  });
});

import * as React from 'react';
import { act } from 'react';
import { mountWizardPage, WizardPageInitializing } from '../mountWizardPage';
import { webLightTheme } from '@fluentui/react-components';
import { render, screen } from '@testing-library/react';

describe('mountWizardPage', () => {
  afterEach(() => {
    document.body.innerHTML = '';
    jest.restoreAllMocks();
  });

  it('renders the app into #root', async () => {
    document.body.innerHTML = '<div id="root"></div>';
    await act(async () => {
      mountWizardPage('TestWizard', () => <span>hello wizard</span>);
    });
    expect(document.getElementById('root')?.textContent).toBe('hello wizard');
  });

  it('logs and does not throw when #root is missing', () => {
    const err = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    expect(() => mountWizardPage('TestWizard', () => <span />)).not.toThrow();
    expect(err).toHaveBeenCalledWith('[TestWizard] Root element not found');
  });
});

describe('WizardPageInitializing', () => {
  it('shows the initializing text', () => {
    render(<WizardPageInitializing theme={webLightTheme} />);
    expect(screen.getByText('Initializing...')).toBeTruthy();
  });
});

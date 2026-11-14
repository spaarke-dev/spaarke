import { render, screen } from '@testing-library/react';
import { webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { App } from '../App';
import { DISCONNECTED, McpHostContext, type McpHostConnection } from '../mcp/mcpHostContext';

function renderWith(connection: Partial<McpHostConnection>) {
  const value: McpHostConnection = { ...DISCONNECTED, ...connection };
  const view = render(
    <McpHostContext.Provider value={value}>
      <App />
    </McpHostContext.Provider>
  );
  const update = (next: Partial<McpHostConnection>) =>
    view.rerender(
      <McpHostContext.Provider value={{ ...value, ...next }}>
        <App />
      </McpHostContext.Provider>
    );
  return { ...view, update };
}

/** The theme provider element, and the value of one Fluent colour variable as the browser resolves it for that element. */
const providerRoot = () => document.querySelector('[data-theme]') as HTMLElement;
const backgroundOf = (el: HTMLElement) => getComputedStyle(el).getPropertyValue('--colorNeutralBackground1');

describe('App theme follows the host context', () => {
  let setItem: jest.SpyInstance; // jsdom local and session storage share Storage.prototype

  beforeEach(() => {
    setItem = jest.spyOn(Storage.prototype, 'setItem');
  });

  afterEach(() => {
    setItem.mockRestore();
  });

  it('renders with the dark theme when the host context is dark, and the light theme when it is light', () => {
    const view = renderWith({ isConnected: true, hostContext: { theme: 'dark' } });

    expect(providerRoot().dataset.theme).toBe('dark');
    expect(backgroundOf(providerRoot())).toBe(webDarkTheme.colorNeutralBackground1);

    view.update({ hostContext: { theme: 'light' } });

    expect(providerRoot().dataset.theme).toBe('light');
    expect(backgroundOf(providerRoot())).toBe(webLightTheme.colorNeutralBackground1);
  });

  it('switches theme when the host context changes, without a remount', () => {
    const view = renderWith({ isConnected: true, hostContext: { theme: 'light' } });
    const main = screen.getByRole('main');

    view.update({ hostContext: { theme: 'dark' } });

    expect(screen.getByRole('main')).toBe(main);
    expect(providerRoot().dataset.theme).toBe('dark');
    expect(backgroundOf(providerRoot())).toBe(webDarkTheme.colorNeutralBackground1);
  });

  it('writes nothing to localStorage or sessionStorage for any theme', () => {
    const view = renderWith({ isConnected: true, hostContext: { theme: 'dark' } });
    view.update({ hostContext: { theme: 'light' } });

    expect(setItem).not.toHaveBeenCalled();
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });
});

describe('App shell states', () => {
  it('shows the connecting indicator before the handshake', () => {
    renderWith({ isConnected: false });
    expect(screen.getByText('Connecting to the host')).toBeInTheDocument();
  });

  it('shows the placeholder once connected', () => {
    renderWith({ isConnected: true, hostContext: { theme: 'light' } });
    expect(screen.getByRole('heading', { name: 'Decide' })).toBeInTheDocument();
    expect(screen.getByText(/need your decision/)).toBeInTheDocument();
  });

  it('tells the user when the connection failed', () => {
    renderWith({ error: new Error('timeout') });
    expect(screen.getByRole('alert')).toHaveTextContent('could not connect');
  });
});

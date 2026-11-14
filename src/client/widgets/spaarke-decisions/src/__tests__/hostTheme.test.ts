import { webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { hostThemeName, themeFromHostContext } from '../theme/hostTheme';

describe('hostTheme', () => {
  it('is dark only when the host context says dark', () => {
    expect(themeFromHostContext({ theme: 'dark' })).toBe(webDarkTheme);
    expect(hostThemeName({ theme: 'dark' })).toBe('dark');
  });

  it.each([
    ['light', { theme: 'light' }],
    ['no context yet', undefined],
    ['null context', null],
    ['an empty context', {}],
    ['an unknown value', { theme: 'high-contrast' }],
  ])('is light for %s', (_label, context) => {
    expect(themeFromHostContext(context)).toBe(webLightTheme);
    expect(hostThemeName(context)).toBe('light');
  });
});

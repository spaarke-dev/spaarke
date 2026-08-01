/**
 * scaledTheme.test.ts — the `--sprk-ui-scale` scaled Fluent theme (spec FR-06 /
 * design §6.9). Verifies px-valued size/spacing/stroke/font tokens multiply while
 * colors stay byte-identical, the scale===1 short-circuit, dark-mode parity, and
 * that the base theme is never mutated.
 */
import { webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { scaleTheme, baseTheme } from '../scaledTheme';

describe('scaleTheme (FR-06 / design §6.9)', () => {
  it('multiplies px-valued size/spacing/stroke/font/radius tokens by scale (2dp)', () => {
    const scaled = scaleTheme(webLightTheme, 1.5);
    const expectPxScaled = (key: keyof typeof webLightTheme) => {
      const base = parseFloat(webLightTheme[key] as string);
      const want = `${Math.round(base * 1.5 * 100) / 100}px`;
      expect(scaled[key as keyof typeof scaled]).toBe(want);
    };
    expectPxScaled('spacingHorizontalM');
    expectPxScaled('fontSizeBase300');
    expectPxScaled('strokeWidthThin');
    expectPxScaled('borderRadiusMedium');
  });

  it('leaves color tokens byte-identical', () => {
    const scaled = scaleTheme(webLightTheme, 1.5);
    expect(scaled.colorNeutralForeground1).toBe(webLightTheme.colorNeutralForeground1);
    expect(scaled.colorBrandBackground).toBe(webLightTheme.colorBrandBackground);
    expect(scaled.colorNeutralBackground1).toBe(webLightTheme.colorNeutralBackground1);
  });

  it('short-circuits to the base object identity when scale === 1', () => {
    expect(scaleTheme(webLightTheme, 1)).toBe(webLightTheme);
  });

  it('preserves the dark palette while scaling sizes (ADR-021 parity)', () => {
    const scaled = scaleTheme(webDarkTheme, 1.25);
    expect(scaled.colorNeutralForeground1).toBe(webDarkTheme.colorNeutralForeground1);
    expect(scaled.spacingHorizontalM).toBe(
      `${Math.round(parseFloat(webDarkTheme.spacingHorizontalM) * 1.25 * 100) / 100}px`,
    );
  });

  it('does not mutate the base theme', () => {
    const before = webLightTheme.spacingHorizontalM;
    scaleTheme(webLightTheme, 2);
    expect(webLightTheme.spacingHorizontalM).toBe(before);
  });
});

describe('baseTheme', () => {
  it('maps dark → webDarkTheme, light → webLightTheme', () => {
    expect(baseTheme(true)).toBe(webDarkTheme);
    expect(baseTheme(false)).toBe(webLightTheme);
  });
});

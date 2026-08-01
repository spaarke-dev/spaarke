/**
 * sizes.test.ts — the SprkModal size scale (spec FR-02 / design §6.2). Verifies the
 * owner-locked caps (md 1040/720, lg 1280/880), the pre-multiplied px scaling, and
 * that landscape aspect holds on tall monitors (no square md/lg).
 */
import { SIZE_SPEC, SIZE_ORDER, getSurfaceStyle, widthLabel } from '../sizes';
import type { SprkModalSize } from '../sizes';

describe('SprkModal size scale (FR-02 / design §6.2)', () => {
  it('defines all 7 named sizes with the owner-locked caps', () => {
    const sizes: SprkModalSize[] = ['xs', 'sm', 'md', 'lg', 'xl', 'full', 'wizard'];
    for (const s of sizes) expect(SIZE_SPEC[s]).toBeDefined();
    expect(SIZE_SPEC.md.cap).toBe(1040);
    expect(SIZE_SPEC.md.heightMax).toBe(720);
    expect(SIZE_SPEC.lg.cap).toBe(1280);
    expect(SIZE_SPEC.lg.heightMax).toBe(880);
    expect(SIZE_ORDER).toHaveLength(7);
  });

  describe('getSurfaceStyle — width/height math', () => {
    it('md at scale 1 → min(1040px, 92vw) × min(72vh, 720px)', () => {
      expect(getSurfaceStyle('md', 1)).toEqual({
        width: 'min(1040px, 92vw)',
        maxWidth: '96vw',
        height: 'min(72vh, 720px)',
        maxHeight: '92vh',
      });
    });

    it('md at scale 1.5 → px caps multiply (1560 width / 1080 height)', () => {
      const style = getSurfaceStyle('md', 1.5);
      expect(style.width).toBe('min(1560px, 92vw)');
      expect(style.height).toBe('min(72vh, 1080px)');
    });

    it('md at scale 1.25 → 1300px width cap', () => {
      expect(getSurfaceStyle('md', 1.25).width).toBe('min(1300px, 92vw)');
    });

    it('lg holds its 880px height cap (× scale) and 1280/94vw width', () => {
      expect(getSurfaceStyle('lg', 1).width).toBe('min(1280px, 94vw)');
      expect(getSurfaceStyle('lg', 1).height).toBe('min(85vh, 880px)');
      expect(getSurfaceStyle('lg', 1.5).height).toBe('min(85vh, 1320px)');
    });

    it('xl is viewport-relative and uncapped (92vw × 88vh), unaffected by scale', () => {
      const style = getSurfaceStyle('xl', 1);
      expect(style.width).toBe('92vw');
      expect(style.height).toBe('88vh');
      expect(style.maxWidth).toBe('96vw');
      expect(style.maxHeight).toBe('92vh');
      expect(getSurfaceStyle('xl', 1.5).width).toBe('92vw');
    });

    it('full is 100vw × 100vh with 100v* max', () => {
      expect(getSurfaceStyle('full', 1)).toEqual({
        width: '100vw',
        maxWidth: '100vw',
        height: '100vh',
        maxHeight: '100vh',
      });
    });

    it('xs/sm are portrait with content/auto height (no height cap)', () => {
      expect(getSurfaceStyle('xs', 1).width).toBe('min(480px, 92vw)');
      expect(getSurfaceStyle('xs', 1).height).toBeUndefined();
      expect(getSurfaceStyle('sm', 1).width).toBe('min(560px, 92vw)');
      expect(getSurfaceStyle('sm', 1).height).toBeUndefined();
    });

    it('defaults uiScale to 1 when omitted', () => {
      expect(getSurfaceStyle('md')).toEqual(getSurfaceStyle('md', 1));
    });
  });

  describe('widthLabel', () => {
    it('renders a W × H label with resolved caps', () => {
      expect(widthLabel('md', 1)).toBe('min(1040px, 92vw) × min(72vh, 720px)');
      expect(widthLabel('xs', 1)).toBe('min(480px, 92vw) × auto');
      expect(widthLabel('xl', 1)).toBe('92vw × 88vh');
    });
  });
});

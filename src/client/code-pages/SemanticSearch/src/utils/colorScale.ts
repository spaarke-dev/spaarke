/**
 * colorScale — Shared Fluent v9 token-based categorical color palette
 *
 * Provides consistent, dark-mode-safe color assignment for category-based
 * visualizations (Map, Treemap, Timeline views).
 *
 * Palette extracted from ClusterNode.tsx for reuse across all views.
 */

import { tokens } from "@fluentui/react-components";

// =============================================
// Palette — 8 Fluent v9 semantic color pairs
// =============================================

const PALETTE_BACKGROUNDS = [
    tokens.colorPaletteBerryBackground2,
    tokens.colorPaletteTealBackground2,
    tokens.colorPaletteMarigoldBackground2,
    tokens.colorPaletteLavenderBackground2,
    tokens.colorPalettePeachBackground2,
    tokens.colorPaletteSteelBackground2,
    tokens.colorPalettePinkBackground2,
    tokens.colorPaletteForestBackground2,
] as const;

const PALETTE_FOREGROUNDS = [
    tokens.colorPaletteBerryForeground2,
    tokens.colorPaletteTealForeground2,
    tokens.colorPaletteMarigoldForeground2,
    tokens.colorPaletteLavenderForeground2,
    tokens.colorPalettePeachForeground2,
    tokens.colorPaletteSteelForeground2,
    tokens.colorPalettePinkForeground2,
    tokens.colorPaletteForestForeground2,
] as const;

export const PALETTE_SIZE = PALETTE_BACKGROUNDS.length;

/** Deterministic hash of a string to a palette index. */
function hashToIndex(key: string): number {
    const hash = key.split("").reduce((acc, ch) => acc + ch.charCodeAt(0), 0);
    return Math.abs(hash) % PALETTE_SIZE;
}

/**
 * Get a background + foreground color pair for a category key.
 * The mapping is deterministic — the same key always returns the same colors.
 */
export function getCategoryColor(categoryKey: string): {
    background: string;
    foreground: string;
} {
    const index = hashToIndex(categoryKey);
    return {
        background: PALETTE_BACKGROUNDS[index],
        foreground: PALETTE_FOREGROUNDS[index],
    };
}

/**
 * Get palette index for a category key (useful for d3 color scales).
 */
export function getCategoryIndex(categoryKey: string): number {
    return hashToIndex(categoryKey);
}

/**
 * Build a lookup map from unique category keys to their color pairs.
 * Useful for rendering color legends.
 */
export function buildColorLegend(
    categoryKeys: string[]
): { key: string; background: string; foreground: string }[] {
    const unique = [...new Set(categoryKeys)];
    return unique.map((key) => ({
        key,
        ...getCategoryColor(key),
    }));
}

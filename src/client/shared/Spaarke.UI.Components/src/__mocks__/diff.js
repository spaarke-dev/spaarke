/**
 * Manual mock for the 'diff' npm package (jsdiff).
 *
 * Provides diffWords and diffChars implementations sufficient
 * for unit testing diffUtils.ts and DiffCompareView.tsx.
 *
 * Uses a simple word/char-level comparison algorithm that produces
 * the same Change[] output format as the real jsdiff library.
 */

/**
 * Split text into word tokens (preserving whitespace in-between).
 * Returns alternating [word, space, word, space, ...] tokens.
 */
function tokenizeWords(text) {
    const tokens = [];
    const regex = /(\S+|\s+)/g;
    let match;
    while ((match = regex.exec(text)) !== null) {
        tokens.push(match[0]);
    }
    return tokens;
}

/**
 * Compute word-level diff between two strings.
 * Returns an array of Change objects compatible with jsdiff.
 */
function diffWords(oldStr, newStr) {
    if (oldStr === newStr) {
        if (!oldStr) return [];
        return [{ value: oldStr, added: false, removed: false, count: 1 }];
    }

    const oldTokens = tokenizeWords(oldStr);
    const newTokens = tokenizeWords(newStr);

    // Simple LCS-based diff on word tokens
    const changes = computeTokenDiff(oldTokens, newTokens);
    return changes;
}

/**
 * Compute character-level diff between two strings.
 * Returns an array of Change objects compatible with jsdiff.
 */
function diffChars(oldStr, newStr) {
    if (oldStr === newStr) {
        if (!oldStr) return [];
        return [{ value: oldStr, added: false, removed: false, count: oldStr.length }];
    }

    const oldChars = oldStr.split('');
    const newChars = newStr.split('');
    return computeTokenDiff(oldChars, newChars);
}

/**
 * Simple LCS-based diff on arrays of tokens.
 */
function computeTokenDiff(oldTokens, newTokens) {
    const m = oldTokens.length;
    const n = newTokens.length;

    // Build LCS table
    const dp = Array.from({ length: m + 1 }, () => Array(n + 1).fill(0));
    for (let i = 1; i <= m; i++) {
        for (let j = 1; j <= n; j++) {
            if (oldTokens[i - 1] === newTokens[j - 1]) {
                dp[i][j] = dp[i - 1][j - 1] + 1;
            } else {
                dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
            }
        }
    }

    // Backtrack to produce changes
    const changes = [];
    let i = m, j = n;

    // We'll build changes in reverse, then reverse at the end
    const rawChanges = [];

    while (i > 0 || j > 0) {
        if (i > 0 && j > 0 && oldTokens[i - 1] === newTokens[j - 1]) {
            rawChanges.push({ value: oldTokens[i - 1], added: false, removed: false });
            i--;
            j--;
        } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
            rawChanges.push({ value: newTokens[j - 1], added: true, removed: false });
            j--;
        } else {
            rawChanges.push({ value: oldTokens[i - 1], added: false, removed: true });
            i--;
        }
    }

    rawChanges.reverse();

    // Merge consecutive changes of the same type
    for (const change of rawChanges) {
        const last = changes[changes.length - 1];
        if (last && last.added === change.added && last.removed === change.removed) {
            last.value += change.value;
            last.count = (last.count || 1) + 1;
        } else {
            changes.push({ ...change, count: 1 });
        }
    }

    return changes;
}

module.exports = { diffWords, diffChars };

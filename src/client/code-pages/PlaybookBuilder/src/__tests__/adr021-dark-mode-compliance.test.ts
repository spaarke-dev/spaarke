/**
 * ADR-021 Dark-Mode Semantic-Token Compliance — Wave 8 surface scan.
 *
 * R7 Wave 8 task 089c (FR-21..FR-27 + ADR-021 binding).
 *
 * ADR-021 mandates that ALL color references in styled components go through
 * Fluent v9 semantic tokens (`tokens.colorXxx*` from `@fluentui/react-components`)
 * so that the dark/light-mode toggle adapts the UI automatically. Hardcoded
 * hex (`#fff`, `#FFB900`), `rgb(...)`, `rgba(...)`, and CSS named colors
 * (`red`, `gray`, etc.) in style contexts BREAK dark mode and are forbidden.
 *
 * This test is the canonical static gate that enforces ADR-021 on the 5 Wave 8
 * UI surfaces. It complements (and substitutes for, on every test-run, the
 * one-time browser-based dark/light screenshot capture described in 089c POML).
 * Static scan + commit-time gate beats one-time visual check.
 *
 * SCOPE: 5 files touched by Wave 8 tasks 080-088:
 *   - NodePalette.tsx (FR-22 categorized executor palette)
 *   - nodes/UnknownNode.tsx (FR-27 unknown-executor warning state)
 *   - properties/ExecutorTypeSelector.tsx (Action tab executor picker)
 *   - properties/NodePropertiesDialog.tsx (FR-24 Action tab promotion)
 *   - properties/TypedConfigForm.tsx (FR-23 schema-driven typed-config renderer)
 *
 * METHODOLOGY: Mock-free, no DOM, no React render. Pure regex scan of file
 * contents. Excludes comments (// and /* * /), JSDoc, HTML entities (&#NNNN;),
 * and string literals that are clearly not CSS color values (URLs, font names,
 * test IDs). ADR-038 KEEP path — this is a maintenance test, not scaffolding:
 * dark-mode regressions are user-facing and not caught by build/typecheck.
 *
 * EXCEPTIONS:
 *   - Comments may contain hex (e.g., BaseNode.tsx documents legacy hex→token
 *     mapping); only NON-comment lines are scanned.
 *   - HTML entities (&#8593;) match the hex regex but are Unicode codepoints,
 *     not colors; filtered by detection rule.
 *   - String literals that are NOT inside a color-property context are NOT
 *     a CSS color (e.g., `name: 'blue'` for a category label is fine; only
 *     `color: 'blue'` / `backgroundColor: 'red'` / etc. are flagged).
 *
 * @see docs/adr/ADR-021-dark-mode-semantic-tokens.md
 * @see docs/adr/ADR-038-testing-strategy.md
 * @see projects/spaarke-ai-platform-unification-r7/tasks/089c-ui-test-adr021-dark-mode-compliance.poml
 */

import * as fs from 'fs';
import * as path from 'path';

// Repo root resolved relative to this test file:
// src/client/code-pages/PlaybookBuilder/src/__tests__/this-file → ../../../../../..
const REPO_ROOT = path.resolve(__dirname, '..', '..', '..', '..', '..', '..');
const PB_ROOT = path.join(
  REPO_ROOT,
  'src',
  'client',
  'code-pages',
  'PlaybookBuilder',
  'src',
  'components'
);

/**
 * Wave 8 touched files (relative to src/client/code-pages/PlaybookBuilder/src/components).
 * Sourced from git log over Wave 8 task PRs.
 */
const WAVE_8_FILES: readonly string[] = [
  'NodePalette.tsx',
  path.join('nodes', 'UnknownNode.tsx'),
  path.join('properties', 'ExecutorTypeSelector.tsx'),
  path.join('properties', 'NodePropertiesDialog.tsx'),
  path.join('properties', 'TypedConfigForm.tsx'),
];

/**
 * CSS color properties that, when followed by a string literal containing a
 * named color, indicate a hardcoded color violation. Used to filter out
 * benign occurrences of color names (e.g., a UI label that happens to be
 * "Blue Theme").
 */
const COLOR_PROPERTY_NAMES: readonly string[] = [
  'color',
  'backgroundColor',
  'background',
  'borderColor',
  'border',
  'borderTopColor',
  'borderRightColor',
  'borderBottomColor',
  'borderLeftColor',
  'outlineColor',
  'fill',
  'stroke',
  'boxShadow',
  'textShadow',
  'caretColor',
];

const CSS_NAMED_COLORS: readonly string[] = [
  'orange',
  'yellow',
  'red',
  'green',
  'blue',
  'gray',
  'grey',
  'black',
  'white',
  'purple',
  'pink',
  'cyan',
  'magenta',
  'brown',
];

interface Finding {
  file: string;
  lineNumber: number;
  line: string;
  pattern: string;
  matched: string;
}

/**
 * Strip block comments and single-line comments from a TSX source string so
 * grep-style scans don't false-positive on documentation hex codes.
 *
 * Note: this is a deliberately simple stripper (not a full lexer) — it does
 * NOT handle hex inside string literals that contain `//` or `/* * /`. For
 * the Wave 8 surface this is sufficient because:
 *   - JSX attribute strings rarely contain `//` (URLs are typed as full URLs)
 *   - Color literals never contain `//` or `/*`
 */
function stripComments(source: string): string {
  // Remove /* ... * / block comments (greedy across lines)
  let stripped = source.replace(/\/\*[\s\S]*?\*\//g, (match) => {
    // Preserve newlines so line numbers in remaining content stay stable
    return match.replace(/[^\n]/g, ' ');
  });
  // Remove // line comments (only after preserving block-comment newlines)
  stripped = stripped.replace(/(^|[^:])\/\/[^\n]*/g, (_match, prefix) => prefix);
  return stripped;
}

/**
 * Decide whether a hex-like token is actually a CSS color or a Unicode entity.
 * HTML entities of the form `&#NNNN;` or `&#xHHHH;` are NOT colors.
 */
function isHtmlEntityContext(line: string, matchIndex: number): boolean {
  // matchIndex points to the `#` character of the hex regex match. An HTML
  // entity has the shape `&#NNNN;` or `&#xHHHH;`, i.e. `&` IMMEDIATELY before
  // the `#`. Inspect the 1-2 chars preceding `#`.
  if (matchIndex === 0) return false;
  if (line[matchIndex - 1] === '&') return true; // &#NNNN; (decimal entity)
  // Hex entities (&#xHHHH;) have shape `&` then `#` then `x` — the `#` here
  // also has `&` immediately before it, so the case above already catches them.
  return false;
}

function scanFileForHardcodedColors(filePath: string): Finding[] {
  const findings: Finding[] = [];
  const raw = fs.readFileSync(filePath, 'utf8');
  const stripped = stripComments(raw);
  const lines = stripped.split(/\r?\n/);

  // Pattern A: hex colors (3-, 4-, 6-, or 8-digit) — `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`.
  // Bounded by char-class to avoid matching e.g. `#region`.
  const hexPattern = /#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b/g;
  // Pattern B: rgb()/rgba() function calls.
  const rgbPattern = /\brgba?\s*\(/g;

  lines.forEach((line, idx) => {
    // --- Hex pattern (with HTML-entity filter) ---
    let m: RegExpExecArray | null;
    while ((m = hexPattern.exec(line)) !== null) {
      if (isHtmlEntityContext(line, m.index)) {
        continue; // &#NNNN; — Unicode codepoint, not a CSS color
      }
      findings.push({
        file: filePath,
        lineNumber: idx + 1,
        line: line.trim(),
        pattern: 'hex color',
        matched: m[0],
      });
    }
    hexPattern.lastIndex = 0;

    // --- rgb()/rgba() pattern ---
    while ((m = rgbPattern.exec(line)) !== null) {
      findings.push({
        file: filePath,
        lineNumber: idx + 1,
        line: line.trim(),
        pattern: 'rgb()/rgba()',
        matched: m[0],
      });
    }
    rgbPattern.lastIndex = 0;

    // --- Named-color pattern (scoped to CSS color properties) ---
    for (const prop of COLOR_PROPERTY_NAMES) {
      for (const namedColor of CSS_NAMED_COLORS) {
        // Match e.g. `color: 'red'` / `backgroundColor: "blue"` / `fill:\`gray\``
        const propPattern = new RegExp(
          `\\b${prop}\\s*:\\s*['"\`]${namedColor}['"\`]`,
          'i'
        );
        if (propPattern.test(line)) {
          findings.push({
            file: filePath,
            lineNumber: idx + 1,
            line: line.trim(),
            pattern: `named CSS color in style context (${prop})`,
            matched: namedColor,
          });
        }
      }
    }
  });

  return findings;
}

describe('ADR-021 dark-mode semantic-token compliance — Wave 8 PlaybookBuilder UI', () => {
  // Pre-resolve absolute file paths so failure messages are clear.
  const absoluteFilePaths = WAVE_8_FILES.map((rel) => path.join(PB_ROOT, rel));

  it('all 5 Wave 8 component files exist on disk (sanity)', () => {
    for (const abs of absoluteFilePaths) {
      expect(fs.existsSync(abs)).toBe(true);
    }
  });

  it('NodePalette.tsx uses ONLY semantic tokens (no hardcoded color)', () => {
    const findings = scanFileForHardcodedColors(absoluteFilePaths[0]);
    expect(findings).toEqual([]);
  });

  it('UnknownNode.tsx uses ONLY semantic tokens (no hardcoded color in warning state)', () => {
    const findings = scanFileForHardcodedColors(absoluteFilePaths[1]);
    expect(findings).toEqual([]);
  });

  it('ExecutorTypeSelector.tsx uses ONLY semantic tokens (Action tab picker)', () => {
    const findings = scanFileForHardcodedColors(absoluteFilePaths[2]);
    expect(findings).toEqual([]);
  });

  it('NodePropertiesDialog.tsx uses ONLY semantic tokens (Action tab structure)', () => {
    const findings = scanFileForHardcodedColors(absoluteFilePaths[3]);
    expect(findings).toEqual([]);
  });

  it('TypedConfigForm.tsx uses ONLY semantic tokens (schema-driven renderer)', () => {
    const findings = scanFileForHardcodedColors(absoluteFilePaths[4]);
    expect(findings).toEqual([]);
  });

  it('Wave 8 surface in aggregate: zero hardcoded color findings across all 5 files', () => {
    const allFindings: Finding[] = [];
    for (const abs of absoluteFilePaths) {
      allFindings.push(...scanFileForHardcodedColors(abs));
    }
    if (allFindings.length > 0) {
      // Build a human-readable report so a regression is debuggable from CI logs alone.
      const report = allFindings
        .map(
          (f) =>
            `  ${path.relative(REPO_ROOT, f.file)}:${f.lineNumber}  [${f.pattern}] ${f.matched}\n      ${f.line}`
        )
        .join('\n');
      throw new Error(
        `ADR-021 violation — ${allFindings.length} hardcoded color reference(s) in Wave 8 UI surface:\n${report}\n\n` +
          `Fix: replace each hardcoded value with the corresponding semantic token from ` +
          `\`tokens.colorXxx*\` (\`@fluentui/react-components\`). See docs/adr/ADR-021-dark-mode-semantic-tokens.md.`
      );
    }
    expect(allFindings).toEqual([]);
  });

  // --- Self-test: prove the scanner can actually detect violations ---
  // If this fails, the tests above are vacuous green and ADR-021 enforcement is broken.
  describe('scanner self-test (negative assertion)', () => {
    it('detects a hex color in a non-comment line', () => {
      const tmpFile = path.join(__dirname, '__adr021-self-test-hex.tsx');
      fs.writeFileSync(
        tmpFile,
        `import * as React from 'react';\nconst style = { color: '#FF0000' };\n`,
        'utf8'
      );
      try {
        const findings = scanFileForHardcodedColors(tmpFile);
        expect(findings.length).toBeGreaterThanOrEqual(1);
        expect(findings[0].pattern).toBe('hex color');
        expect(findings[0].matched).toBe('#FF0000');
      } finally {
        fs.unlinkSync(tmpFile);
      }
    });

    it('detects rgb()/rgba() function calls', () => {
      const tmpFile = path.join(__dirname, '__adr021-self-test-rgb.tsx');
      fs.writeFileSync(
        tmpFile,
        `const style = { backgroundColor: 'rgba(255, 0, 0, 0.5)' };\n`,
        'utf8'
      );
      try {
        const findings = scanFileForHardcodedColors(tmpFile);
        expect(findings.length).toBeGreaterThanOrEqual(1);
        expect(findings[0].pattern).toBe('rgb()/rgba()');
      } finally {
        fs.unlinkSync(tmpFile);
      }
    });

    it('detects named CSS color in style-property context', () => {
      const tmpFile = path.join(__dirname, '__adr021-self-test-named.tsx');
      fs.writeFileSync(
        tmpFile,
        `const style = { color: 'red', backgroundColor: "blue" };\n`,
        'utf8'
      );
      try {
        const findings = scanFileForHardcodedColors(tmpFile);
        expect(findings.length).toBeGreaterThanOrEqual(2);
        const patterns = findings.map((f) => f.pattern).join(',');
        expect(patterns).toMatch(/named CSS color/);
      } finally {
        fs.unlinkSync(tmpFile);
      }
    });

    it('IGNORES hex inside line and block comments', () => {
      const tmpFile = path.join(__dirname, '__adr021-self-test-comment.tsx');
      fs.writeFileSync(
        tmpFile,
        `// Legacy palette: #FF0000 = red, #00FF00 = green\n/* maps to tokens.colorXxx */\nconst x = 1;\n`,
        'utf8'
      );
      try {
        const findings = scanFileForHardcodedColors(tmpFile);
        expect(findings).toEqual([]);
      } finally {
        fs.unlinkSync(tmpFile);
      }
    });

    it('IGNORES HTML entities like &#8593; that look like hex tokens', () => {
      const tmpFile = path.join(__dirname, '__adr021-self-test-entity.tsx');
      fs.writeFileSync(
        tmpFile,
        `const arrow = <span>&#8593; up &#8595; down</span>;\n`,
        'utf8'
      );
      try {
        const findings = scanFileForHardcodedColors(tmpFile);
        // &#8593; and &#8595; have 4-digit decimal payloads that the bare hex
        // regex could match against the leading '8' if not for the HTML-entity
        // filter. Verify the filter holds.
        expect(findings).toEqual([]);
      } finally {
        fs.unlinkSync(tmpFile);
      }
    });

    it('IGNORES color names in non-style contexts (e.g., a label string)', () => {
      const tmpFile = path.join(__dirname, '__adr021-self-test-label.tsx');
      fs.writeFileSync(
        tmpFile,
        `const category = { name: 'blue theme', label: "red flag indicator" };\n`,
        'utf8'
      );
      try {
        const findings = scanFileForHardcodedColors(tmpFile);
        expect(findings).toEqual([]);
      } finally {
        fs.unlinkSync(tmpFile);
      }
    });
  });
});

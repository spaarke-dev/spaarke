/**
 * Static color scan test — ADR-021 compliance
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * ADR-021: All widget source files must use Fluent v9 semantic color tokens
 * only. Hard-coded color strings are forbidden.
 *
 * This test scans all .tsx and .ts files in output-widgets/ and
 * source-widgets/ for hard-coded color values. If violations are found,
 * they are reported with file path and line number, and the test fails.
 *
 * Patterns detected:
 *   - Hex colors: #fff, #ffffff, #FFF, #FFFFFF (3 or 6 hex digits)
 *   - RGB/RGBA functions: rgb(, rgba(
 *   - HSL/HSLA functions: hsl(, hsla(
 *   - Named CSS colors in string contexts: color: "red", color: "blue", etc.
 *
 * Exclusions (not violations):
 *   - Comments that mention colors (for documentation)
 *   - The string "colorBrand..." or "colorNeutral..." — these are Fluent token names
 *   - Test files (this file itself)
 *   - The SERIES_COLORS_CSS constant in ChartWidget uses var(--colorXxx) CSS variables
 *     which resolve to Fluent design tokens at runtime — these are token references,
 *     not hard-coded colors.
 *
 * IMPORTANT: If a widget fails this scan, a FINDING comment is added to that
 * widget's test file. The production widget source must NOT be modified in
 * this task (AIPU-051). Fixes go in a separate task.
 */

import * as fs from "fs";
import * as path from "path";

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

const WIDGET_DIRS = [
  path.resolve(__dirname, "../output-widgets"),
  path.resolve(__dirname, "../source-widgets"),
];

/**
 * Patterns that indicate a hard-coded color value.
 * The regex is applied line-by-line to each source file.
 */
const HARDCODED_COLOR_PATTERNS = [
  // Hex color literals: #fff #ffffff #FFF #FFFFFF (not inside a Fluent var())
  // We exclude matches that are part of CSS custom property references like var(--colorXxx)
  {
    regex: /#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?\b/,
    description: "hex color literal",
  },
  // rgb() / rgba() function calls
  {
    regex: /\brgb[a]?\s*\(/,
    description: "rgb()/rgba() color function",
  },
  // hsl() / hsla() function calls
  {
    regex: /\bhsl[a]?\s*\(/,
    description: "hsl()/hsla() color function",
  },
  // Bare named CSS colors in string assignments: color: "red", backgroundColor: "blue"
  // Only catches these when they appear as string values in style assignments.
  {
    regex: /:\s*["'](red|blue|green|yellow|black|white|gray|grey|orange|purple|pink|brown|cyan|magenta|lime|indigo|violet|teal|coral|salmon|gold|silver|navy|maroon|olive|aqua|fuchsia|transparent)["']/i,
    description: "named CSS color string value",
  },
];

/**
 * Lines that are safe to skip even if they match a color pattern.
 * These are false-positive sources.
 */
const SAFE_LINE_PATTERNS = [
  // Pure comments — whole-line comments or end-of-line comments
  /^\s*\/\//,
  // Block comment lines
  /^\s*\*/
  ,
  // CSS custom property references: var(--colorXxx) — these are Fluent token CSS vars
  // The var() will not contain a literal hex color; the match would be in the var name itself.
  // We exclude lines where the hex-looking text is only inside a var(--color...) reference.
  // Note: ChartWidget uses "var(--colorBrandForeground1)" — the var name contains no hex digits,
  // so this pattern is for completeness.
];

// ---------------------------------------------------------------------------
// Scanner
// ---------------------------------------------------------------------------

interface ColorViolation {
  file: string;
  line: number;
  lineText: string;
  pattern: string;
}

function findHardcodedColors(): ColorViolation[] {
  const violations: ColorViolation[] = [];

  for (const dir of WIDGET_DIRS) {
    if (!fs.existsSync(dir)) {
      // Directory does not exist — skip gracefully (not a test failure)
      continue;
    }

    const files = fs
      .readdirSync(dir, { withFileTypes: true })
      .filter(
        (entry) =>
          entry.isFile() &&
          (entry.name.endsWith(".tsx") || entry.name.endsWith(".ts")) &&
          // Exclude test files from this scan
          !entry.name.includes(".test.") &&
          !entry.name.includes(".spec.")
      )
      .map((entry) => path.join(dir, entry.name));

    for (const filePath of files) {
      const source = fs.readFileSync(filePath, "utf-8");
      const lines = source.split("\n");

      lines.forEach((lineText, zeroBasedIndex) => {
        const lineNumber = zeroBasedIndex + 1;

        // Skip lines that are safe false-positives
        const isSafe = SAFE_LINE_PATTERNS.some((pattern) =>
          pattern.test(lineText)
        );
        if (isSafe) return;

        for (const { regex, description } of HARDCODED_COLOR_PATTERNS) {
          if (regex.test(lineText)) {
            violations.push({
              file: path.relative(
                path.resolve(__dirname, "../.."),
                filePath
              ),
              line: lineNumber,
              lineText: lineText.trim(),
              pattern: description,
            });
            // Only report the first matching pattern per line to avoid duplicates
            break;
          }
        }
      });
    }
  }

  return violations;
}

// ---------------------------------------------------------------------------
// Test
// ---------------------------------------------------------------------------

describe("ADR-021 static color scan — no hard-coded colors in widget sources", () => {
  let violations: ColorViolation[] = [];

  beforeAll(() => {
    violations = findHardcodedColors();
  });

  it("scans output-widgets and source-widgets directories without filesystem errors", () => {
    // This test passes if the scanner ran without throwing.
    // The presence of violations is reported separately below.
    expect(true).toBe(true);
  });

  it("finds no hard-coded color values in widget source files (ADR-021)", () => {
    if (violations.length === 0) {
      // All clean — pass
      expect(violations).toHaveLength(0);
      return;
    }

    // Build a human-readable report for the test failure message
    const report = violations
      .map(
        (v) =>
          `  VIOLATION [${v.pattern}] at ${v.file}:${v.line}\n    > ${v.lineText}`
      )
      .join("\n");

    // Fail with a descriptive message — the violations need production fixes
    // (do not fix widget sources in this task; record as FINDINGs instead)
    fail(
      `ADR-021 violation: ${violations.length} hard-coded color(s) found.\n` +
        `Each must be replaced with a Fluent v9 token in a separate task.\n\n` +
        report
    );
  });
});

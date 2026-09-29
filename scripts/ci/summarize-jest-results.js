#!/usr/bin/env node
/**
 * summarize-jest-results.js — render a jest `--json` result file as a GitHub
 * Actions job summary (Markdown on stdout) plus an `::error::` annotation when
 * anything failed.
 *
 * Usage:
 *   node scripts/ci/summarize-jest-results.js <results.json> [title] >> "$GITHUB_STEP_SUMMARY"
 *
 * Why this exists (CLAUDE.md §11):
 *   - Existing: `scripts/ci/classify-and-retry.ps1` is the only sibling, and it
 *     parses dotnet TRX for the two-pass retry policy — a different format, a
 *     different language, and a different decision. Nothing here overlaps it.
 *   - Extension: not applicable — TRX and jest JSON share no parser.
 *   - Cost of doing nothing: a job carrying `continue-on-error: true` renders a
 *     FAILED step as a green check with an easily-missed warning annotation. An
 *     advisory gate whose red is invisible enforces nothing — precisely the
 *     failure mode spaarkeai-compose-r8 task 018 exists to close. This turns the
 *     result into a summary table and a loud annotation that survive the
 *     advisory posture, and stay useful after the gate flips to blocking.
 *
 * Exit code is always 0: reporting must never be the thing that fails a job.
 * The jest step itself owns the verdict.
 */

'use strict';

const fs = require('fs');
const path = require('path');

const [, , resultsPath, titleArg] = process.argv;
const title = titleArg || 'Jest results';

if (!resultsPath) {
  console.log('### ' + title + ' — no results file argument');
  process.exit(0);
}

let report;
try {
  report = JSON.parse(fs.readFileSync(resultsPath, 'utf8'));
} catch (err) {
  console.log('### ' + title + ' — results file unreadable (' + err.message + ')');
  process.exit(0);
}

const suites = Array.isArray(report.testResults) ? report.testResults : [];
const failedSuites = suites.filter(s => s.status === 'failed').map(s => path.basename(s.name || 'unknown'));

const lines = [
  '### ' + title,
  '',
  '| Metric | Count |',
  '|---|---|',
  '| Suites passed | ' + (report.numPassedTestSuites || 0) + ' |',
  '| Suites failed | ' + (report.numFailedTestSuites || 0) + ' |',
  '| Tests passed | ' + (report.numPassedTests || 0) + ' |',
  '| Tests failed | ' + (report.numFailedTests || 0) + ' |',
  '| Tests skipped | ' + (report.numPendingTests || 0) + ' |',
  '',
];

if (failedSuites.length > 0) {
  lines.push('**Failed suites**', '');
  failedSuites.forEach(name => lines.push('- `' + name + '`'));
  lines.push('');
  // Annotation goes to the workflow log, not the summary — hence console.error.
  console.error(
    '::error::' +
      title +
      ': ' +
      (report.numFailedTests || 0) +
      ' test(s) failed across ' +
      failedSuites.length +
      ' suite(s) — ' +
      failedSuites.join(', ')
  );
}

console.log(lines.join('\n'));

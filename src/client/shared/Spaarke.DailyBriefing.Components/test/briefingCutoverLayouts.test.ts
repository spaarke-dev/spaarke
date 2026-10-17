/**
 * briefingCutoverLayouts.test.ts - spaarke-ontology-platform-r1 task 064 (BR-4).
 *
 * The Console lands on the system `sprk_workspacelayout` row flagged default, so "the worklist replaces the Daily Briefing tab" is a
 * statement about DATA, and "a regression is recoverable without a deploy" is a statement about what is still installed. This test pins
 * the repo-side half of both, from the files alone (no Dataverse, no LegalWorkspace imports):
 *
 *   1. `scripts/system-layouts.json` (the seed definition a fresh environment gets) names the Worklist as the ONE default landing layout.
 *   2. Negative: the Daily Briefing row is still defined (not default) and its `daily-briefing` section is still registered in the
 *      LegalWorkspace section registry next to `worklist`, so `scripts/Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'` is a real revert.
 *   3. The revert script exists and accepts both names.
 *
 * Why here: LegalWorkspace has no Jest setup and this package already hosts its registry tests. This file reads text only, so it also runs
 * in a package-only install (the registry-import tests next to it do not, see the 062 notes P-2).
 *
 * ADR-038 class: MAINTAIN - a deployed layout row and the revert path depend on these ids.
 */

import * as fs from 'fs';
import * as path from 'path';

const REPO_ROOT = path.resolve(__dirname, '..', '..', '..', '..', '..');
const read = (...parts: string[]): string => fs.readFileSync(path.join(REPO_ROOT, ...parts), 'utf8');

interface SystemLayoutSeed {
  name: string;
  sectionId: string;
  isDefault: boolean;
  sortOrder: number;
}
const layouts = (JSON.parse(read('scripts', 'system-layouts.json')) as { layouts: SystemLayoutSeed[] }).layouts;

describe('Daily Briefing cutover (task 064)', () => {
  it('the seed names the Worklist as the one default landing layout', () => {
    const defaults = layouts.filter(l => l.isDefault);
    expect(defaults.map(l => [l.name, l.sectionId])).toEqual([['Worklist', 'worklist']]);
  });

  it('every seeded layout has a unique name and sort order is not reused (the BFF breaks default ties by sort order)', () => {
    expect(new Set(layouts.map(l => l.name)).size).toBe(layouts.length);
    expect(new Set(layouts.map(l => l.sortOrder)).size).toBe(layouts.length);
  });

  it('negative: the Daily Briefing row is still defined, not default, on the daily-briefing section (the revert target)', () => {
    const briefing = layouts.find(l => l.name === 'Daily Briefing');
    expect(briefing).toBeDefined();
    expect(briefing?.sectionId).toBe('daily-briefing');
    expect(briefing?.isDefault).toBe(false);
  });

  it('negative: both the worklist and the daily-briefing sections stay registered in the LegalWorkspace registry', () => {
    const registry = read('src', 'solutions', 'LegalWorkspace', 'src', 'sectionRegistry.ts');
    expect(registry).toMatch(/^\s*worklistRegistration,/m);
    expect(registry).toMatch(/^\s*createLegalWorkspaceDailyBriefingRegistration\(/m);
    expect(read('src', 'solutions', 'LegalWorkspace', 'src', 'sections', 'worklist.registration.ts')).toMatch(
      /id: "worklist"/
    );
  });

  it('the no-deploy switch exists, takes both layouts, supports -WhatIf and writes only sprk_isdefault', () => {
    const script = read('scripts', 'Set-DefaultWorkspaceLayout.ps1');
    expect(script).toMatch(/ValidateSet\('Worklist', 'Daily Briefing'\)/);
    expect(script).toMatch(/SupportsShouldProcess/);
    const bodies = script.match(/-Body \(@\{[^}]*\}/g) ?? [];
    expect(bodies).toHaveLength(1);
    expect(bodies[0]).toMatch(/@\{ sprk_isdefault = /);
  });
});

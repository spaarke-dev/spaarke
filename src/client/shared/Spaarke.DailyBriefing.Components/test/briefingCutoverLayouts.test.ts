/**
 * briefingCutoverLayouts.test.ts - spaarke-ontology-platform-r1 task 064 (BR-4).
 *
 * The Console lands on the system `sprk_workspacelayout` row flagged default, so "the worklist replaces the Daily Briefing tab" is a
 * statement about DATA on dev (`scripts/Set-DefaultWorkspaceLayout.ps1`), and "a regression is recoverable without a deploy" is a
 * statement about what is still installed. This test pins the repo-side half, from the files alone (no Dataverse, no LegalWorkspace
 * imports):
 *
 *   1. The `daily-briefing` and `worklist` sections both stay registered in the LegalWorkspace section registry, so the switch can go
 *      either way with no deploy.
 *   2. `scripts/system-layouts.json` (embedded into customer provisioning) still seeds the Daily Briefing row on the `daily-briefing`
 *      section: removing that section would break every provisioned environment, not just the revert. The cutover does NOT edit this
 *      file (whether new customers land on the Worklist is an owner scope decision, #2006).
 *   3. The switch script exists, accepts both names, supports -WhatIf, writes only `sprk_isdefault`, never upserts (If-Match) and
 *      reports a declined confirmation as cancelled.
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
}

describe('Daily Briefing cutover (task 064)', () => {
  it('negative: both the worklist and the daily-briefing sections stay registered in the LegalWorkspace registry', () => {
    const registry = read('src', 'solutions', 'LegalWorkspace', 'src', 'sectionRegistry.ts');
    expect(registry).toMatch(/^\s*worklistRegistration,/m);
    expect(registry).toMatch(/^\s*createLegalWorkspaceDailyBriefingRegistration\(/m);
    expect(read('src', 'solutions', 'LegalWorkspace', 'src', 'sections', 'worklist.registration.ts')).toMatch(
      /id: "worklist"/
    );
  });

  it('negative: the provisioning seed still defines the Daily Briefing row on the daily-briefing section', () => {
    const layouts = (JSON.parse(read('scripts', 'system-layouts.json')) as { layouts: SystemLayoutSeed[] }).layouts;
    const briefing = layouts.find(l => l.name === 'Daily Briefing');
    expect(briefing?.sectionId).toBe('daily-briefing');
  });

  it('the no-deploy switch exists, takes both layouts, supports -WhatIf and writes only sprk_isdefault', () => {
    const script = read('scripts', 'Set-DefaultWorkspaceLayout.ps1');
    expect(script).toMatch(/ValidateSet\('Worklist', 'Daily Briefing'\)/);
    expect(script).toMatch(/SupportsShouldProcess/);
    const bodies = script.match(/-Body \(@\{[^}]*\}/g) ?? [];
    expect(bodies).toHaveLength(1);
    expect(bodies[0]).toMatch(/@\{ sprk_isdefault = /);
  });

  it('the switch never upserts and reports a declined confirmation as cancelled, not as a failed verification', () => {
    const script = read('scripts', 'Set-DefaultWorkspaceLayout.ps1');
    expect(script).toMatch(/'If-Match' = '\*'/);
    const cancelled = script.indexOf('Cancelled, nothing written.');
    const verification = script.indexOf('Verification failed');
    expect(cancelled).toBeGreaterThan(-1);
    expect(verification).toBeGreaterThan(cancelled);
  });
});

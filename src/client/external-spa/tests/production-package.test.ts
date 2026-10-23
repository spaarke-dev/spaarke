// Production SPA build + production Teams package in CI (spaarke-SPA-external-access-platform-r3 task 043; spec FR-20,
// design actions A1/A2/A4; ADR-028 A8).
//
// Shipped-config tests (they read the committed workflows and appPackage files): the two workflows that build this SPA
// take every environment-specific value from a GitHub environment (no identifier, URL or scope literal), deploy the
// production build only with the production token, stay manual-only, and package a production manifest that is on
// external.spaarke.com, has its own app id placeholder and declares no webApplicationInfo.
import * as fs from 'node:fs';
import { createRequire } from 'node:module';
import * as path from 'node:path';
import { describe, expect, it } from 'vitest';

// js-yaml is not a declared dependency of this package (it arrives with eslint) and ships no types, so it is loaded
// through require with a minimal local type. The workflows are PARSED, not scanned with a regex over the file.
const { load: loadYaml } = createRequire(import.meta.url)('js-yaml') as { load: (text: string) => unknown };

const SPA_ROOT = path.resolve(__dirname, '..');
const REPO_ROOT = path.resolve(SPA_ROOT, '../../..');
const WORKFLOWS = ['deploy-external-spa.yml', 'deploy-teams-app.yml'];
const PROD_ORIGIN_HOST = 'external.spaarke.com';
const ALL_ZERO_GUID = '00000000-0000-0000-0000-000000000000';

const read = (p: string) => fs.readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
const workflow = (name: string) => read(path.join(REPO_ROOT, '.github/workflows', name));
/** The workflow text without comment lines (a comment may explain; only live YAML/shell may not carry a literal). */
const code = (text: string) =>
  text
    .split('\n')
    .filter(line => !/^\s*#/.test(line))
    .join('\n');
interface WorkflowStep {
  name?: string;
  env?: Record<string, string>;
  run?: string;
  uses?: string;
}
interface ParsedWorkflow {
  concurrency?: { group?: string; 'cancel-in-progress'?: boolean };
  jobs: Record<string, { steps: WorkflowStep[] }>;
}
const parsedWorkflow = (name: string) => loadYaml(workflow(name)) as ParsedWorkflow;
const appPackage = (name: string) => JSON.parse(read(path.join(SPA_ROOT, 'appPackage', name)));

describe('Production build workflows (FR-20)', () => {
  it.each(WORKFLOWS)(
    '%s: manual-only (workflow_dispatch) with a dev|prod environment input defaulting to dev',
    name => {
      const text = code(workflow(name));
      expect(text).toMatch(/^on:\n {2}workflow_dispatch:\n {4}inputs:\n {6}environment:/m);
      expect(text).not.toMatch(/^\s*(push|pull_request|pull_request_target|schedule|workflow_run|release):/m);
      expect(text).toMatch(/type: choice\n\s+default: dev\n\s+options:\n\s+- dev\n\s+- prod\n/);
    }
  );

  it.each(WORKFLOWS)('%s: the job runs in external-spa-prod for prod and external-spa-dev for dev', name => {
    const text = code(workflow(name));
    expect(text).toContain(
      "environment: ${{ inputs.environment == 'prod' && 'external-spa-prod' || 'external-spa-dev' }}"
    );
  });

  it.each(WORKFLOWS)('%s: no identifier, URL or scope literal (every value is an environment variable)', name => {
    const text = code(workflow(name));
    const guids = [...text.matchAll(/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/g)]
      .map(m => m[0])
      .filter(g => g !== ALL_ZERO_GUID); // the all-zero id is the "placeholder, refuse it" comparison, not an identifier
    expect(guids, `${name}: GUID literals`).toEqual([]);
    expect(text, `${name}: hosts`).not.toMatch(
      /azurewebsites\.net|azurestaticapps\.net|\.crm\d*\.dynamics\.com|external\.spaarke\.com|login\.microsoftonline\.com/
    );
    // A concrete CIAM authority would be https://{subdomain}.ciamlogin.com/{tenant}; only the format pattern may appear.
    expect(text, `${name}: concrete URL`).not.toMatch(/https?:\/\/[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+/);
    expect(text, `${name}: scope literal`).not.toMatch(/api:\/\/[0-9a-fA-F]{8}/);
    expect(text).not.toContain('SDAP.Access');
  });

  it.each(WORKFLOWS)('%s: every VITE_* build value is read from vars.* under the same name', name => {
    const text = code(workflow(name));
    const assignments = [...text.matchAll(/^\s+(VITE_[A-Z_]+): (.+)$/gm)];
    expect(assignments.length).toBeGreaterThanOrEqual(9);
    for (const [, key, value] of assignments) {
      expect(value, `${name}: ${key}`).toBe('${{ vars.' + key + ' }}');
    }
    for (const key of [
      'VITE_MSAL_AUTHORITY',
      'VITE_MSAL_CLIENT_ID',
      'VITE_MSAL_TENANT_ID',
      'VITE_MSAL_BFF_SCOPE',
      'VITE_TEAMS_MSAL_CLIENT_ID',
      'VITE_TEAMS_MSAL_TENANT_ID',
      'VITE_TEAMS_MSAL_BFF_SCOPE',
      'VITE_WORKFORCE_HOME_MSAL_CLIENT_ID',
    ]) {
      expect(text, `${name} does not set ${key}`).toContain(`${key}: \${{ vars.${key} }}`);
    }
  });

  it('deploy-external-spa.yml: prod deploys only with the prod environment secret, dev only with the dev one; the prod run has no dev fall-back', () => {
    const text = code(workflow('deploy-external-spa.yml'));
    const steps = text.split(/\n {6}- /);
    const devStep = steps.find(s => s.startsWith('name: Deploy to Azure Static Web Apps (dev)'));
    const prodStep = steps.find(s => s.startsWith('name: Deploy to Azure Static Web Apps (prod)'));
    expect(devStep, 'dev deploy step').toBeDefined();
    expect(prodStep, 'prod deploy step').toBeDefined();
    expect(devStep).toContain("if: ${{ inputs.environment == 'dev' }}");
    expect(devStep).toContain('secrets.AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV');
    expect(devStep).not.toContain('_PROD');
    expect(prodStep).toContain("if: ${{ inputs.environment == 'prod' }}");
    expect(prodStep).toContain('secrets.AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD');
    expect(prodStep).not.toContain('_DEV');
    // No expression anywhere chooses a token with a `||` fall-back to the other environment's secret.
    expect(text).not.toMatch(/secrets\.AZURE_SWA_TOKEN_EXTERNAL_SPA_\w+\s*\|\|/);
    expect(text).not.toMatch(/\|\|\s*secrets\.AZURE_SWA_TOKEN_EXTERNAL_SPA_\w+/);
  });

  it.each(WORKFLOWS)(
    '%s: fails closed before the build when a required environment variable is missing or malformed',
    name => {
      const text = code(workflow(name));
      const preflight = text.indexOf('name: Check required environment variables');
      const build = text.indexOf('name: Build (Vite production)');
      expect(preflight).toBeGreaterThan(-1);
      expect(preflight).toBeLessThan(build);
      const block = text.slice(preflight, build);
      for (const key of [
        'VITE_MSAL_AUTHORITY',
        'VITE_MSAL_CLIENT_ID',
        'VITE_TEAMS_MSAL_CLIENT_ID',
        'VITE_TEAMS_MSAL_TENANT_ID',
        'VITE_TEAMS_MSAL_BFF_SCOPE',
        // ADR-028 A9 (tasks 058/076): required, a GUID, and different from the A8 client.
        'VITE_WORKFORCE_HOME_MSAL_CLIENT_ID',
      ]) {
        expect(block, `${name}: ${key} in the preflight`).toContain(`need "$k"`);
        expect(block, `${name}: ${key} in the preflight`).toContain(key);
      }
      expect(block).toContain('match VITE_WORKFORCE_HOME_MSAL_CLIENT_ID "$guid" "GUID"');
      expect(block).toContain('must differ from VITE_TEAMS_MSAL_CLIENT_ID');
      // A whole-string match: `grep` matches line by line, so a value with a newline would pass a grep check (task 076 review).
      expect(block).toContain('! [[ ${!1} =~ $2 ]]');
      expect(block).not.toMatch(/grep -Eq "\$2"/);
      expect(block).toContain('exit "$fail"');
    }
  );

  it('deploy-teams-app.yml: before packaging, the staged manifest must have no webApplicationInfo, https tabs and the SPA domain in validDomains', () => {
    const text = code(workflow('deploy-teams-app.yml'));
    const preflight = text.indexOf('name: Check required environment variables');
    const validate = text.indexOf('name: Validate Teams app manifest');
    const pack = text.indexOf('name: Package Teams app');
    expect(preflight).toBeGreaterThan(-1);
    expect(validate).toBeGreaterThan(preflight);
    expect(pack).toBeGreaterThan(validate);
    const checks = text.slice(validate, pack);
    expect(checks).toContain(`jq -e 'has("webApplicationInfo") | not' manifest.json`);
    expect(checks).toContain(`jq -e 'all(.staticTabs[].contentUrl; startswith("https://"))' manifest.json`);
    expect(checks).toContain(
      `jq -e '[.staticTabs[].contentUrl | sub("^https://"; "") | sub("[/?#:].*$"; "")] - .validDomains | length == 0' manifest.json`
    );
    // prod: the package id variable is checked in the preflight too, not only when the manifest is rendered.
    expect(text.slice(preflight, validate)).toContain('need TEAMS_APP_ID');
  });

  it('deploy-external-spa.yml: the built bundle is scanned (npm run check:dist) right after the build and before it is staged', () => {
    const steps = parsedWorkflow('deploy-external-spa.yml').jobs.build_and_deploy.steps;
    const build = steps.findIndex(s => s.run === 'npm run build');
    expect(build).toBeGreaterThan(-1);
    const check = steps[build + 1];
    expect(check.run).toBe('npm run check:dist');
    expect(check.env, 'no workflow value reaches the scan').toBeUndefined();
    const stage = steps.findIndex(s => (s.run ?? '').includes('staticwebapp.config.json'));
    expect(stage).toBeGreaterThan(build + 1);
  });

  it.each(WORKFLOWS)(
    '%s: the first step of every job rejects any environment other than dev|prod before anything runs',
    name => {
      const jobs = Object.entries(parsedWorkflow(name).jobs);
      expect(jobs.length).toBeGreaterThan(0);
      for (const [jobId, job] of jobs) {
        const first = job.steps[0];
        expect(first.name, `${name}/${jobId}: first step`).toBe('Validate environment input');
        expect(first.env, `${name}/${jobId}: first step env`).toEqual({ TARGET: '${{ inputs.environment }}' });
        expect(first.run, `${name}/${jobId}: first step script`).toContain(
          `case "$TARGET" in dev|prod) ;; *) echo "::error::environment must be dev or prod (got '$TARGET')"; exit 1;; esac`
        );
      }
    }
  );

  it.each(WORKFLOWS)('%s: no run: body interpolates inputs.* or vars.* (they reach the shell through env:)', name => {
    const offenders: string[] = [];
    for (const [jobId, job] of Object.entries(parsedWorkflow(name).jobs)) {
      for (const step of job.steps) {
        if (step.run && /\$\{\{\s*(inputs|vars|github\.event\.inputs)\b/.test(step.run)) {
          offenders.push(`${jobId}/${step.name ?? step.run.slice(0, 40)}`);
        }
      }
    }
    expect(offenders, `${name}: steps with an expression inside run:`).toEqual([]);
  });

  it('workflow-level concurrency is keyed on inputs.environment, queues instead of cancelling, and differs per workflow', () => {
    const spa = parsedWorkflow('deploy-external-spa.yml').concurrency;
    const teams = parsedWorkflow('deploy-teams-app.yml').concurrency;
    expect(spa).toEqual({ group: 'deploy-external-spa-${{ inputs.environment }}', 'cancel-in-progress': false });
    expect(teams).toEqual({ group: 'deploy-teams-app-${{ inputs.environment }}', 'cancel-in-progress': false });
    expect(spa?.group).not.toBe(teams?.group);
  });

  it('deploy-external-spa.yml: the authority preflight is an https URL with one tenant GUID path segment, not tied to a host', () => {
    const wf = parsedWorkflow('deploy-external-spa.yml');
    const preflight = wf.jobs.build_and_deploy.steps.find(s => s.name === 'Check required environment variables');
    expect(preflight?.run).toBeDefined();
    expect(preflight?.run).not.toContain('ciamlogin');
    expect(preflight?.run).toContain(
      "authority='^https://[^/?#[:space:]]+/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/?$'"
    );
    expect(preflight?.run).toContain('match VITE_MSAL_AUTHORITY "$authority"');
  });

  it('deploy-teams-app.yml: stages manifest.json for dev and a TEAMS_APP_ID-rendered manifest.prod.json for prod, and validates the staged file', () => {
    const text = code(workflow('deploy-teams-app.yml'));
    expect(text).toContain('TEAMS_APP_ID: ${{ vars.TEAMS_APP_ID }}');
    expect(text).toContain('jq --arg id "$TEAMS_APP_ID" \'.id = $id\' manifest.prod.json > build/stage/manifest.json');
    expect(text).toContain('cp manifest.json build/stage/manifest.json');
    expect(text).toContain('zip -j build/appPackage.zip build/stage/manifest.json color.png outline.png');
    // The prod run refuses an unset, non-GUID or all-zero app id.
    expect(text).toContain('TEAMS_APP_ID must be a GUID');
    expect(text).toContain('TEAMS_APP_ID is the all-zero placeholder');
    // Dev and prod packages are different artifacts.
    expect(text).toContain("name: teams-app-package${{ inputs.environment == 'prod' && '-prod' || '' }}");
  });
});

describe('Production Teams manifest (A1/A2)', () => {
  it('manifest.prod.json: placeholder id, external.spaarke.com tab and domains, no webApplicationInfo, no dev host', () => {
    const prod = appPackage('manifest.prod.json');
    const dev = appPackage('manifest.json');
    expect(prod.id).toBe('${{TEAMS_APP_ID}}');
    expect(prod.id).not.toBe(dev.id);
    expect(prod).not.toHaveProperty('webApplicationInfo');
    expect(prod.staticTabs.length).toBeGreaterThan(0);
    for (const tab of prod.staticTabs) {
      expect(new URL(tab.contentUrl).origin).toBe(`https://${PROD_ORIGIN_HOST}`);
      expect(new URL(tab.websiteUrl).origin).toBe(`https://${PROD_ORIGIN_HOST}`);
    }
    expect(prod.validDomains).toContain(PROD_ORIGIN_HOST);
    expect(prod.validDomains).toEqual([PROD_ORIGIN_HOST, 'teams.microsoft.com', '*.cloud.microsoft']);
    expect(JSON.stringify(prod)).not.toMatch(/azurewebsites\.net|azurestaticapps\.net/);
    // Same product surface as dev apart from identity and origin.
    expect(prod.name).toEqual(dev.name);
    expect(prod.description).toEqual(dev.description);
    expect(prod.accentColor).toBe(dev.accentColor);
    expect(dev.accentColor).toBeDefined();
    expect(prod.manifestVersion).toBe(dev.manifestVersion);
    expect(prod.icons).toEqual(dev.icons);
    expect(prod.staticTabs[0].entityId).toBe(dev.staticTabs[0].entityId);
    expect(prod.permissions).toEqual(dev.permissions);
    for (const icon of Object.values<string>(prod.icons)) {
      expect(fs.existsSync(path.join(SPA_ROOT, 'appPackage', icon)), icon).toBe(true);
    }
  });

  it('manifest.json (dev) is unchanged in shape: dev SWA origin, no webApplicationInfo', () => {
    const dev = appPackage('manifest.json');
    expect(dev).not.toHaveProperty('webApplicationInfo');
    expect(dev.staticTabs[0].contentUrl).toMatch(/\.azurestaticapps\.net$/);
    expect(JSON.stringify(dev)).not.toContain(PROD_ORIGIN_HOST);
  });

  it('env.prod.json: production origin, placeholder ids only (no real id is committed)', () => {
    const env = appPackage('env.prod.json');
    expect(env.SPA_BASE_URL).toBe(`https://${PROD_ORIGIN_HOST}`);
    expect(env.TEAMS_APP_ID).toBe(ALL_ZERO_GUID);
    expect(env.AAD_APP_CLIENT_ID).toBe(ALL_ZERO_GUID);
    expect(env.WORKFORCE_HOME_CLIENT_ID).toBe(ALL_ZERO_GUID);
    expect(JSON.stringify(env)).not.toMatch(/azurewebsites\.net|azurestaticapps\.net/);
  });

  it('rendering the id (what the workflow does with jq) yields a manifest with no placeholder left', () => {
    const appId = '12345678-aaaa-bbbb-cccc-1234567890ab';
    const rendered = { ...appPackage('manifest.prod.json'), id: appId };
    expect(rendered.id).toBe(appId);
    expect(JSON.stringify(rendered)).not.toContain('${{');
    expect(rendered).not.toHaveProperty('webApplicationInfo');
  });
});

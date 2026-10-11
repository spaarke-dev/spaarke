// Production SPA build + production Teams package in CI (spaarke-SPA-external-access-platform-r3 task 043; spec FR-20,
// design actions A1/A2/A4; ADR-028 A7).
//
// Shipped-config tests (they read the committed workflows and appPackage files): the two workflows that build this SPA
// take every environment-specific value from a GitHub environment (no identifier, URL or scope literal), deploy the
// production build only with the production token, stay manual-only, and package a production manifest that is on
// external.spaarke.com, has its own app id placeholder and declares no webApplicationInfo.
import * as fs from 'node:fs';
import * as path from 'node:path';
import { describe, expect, it } from 'vitest';

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

  it('deploy-external-spa.yml: fails closed before the build when a required environment variable is missing or malformed', () => {
    const text = code(workflow('deploy-external-spa.yml'));
    const preflight = text.indexOf('name: Check required environment variables');
    const build = text.indexOf('name: Build (Vite production)');
    expect(preflight).toBeGreaterThan(-1);
    expect(preflight).toBeLessThan(build);
    for (const key of [
      'VITE_MSAL_AUTHORITY',
      'VITE_MSAL_CLIENT_ID',
      'VITE_TEAMS_MSAL_TENANT_ID',
      'VITE_TEAMS_MSAL_BFF_SCOPE',
    ]) {
      expect(text.slice(preflight, build)).toContain(key);
    }
    expect(text.slice(preflight, build)).toContain('exit "$fail"');
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

/**
 * Ontology admin (task 104; D-22, FR-56): the capability probe client the host uses to decide whether to register the tab, and the
 * tab shell with its two R1 areas.
 *
 *  - the probe is true ONLY for a 2xx saying `canAuthorPolicies: true`; a refusal, a thrown ApiError, a network failure or an
 *    unreadable body is false (the tab is not offered);
 *  - the shell has exactly Rules and Classification; no Actions, Suppression, Health or Access area exists (M-11..M-14 not built);
 *  - each area renders the slot its task fills (100, 103), or a neutral empty state;
 *  - tokens only, light and dark (ADR-021).
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import {
  ONTOLOGY_ADMIN_AREAS,
  ONTOLOGY_ADMIN_CAPABILITIES_ROUTE,
  OntologyAdminTab,
  fetchOntologyAdminCapability,
} from '..';

const response = (status: number, json: unknown) =>
  ({ ok: status >= 200 && status < 300, status, json: async () => json }) as unknown as Response;

describe('fetchOntologyAdminCapability', () => {
  it('is true when the route says the caller can author policies, and asks the probe route on the base URL', async () => {
    const fetch = jest.fn(async () => response(200, { canAuthorPolicies: true }));

    await expect(fetchOntologyAdminCapability({ fetch, baseUrl: 'https://bff.example/' })).resolves.toBe(true);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toBe(`https://bff.example${ONTOLOGY_ADMIN_CAPABILITIES_ROUTE}`);
    expect(ONTOLOGY_ADMIN_CAPABILITIES_ROUTE).toBe('/api/v1/ontology/admin/capabilities');
  });

  it('is false for a Console User (canAuthorPolicies false)', async () => {
    const fetch = jest.fn(async () => response(200, { canAuthorPolicies: false }));
    await expect(fetchOntologyAdminCapability({ fetch, baseUrl: 'https://bff.example' })).resolves.toBe(false);
  });

  it.each([
    [
      'a returned 403 (unresolved caller)',
      async () => response(403, { reasonCode: 'sdap.access.deny.caller_unresolved' }),
    ],
    ['a returned 503 (fault)', async () => response(503, { reasonCode: 'ontology.admin.capability_unavailable' })],
    ['a 200 with no capability', async () => response(200, {})],
    ['a 200 with a truthy non-boolean', async () => response(200, { canAuthorPolicies: 'true' })],
    ['a 200 that is not an object', async () => response(200, null)],
  ])('is false for %s', async (_case, impl) => {
    await expect(fetchOntologyAdminCapability({ fetch: jest.fn(impl), baseUrl: 'https://bff.example' })).resolves.toBe(
      false
    );
  });

  it('is false when the fetch throws (an ApiError for a non-2xx, or a network failure), and never rejects', async () => {
    const thrown = jest.fn(async () => {
      // @spaarke/auth's authenticatedFetch throws an ApiError (name 'ApiError', status) for a non-2xx.
      throw Object.assign(new Error('Unauthorized'), { name: 'ApiError', status: 401, problemDetails: null });
    });
    const network = jest.fn(async () => {
      throw new TypeError('Failed to fetch');
    });
    await expect(fetchOntologyAdminCapability({ fetch: thrown, baseUrl: 'https://bff.example' })).resolves.toBe(false);
    await expect(fetchOntologyAdminCapability({ fetch: network, baseUrl: 'https://bff.example' })).resolves.toBe(false);
  });
});

describe('OntologyAdminTab', () => {
  const renderTab = (props: React.ComponentProps<typeof OntologyAdminTab> = {}, theme = webLightTheme) =>
    render(
      <FluentProvider theme={theme}>
        <OntologyAdminTab {...props} />
      </FluentProvider>
    );

  it('has exactly the two R1 areas, Rules then Classification', () => {
    renderTab();
    expect(ONTOLOGY_ADMIN_AREAS.map(a => a.label)).toEqual(['Rules', 'Classification']);
    const tabs = screen.getAllByRole('tab');
    expect(tabs).toHaveLength(2);
    expect(tabs[0]).toHaveAccessibleName('Rules');
    expect(tabs[1]).toHaveAccessibleName('Classification');
  });

  it('has no Actions, Suppression, Health or Access page (M-11..M-14 are not built in R1)', () => {
    renderTab();
    for (const name of ['Actions', 'Suppression', 'Health', 'Access']) {
      expect(screen.queryByRole('tab', { name: new RegExp(name, 'i') })).toBeNull();
      expect(screen.queryByText(new RegExp(`^${name}$`, 'i'))).toBeNull();
    }
    const dir = path.join(__dirname, '..');
    const pages = fs.readdirSync(dir).filter(f => /(Action|Suppression|Health|Access)/i.test(f));
    expect(pages).toEqual([]);
  });

  it('shows the Rules slot first and the Classification slot when that area is chosen', async () => {
    renderTab({ rules: <div>rules area</div>, classification: <div>classification area</div> });

    expect(screen.getByRole('tab', { name: 'Rules' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('rules area')).toBeInTheDocument();
    expect(screen.queryByText('classification area')).toBeNull();

    await userEvent.click(screen.getByRole('tab', { name: 'Classification' }));

    expect(screen.getByText('classification area')).toBeInTheDocument();
    expect(screen.queryByText('rules area')).toBeNull();
    expect(screen.getByRole('tabpanel')).toHaveAttribute('aria-labelledby', 'ontology-admin-area-classification');
  });

  it('shows a neutral empty state for an area whose task has not filled it', async () => {
    renderTab();
    expect(screen.getByText('Rules will appear here.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Classification' }));
    expect(screen.getByText('Triage categories will appear here.')).toBeInTheDocument();
  });

  it('opens on the requested area', () => {
    renderTab({ defaultArea: 'classification' });
    expect(screen.getByRole('tab', { name: 'Classification' })).toHaveAttribute('aria-selected', 'true');
  });

  it('renders under the dark theme (ADR-021)', () => {
    renderTab({}, webDarkTheme);
    expect(screen.getByTestId('ontology-admin-tab')).toBeInTheDocument();
  });

  it('component sources contain no hex, rgb or named colour literal (ADR-021)', () => {
    const dir = path.join(__dirname, '..');
    for (const f of fs.readdirSync(dir).filter(n => /\.tsx?$/.test(n))) {
      const source = fs
        .readFileSync(path.join(dir, f), 'utf8')
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/\/\/.*$/gm, '');
      expect(source).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(|\b(white|black|red|green|blue|gray|grey)\b['"]/);
    }
  });
});

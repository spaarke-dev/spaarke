/**
 * Record Procedures Code Page entry point (ebilling-r1 task 084a, EB-D-217).
 *
 * Embedded in the Billing tab of the `sprk_matter`, `sprk_project` and `sprk_workassignment` main forms as a web resource control with "Pass
 * record object-type code and unique identifier as parameters", so the URL carries `id` and `typename` (see `services/launch.ts`). Shows the
 * Procedures the record is subject to and the rules in force, for one firm and one work date, from task 084's read routes
 * (`GET /api/finance/{matters|projects|workassignments}/{recordId}/procedures` and `/rules-in-force`). Task 084b: a row opens the Procedure or
 * Rule wizard, whose writes go through task 089's routes (`services/overridesApi.ts`) and nowhere else.
 *
 * Every call to the BFF goes through `authenticatedFetch`; the page never reads or writes the Procedure, override or rule tables itself
 * (ADR-002). The one `Xrm.WebApi` read is the picker's list of firms that have invoiced the record (`services/firmChoices.ts`).
 *
 * The page scaffolding is the e-billing pages' shared one (`src/solutions/_ebilling-shared/`, issue 2965): the theme, the async auth bootstrap
 * (`resolveRuntimeConfig` -> `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028) that the page waits on before it mounts, a retry state
 * when it fails (fail closed), and the themed surface with its error boundary. The Navigator side pane is the host form's own concern (the
 * Matter and Project ribbons carry the trigger), so it is not registered here: this is an embedded section, not a page the user navigates to.
 *
 * Web resource: sprk_recordprocedures (see the build rename in package.json).
 */

import * as React from 'react';
import { parseDataParams, getXrm } from '@spaarke/ui-components';
import { useCodePageBootstrap } from '../../_ebilling-shared/codePageBootstrap';
import { CodePageMessage, CodePageSpinner, CodePageSurface, mountCodePage } from '../../_ebilling-shared/codePageChrome';
import { ProceduresPanel } from './components/ProceduresPanel';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createRecordProceduresApi } from './services/recordProceduresApi';
import { createOverridesApi } from './services/overridesApi';
import { createDryRunApi } from './services/dryRunApi';
import { createFirmChoices, type WebApiLike } from './services/firmChoices';
import { parseLaunch } from './services/launch';

/** `Xrm.WebApi` as the signed-in user, resolved at call time; read methods only. Undefined outside a Dataverse-hosted page. */
function hostWebApi(): WebApiLike | undefined {
  const api = getXrm()?.WebApi;
  if (!api) return undefined;
  return {
    retrieveMultipleRecords: async (entity, options) => {
      const result = await api.retrieveMultipleRecords(entity, options);
      return { entities: result.entities as Record<string, unknown>[] };
    },
  };
}

const firmChoices = createFirmChoices(hostWebApi);

function Root() {
  const launch = React.useMemo(() => parseLaunch(parseDataParams()), []);
  // An unsaved or foreign record needs no auth: the page only says so.
  const { theme, ready, bootstrapError, retry } = useCodePageBootstrap({
    label: 'RecordProcedures',
    enabled: launch.ok,
    setRuntimeConfig,
    ensureAuthInitialized,
  });

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createRecordProceduresApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const overrides = React.useMemo(() => (ready ? createOverridesApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const dryRun = React.useMemo(() => (ready ? createDryRunApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);

  let body: React.ReactNode;
  if (!launch.ok) {
    body = <CodePageMessage testId="recordprocedures-launch" title="Procedures" body={launch.message} />;
  } else if (bootstrapError) {
    body = (
      <CodePageMessage
        testId="recordprocedures-auth-error"
        title="Unable to load"
        body="Sign-in could not be verified for this session. Retry, or reload the form."
        onRetry={retry}
      />
    );
  } else if (!api || !overrides || !dryRun) {
    body = <CodePageSpinner testId="recordprocedures-bootstrapping" label="Loading procedures…" />;
  } else {
    body = <ProceduresPanel core={launch.core} table={launch.table} recordId={launch.recordId} api={api} overrides={overrides} dryRun={dryRun} firmChoices={firmChoices} />;
  }

  return (
    <CodePageSurface theme={theme} surfaceName="Record Procedures">
      {body}
    </CodePageSurface>
  );
}

mountCodePage('RecordProcedures', <Root />);

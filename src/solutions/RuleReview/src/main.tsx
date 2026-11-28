/**
 * Rule Review Code Page entry point (ebilling-r1 task 077, FR-28).
 *
 * The page scaffolding is the e-billing pages' shared one (`src/solutions/_ebilling-shared/`, issue 2965): the theme, the async auth bootstrap
 * (`resolveRuntimeConfig` -> `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028), and the themed surface with its error boundary. The
 * page mounts only after the bootstrap resolves; if it fails the page shows a retry state (fail closed). Every rule set call goes through
 * `authenticatedFetch` to the BFF; the page never writes Dataverse rows itself (ADR-002). The only `Xrm.WebApi` use is the read-only firm
 * lookup (`services/firmLookup.ts`).
 *
 * The Navigator side pane is registered on mount (root CLAUDE.md section 17, docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md).
 *
 * Web resource: sprk_rulereviewpage (see the build rename in package.json). Deployment is task 079's.
 */

import * as React from 'react';
import { createXrmNavigationService, getXrm, parseDataParams } from '@spaarke/ui-components';
import { useCodePageBootstrap } from '../../_ebilling-shared/codePageBootstrap';
import { CodePageMessage, CodePageSpinner, CodePageSurface } from '../../_ebilling-shared/codePageChrome';
import { mountWizardPage } from '@spaarke/ui-components/utils/mountWizardPage';
import { App } from './App';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createRuleSetApi } from './services/ruleSetApi';
import { createFirmLookup } from './services/firmLookup';
import { parseRuleSetLaunch } from './services/launch';

function Root() {
  // DEF-069: the rule set form's "Rule Review" command passes `ruleSetId`; read once at launch. Absent or invalid, the list opens as before.
  const initialRuleSetId = React.useMemo(() => parseRuleSetLaunch(parseDataParams()), []);
  const { theme, ready, bootstrapError, retry } = useCodePageBootstrap({
    label: 'RuleReview',
    registerNavigator: true,
    setRuntimeConfig,
    ensureAuthInitialized,
  });

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const services = React.useMemo(
    () =>
      ready
        ? {
            api: createRuleSetApi(authenticatedFetch, getBffBaseUrl()),
            // D-219 Q5: the Firm picker opens the Dataverse side pane when this frame can reach Xrm.Utility.lookupObjects.
            firmLookup: createFirmLookup(undefined, getXrm('lookupObjects')?.Utility?.lookupObjects ? createXrmNavigationService() : undefined),
          }
        : null,
    [ready]
  );

  let body: React.ReactNode;
  if (bootstrapError) {
    body = (
      <CodePageMessage
        testId="rulereview-auth-error"
        title="Unable to load Rule Review"
        body="Sign-in could not be verified for this session. Retry, or reopen this page from Spaarke navigation."
        onRetry={retry}
        density="roomy"
      />
    );
  } else if (!services) {
    body = <CodePageSpinner testId="rulereview-loading" label="Loading Rule Review..." density="roomy" />;
  } else {
    body = <App api={services.api} firmLookup={services.firmLookup} initialRuleSetId={initialRuleSetId} />;
  }

  return (
    <CodePageSurface theme={theme} surfaceName="Rule Review">
      {body}
    </CodePageSurface>
  );
}

mountWizardPage('RuleReview', Root);

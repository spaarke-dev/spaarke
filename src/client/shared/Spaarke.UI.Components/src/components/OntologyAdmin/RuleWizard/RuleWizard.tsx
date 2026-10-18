/**
 * RuleWizard - the Ontology admin rule-authoring wizard (spaarke-ontology-platform-r1 task 102; D-22, D-149, D-23, FR-10, FR-58).
 *
 * Seven steps: Basics, Condition, Wording, Actions, Scope and noise, Test, Save. It writes a NEW rule (saved Off) or a NEW VERSION of an
 * existing rule (published). It is UI only: every list it offers comes from the BFF catalog (D-149), every check is the BFF's
 * (`validate`, then the same validation again on the dry run, save and publish), and every refusal is shown on the step and at the field
 * it concerns.
 *
 *   - The CONDITION is built only from the catalog; there is no free-text JSON entry.
 *   - The WORDING offers only the fields `validate` returns as `templateEligibleFields`; the headline is not templated (D-23).
 *   - The ACTIONS and NEXT STEPS come from the rule's lane in the catalog; a code is in at most one of them.
 *   - TEST gates the end: save and publish stay disabled until the test was run on exactly this draft, and ANY edit after a test
 *     makes it stale (the draft key changes).
 *   - PUBLISH asks for confirmation first, with the supersede and re-raise counts the dry run reported.
 *
 * It is an IN-APP wizard: `WizardShell` in modal mode (`SprkModal`, dismiss 'explicit'), so Escape and the backdrop never close it;
 * it is not opened with `navigateTo` and does not use `WizardModal`.
 *
 * Fluent v9 tokens only (ADR-021). React 16-safe.
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { CheckmarkCircleRegular } from '@fluentui/react-icons';
import { ConfirmModal, SprkModal } from '../../SprkModal';
import { WizardShell } from '../../Wizard/WizardShell';
import type {
  IWizardFooterContext,
  IWizardFooterSlots,
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '../../Wizard/wizardShellTypes';
import { fieldErrorsOfValidation, REASON_DRY_RUN_REQUIRED, REASON_POLICY_CODE_TAKEN } from './authoringService';
import type {
  AuthoringCatalog,
  AuthoringFailure,
  FieldErrors,
  OntologyAuthoringService,
  PolicySaved,
  VersionPublished,
} from './authoringService';
import {
  buildDraftBody,
  draftKey,
  draftPayload,
  hasWork,
  newPolicyRequest,
  priorityProblem,
  quietWindowProblem,
  scopeChangeSentence,
  scopeIntentRequest,
  validateRequest,
} from './ruleDraft';
import type { RuleDraft } from './ruleDraft';
import { initialDraftOf } from './ruleWizardTypes';
import type { MatterPicker, NewVersionMode, RuleWizardMode } from './ruleWizardTypes';
import { ActionsStep } from './steps/ActionsStep';
import { BasicsStep } from './steps/BasicsStep';
import { ConditionStep } from './steps/ConditionStep';
import { SaveStep } from './steps/SaveStep';
import { ScopeNoiseStep } from './steps/ScopeNoiseStep';
import { NO_TEST_RUN, TestStep } from './steps/TestStep';
import type { TestRun } from './steps/TestStep';
import { WordingStep } from './steps/WordingStep';
import type { StepBaseProps } from './stepCommon';
import { RULE_WIZARD_STEPS, STEP_FIELDS, stepLabel, stepOfField } from './wizardSteps';
import type { RuleWizardStepId } from './wizardSteps';

// ---- public contract --------------------------------------------------------------------------------------------------------

/**
 * What the wizard did, for the host to refresh what it shows. Null from `onClose` when nothing was written.
 *
 * `unknown` is a write that was attempted and ended without a proven outcome (or a policy that was created Off but not finished): the
 * host must reload what it shows, because the rule may or may not exist now.
 */
export type RuleWizardResult =
  | { kind: 'saved' | 'published'; policyId: string; versionNumber: number }
  | { kind: 'unknown'; policyId: string | null; versionNumber: null };

export interface RuleWizardProps {
  service: OntologyAuthoringService;
  mode: RuleWizardMode;
  /** The `--sprk-ui-scale` factor the host passes to `scaleTheme` (modal sizing). Default 1. */
  uiScale?: number;
  /**
   * Task 102b (D-165): the Scope and noise step's matter picker (the subject it belongs to and a search as the caller). Without it the
   * rule keeps the scope it has (a new rule: all matters); the limit can still be removed.
   */
  matterPicker?: MatterPicker;
  /** The wizard was closed. `result` is set only when a rule or version was written. */
  onClose: (result: RuleWizardResult | null) => void;
}

// ---- state shapes -----------------------------------------------------------------------------------------------------------

/** What `validate` said, for the draft it was asked about, from the step that asked. */
interface ValidationState {
  key: string;
  step: RuleWizardStepId;
  errors: FieldErrors;
  /** Set when the call itself failed (not a refusal of the draft). */
  failure: string | null;
}

/** What a dry run, save or publish refused with, for the draft it was sent for. */
interface RefusalState {
  key: string;
  errors: FieldErrors;
}

/** The templateEligibleFields for one compiled body. Keyed by the body, so editing the wording never discards them. */
interface EligibleState {
  body: string;
  fields: string[];
}

/** The fields whose refusal keeps a step from being left, by the step that asked. */
const GATE_FIELDS: Readonly<Record<RuleWizardStepId, readonly string[]>> = {
  basics: [],
  condition: ['ruleType', 'ruleBody'],
  wording: ['shortHeadline', 'messageTemplate', 'proposedAction'],
  actions: ['decisionPlan'],
  // The quiet window is part of the rule body, so a body refusal on this step is the quiet window's.
  scope: ['matterId', 'priority', 'countsTowardSuppression', 'ruleBody'],
  test: [],
  save: [],
};

const useStyles = makeStyles({
  notices: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
  },
  problems: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalL,
  },
  centered: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    columnGap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalXL,
  },
});

function titleOf(mode: RuleWizardMode): string {
  return mode.kind === 'new' ? 'New rule' : `New version of ${mode.code}`;
}

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

/** The errors of `source` for the fields `step` shows. */
function pickFields(source: FieldErrors, shown: (field: string) => boolean): FieldErrors {
  const picked: FieldErrors = {};
  for (const [field, messages] of Object.entries(source)) {
    if (shown(field)) {
      picked[field] = messages;
    }
  }
  return picked;
}

/** What a refusal puts on a field: the BFF's own field errors, or the code-taken conflict on the code field. */
function refusalErrorsOf(failure: AuthoringFailure): FieldErrors {
  if (Object.keys(failure.fieldErrors).length > 0) {
    return failure.fieldErrors;
  }
  return failure.reasonCode === REASON_POLICY_CODE_TAKEN ? { policyCode: [failure.message] } : {};
}

// ---- the catalog gate -------------------------------------------------------------------------------------------------------

type CatalogLoad =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; catalog: AuthoringCatalog };

/**
 * Loads the catalog, then opens the wizard on it. The catalog is the source of every list, so the wizard does not open without it; a
 * catalog that cannot be read is an error with a retry, never a wizard with empty lists.
 */
export const RuleWizard: React.FC<RuleWizardProps> = props => {
  const { service, mode, uiScale, onClose } = props;
  const styles = useStyles();
  const [load, setLoad] = React.useState<CatalogLoad>({ status: 'loading' });
  const [attempt, setAttempt] = React.useState(0);
  const mounted = React.useRef(true);

  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  React.useEffect(() => {
    let current = true;
    setLoad({ status: 'loading' });
    void service.getCatalog().then(answer => {
      if (!current || !mounted.current) {
        return;
      }
      setLoad(answer.ok ? { status: 'ready', catalog: answer.data } : { status: 'error', message: answer.message });
    });
    return () => {
      current = false;
    };
  }, [service, attempt]);

  if (load.status === 'ready') {
    return <RuleWizardBody {...props} catalog={load.catalog} />;
  }

  return (
    <SprkModal
      open
      onClose={() => onClose(null)}
      title={titleOf(mode)}
      size="sm"
      dismiss="explicit"
      maximizable={false}
      uiScale={uiScale}
      footerStart={
        <Button appearance="secondary" onClick={() => onClose(null)} data-testid="rule-wizard-catalog-cancel">
          Cancel
        </Button>
      }
      footer={
        load.status === 'error' ? (
          <Button appearance="primary" onClick={() => setAttempt(a => a + 1)} data-testid="rule-wizard-catalog-retry">
            Try again
          </Button>
        ) : undefined
      }
    >
      {load.status === 'loading' ? (
        <div className={styles.centered} data-testid="rule-wizard-loading">
          <Spinner size="small" label="Loading what a rule can use" />
        </div>
      ) : (
        <MessageBar intent="error" data-testid="rule-wizard-catalog-error">
          <MessageBarBody>{load.message}</MessageBarBody>
        </MessageBar>
      )}
    </SprkModal>
  );
};

RuleWizard.displayName = 'RuleWizard';

// ---- the wizard -------------------------------------------------------------------------------------------------------------

interface RuleWizardBodyProps extends RuleWizardProps {
  catalog: AuthoringCatalog;
}

const RuleWizardBody: React.FC<RuleWizardBodyProps> = ({ service, mode, uiScale, matterPicker, onClose, catalog }) => {
  const styles = useStyles();

  const initialRef = React.useRef<RuleDraft | null>(null);
  if (initialRef.current === null) {
    initialRef.current = initialDraftOf(catalog, mode);
  }
  const initial = initialRef.current;

  const [draft, setDraft] = React.useState<RuleDraft>(initial);
  const [validation, setValidation] = React.useState<ValidationState | null>(null);
  const [eligible, setEligible] = React.useState<EligibleState | null>(null);
  const [refusal, setRefusal] = React.useState<RefusalState | null>(null);
  const [test, setTest] = React.useState<TestRun>(NO_TEST_RUN);
  const [checking, setChecking] = React.useState(false);
  /** The write ended without a proven outcome, or the policy was created but not finished: nothing more is sent from this wizard. */
  const [writeLock, setWriteLock] = React.useState<string | null>(null);
  /** The policy a failed save left Off, when the BFF named one; handed to the host on close. */
  const lockedPolicyId = React.useRef<string | null>(null);
  /** A save or publish is in flight. The modal's own close control stays live, so closing is ignored until the write has an answer. */
  const writing = React.useRef(false);
  const [result, setResult] = React.useState<RuleWizardResult | null>(null);
  const [discardOpen, setDiscardOpen] = React.useState(false);
  const [publishAsk, setPublishAsk] = React.useState<((confirmed: boolean) => void) | null>(null);

  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const key = draftKey(draft);
  const keyRef = React.useRef(key);
  keyRef.current = key;

  const built = buildDraftBody(draft, catalog);
  const policyId = mode.kind === 'version' ? mode.policyId : null;
  const finished = result !== null;
  const locked = finished || writeLock !== null;

  const testCurrent = test.status === 'done' && test.forKey === key && test.result !== null;
  const testResult = testCurrent ? test.result : null;
  const testPassed = testResult !== null && testResult.recorded;
  const ready = built.ruleBody !== null && draft.lane !== '' && draft.messageTemplate.trim() !== '';

  const footerContext = React.useRef<IWizardFooterContext | null>(null);

  // ---- editing ----------------------------------------------------------------------------------------------------------------

  const onChange = React.useCallback((change: (current: RuleDraft) => RuleDraft) => setDraft(change), []);

  // ---- what each step shows ---------------------------------------------------------------------------------------------------

  const errorsFor = (step: RuleWizardStepId): FieldErrors => {
    const shown = (field: string): boolean =>
      step === 'save' ? stepOfField(field) === 'save' : STEP_FIELDS[step].includes(field);
    const merged: FieldErrors = {};
    const add = (source: FieldErrors) => {
      for (const [field, messages] of Object.entries(source)) {
        merged[field] = field in merged ? [...merged[field], ...messages] : messages;
      }
    };
    if (refusal !== null && refusal.key === key) {
      add(pickFields(refusal.errors, shown));
    }
    if (validation !== null && validation.key === key && validation.step === step) {
      add(pickFields(validation.errors, f => shown(f) || GATE_FIELDS[step].includes(f)));
    }
    return merged;
  };

  const baseProps = (step: RuleWizardStepId): StepBaseProps => ({
    catalog,
    draft,
    onChange,
    errors: errorsFor(step),
    locked,
  });

  // ---- navigation -------------------------------------------------------------------------------------------------------------

  const goTo = (target: RuleWizardStepId) => {
    const context = footerContext.current;
    if (context === null) {
      return;
    }
    const from = RULE_WIZARD_STEPS.findIndex(s => s.id === context.currentStepId);
    const to = RULE_WIZARD_STEPS.findIndex(s => s.id === target);
    for (let at = from; at > to; at -= 1) {
      context.goBack();
    }
    for (let at = from; at < to; at += 1) {
      context.goNext();
    }
  };

  const canLeave = (step: RuleWizardStepId): boolean => {
    switch (step) {
      case 'basics':
        return (
          mode.kind === 'version' || (draft.policyCode.trim() !== '' && draft.name.trim() !== '' && draft.lane !== '')
        );
      case 'condition':
      case 'wording':
      case 'actions':
        return built.ruleBody !== null;
      case 'scope':
        return (
          built.ruleBody !== null &&
          priorityProblem(draft.priority) === null &&
          quietWindowProblem(draft.quietWindowDays, catalog) === null
        );
      case 'test':
        return testPassed;
      case 'save':
        return testPassed && !locked;
    }
  };

  /** Ask the BFF to validate the draft as it stands, for the step being left. True when that step's own fields were accepted. */
  const validateFor = async (step: RuleWizardStepId): Promise<boolean> => {
    const body = built.ruleBody;
    if (body === null) {
      return false;
    }
    const asked = key;
    setChecking(true);
    const answer = await service.validate(validateRequest(draft, body, policyId));
    if (!mounted.current) {
      return false;
    }
    setChecking(false);
    if (keyRef.current !== asked) {
      // Edited while the question was out: the answer is for a draft that no longer exists.
      return false;
    }
    if (!answer.ok) {
      setValidation({ key: asked, step, errors: answer.fieldErrors, failure: answer.message });
      return false;
    }
    const errors = fieldErrorsOfValidation(answer.data);
    setEligible({ body, fields: answer.data.templateEligibleFields });
    setValidation({ key: asked, step, errors, failure: null });
    return GATE_FIELDS[step].every(field => !(field in errors) || errors[field].length === 0);
  };

  const advance = async (context: IWizardFooterContext) => {
    const step = context.currentStepId as RuleWizardStepId | undefined;
    if (step === undefined) {
      return;
    }
    if (step === 'condition' || step === 'wording' || step === 'actions' || step === 'scope') {
      if (!(await validateFor(step))) {
        return;
      }
    }
    if (mounted.current) {
      context.goNext();
    }
  };

  // ---- the test ---------------------------------------------------------------------------------------------------------------

  const runTest = async () => {
    const body = built.ruleBody;
    if (body === null || !ready) {
      return;
    }
    const asked = key;
    setRefusal(null);
    setTest({ status: 'running', forKey: asked, result: null, failure: null });
    const answer = await service.dryRun({
      ...(policyId === null ? { lane: draft.lane } : { policyId }),
      ruleType: draft.condition.ruleType,
      ruleBody: body,
      messageTemplate: draft.messageTemplate,
      // Task 102b (DI-112): the test runs within the same scope the save or publish will have, so its counts are exact. Fix round 1
      // (F4): said explicitly, a matter or all matters.
      ...scopeIntentRequest(draft),
    });
    if (!mounted.current) {
      return;
    }
    if (answer.ok) {
      setTest({ status: 'done', forKey: asked, result: answer.data, failure: null });
    } else {
      setTest({ status: 'failed', forKey: asked, result: null, failure: answer.message });
      const errors = refusalErrorsOf(answer);
      if (Object.keys(errors).length > 0) {
        setRefusal({ key: asked, errors });
      }
    }
  };

  // ---- finishing --------------------------------------------------------------------------------------------------------------

  const askToPublish = (): Promise<boolean> =>
    new Promise<boolean>(resolve => {
      setPublishAsk(() => resolve);
    });

  const answerPublish = (confirmed: boolean) => {
    const resolve = publishAsk;
    setPublishAsk(null);
    if (resolve !== null) {
      resolve(confirmed);
    }
  };

  /**
   * A refused or failed write: keep the wizard open, place the refusal on its fields, and stop sending when the outcome is unknown.
   * The message is shown ONCE: in the banner (a field refusal) or the write lock when either is set, and by the shell's own error
   * bar (the thrown message) only when neither is.
   */
  const failWrite = (failure: AuthoringFailure, asked: string): undefined => {
    if (!mounted.current) {
      return undefined;
    }
    if (failure.reasonCode === REASON_DRY_RUN_REQUIRED) {
      // The BFF no longer holds the proof for this draft: the test on screen is spent, whatever the draft says.
      setTest(current => ({ ...current, forKey: null, expired: true }));
    }
    const errors = refusalErrorsOf(failure);
    const hasErrors = Object.keys(errors).length > 0;
    const locks = failure.outcomeUnknown || failure.policyId !== null;
    setRefusal(hasErrors ? { key: asked, errors } : null);
    if (locks) {
      lockedPolicyId.current = failure.policyId;
      setWriteLock(failure.message);
    }
    if (hasErrors || locks) {
      return undefined;
    }
    throw new Error(failure.message);
  };

  const finishNew = async (body: string, asked: string): Promise<IWizardSuccessConfig | void> => {
    const answer = await service.savePolicy(newPolicyRequest(draft, body));
    if (!answer.ok) {
      return failWrite(answer, asked);
    }
    const saved: PolicySaved = answer.data;
    setResult({ kind: 'saved', policyId: saved.policyId, versionNumber: saved.versionNumber });
    return {
      icon: <CheckmarkCircleRegular fontSize={48} />,
      title: 'Rule saved',
      body: (
        <Text data-testid="rule-wizard-saved">
          {draft.policyCode.trim()} was saved as version {saved.versionNumber}. It is Off: nothing is raised until you
          turn it On from its page.
        </Text>
      ),
      actions: (
        <Button
          appearance="primary"
          onClick={() => onClose({ kind: 'saved', policyId: saved.policyId, versionNumber: saved.versionNumber })}
        >
          Done
        </Button>
      ),
    };
  };

  const finishVersion = async (
    current: NewVersionMode,
    body: string,
    asked: string
  ): Promise<IWizardSuccessConfig | void> => {
    const confirmed = await askToPublish();
    if (!confirmed) {
      return undefined;
    }
    const answer = await service.publishVersion(current.policyId, {
      draft: draftPayload(draft, body),
      ...scopeIntentRequest(draft),
    });
    if (!answer.ok) {
      return failWrite(answer, asked);
    }
    const published: VersionPublished = answer.data;
    setResult({ kind: 'published', policyId: published.policyId, versionNumber: published.versionNumber });
    return {
      icon: <CheckmarkCircleRegular fontSize={48} />,
      title: 'Version published',
      body: (
        <Text data-testid="rule-wizard-published">
          Version {published.versionNumber} of {current.code} is now in force. {describeReevaluation(published)}
        </Text>
      ),
      actions: (
        <Button
          appearance="primary"
          onClick={() =>
            onClose({ kind: 'published', policyId: published.policyId, versionNumber: published.versionNumber })
          }
        >
          Done
        </Button>
      ),
    };
  };

  const onFinish = async (): Promise<IWizardSuccessConfig | void> => {
    const body = built.ruleBody;
    if (body === null || !testPassed || locked) {
      throw new Error('Run the test on this rule, exactly as it is, before you save it.');
    }
    setRefusal(null);
    const asked = key;
    writing.current = true;
    try {
      return await (mode.kind === 'new' ? finishNew(body, asked) : finishVersion(mode, body, asked));
    } finally {
      writing.current = false;
    }
  };

  // ---- closing ----------------------------------------------------------------------------------------------------------------

  const handleClose = () => {
    if (writing.current) {
      // A write is out and its answer is not in: closing now would drop the answer and the admin's chance to see it.
      return;
    }
    if (result !== null) {
      onClose(result);
    } else if (writeLock !== null) {
      // A write was attempted and its outcome is not proven: there may be a rule now, so "nothing has been saved" would be untrue. Close
      // without the discard question and tell the host to reload.
      onClose({ kind: 'unknown', policyId: lockedPolicyId.current, versionNumber: null });
    } else if (hasWork(draft, initial)) {
      setDiscardOpen(true);
    } else {
      onClose(null);
    }
  };

  // ---- the steps --------------------------------------------------------------------------------------------------------------

  const eligibleFields = eligible !== null && eligible.body === built.ruleBody ? eligible.fields : null;

  const steps: IWizardStepConfig[] = [
    {
      id: 'basics',
      label: 'Basics',
      canAdvance: () => canLeave('basics'),
      renderContent: () => <BasicsStep {...baseProps('basics')} mode={mode} />,
    },
    {
      id: 'condition',
      label: 'Condition',
      canAdvance: () => canLeave('condition'),
      renderContent: () => <ConditionStep {...baseProps('condition')} />,
    },
    {
      id: 'wording',
      label: 'Wording',
      canAdvance: () => canLeave('wording'),
      renderContent: () => (
        <WordingStep {...baseProps('wording')} eligibleFields={eligibleFields} checking={checking} />
      ),
    },
    {
      id: 'actions',
      label: 'Actions',
      canAdvance: () => canLeave('actions'),
      renderContent: () => <ActionsStep {...baseProps('actions')} />,
    },
    {
      id: 'scope',
      label: 'Scope and noise',
      canAdvance: () => canLeave('scope'),
      renderContent: () => <ScopeNoiseStep {...baseProps('scope')} mode={mode} matterPicker={matterPicker} />,
    },
    {
      id: 'test',
      label: 'Test',
      canAdvance: () => canLeave('test'),
      renderContent: () => (
        <TestStep
          mode={mode}
          ready={ready}
          run={test}
          isCurrent={testCurrent}
          onRun={() => void runTest()}
          locked={locked}
        />
      ),
    },
    {
      id: 'save',
      label: 'Save',
      canAdvance: () => canLeave('save'),
      renderContent: () => <SaveStep {...baseProps('save')} mode={mode} testResult={testResult} />,
    },
  ];

  /** Every step but the last has its own Back / Next: Next asks the BFF about the step being left before it moves on. */
  const footer = (context: IWizardFooterContext): IWizardFooterSlots | null => {
    footerContext.current = context;
    if (context.isLastStep) {
      return null;
    }
    const step = context.currentStepId as RuleWizardStepId | undefined;
    return {
      end: (
        <>
          {checking ? <Spinner size="tiny" /> : null}
          {context.isFirstStep ? null : (
            <Button appearance="secondary" onClick={context.goBack} disabled={checking} data-testid="rule-wizard-back">
              Back
            </Button>
          )}
          <Button
            appearance="primary"
            onClick={() => void advance(context)}
            disabled={checking || step === undefined || !canLeave(step)}
            data-testid="rule-wizard-next"
          >
            Next
          </Button>
        </>
      ),
    };
  };

  // ---- what is shown above every step -----------------------------------------------------------------------------------------

  const refused = refusal !== null && refusal.key === key ? refusal.errors : {};
  const problems = Object.entries(refused).map(([field, messages]) => ({ field, step: stepOfField(field), messages }));
  const callFailure = validation !== null && validation.key === key ? validation.failure : null;
  const hasNotices = problems.length > 0 || callFailure !== null || writeLock !== null;

  const statusBar = hasNotices ? (
    <div className={styles.notices} data-testid="rule-wizard-notices">
      {writeLock !== null ? (
        <MessageBar intent="warning" role="alert" data-testid="rule-wizard-write-lock">
          <MessageBarBody>
            {writeLock} Nothing more is sent from this wizard; close it and check the Rules list before you try again.
          </MessageBarBody>
        </MessageBar>
      ) : null}
      {callFailure !== null ? (
        <MessageBar intent="error" role="alert" data-testid="rule-wizard-validate-failure">
          <MessageBarBody>{callFailure}</MessageBarBody>
        </MessageBar>
      ) : null}
      {problems.length > 0 ? (
        <MessageBar intent="error" role="alert" data-testid="rule-wizard-problems">
          <MessageBarBody>
            <ul className={styles.problems}>
              {problems.map(problem => (
                <li key={problem.field}>
                  <Button
                    appearance="transparent"
                    size="small"
                    onClick={() => goTo(problem.step)}
                    data-testid={`rule-wizard-problem-${problem.field}`}
                  >
                    {stepLabel(problem.step)}
                  </Button>{' '}
                  {problem.messages.join(' ')}
                </li>
              ))}
            </ul>
          </MessageBarBody>
        </MessageBar>
      ) : null}
    </div>
  ) : undefined;

  const publishMessage = (current: NewVersionMode): React.ReactNode => {
    const counts = testResult;
    const supersede = counts === null ? 0 : counts.wouldSupersede;
    const reraise = counts === null ? 0 : counts.wouldReraise;
    // Fix round 1 (F4): the scope in force as the test read it (the BFF refuses the publish if it changed since), else as the page showed.
    const stored =
      counts !== null && counts.storedMatterId !== undefined
        ? {
            matterId: counts.storedMatterId,
            matterName:
              counts.storedMatterName ??
              (counts.storedMatterId !== null && counts.storedMatterId === current.scope.matterId
                ? current.scope.matter
                : null),
          }
        : { matterId: current.scope.matterId, matterName: current.scope.matter };
    const scopeChange = scopeChangeSentence(stored, draft);
    return (
      <span data-testid="rule-wizard-publish-confirm">
        Publishing makes this the version in force for {current.code}. {plural(supersede, 'open item', 'open items')}{' '}
        from the current version will be replaced and {plural(reraise, 'item', 'items')} will be raised again, as the
        test showed.
        {counts !== null && counts.policyEnabled === false
          ? ' The rule is Off, so nothing is replaced or raised until it is turned On.'
          : ''}
        {scopeChange !== null ? <span data-testid="rule-wizard-publish-scope-change"> {scopeChange}</span> : null}
      </span>
    );
  };

  return (
    <>
      <WizardShell
        open
        title={titleOf(mode)}
        steps={steps}
        onClose={handleClose}
        onFinish={onFinish}
        finishLabel={mode.kind === 'new' ? 'Save rule' : 'Publish'}
        finishingLabel="Working"
        stayOpenOnFinish
        uiScale={uiScale}
        statusBar={statusBar}
        footer={footer}
      />
      <ConfirmModal
        open={discardOpen}
        onClose={() => setDiscardOpen(false)}
        onConfirm={() => {
          setDiscardOpen(false);
          onClose(null);
        }}
        title="Discard this rule?"
        message="What you have entered will be lost. Nothing has been saved."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        destructive
        uiScale={uiScale}
      />
      <ConfirmModal
        open={publishAsk !== null && mode.kind === 'version'}
        onClose={() => answerPublish(false)}
        onConfirm={() => answerPublish(true)}
        title="Publish this version?"
        message={mode.kind === 'version' ? publishMessage(mode) : ''}
        confirmLabel="Publish"
        cancelLabel="Not yet"
        uiScale={uiScale}
      />
    </>
  );
};

RuleWizardBody.displayName = 'RuleWizardBody';

/** What the re-evaluation after a publish did, in a sentence. */
function describeReevaluation(published: VersionPublished): string {
  switch (published.reevaluation) {
    case 'completed':
      return `${plural(published.supersededClosed, 'open item was', 'open items were')} replaced and ${plural(published.raised, 'item was', 'items were')} raised.`;
    case 'notEvaluated':
      return 'The rule is Off, so nothing was re-checked.';
    default:
      return 'The re-check could not finish just now; the nightly pass will retry.';
  }
}

/**
 * RegistrationPage - the profile form a signed-in workforce contact completes once
 * (spaarke-SPA-external-access-platform-r3 task 058; ADR-028 Amendment A9; design notes/a9-bff-design.md section 5).
 * It replaces the 056 join page, which handed the browser to a separate registration service: the customer BFF now
 * registers the contact itself (`POST /api/v1/external/registration`).
 *
 * The gate (`WorkforceHomeGate`) shows it after `GET /api/v1/external/me` answered `registration_required` and the silent
 * link did not apply. The email is read-only and comes from the BFF's prefill (the token decides it, never this form).
 * Sub-states, each a fixed screen (a server message is never shown):
 *
 *   form                           first name, last name, department (optional); validated here and by the BFF
 *   existing record (409)          "We found your existing record" - Confirm resubmits with `confirmExistingRecord: true`
 *   licensed user (403)            "Link my account": the Spaarke sign-in popup opens ONLY from that click; a closed
 *                                  popup leaves the page as it was, with an explanation, and sends nothing
 *   contact conflict (403)         an administrator must resolve the record
 *   email missing (403)            the sign-in carried no email address
 *   not invited (403)              no current invitation names this sign-in: ask for a new invitation
 *   error                          anything else: "Try again" repeats the same request
 *
 * ADR-021: Fluent v9 semantic tokens only. ADR-012: the shared `EmptyState` shell renders the message states.
 */
import * as React from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  ErrorCircle48Regular,
  Link48Regular,
  PersonAdd48Regular,
  Search48Regular,
  Warning48Regular,
} from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import { acquireSpaarkeGuestToken, GuestLinkTokenError } from '../auth/msal-auth';
import { getActiveBackend } from '../auth/backend-selection';
import {
  MAX_DEPARTMENT_LENGTH,
  MAX_NAME_LENGTH,
  linkWorkforceAccount,
  submitRegistration,
  validateRegistrationInput,
  type RegistrationFieldErrors,
  type RegistrationPrefill,
  type RegistrationRefusal,
} from '../auth/registration-client';
import { BffAccessState } from '../components/auth/BffAccessState';

export interface RegistrationPageProps {
  /** What the BFF knows about the caller (from the `registration_required` refusal). The email is shown read-only. */
  prefill: RegistrationPrefill;
  /** The home account's username (UPN), the login hint for the link popup; absent when none is known. */
  loginHint?: string;
  /** Registration or linking completed: the gate re-reads `/me`. */
  onDone: () => void;
}

type View =
  | { kind: 'form'; formError?: string }
  | { kind: 'submitting' }
  | { kind: 'existing' }
  | { kind: 'linkRequired'; busy: boolean; notice?: 'cancelled' | 'failed' | 'unavailable' }
  | { kind: 'conflict' }
  | { kind: 'emailMissing' }
  | { kind: 'notInvited' }
  | { kind: 'linkBlocked' }
  | { kind: 'signin' }
  /**
   * A registration submit failed in a way that may pass on a retry (5xx / network). `confirm` is whether that submit carried
   * `confirmExistingRecord`: "Try again" repeats the SAME submit, so a person who already confirmed the existing record is
   * not sent back to the confirm screen. (A link failure never lands here: it keeps its own "Link my account" retry.)
   */
  | { kind: 'error'; confirm: boolean };

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flex: '1 1 auto',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    boxSizing: 'border-box',
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  description: { maxWidth: '28rem' },
  iconNeutral: { color: tokens.colorNeutralForeground3 },
  iconBrand: { color: tokens.colorBrandForeground1 },
  iconWarning: { color: tokens.colorStatusWarningForeground1 },
  iconDanger: { color: tokens.colorStatusDangerForeground1 },
  form: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    width: '100%',
    maxWidth: '26rem',
  },
  lead: { color: tokens.colorNeutralForeground3 },
  actions: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'center',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalS,
  },
  notice: { maxWidth: '28rem', textAlign: 'left' },
});

/** The administrator-must-resolve screen for a refused link; shared with the gate (a refusal before the form). */
export const LinkBlockedNotice: React.FC = () => {
  const s = useStyles();
  return (
    <div className={s.root}>
      <EmptyState
        icon={<Link48Regular className={s.iconDanger} />}
        heading="We could not link your account"
        headingAs="h1"
        description="An administrator must resolve your account link before you can continue. Contact your Spaarke administrator."
        descriptionClassName={s.description}
      />
    </div>
  );
};

function refusalView(reason: RegistrationRefusal, confirm: boolean): View {
  switch (reason) {
    case 'existing_record_found':
      return { kind: 'existing' };
    case 'licensed_user_link_required':
      return { kind: 'linkRequired', busy: false };
    case 'registration_contact_conflict':
    case 'blocked': // any other deny code: the server refused on purpose, so there is no retry - an administrator must act
      return { kind: 'conflict' };
    case 'registration_email_missing':
      return { kind: 'emailMissing' };
    case 'contact_not_invited':
      return { kind: 'notInvited' };
    case 'invalid':
      return { kind: 'form', formError: 'Check the values you entered and try again.' };
    case 'signin':
      return { kind: 'signin' };
    case 'unavailable':
    default:
      return { kind: 'error', confirm };
  }
}

export const RegistrationPage: React.FC<RegistrationPageProps> = ({ prefill, loginHint, onDone }) => {
  const s = useStyles();
  const [view, setView] = React.useState<View>({ kind: 'form' });
  const [firstName, setFirstName] = React.useState(prefill.givenName);
  const [lastName, setLastName] = React.useState(prefill.familyName);
  const [department, setDepartment] = React.useState('');
  const [errors, setErrors] = React.useState<RegistrationFieldErrors>({});
  const rootRef = React.useRef<HTMLDivElement>(null);
  const mounted = React.useRef(true);

  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  // On every state change move focus to the heading (a real h1), so a keyboard / screen-reader user lands on the new state.
  const viewKind = view.kind === 'linkRequired' ? `linkRequired:${view.notice ?? ''}` : view.kind;
  React.useEffect(() => {
    if (view.kind === 'submitting') return;
    const heading = rootRef.current?.querySelector('h1');
    if (heading) {
      heading.tabIndex = -1;
      heading.focus();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- keyed by the state that changed, not the whole view object
  }, [viewKind]);

  const submit = React.useCallback(
    async (confirmExistingRecord: boolean) => {
      const found = validateRegistrationInput({ firstName, lastName, department });
      setErrors(found);
      if (Object.keys(found).length > 0) {
        setView({ kind: 'form' });
        return;
      }
      setView({ kind: 'submitting' });
      const result = await submitRegistration({
        firstName,
        lastName,
        department,
        ...(confirmExistingRecord ? { confirmExistingRecord: true } : {}),
      });
      if (!mounted.current) return;
      if (result.kind === 'ok') {
        onDone();
        return;
      }
      setView(refusalView(result.reason, confirmExistingRecord));
    },
    [firstName, lastName, department, onDone]
  );

  /** The link popup. Called ONLY from the "Link my account" click (a user gesture). */
  const link = React.useCallback(async () => {
    setView({ kind: 'linkRequired', busy: true });
    let token: string | null;
    try {
      token = await acquireSpaarkeGuestToken('popup', loginHint ?? '', getActiveBackend().bffScope);
    } catch (err) {
      if (!mounted.current) return;
      const reason = err instanceof GuestLinkTokenError && err.reason === 'cancelled' ? 'cancelled' : 'failed';
      setView({ kind: 'linkRequired', busy: false, notice: reason });
      return;
    }
    if (!mounted.current) return;
    if (!token) {
      setView({ kind: 'linkRequired', busy: false, notice: 'failed' });
      return;
    }
    const result = await linkWorkforceAccount(token);
    if (!mounted.current) return;
    if (result.kind === 'ok') {
      onDone();
      return;
    }
    switch (result.reason) {
      case 'signin':
        setView({ kind: 'signin' });
        break;
      case 'unavailable':
        setView({ kind: 'linkRequired', busy: false, notice: 'unavailable' });
        break;
      case 'no_systemuser':
      case 'blocked':
      default:
        setView({ kind: 'linkBlocked' });
    }
  }, [loginHint, onDone]);

  const message = (icon: React.ReactElement, heading: string, description: string, footer?: React.ReactNode) => (
    <div className={s.root} ref={rootRef}>
      <EmptyState
        icon={icon}
        heading={heading}
        headingAs="h1"
        description={description}
        descriptionClassName={s.description}
        footer={footer}
      />
    </div>
  );

  switch (view.kind) {
    case 'existing':
      return message(
        <Search48Regular className={s.iconBrand} />,
        'We found your existing record',
        `We found a record for ${prefill.email}. Confirm that it is yours to continue.`,
        <div className={s.actions}>
          <Button appearance="primary" onClick={() => void submit(true)}>
            Confirm and continue
          </Button>
          <Button appearance="secondary" onClick={() => setView({ kind: 'form' })}>
            Back
          </Button>
        </div>
      );

    case 'linkRequired': {
      const notice =
        view.notice === 'cancelled'
          ? 'The sign-in window was closed before it finished. Nothing was changed.'
          : view.notice === 'unavailable'
            ? 'Linking is not available right now. Try again in a moment.'
            : view.notice === 'failed'
              ? 'The sign-in could not be completed. Try again.'
              : null;
      return message(
        <Link48Regular className={s.iconBrand} />,
        'Link your Spaarke account',
        'This address belongs to a Spaarke user. Link your accounts to continue; a sign-in window opens when you select the button.',
        <div className={s.actions}>
          {notice && (
            <MessageBar intent="warning" className={s.notice}>
              <MessageBarBody>{notice}</MessageBarBody>
            </MessageBar>
          )}
          <Button appearance="primary" disabled={view.busy} onClick={() => void link()}>
            Link my account
          </Button>
          {view.busy && <Spinner size="tiny" label="Waiting for the sign-in window..." />}
        </div>
      );
    }

    case 'conflict':
      return message(
        <Warning48Regular className={s.iconWarning} />,
        'We cannot finish your registration',
        'An administrator must resolve your record before you can continue. Contact your Spaarke administrator.'
      );

    case 'emailMissing':
      return message(
        <Warning48Regular className={s.iconWarning} />,
        'We could not read your email address',
        'Your sign-in did not include an email address, so we cannot register you. Contact your administrator.'
      );

    case 'notInvited':
      return message(
        <Warning48Regular className={s.iconWarning} />,
        'You need an invitation',
        'This sign-in cannot be registered until you are invited. Ask the person who invited you to send a new invitation.'
      );

    case 'linkBlocked':
      return <LinkBlockedNotice />;

    case 'signin':
      return (
        <div className={s.root}>
          <BffAccessState failure="signin" />
        </div>
      );

    case 'error':
      return message(
        <ErrorCircle48Regular className={s.iconDanger} />,
        'Something went wrong',
        'We could not finish your registration. Please try again.',
        <Button appearance="primary" onClick={() => void submit(view.confirm)}>
          Try again
        </Button>
      );

    case 'submitting':
      return (
        <div className={s.root} ref={rootRef}>
          <Spinner size="large" label="Saving your details..." />
        </div>
      );

    case 'form':
    default:
      return (
        <div className={s.root} ref={rootRef}>
          <form
            className={s.form}
            noValidate
            onSubmit={event => {
              event.preventDefault();
              void submit(false);
            }}
          >
            <PersonAdd48Regular className={s.iconBrand} />
            <Text as="h1" size={600} weight="semibold">
              Complete your profile
            </Text>
            <Text className={s.lead} block>
              Tell us who you are so we can set up your access.
            </Text>
            {view.kind === 'form' && view.formError && (
              <MessageBar intent="error">
                <MessageBarBody>{view.formError}</MessageBarBody>
              </MessageBar>
            )}
            <Field
              label="First name"
              validationState={errors.firstName ? 'error' : 'none'}
              validationMessage={errors.firstName}
            >
              <Input
                value={firstName}
                maxLength={MAX_NAME_LENGTH}
                autoComplete="given-name"
                onChange={(_, data) => setFirstName(data.value)}
              />
            </Field>
            <Field
              label="Last name"
              validationState={errors.lastName ? 'error' : 'none'}
              validationMessage={errors.lastName}
            >
              <Input
                value={lastName}
                maxLength={MAX_NAME_LENGTH}
                autoComplete="family-name"
                onChange={(_, data) => setLastName(data.value)}
              />
            </Field>
            <Field
              label="Department (optional)"
              validationState={errors.department ? 'error' : 'none'}
              validationMessage={errors.department}
            >
              <Input
                value={department}
                maxLength={MAX_DEPARTMENT_LENGTH}
                autoComplete="organization-title"
                onChange={(_, data) => setDepartment(data.value)}
              />
            </Field>
            <Field label="Email">
              <Input value={prefill.email} readOnly type="email" />
            </Field>
            <div className={s.actions}>
              <Button appearance="primary" type="submit">
                Continue
              </Button>
            </div>
          </form>
        </div>
      );
  }
};

export default RegistrationPage;

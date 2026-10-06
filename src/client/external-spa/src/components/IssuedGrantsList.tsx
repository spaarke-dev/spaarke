import * as React from 'react';
import {
  Badge,
  Button,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { listContactGrants, problemMessage, revokeContactGrant, type ContactIssuedGrant } from '../auth/bff-client';
import { accessLevelLabel } from './InviteUserDialog';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalL,
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  who: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
  },
  meta: {
    color: tokens.colorNeutralForeground2,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
});

interface IssuedGrantsListProps {
  /** 'project' | 'matter' | 'workassignment' */
  recordType?: string;
  /** The record */
  recordId: string;
  /** Bump to reload (e.g. after a new grant). */
  refreshKey?: number;
}

/**
 * IssuedGrantsList — the access the CALLER granted on this record, with Revoke (unified-access-control-r2 task 140).
 *
 * Reads GET /api/v1/external/contact-grants (only the caller's own issued grants come back) and revokes through
 * POST /api/v1/external/contact-grants/revoke. A revoke never touches access somebody else gave; when the colleague
 * keeps such access, that is said. Every server refusal is shown with the server's own message.
 */
export const IssuedGrantsList: React.FC<IssuedGrantsListProps> = ({
  recordType = 'project',
  recordId,
  refreshKey = 0,
}) => {
  const styles = useStyles();
  const [grants, setGrants] = React.useState<ContactIssuedGrant[] | null>(null);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const [revoking, setRevoking] = React.useState<string | null>(null);
  const [notice, setNotice] = React.useState<{ intent: 'success' | 'warning' | 'error'; text: string } | null>(null);
  const [reload, setReload] = React.useState(0);

  React.useEffect(() => {
    let cancelled = false;
    setLoadError(null);
    listContactGrants(recordType, recordId)
      .then(response => {
        if (!cancelled) setGrants(response.grants);
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setGrants([]);
          setLoadError(problemMessage(err, 'The access you granted could not be loaded.'));
        }
      });
    return () => {
      cancelled = true;
    };
  }, [recordType, recordId, refreshKey, reload]);

  async function handleRevoke(grant: ContactIssuedGrant): Promise<void> {
    setRevoking(grant.accessRecordId);
    setNotice(null);
    const who = grant.fullName ?? grant.email ?? 'This colleague';
    try {
      const result = await revokeContactGrant(grant.accessRecordId);
      setNotice(
        result.accessRemainsFromOthers
          ? {
              intent: 'warning',
              text: `You removed the access you gave ${who}. They still have access someone else gave them.`,
            }
          : { intent: 'success', text: `${who} no longer has the access you gave them.` }
      );
      setReload(n => n + 1);
    } catch (err: unknown) {
      setNotice({ intent: 'error', text: problemMessage(err, 'The access could not be revoked. Please try again.') });
      // A refusal can mean the grant changed hands meanwhile (409 managed_elsewhere — an internal user took it over while
      // you were revoking, session 27 round 42 item 1) or that part of it was ended: re-read the list, so it shows what
      // is still yours rather than a row Revoke can no longer act on.
      setReload(n => n + 1);
    } finally {
      setRevoking(null);
    }
  }

  return (
    <div className={styles.root} aria-label="Access you granted">
      <Text weight="semibold">Access you granted</Text>

      {notice && (
        <MessageBar intent={notice.intent}>
          <MessageBarBody>{notice.text}</MessageBarBody>
        </MessageBar>
      )}

      {loadError && (
        <MessageBar intent="error">
          <MessageBarBody>{loadError}</MessageBarBody>
        </MessageBar>
      )}

      {grants === null && <Spinner size="tiny" label="Loading..." />}

      {grants !== null && grants.length === 0 && !loadError && (
        <Text className={styles.meta}>You have not given anyone access to this project.</Text>
      )}

      {grants?.map(grant => (
        <div className={styles.row} key={grant.accessRecordId}>
          <div className={styles.who}>
            <Text>{grant.fullName ?? grant.email ?? 'Unknown contact'}</Text>
            <Text size={200} className={styles.meta}>
              {grant.email && grant.fullName ? `${grant.email} · ` : ''}
              {grant.expiryDate ? `until ${grant.expiryDate}` : ''}
            </Text>
          </div>
          <div className={styles.actions}>
            <Badge appearance="tint" color="informative">
              {accessLevelLabel(grant.accessLevel)}
            </Badge>
            <Button
              appearance="subtle"
              size="small"
              disabled={revoking !== null}
              onClick={() => void handleRevoke(grant)}
              aria-label={`Revoke access for ${grant.fullName ?? grant.email ?? 'this colleague'}`}
            >
              {revoking === grant.accessRecordId ? 'Revoking...' : 'Revoke'}
            </Button>
          </div>
        </div>
      ))}
    </div>
  );
};

export default IssuedGrantsList;

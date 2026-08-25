/**
 * ContainerTypeOwnersPanel — list, add, and remove the PEOPLE who administer a container type.
 *
 * Spec FR-C09 / task 027.
 *
 * 🔑 This is NOT the Permissions tab. That tab lists which APPLICATIONS may access containers of this
 * type (Graph `applicationPermissions`); this lists which PEOPLE administer the type itself (Graph
 * `fileStorageContainerType.permissions`). Task 027's POML described the second as superseding part
 * of the first — it does not, and nothing was retired. Keeping them as separate tabs, on separate
 * routes, is what stops the conflation being re-made.
 *
 * ADR-021: Fluent v9 semantic tokens throughout; no hex literals.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
  Input,
  Field,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Badge,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Tooltip,
} from "@fluentui/react-components";
import { Add20Regular, Delete20Regular, Person20Regular } from "@fluentui/react-icons";
import { speApiClient, describeApiError } from "../../services/speApiClient";
import type { ContainerTypeOwner } from "../../types/spe";
import {
  ADMIN_CENTER_OWNER_GUIDANCE,
  ADMIN_CENTER_OWNER_LIMIT,
  LAST_OWNER_WARNING,
  describeOwner,
} from "./containerTypeOwners";

const useStyles = makeStyles({
  root: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalM },
  addRow: { display: "flex", gap: tokens.spacingHorizontalS, alignItems: "flex-end" },
  grow: { flexGrow: 1 },
  ownerRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  ownerText: { display: "flex", flexDirection: "column", minWidth: 0 },
  muted: { color: tokens.colorNeutralForeground3 },
  empty: {
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    textAlign: "center",
  },
});

export interface ContainerTypeOwnersPanelProps {
  containerTypeId: string;
}

export const ContainerTypeOwnersPanel: React.FC<ContainerTypeOwnersPanelProps> = ({
  containerTypeId,
}) => {
  const styles = useStyles();

  const [owners, setOwners] = React.useState<ContainerTypeOwner[] | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [newOwner, setNewOwner] = React.useState("");
  const [saving, setSaving] = React.useState(false);
  const [pendingRemoval, setPendingRemoval] = React.useState<ContainerTypeOwner | null>(null);

  const load = React.useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setOwners(await speApiClient.containerTypes.listOwners(containerTypeId));
    } catch (err) {
      setError(describeApiError(err, "Failed to load owners."));
      // Null, not [] — "we could not load" must stay distinguishable from "there are none".
      setOwners(null);
    } finally {
      setLoading(false);
    }
  }, [containerTypeId]);

  React.useEffect(() => {
    void load();
  }, [load]);

  /*
   * The limit is advisory here, and deliberately so.
   *
   * "Up to three owners" comes from the SharePoint admin center's UX (task 027's POML). It is NOT in
   * Graph's schema — the `permissions` collection is unbounded in both CSDL versions — and it is not
   * stated anywhere in this repo's SPE knowledge corpus. So the UI states where the limit comes from
   * rather than asserting Graph enforces it, and the add path still surfaces a server error, because
   * a client-side guard is not evidence about what the API will accept.
   */
  const atLimit = (owners?.length ?? 0) >= ADMIN_CENTER_OWNER_LIMIT;
  const isLastOwner = (owners?.length ?? 0) === 1;

  const handleAdd = React.useCallback(async () => {
    const identifier = newOwner.trim();
    if (!identifier || saving) return;
    setSaving(true);
    setError(null);
    try {
      await speApiClient.containerTypes.addOwner(containerTypeId, identifier);
      setNewOwner("");
      await load();
    } catch (err) {
      setError(describeApiError(err, "Failed to add the owner."));
    } finally {
      setSaving(false);
    }
  }, [newOwner, saving, containerTypeId, load]);

  const handleRemove = React.useCallback(
    async (owner: ContainerTypeOwner) => {
      setSaving(true);
      setError(null);
      try {
        await speApiClient.containerTypes.removeOwner(containerTypeId, owner.permissionId);
        setPendingRemoval(null);
        await load();
      } catch (err) {
        setError(describeApiError(err, "Failed to remove the owner."));
      } finally {
        setSaving(false);
      }
    },
    [containerTypeId, load],
  );

  return (
    <div className={styles.root}>
      <MessageBar intent="info">
        <MessageBarBody>{ADMIN_CENTER_OWNER_GUIDANCE}</MessageBarBody>
      </MessageBar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Owner management failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.addRow}>
        <Field
          className={styles.grow}
          label="Add an owner"
          hint={
            atLimit
              ? `This container type already has ${ADMIN_CENTER_OWNER_LIMIT} owners.`
              : "Email address (UPN) or directory object ID"
          }
        >
          <Input
            value={newOwner}
            onChange={(_e, d) => setNewOwner(d.value)}
            placeholder="person@contoso.com"
            disabled={saving || atLimit || owners === null}
            onKeyDown={(e) => {
              if (e.key === "Enter") void handleAdd();
            }}
          />
        </Field>
        <Tooltip
          content={
            atLimit
              ? `The SharePoint admin center allows up to ${ADMIN_CENTER_OWNER_LIMIT} owners. Remove one before adding another.`
              : "Grant ownership"
          }
          relationship="label"
        >
          <span>
            <Button
              appearance="primary"
              icon={<Add20Regular />}
              disabled={saving || atLimit || !newOwner.trim() || owners === null}
              onClick={() => void handleAdd()}
            >
              Add
            </Button>
          </span>
        </Tooltip>
      </div>

      {loading && <Spinner size="tiny" label="Loading owners…" />}

      {!loading && owners !== null && owners.length === 0 && (
        <div className={styles.empty}>
          <Text className={styles.muted}>
            Microsoft Graph reported no owners for this container type.
          </Text>
        </div>
      )}

      {!loading &&
        owners?.map((owner) => {
          const described = describeOwner(owner);
          return (
            <div key={owner.permissionId} className={styles.ownerRow}>
              <div className={styles.ownerText}>
                <Text weight="semibold" truncate>
                  <Person20Regular style={{ verticalAlign: "middle" }} /> {described.primary}
                </Text>
                {described.secondary && (
                  <Text size={200} className={styles.muted} truncate>
                    {described.secondary}
                  </Text>
                )}
              </div>
              <div style={{ display: "flex", gap: tokens.spacingHorizontalXS, alignItems: "center" }}>
                {owner.roles.map((role) => (
                  <Badge key={role} appearance="outline" size="small">
                    {role}
                  </Badge>
                ))}
                <Button
                  appearance="subtle"
                  icon={<Delete20Regular />}
                  disabled={saving}
                  onClick={() => setPendingRemoval(owner)}
                  aria-label={`Remove ${described.primary}`}
                />
              </div>
            </div>
          );
        })}

      {/*
        Removal confirmation. Always confirmed, but the LAST owner carries an extra consequence
        warning — removing it can leave the container type with nobody able to administer it, and
        that is not obviously reversible from inside this app.
      */}
      <Dialog open={pendingRemoval !== null} onOpenChange={(_e, d) => !d.open && setPendingRemoval(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Remove this owner?</DialogTitle>
            <DialogContent>
              <Text block>
                {pendingRemoval ? describeOwner(pendingRemoval).primary : ""} will no longer be able to
                administer this container type.
              </Text>
              {isLastOwner && (
                <MessageBar intent="warning" style={{ marginTop: tokens.spacingVerticalS }}>
                  <MessageBarBody>
                    <MessageBarTitle>This is the last owner</MessageBarTitle>
                    {LAST_OWNER_WARNING}
                  </MessageBarBody>
                </MessageBar>
              )}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setPendingRemoval(null)} disabled={saving}>
                Cancel
              </Button>
              <Button
                appearance="primary"
                disabled={saving}
                onClick={() => pendingRemoval && void handleRemove(pendingRemoval)}
              >
                Remove owner
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};

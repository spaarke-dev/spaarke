/**
 * MemoryDialog.tsx — "What the Assistant remembers about you" (UAT 2026-07-21, item #8).
 *
 * A read + point-delete surface over the SHIPPED memory-governance endpoints (no new BFF surface):
 *   - GET    /api/memory/user            → the caller's OWN User-scope memory (MemoryListResponse)
 *   - DELETE /api/memory/user/{itemId}   → forget one item (GDPR point-delete, 204)
 *   - DELETE /api/memory/user            → forget everything (GDPR Art. 17 erase, 204)
 *
 * Ownership is structural server-side (the subject partition IS the caller's systemuserid), so the
 * user only ever sees / deletes their own memory — this component carries no ownership logic.
 *
 * Items split into two groups by provenance: STATED by the user (`source=user`, from the My Assistant
 * questionnaire / seed) vs LEARNED by the Assistant (`source=ai-derived`, captured automatically via
 * `memory.write`). Both feed the per-turn recall fragment, so surfacing + letting the user prune them
 * is the transparency/control half of the memory system.
 *
 * Modal choice (docs/standards/MODAL-DECISION-CRITERIA.md): a plain Fluent v9 Dialog (a review/manage
 * surface, not a record browse). Fluent v9 semantic tokens only (ADR-021) — dark mode adapts.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/Memory/MemoryGovernanceEndpoints.cs — the endpoints consumed
 */

import * as React from "react";
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Spinner,
  Badge,
  Tooltip,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { DeleteRegular, DismissRegular } from "@fluentui/react-icons";

/** One memory item as returned by GET /api/memory/user (mirrors the BFF `MemoryItemDto`). */
interface MemoryItemDto {
  id: string;
  factType: string;
  key: string;
  value: string;
  source: string; // "user" (stated) | "ai-derived" (learned) | …
  confidence: number;
  createdAt: string;
}

interface MemoryListResponse {
  items: MemoryItemDto[];
  count: number;
}

export interface MemoryDialogProps {
  open: boolean;
  onClose: () => void;
  /** @spaarke/auth authenticated fetch (throws ApiError on non-2xx; returns a Response otherwise). */
  authenticatedFetch: (input: string, init?: RequestInit) => Promise<Response>;
  /** BFF origin (no trailing `/api`). */
  bffBaseUrl: string;
}

const useStyles = makeStyles({
  content: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    minWidth: "460px",
    maxWidth: "640px",
    minHeight: "200px",
    maxHeight: "60vh",
    overflowY: "auto",
  },
  intro: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    margin: 0,
  },
  center: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "160px",
    color: tokens.colorNeutralForeground3,
  },
  groupHeader: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: "uppercase",
    letterSpacing: "0.04em",
    margin: `${tokens.spacingVerticalS} 0 ${tokens.spacingVerticalXS}`,
  },
  row: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  rowMain: { flex: 1, minWidth: 0 },
  rowKey: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    wordBreak: "break-word",
  },
  rowValue: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    wordBreak: "break-word",
  },
  rowMeta: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    marginTop: "2px",
  },
  actionsRow: { justifyContent: "space-between" },
});

function isStated(source: string): boolean {
  return (source ?? "").toLowerCase() === "user";
}

export const MemoryDialog: React.FC<MemoryDialogProps> = ({ open, onClose, authenticatedFetch, bffBaseUrl }) => {
  const styles = useStyles();
  const [items, setItems] = React.useState<MemoryItemDto[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [busyId, setBusyId] = React.useState<string | null>(null);

  const load = React.useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await authenticatedFetch(`${bffBaseUrl}/api/memory/user`, { method: "GET" });
      const body = (await res.json()) as MemoryListResponse;
      setItems(Array.isArray(body?.items) ? body.items : []);
    } catch {
      setError("Couldn't load your memory right now. Please try again.");
    } finally {
      setLoading(false);
    }
  }, [authenticatedFetch, bffBaseUrl]);

  // Fetch fresh each time the dialog opens (memory changes as the user chats).
  React.useEffect(() => {
    if (open) void load();
  }, [open, load]);

  const forgetOne = React.useCallback(
    async (id: string) => {
      setBusyId(id);
      try {
        await authenticatedFetch(`${bffBaseUrl}/api/memory/user/${encodeURIComponent(id)}`, { method: "DELETE" });
        setItems((prev) => prev.filter((i) => i.id !== id));
      } catch {
        setError("Couldn't forget that item. Please try again.");
      } finally {
        setBusyId(null);
      }
    },
    [authenticatedFetch, bffBaseUrl]
  );

  const forgetAll = React.useCallback(async () => {
    if (items.length === 0) return;
    if (typeof window !== "undefined" && !window.confirm("Forget everything the Assistant has remembered about you?")) {
      return;
    }
    setBusyId("__all__");
    try {
      await authenticatedFetch(`${bffBaseUrl}/api/memory/user`, { method: "DELETE" });
      setItems([]);
    } catch {
      setError("Couldn't clear your memory. Please try again.");
    } finally {
      setBusyId(null);
    }
  }, [authenticatedFetch, bffBaseUrl, items.length]);

  const stated = items.filter((i) => isStated(i.source));
  const learned = items.filter((i) => !isStated(i.source));

  const renderRow = (item: MemoryItemDto) => (
    <div key={item.id} className={styles.row}>
      <div className={styles.rowMain}>
        <div className={styles.rowKey}>{item.key}</div>
        <div className={styles.rowValue}>{item.value}</div>
        <div className={styles.rowMeta}>
          {item.factType}
          {isStated(item.source) ? "" : ` · confidence ${Math.round((item.confidence ?? 0) * 100)}%`}
        </div>
      </div>
      <Tooltip content="Forget this" relationship="label">
        <Button
          appearance="subtle"
          size="small"
          icon={<DeleteRegular />}
          aria-label={`Forget "${item.key}"`}
          disabled={busyId !== null}
          onClick={() => void forgetOne(item.id)}
        />
      </Tooltip>
    </div>
  );

  return (
    <Dialog open={open} onOpenChange={(_ev, data) => { if (!data.open) onClose(); }} modalType="modal">
      <DialogSurface data-testid="memory-dialog">
        <DialogBody>
          <DialogTitle
            action={
              <Button
                appearance="subtle"
                size="small"
                icon={<DismissRegular />}
                aria-label="Close"
                onClick={onClose}
              />
            }
          >
            What the Assistant remembers about you
          </DialogTitle>
          <DialogContent className={styles.content}>
            <p className={styles.intro}>
              These facts help the Assistant tailor its answers. Remove anything that's wrong or you'd
              rather it not use — it's forgotten immediately.
            </p>

            {loading ? (
              <div className={styles.center}><Spinner size="small" label="Loading your memory…" /></div>
            ) : error ? (
              <div className={styles.center}>{error}</div>
            ) : items.length === 0 ? (
              <div className={styles.center}>
                The Assistant hasn't remembered anything about you yet.
              </div>
            ) : (
              <>
                {stated.length > 0 && (
                  <div>
                    <div className={styles.groupHeader}>
                      Stated by you <Badge appearance="tint" size="small">{stated.length}</Badge>
                    </div>
                    {stated.map(renderRow)}
                  </div>
                )}
                {learned.length > 0 && (
                  <div>
                    <div className={styles.groupHeader}>
                      Learned by the Assistant <Badge appearance="tint" size="small">{learned.length}</Badge>
                    </div>
                    {learned.map(renderRow)}
                  </div>
                )}
              </>
            )}
          </DialogContent>
          <DialogActions className={styles.actionsRow}>
            <Button
              appearance="subtle"
              disabled={items.length === 0 || busyId !== null}
              onClick={() => void forgetAll()}
            >
              Forget everything
            </Button>
            <Button appearance="secondary" onClick={onClose}>Done</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

MemoryDialog.displayName = "MemoryDialog";

export default MemoryDialog;

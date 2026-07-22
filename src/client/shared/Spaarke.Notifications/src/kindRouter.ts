import { isKnownNotificationKind, type NotificationEvent, type NotificationKind } from './types';

/** A per-kind handler registered via `KindRouter.registerHandler`. */
export type NotificationHandler = (event: NotificationEvent) => void;

/**
 * Dispatches incoming notification events to per-kind handlers (spec FR-05).
 *
 * MUST NEVER throw on an unrecognized `kind` — a future reserved kind (e.g.
 * `job-complete`) going active server-side must not break an already-shipped
 * host still running an OLDER version of this library that predates the kind
 * being added to the taxonomy. Unrecognized kinds are logged and skipped.
 *
 * A `kind` that IS in the locked taxonomy but has no registered handler is NOT
 * an error — it is simply a signal this host doesn't render (e.g. a RESERVED
 * kind with no consumer yet, or an ACTIVE kind this particular host doesn't
 * subscribe to). That case is a silent no-op, not a log line.
 */
export class KindRouter {
  private readonly handlersByKind = new Map<NotificationKind, NotificationHandler[]>();

  /**
   * Registers `callback` to receive every {@link NotificationEvent} whose `kind`
   * matches. Multiple handlers may be registered for the same kind — all fire.
   *
   * @returns An unregister function. Call it to remove this specific handler.
   */
  registerHandler(kind: NotificationKind, callback: NotificationHandler): () => void {
    const existing = this.handlersByKind.get(kind);
    if (existing) {
      existing.push(callback);
    } else {
      this.handlersByKind.set(kind, [callback]);
    }

    return () => {
      const handlers = this.handlersByKind.get(kind);
      if (!handlers) {
        return;
      }
      const index = handlers.indexOf(callback);
      if (index >= 0) {
        handlers.splice(index, 1);
      }
    };
  }

  /**
   * Routes `event` to every handler registered for `event.kind`. Logs and
   * skips (never throws) when `event.kind` is not in the locked taxonomy this
   * library version recognizes. A handler that throws is caught + logged so
   * one bad handler cannot prevent delivery to other handlers of the same
   * kind, or corrupt the caller's SignalR message loop / poll tick.
   */
  dispatch(event: { outboxRowId: string; kind: string; envelope?: unknown; source: NotificationEvent['source'] }): void {
    if (!isKnownNotificationKind(event.kind)) {
      // eslint-disable-next-line no-console
      console.warn(
        `[@spaarke/notifications] Unrecognized notification kind "${event.kind}" — logged and skipped. ` +
          'The kind taxonomy is a closed set; a novel/reserved value activating server-side must not break ' +
          'a host running an older library version.',
      );
      return;
    }

    const handlers = this.handlersByKind.get(event.kind);
    if (!handlers || handlers.length === 0) {
      return;
    }

    const normalized: NotificationEvent = {
      outboxRowId: event.outboxRowId,
      kind: event.kind,
      envelope: event.envelope as NotificationEvent['envelope'],
      source: event.source,
    };

    for (const handler of handlers) {
      try {
        handler(normalized);
      } catch (err) {
        // eslint-disable-next-line no-console
        console.error(`[@spaarke/notifications] Handler for kind "${event.kind}" threw:`, err);
      }
    }
  }

  /** Removes every registered handler for every kind. Used by `NotificationsClient.stop()`. */
  clear(): void {
    this.handlersByKind.clear();
  }
}

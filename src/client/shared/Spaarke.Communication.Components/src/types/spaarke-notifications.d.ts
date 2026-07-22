/**
 * Minimal TYPE surface of @spaarke/notifications consumed by this package (task 045 / FR-22).
 *
 * The real, host-installed @spaarke/notifications package provides the full implementation at runtime:
 * the two Vite hosts (SpaarkeAi / LegalWorkspace) resolve it directly against their own node_modules.
 * This declaration keeps THIS package's isolated tsc build self-contained (mirroring the dist-declaration
 * paths mappings already used for @spaarke/auth and @spaarke/ui-components in tsconfig.json) without
 * requiring the sibling lib's dist to be pre-built.
 *
 * It is a plain module declaration file reached ONLY via this package's tsconfig paths mapping, NOT an
 * ambient declare-module, so it cannot collide with the real package's types when a host compiles (the
 * host has no such paths entry and resolves the real package instead). Only the members this package
 * actually calls are typed.
 *
 * This file is hand-authored (NOT a build artifact); it is force-tracked past the repo .gitignore rule
 * for src-tree .d.ts files. Source of truth for the shape:
 * src/client/shared/Spaarke.Notifications/src/{NotificationsClient,types}.ts.
 */

export type NotificationKind = 'communication-arrived' | 'communication-assessed' | 'suggestion' | string;

export interface NotificationEvent {
  outboxRowId: string;
  kind: NotificationKind;
  envelope?: unknown;
  source: 'live' | 'poll';
}

export type NotificationHandler = (event: NotificationEvent) => void;

export interface NotificationsClientOptions {
  pollIntervalMs?: number;
  pollMaxBackoffMs?: number;
  onConnectionStateChange?: (state: string) => void;
}

export declare class NotificationsClient {
  constructor(options?: NotificationsClientOptions);
  registerHandler(kind: NotificationKind, callback: NotificationHandler): () => void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

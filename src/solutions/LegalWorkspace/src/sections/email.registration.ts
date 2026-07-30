/**
 * email.registration.ts — SectionRegistration for the Email workspace section.
 *
 * email-communication-solution-r5 task 041 (FR-01): LegalWorkspace section
 * shim for the Outlook-style Email surface. Renders the SAME shared
 * `EmailWorkspace` composition root (task 040, `@spaarke/communication-
 * components`) as the SpaarkeAi `email` direct widget
 * (`register-workspace-widgets.ts`) — Pattern D dual-use, mirroring
 * `communications.registration.ts`.
 *
 * `EmailWorkspace` is host-agnostic (ADR-012): it declares its dependencies
 * as plain props (`dataverseClient` / `dataService` / `navigationService` /
 * `webApi` / `authenticatedFetch` / `bffBaseUrl`) instead of resolving them
 * itself (see `EmailWorkspace.types.ts` docblock). This shim resolves them
 * the same way `composeEditor.registration.ts`'s `ComposeSectionMount` /
 * `dailyBriefing.registration.ts` resolve their own host dependencies inside
 * LegalWorkspace:
 *   - `dataverseClient` / `dataService` / `navigationService` — Xrm-backed
 *     adapters from `@spaarke/ui-components` (same adapters
 *     `documents.registration.ts`-family sections and `DataGrid.tsx` use).
 *   - `webApi` — the raw `Xrm.WebApi` bridge (frame-independent; LegalWorkspace
 *     always runs inside a Dataverse MDA host or Code Page — same pattern
 *     `composeEditor.registration.ts` uses for its create-on-save container
 *     resolution).
 *   - `authenticatedFetch` — the LegalWorkspace-local module-level function
 *     from `services/authInit` (same source `dailyBriefing.registration.ts`
 *     uses).
 *   - `bffBaseUrl` — from the `SectionFactoryContext` supplied by
 *     `WorkspaceShell` (same source `composeEditor.registration.ts` uses).
 *
 * ADR-039 / BFF §10 (Path C): surface identity stays in CODE — this file
 * introduces no server-side surface-identity endpoint or record. Section id
 * `email` is distinct from the existing `communications` section id and the
 * `communications-list` / `email-compose` direct-widget type strings.
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React 19),
 *            ADR-028 (function-based auth contract — no token snapshots).
 */

import * as React from "react";
import { MailRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import {
  XrmDataverseClient,
  createXrmDataService,
  createXrmNavigationService,
  getXrm,
} from "@spaarke/ui-components";
import { EmailWorkspace } from "@spaarke/communication-components";
import { authenticatedFetch } from "../services/authInit";

// ---------------------------------------------------------------------------
// EmailSectionMount — inner functional component that resolves the Xrm-backed
// host adapters `EmailWorkspace` requires and forwards the factory context's
// `bffBaseUrl`. Kept inside this file (not a separate module) to match the
// single-file Pattern D shim shape of `calendar.registration.ts` /
// `composeEditor.registration.ts`. Declared as `React.FC` per this
// directory's `.ts`-not-`.tsx` convention (esbuild does not parse JSX in
// `.ts` files — React tree construction uses `React.createElement`).
// ---------------------------------------------------------------------------

interface EmailSectionMountProps {
  bffBaseUrl: string;
}

const EmailSectionMount: React.FC<EmailSectionMountProps> = ({ bffBaseUrl }) => {
  // Stable across the section's lifetime — one Xrm-backed adapter set per
  // mount, matching `XrmDataverseClient`'s own "instantiate once" guidance
  // (see DataGrid.tsx's defaultClientRef pattern).
  const dataverseClient = React.useMemo(() => new XrmDataverseClient(), []);
  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);
  // Xrm.WebApi-compatible bridge for the associations additive-write path
  // (035) + the embedded PolymorphicPicker (035) — the same object shape a
  // PCF host passes as `context.webAPI`.
  const webApi = React.useMemo(() => getXrm()?.WebApi, []);

  if (!webApi) {
    // No Dataverse host available. EmailWorkspace's required host props
    // cannot be resolved — fail closed rather than mount a partially-wired
    // component. Mirrors `XrmDataverseClient`'s own throw-at-first-call
    // contract for non-MDA hosts.
    return null;
  }

  return React.createElement(EmailWorkspace, {
    dataverseClient,
    dataService,
    navigationService,
    webApi,
    authenticatedFetch,
    bffBaseUrl,
  });
};

export const emailRegistration: SectionRegistration = {
  id: "email",
  label: "Email",
  description: "Outlook-style reading pane for your email communications",
  icon: MailRegular,
  category: "data",
  // Tall two-pane surface (card list + reading pane) — same rationale as
  // Calendar (720px) and Compose (720px): give the section room to breathe
  // rather than clamping.
  defaultHeight: "720px",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "email",
      type: "content",
      title: "Email",
      style: { overflow: "hidden" },
      renderContent: () =>
        React.createElement(EmailSectionMount, { bffBaseUrl: context.bffBaseUrl }),
    };
  },
};

export default emailRegistration;

'use strict';

/**
 * mergeUnifiedManifest — builds the ONE Spaarke app-package manifest that serves BOTH Outlook and Word
 * (spaarkeai-word-add-in-r1 task 078, FR-05).
 *
 * WHY ONE PACKAGE. The Microsoft 365 unified manifest supports a single app whose one `extensions[]` object
 * targets several hosts (`requirements.scopes: ["mail","document"]`), with each runtime and ribbon scoped to
 * its own host through nested `requirements` (Microsoft Learn: "Each app supports only one extension";
 * "specify any subset of mail, workbook, document, presentation"). Spaarke's add-ins are already one product
 * — one codebase, one build, one hosted site, one Entra registration, one BFF — and per-customer provisioning
 * (Model 1 / Model 2) deploys and consents the add-in once per tenant. One package means one upload and one
 * consent per customer instead of two. Evidence and the decision: projects/spaarkeai-word-add-in-r1/notes/
 * 078-manifest-decision.md.
 *
 * WHAT IS LIVE TODAY, AND WHY THIS IS A NEW APP. Both hosts are registered in production from XML
 * manifests — Outlook `outlook/outlook-manifest.xml`, Word `word/word-manifest.xml`. The two JSON manifests
 * were dev-sideload only. So this package is a NEW app with its own id (not either XML id — Microsoft
 * requires a different GUID — and not the Entra client id). It carries `alternates.hide` entries naming
 * both XML add-ins, so a client that can run the unified package shows only it, while clients that cannot
 * (Outlook on Mac; Word older than 2501) keep the XML add-in. Hiding works in Outlook; in Word it does not
 * yet (office-js #6938), so Word's XML is retired by a manual admin-center step — see notes/078.
 *
 * WHY THE OUTLOOK IDS ARE NOT RENAMED. `outlook/manifest.json` is the precedent FR-05 names, so its ids are
 * kept exactly as authored and the package is a strict superset of it. Only Word's ids are namespaced where
 * they would collide inside the one extension.
 *
 * WHY EXECUTE-FUNCTION ACTIONS ARE NOT RENAMED BLINDLY. An `executeFunction` action id is the name the host's
 * commands script registers with `Office.actions.associate(id, fn)`. Renaming one in the manifest without the
 * matching registration produces a ribbon button that silently does nothing. So a colliding executeFunction id
 * must be listed in WORD_FUNCTION_RENAMES, and the build fails loudly if one is not — pointing at the file
 * that needs the alias.
 *
 * This module is PURE (no fs, no network): webpack supplies the manifests and an `assetExists` predicate.
 * Tests run it against the REAL source manifests, so an edit to either host's manifest that would produce an
 * invalid package fails CI rather than an admin-center upload.
 */

const SCHEMA_URL = 'https://developer.microsoft.com/json-schemas/teams/v1.30/MicrosoftTeams.schema.json';
const MANIFEST_VERSION = '1.30';

const OUTLOOK_SCOPE = 'mail';
const WORD_SCOPE = 'document';

/**
 * Word executeFunction actions whose ids collide with an Outlook action. Each rename REQUIRES a matching
 * `Office.actions.associate('<new id>', fn)` in word/commands/index.ts — the old id stays registered too,
 * because the Word XML manifest (the fallback for Word builds older than 2501) still calls it.
 */
const WORD_FUNCTION_RENAMES = {
  quickSave: 'quickSaveDocument',
};

/** Package-level icon file names, relative to the root of the app-package zip. */
const PACKAGE_ICONS = { color: 'color.png', outline: 'outline.png' };

/**
 * XML `<Permissions>` value → the unified manifest's `authorization.permissions.resourceSpecific` entry that grants
 * the same access (Microsoft Learn, "Specify permissions" for the unified manifest). The v1.30 schema types the name
 * as a FREE STRING, so a wrong name is invisible to schema validation and only fails at install or runtime — which
 * is why this table exists and the merge checks the package against the live XML (see assertPermissionParity).
 */
const XML_PERMISSION_TO_RSC = {
  // Outlook
  Restricted: 'MailboxItem.RestrictedRead.User',
  ReadItem: 'MailboxItem.Read.User',
  ReadWriteItem: 'MailboxItem.ReadWrite.User',
  ReadWriteMailbox: 'Mailbox.ReadWrite.User',
  // Word / Excel / PowerPoint
  ReadDocument: 'Document.Read.User',
  ReadWriteDocument: 'Document.ReadWrite.User',
};

/** Union of resourceSpecific permissions, de-duplicated by name + type. */
function unionPermissions(...lists) {
  const seen = new Map();
  for (const entry of lists.flat()) {
    if (entry && entry.name) seen.set(`${entry.name}|${entry.type}`, { name: entry.name, type: entry.type });
  }
  return [...seen.values()];
}

/** Union of validDomains, de-duplicated, order preserved. */
function unionDomains(...lists) {
  return [...new Set(lists.flat().filter(Boolean))];
}

/**
 * The package replaces two live XML add-ins, so it must never grant LESS than they do — an Outlook half that cannot
 * read the item, or a Word half that cannot write the document (the FR-02 identity stamp writes to it), installs
 * cleanly and then fails at the very features it exists for. Throws when a live XML permission has no equivalent.
 */
function assertPermissionParity(permissions, legacyXmlPermissions) {
  for (const [host, xmlPermission] of Object.entries(legacyXmlPermissions)) {
    const required = XML_PERMISSION_TO_RSC[xmlPermission];
    if (!required) {
      throw new ManifestMergeError(
        `The live ${host} XML add-in declares <Permissions>${xmlPermission}</Permissions>, which has no entry in ` +
          `XML_PERMISSION_TO_RSC — add its unified equivalent before building the package.`
      );
    }
    if (!permissions.some(p => p.name === required && p.type === 'Delegated')) {
      throw new ManifestMergeError(
        `The package does not grant ${required} (Delegated), the equivalent of the live ${host} XML add-in's ` +
          `<Permissions>${xmlPermission}</Permissions>. It would install and then fail at runtime. Declare it in ` +
          `that host's manifest.json under authorization.permissions.resourceSpecific.`
      );
    }
  }
}

class ManifestMergeError extends Error {
  constructor(message) {
    super(`[Spaarke unified package] ${message}`);
    this.name = 'ManifestMergeError';
  }
}

function clone(value) {
  return JSON.parse(JSON.stringify(value));
}

function onlyExtension(manifest, host) {
  const extensions = manifest.extensions || [];
  if (extensions.length !== 1) {
    throw new ManifestMergeError(`${host} manifest must have exactly one extension; found ${extensions.length}.`);
  }
  return extensions[0];
}

/** 'TaskpaneRuntime' -> 'WordTaskpaneRuntime'; already-prefixed ids are left alone. */
function wordPrefixed(id) {
  return id.startsWith('Word') ? id : `Word${id}`;
}

/** 'openTaskPane' -> 'wordOpenTaskPane' (camelCase ids stay camelCase). */
function wordPrefixedCamel(id) {
  return id.startsWith('word') ? id : `word${id.charAt(0).toUpperCase()}${id.slice(1)}`;
}

/** Returns the new id for a Word action, or throws for an unmapped colliding executeFunction. */
function renameWordAction(action, outlookActionIds) {
  if (action.type === 'executeFunction') {
    if (Object.prototype.hasOwnProperty.call(WORD_FUNCTION_RENAMES, action.id)) {
      return WORD_FUNCTION_RENAMES[action.id];
    }
    if (outlookActionIds.has(action.id)) {
      throw new ManifestMergeError(
        `Word executeFunction action "${action.id}" collides with an Outlook action of the same id. Add it to ` +
          `WORD_FUNCTION_RENAMES in packaging/mergeUnifiedManifest.js AND register the new name with ` +
          `Office.actions.associate in word/commands/index.ts — renaming only the manifest leaves a dead button.`
      );
    }
    return action.id;
  }
  // openPage (and any non-code action) has no registration to keep in step, so it is namespaced outright.
  return wordPrefixedCamel(action.id);
}

/** Extracts the file name from an `…/assets/<file>` URL, or null for any other shape. */
function assetFileName(url) {
  const match = /\/assets\/([^/?#]+)$/.exec(url || '');
  return match ? match[1] : null;
}

/**
 * Points any icon URL whose file does not exist at the same-size Spaarke icon (`icon-<size>.png`). The Outlook
 * JSON (dev-sideload only; production Outlook runs its XML) references save-*, share-* and grant-* icons that were
 * never created (they return 404 on the hosted site), and
 * the admin center fails validation on an icon URL that does not return 200.
 */
function repairIcons(icons, assetExists, where) {
  if (!Array.isArray(icons)) return icons;
  return icons.map(icon => {
    if (assetExists(icon.url)) return icon;
    const file = assetFileName(icon.url);
    const fallbackUrl = file ? icon.url.replace(`/assets/${file}`, `/assets/icon-${icon.size}.png`) : null;
    if (!fallbackUrl || !assetExists(fallbackUrl)) {
      throw new ManifestMergeError(
        `${where}: icon ${icon.url} does not exist, and no same-size fallback icon-${icon.size}.png exists either.`
      );
    }
    return { ...icon, url: fallbackUrl };
  });
}

function repairRibbonIcons(ribbons, assetExists, host) {
  for (const ribbon of ribbons) {
    for (const tab of ribbon.tabs || []) {
      for (const group of tab.groups || []) {
        group.icons = repairIcons(group.icons, assetExists, `${host} group ${group.id}`);
        for (const control of group.controls || []) {
          control.icons = repairIcons(control.icons, assetExists, `${host} control ${control.id}`);
        }
      }
    }
  }
}

function scopeTo(node, scope, capabilities) {
  const requirements = { ...(node.requirements || {}), scopes: [scope] };
  if (capabilities && capabilities.length > 0) {
    requirements.capabilities = clone(capabilities);
  }
  return { ...node, requirements };
}

/**
 * The invariants a valid one-extension package must hold. Run on every merge AND on the test variant.
 * Throws ManifestMergeError on the first violation.
 */
function assertPackageInvariants(manifest) {
  const extension = onlyExtension(manifest, 'Merged');
  const scopes = extension.requirements && extension.requirements.scopes;
  if (!Array.isArray(scopes) || scopes.length === 0) {
    throw new ManifestMergeError('The extension declares no scopes.');
  }

  const seen = { runtime: new Set(), action: new Set(), group: new Set(), control: new Set() };
  const claim = (kind, id) => {
    if (seen[kind].has(id)) {
      throw new ManifestMergeError(`Duplicate ${kind} id "${id}" in the merged package.`);
    }
    seen[kind].add(id);
  };
  const assertSingleScope = (label, node) => {
    const nodeScopes = node.requirements && node.requirements.scopes;
    if (!Array.isArray(nodeScopes) || nodeScopes.length !== 1 || !scopes.includes(nodeScopes[0])) {
      throw new ManifestMergeError(
        `${label} must be scoped to exactly one of the package's hosts (${scopes.join(', ')}).`
      );
    }
  };

  for (const runtime of extension.runtimes || []) {
    claim('runtime', runtime.id);
    assertSingleScope(`Runtime "${runtime.id}"`, runtime);
    for (const action of runtime.actions || []) claim('action', action.id);
  }

  for (const [index, ribbon] of (extension.ribbons || []).entries()) {
    assertSingleScope(`Ribbon #${index}`, ribbon);
    for (const tab of ribbon.tabs || []) {
      for (const group of tab.groups || []) {
        claim('group', group.id);
        for (const control of group.controls || []) {
          claim('control', control.id);
          if (control.actionId && !seen.action.has(control.actionId)) {
            throw new ManifestMergeError(
              `Control "${control.id}" points at action "${control.actionId}", which no runtime declares.`
            );
          }
        }
      }
    }
  }

  return manifest;
}

/**
 * @param {object} outlook  Outlook unified manifest, URL/resource placeholders already substituted
 * @param {object} word     Word unified manifest, placeholders already substituted
 * @param {object} options
 * @param {string} options.appId     the PACKAGE id — a GUID of its own (not an XML add-in id, not the Entra id)
 * @param {string} options.clientId  the Entra app registration (client) id — used ONLY to refuse it as the package id.
 *   The package carries no `webApplicationInfo` (task 115): the add-in never uses Office SSO (sign-in is
 *   @spaarke/auth NAA), and that entry made a customer tenant's deployment ask to consent to Spaarke's
 *   single-tenant app (AADSTS700016). Tokens are issued by Spaarke's tenant, where the consent already exists.
 * @param {string} options.version   3-part version (the unified manifest rejects 4-part)
 * @param {{mail: string, document: string}} options.legacyXmlIds  the <Id> of each host's LIVE XML add-in, to hide
 * @param {{mail: string, document: string}} options.legacyXmlPermissions  each live XML add-in's <Permissions> value;
 *   the package must grant at least the unified equivalent of each (assertPermissionParity)
 * @param {(url: string) => boolean} options.assetExists  whether an …/assets/<file> URL resolves to a real file
 */
function mergeUnifiedManifest(outlook, word, options) {
  const { appId, clientId, version, legacyXmlIds, legacyXmlPermissions, assetExists } = options || {};
  if (!appId || !clientId || !version || !legacyXmlIds || !legacyXmlPermissions || typeof assetExists !== 'function') {
    throw new ManifestMergeError(
      'appId, clientId, version, legacyXmlIds, legacyXmlPermissions and assetExists are all required.'
    );
  }
  if (!/^\d+\.\d+\.\d+$/.test(version)) {
    throw new ManifestMergeError(
      `Package version "${version}" must be 3-part (e.g. 1.1.0); the unified manifest rejects 4-part.`
    );
  }
  const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  for (const [host, id] of Object.entries({ mail: legacyXmlIds.mail, document: legacyXmlIds.document })) {
    if (!guid.test(id || '')) {
      throw new ManifestMergeError(
        `legacyXmlIds.${host} must be the GUID <Id> of the live ${host} XML add-in; got "${id}".`
      );
    }
  }
  const reserved = new Set([clientId, legacyXmlIds.mail, legacyXmlIds.document].map(v => String(v).toLowerCase()));
  if (reserved.has(String(appId).toLowerCase())) {
    throw new ManifestMergeError(
      `The package id ${appId} must be its OWN GUID — not the Entra client id and not a live XML add-in id. ` +
        `Reusing an XML id is unsupported for the unified manifest, and reusing the client id is the ` +
        `conflation task 078 removed.`
    );
  }

  const outlookExtension = clone(onlyExtension(outlook, 'Outlook'));
  const wordExtension = clone(onlyExtension(word, 'Word'));
  const outlookCapabilities = (outlookExtension.requirements && outlookExtension.requirements.capabilities) || [];
  const wordCapabilities = (wordExtension.requirements && wordExtension.requirements.capabilities) || [];

  // Outlook: ids untouched, only scoped (it is the live app).
  const outlookRuntimes = (outlookExtension.runtimes || []).map(r => scopeTo(r, OUTLOOK_SCOPE, outlookCapabilities));
  const outlookRibbons = (outlookExtension.ribbons || []).map(r => scopeTo(r, OUTLOOK_SCOPE, outlookCapabilities));
  const outlookActionIds = new Set(outlookRuntimes.flatMap(r => (r.actions || []).map(a => a.id)));

  // Word: namespaced where the one extension needs it, then scoped.
  const actionRenames = {};
  const wordRuntimes = (wordExtension.runtimes || []).map(runtime => {
    const actions = (runtime.actions || []).map(action => {
      const renamed = renameWordAction(action, outlookActionIds);
      actionRenames[action.id] = renamed;
      return { ...action, id: renamed };
    });
    return scopeTo({ ...runtime, id: wordPrefixed(runtime.id), actions }, WORD_SCOPE, wordCapabilities);
  });
  const wordRibbons = (wordExtension.ribbons || []).map(ribbon => {
    const tabs = (ribbon.tabs || []).map(tab => ({
      ...tab,
      groups: (tab.groups || []).map(group => ({
        ...group,
        id: wordPrefixed(group.id),
        controls: (group.controls || []).map(control => ({
          ...control,
          id: wordPrefixed(control.id),
          ...(control.actionId ? { actionId: actionRenames[control.actionId] || control.actionId } : {}),
        })),
      })),
    }));
    return scopeTo({ ...ribbon, tabs }, WORD_SCOPE, wordCapabilities);
  });

  repairRibbonIcons(outlookRibbons, assetExists, 'Outlook');
  repairRibbonIcons(wordRibbons, assetExists, 'Word');
  const ribbons = [...outlookRibbons, ...wordRibbons];

  const merged = {
    $schema: SCHEMA_URL,
    manifestVersion: MANIFEST_VERSION,
    id: appId,
    version,
    name: {
      short: 'Spaarke',
      full: 'Spaarke Document Management for Outlook and Word',
    },
    description: {
      short: 'Save, find, and share emails and documents in Spaarke',
      full:
        'Spaarke for Outlook and Word saves emails, attachments, and documents to Spaarke Document Management, ' +
        'files them against the right matter or project, finds similar documents and matching records, and ' +
        'creates Spaarke To Dos — directly from Outlook and Word.',
    },
    developer: clone(outlook.developer),
    icons: { ...PACKAGE_ICONS },
    accentColor: outlook.accentColor,
    localizationInfo: clone(outlook.localizationInfo),
    authorization: {
      permissions: {
        resourceSpecific: unionPermissions(
          (outlook.authorization &&
            outlook.authorization.permissions &&
            outlook.authorization.permissions.resourceSpecific) ||
            [],
          (word.authorization && word.authorization.permissions && word.authorization.permissions.resourceSpecific) ||
            []
        ),
      },
    },
    // The unified equivalent of the XML add-ins' <AppDomains>; each host's manifest.json declares its own.
    validDomains: unionDomains(outlook.validDomains || [], word.validDomains || []),
    extensions: [
      {
        // Host-specific capabilities live on each runtime and ribbon: an extension-level WordApi requirement
        // would stop the package installing in Outlook, and a Mailbox requirement would stop it in Word.
        requirements: { scopes: [OUTLOOK_SCOPE, WORD_SCOPE] },
        runtimes: [...outlookRuntimes, ...wordRuntimes],
        ribbons,
        // A client that can run this package hides the matching XML add-in, so users see one Spaarke ribbon.
        // Honoured by Outlook; not yet by Word (office-js #6938) — Word's XML is retired manually.
        alternates: [
          {
            requirements: { scopes: [OUTLOOK_SCOPE] },
            hide: { customOfficeAddin: { officeAddinId: legacyXmlIds.mail } },
          },
          {
            requirements: { scopes: [WORD_SCOPE] },
            hide: { customOfficeAddin: { officeAddinId: legacyXmlIds.document } },
          },
        ],
      },
    ],
  };
  // Task 115: no `webApplicationInfo` — see the clientId note above. A source manifest that brings one back would
  // re-break customer-tenant deployment, so it is refused rather than silently dropped.
  if (outlook.webApplicationInfo || word.webApplicationInfo) {
    throw new ManifestMergeError(
      'A source manifest declares webApplicationInfo. The package must not: it makes deployment in a customer ' +
        "tenant ask to consent to Spaarke's single-tenant app (AADSTS700016), and the add-in does not use Office SSO."
    );
  }

  assertPermissionParity(merged.authorization.permissions.resourceSpecific, legacyXmlPermissions);
  if (merged.validDomains.length === 0) delete merged.validDomains;
  return assertPackageInvariants(merged);
}

/**
 * A TEST copy for a "Just me" admin-center upload, built so it can NEVER disturb the tester's working add-ins:
 *  - its own package id and a "(TEST)" name, so it installs BESIDE everything rather than replacing anything;
 *  - NO `alternates`: the production package hides the live XML add-ins, and a test copy doing the same would
 *    hide the tester's working Spaarke ribbon — and if the test build were broken, leave them with none;
 *  - "(TEST)" on every ribbon group label, so the two Spaarke ribbons shown side by side are distinguishable.
 */
function deriveTestVariant(manifest, { appId, label = 'TEST' }) {
  if (!appId || appId === manifest.id) {
    throw new ManifestMergeError('The TEST variant needs its own package id, different from production.');
  }
  const variant = clone(manifest);
  variant.id = appId;
  variant.name = {
    short: `${manifest.name.short} (${label})`,
    full: `${manifest.name.full} (${label})`,
  };
  for (const extension of variant.extensions) {
    delete extension.alternates;
    for (const ribbon of extension.ribbons || []) {
      for (const tab of ribbon.tabs || []) {
        for (const group of tab.groups || []) {
          group.label = `${group.label} (${label})`;
        }
      }
    }
  }
  return assertPackageInvariants(variant);
}

module.exports = {
  mergeUnifiedManifest,
  deriveTestVariant,
  assertPackageInvariants,
  ManifestMergeError,
  WORD_FUNCTION_RENAMES,
  XML_PERMISSION_TO_RSC,
  PACKAGE_ICONS,
  SCHEMA_URL,
  MANIFEST_VERSION,
};

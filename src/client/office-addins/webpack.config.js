const path = require('path');
const HtmlWebpackPlugin = require('html-webpack-plugin');
const CopyWebpackPlugin = require('copy-webpack-plugin');
const MiniCssExtractPlugin = require('mini-css-extract-plugin');
const webpack = require('webpack');
const devCerts = require('office-addin-dev-certs');
const fs = require('fs');
const { mergeUnifiedManifest, deriveTestVariant, PACKAGE_ICONS } = require('./packaging/mergeUnifiedManifest');
require('dotenv').config({ path: path.resolve(__dirname, '.env') });

const isProduction = process.env.NODE_ENV === 'production';

// Build date for version display
const BUILD_DATE = new Date().toLocaleDateString('en-US', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});

// Environment variables for add-in configuration
// All values REQUIRED — no dev-specific fallbacks.
// Set in .env (local dev) or CI/CD pipeline environment variables.
const REQUIRED_ENV_VARS = ['ADDIN_CLIENT_ID', 'TENANT_ID', 'BFF_API_CLIENT_ID', 'BFF_API_BASE_URL'];
const missingVars = REQUIRED_ENV_VARS.filter((v) => !process.env[v]);
if (missingVars.length > 0) {
  throw new Error(
    `[Office Add-in Webpack] Missing required environment variables: ${missingVars.join(', ')}.\n` +
      `Copy .env.example to .env and set all values, or provide them via CI/CD pipeline.`
  );
}

const ENV_CONFIG = {
  ADDIN_CLIENT_ID: process.env.ADDIN_CLIENT_ID,
  TENANT_ID: process.env.TENANT_ID,
  BFF_API_CLIENT_ID: process.env.BFF_API_CLIENT_ID,
  BFF_API_BASE_URL: process.env.BFF_API_BASE_URL,
  // Optional: SmartTodo Code Page URL (smart-todo-decoupling-r3 FR-27 / task 070).
  // When set, the Outlook "Create To Do" ribbon opens the wizard from this URL
  // with launch-context query params. When unset, the ribbon action is hidden
  // / inert. Documented in .env.example.
  SMARTTODO_CODEPAGE_URL: process.env.SMARTTODO_CODEPAGE_URL || '',
  // Optional: Dataverse org URL for the SaveView "Quick Create" deep link
  // (email-communication-solution-r4 task 072 / FR-25). Config-driven, no
  // hardcoded org — when unset, Quick Create degrades to a no-op.
  ORG_URL: process.env.ORG_URL || '',
  // Optional: the unique name of the Spaarke model-driven app every record link opens in
  // (spaarkeai-word-add-in-r1 task 088 / UAT-1 — `main.aspx?appname=…`). A link with no app opens in the
  // user's DEFAULT app, which may not be Spaarke's. The unique name travels with the solution, so it is the
  // same in every environment; a customer whose app has another name changes this one setting. UNSET →
  // `sprk_MatterManagement`; set to an EMPTY string → record links name no app (the pre-088 behaviour).
  SPAARKE_APP_NAME: process.env.SPAARKE_APP_NAME !== undefined ? process.env.SPAARKE_APP_NAME : 'sprk_MatterManagement',
  // Optional: switches Word's Email tab (task 096) on. Default OFF in code; only the exact string "true" turns
  // it on. The deploy workflow (`.github/workflows/deploy-office-addins.yml`) sets it to "true" since task 097
  // (2026-10-06): `/api/communications/send` now authorizes every attachment and association as the caller
  // (unified-access-control-r2 task 161, on master via #1312). A local or other build without the setting
  // keeps the tab off.
  ADDIN_EMAIL_TAB_ENABLED: process.env.ADDIN_EMAIL_TAB_ENABLED === 'true' ? 'true' : 'false',
  // Optional: fallback MSAL popup redirect URI used only when the Office host
  // does not support NAA (`OfficeNaaStrategy`'s legacy-client fallback path).
  // Defaults to `${origin}/auth-callback.html` inside AuthService when unset.
  FALLBACK_REDIRECT_URI: process.env.FALLBACK_REDIRECT_URI || '',
  // Base URL the unified manifest's runtime/ribbon `code.page` + icon URLs point
  // at. Dev defaults to the local webpack-dev-server origin; production defaults
  // to the deployed Azure Static Web App (office-addins-deploy skill resource
  // reference) — override via ADDIN_BASE_URL for a custom domain.
  ADDIN_BASE_URL:
    process.env.ADDIN_BASE_URL ||
    (isProduction ? 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net' : 'https://localhost:3000'),
};

/**
 * The COMBINED Outlook + Word app package (spaarkeai-word-add-in-r1 task 078, FR-05) —
 * see packaging/mergeUnifiedManifest.js and projects/spaarkeai-word-add-in-r1/notes/078-manifest-decision.md.
 *
 * Two ids that the standalone JSON manifests used to CONFLATE are kept apart here:
 *   - ADDIN_CLIENT_ID  → the Entra app registration. Goes in `webApplicationInfo.id` ONLY.
 *   - ADDIN_APP_ID     → the app PACKAGE id. Its own GUID. The admin center identifies the app by it, so it
 *                        must stay STABLE for the life of the package — every release is an update of it.
 * Both live add-ins today are XML (Outlook 5e4d66d0-…, Word b3965ea0-…), so the package is a NEW app; the XML
 * ids are read from the XML files below so the `alternates.hide` entries can never drift from what is registered.
 *
 * Bump UNIFIED_PACKAGE_VERSION (3-part) on every package change — the admin center rejects a same-version update.
 * It is also the version the Word pane's footer shows (`process.env.ADDIN_PACKAGE_VERSION`, task 089), so what the
 * user sees is what the admin uploaded.
 *
 * 1.1.1 (task 089, UAT-10): the Word ribbon's Share is replaced by "Open Spaarke".
 */
const UNIFIED_PACKAGE = {
  APP_ID: process.env.ADDIN_APP_ID || 'e68f3cb1-3702-4a58-8c02-972e7d1667eb',
  TEST_APP_ID: process.env.ADDIN_TEST_APP_ID || 'b490de25-d155-44cd-8825-6e125102dd84',
  VERSION: '1.1.1',
};

/** Reads the `<Id>` of a live XML add-in manifest — the id its `alternates.hide` entry must name. */
function readXmlAddinId(relativePath) {
  const xml = fs.readFileSync(path.resolve(__dirname, relativePath), 'utf8');
  const match = /<Id>\s*([0-9a-fA-F-]{36})\s*<\/Id>/.exec(xml);
  if (!match) {
    throw new Error(`[Office Add-in Webpack] No <Id> found in ${relativePath}; cannot build the unified package.`);
  }
  return match[1];
}

/** Reads the `<Permissions>` of a live XML add-in manifest — the access the package must not fall below. */
function readXmlAddinPermissions(relativePath) {
  const xml = fs.readFileSync(path.resolve(__dirname, relativePath), 'utf8');
  const match = /<Permissions>\s*([A-Za-z]+)\s*<\/Permissions>/.exec(xml);
  if (!match) {
    throw new Error(`[Office Add-in Webpack] No <Permissions> found in ${relativePath}; cannot build the unified package.`);
  }
  return match[1];
}

/** Applies the same placeholder substitution the standalone manifests get (base URL + BFF resource). */
function substituteManifestPlaceholders(text) {
  return text
    .split('https://localhost:3000')
    .join(ENV_CONFIG.ADDIN_BASE_URL)
    .replace(/"resource":\s*"api:\/\/[a-f0-9-]+"/g, `"resource": "api://${ENV_CONFIG.BFF_API_CLIENT_ID}"`);
}

/**
 * Emits the unified package into dist/spaarke/: manifest.json (production), manifest.test.json (a TEST copy with
 * its own id and a "(TEST)" name for a "Just me" admin-center upload), color.png (192×192) and outline.png (32×32).
 * scripts/Package-SpaarkeAddin.ps1 zips them. The merge throws — failing the build — on a duplicate id, an action
 * no runtime declares, or an icon file that does not exist.
 */
class SpaarkeUnifiedPackagePlugin {
  apply(compiler) {
    compiler.hooks.thisCompilation.tap('SpaarkeUnifiedPackagePlugin', compilation => {
      compilation.hooks.processAssets.tap(
        { name: 'SpaarkeUnifiedPackagePlugin', stage: webpack.Compilation.PROCESS_ASSETS_STAGE_ADDITIONAL },
        () => {
          const readManifest = relativePath =>
            JSON.parse(substituteManifestPlaceholders(fs.readFileSync(path.resolve(__dirname, relativePath), 'utf8')));
          const assetsDir = path.resolve(__dirname, 'shared/assets');
          const assetExists = url => {
            const match = /\/assets\/([^/?#]+)$/.exec(url || '');
            return Boolean(match) && fs.existsSync(path.join(assetsDir, match[1]));
          };

          const production = mergeUnifiedManifest(readManifest('./outlook/manifest.json'), readManifest('./word/manifest.json'), {
            appId: UNIFIED_PACKAGE.APP_ID,
            clientId: ENV_CONFIG.ADDIN_CLIENT_ID,
            version: UNIFIED_PACKAGE.VERSION,
            legacyXmlIds: {
              mail: readXmlAddinId('./outlook/outlook-manifest.xml'),
              document: readXmlAddinId('./word/word-manifest.xml'),
            },
            legacyXmlPermissions: {
              mail: readXmlAddinPermissions('./outlook/outlook-manifest.xml'),
              document: readXmlAddinPermissions('./word/word-manifest.xml'),
            },
            assetExists,
          });
          const test = deriveTestVariant(production, { appId: UNIFIED_PACKAGE.TEST_APP_ID });

          const { RawSource } = webpack.sources;
          compilation.emitAsset('spaarke/manifest.json', new RawSource(JSON.stringify(production, null, 2)));
          compilation.emitAsset('spaarke/manifest.test.json', new RawSource(JSON.stringify(test, null, 2)));
          compilation.emitAsset(
            `spaarke/${PACKAGE_ICONS.color}`,
            new RawSource(fs.readFileSync(path.join(assetsDir, 'icon-color-192.png')))
          );
          compilation.emitAsset(
            `spaarke/${PACKAGE_ICONS.outline}`,
            new RawSource(fs.readFileSync(path.join(assetsDir, 'icon-outline.png')))
          );
        }
      );
    });
  }
}

async function getHttpsOptions() {
  if (isProduction) {
    return undefined;
  }
  const httpsOptions = await devCerts.getHttpsServerOptions();
  return {
    ca: httpsOptions.ca,
    key: httpsOptions.key,
    cert: httpsOptions.cert,
  };
}

module.exports = async (env, options) => {
  const mode = options.mode || 'development';
  const addin = env?.addin || 'outlook'; // Default to outlook

  return {
    mode,
    devtool: mode === 'production' ? 'source-map' : 'eval-source-map',
    entry: {
      // Outlook taskpane
      'outlook/taskpane': './outlook/taskpane/index.tsx',
      // Word taskpane
      'word/taskpane': './word/taskpane/index.tsx',
      // Commands (function files)
      'outlook/commands': './outlook/commands/index.ts',
      'word/commands': './word/commands/index.ts',
    },
    output: {
      path: path.resolve(__dirname, 'dist'),
      filename: '[name].bundle.js',
      clean: true,
    },
    resolve: {
      extensions: ['.ts', '.tsx', '.js', '.jsx'],
      alias: {
        '@shared': path.resolve(__dirname, 'shared'),
        '@outlook': path.resolve(__dirname, 'outlook'),
        '@word': path.resolve(__dirname, 'word'),
        // Task 042 (FR-B2 / ADR-045): reuse the code page's EXACT candidate model
        // (`derivePrimaryReview`) — do NOT fork it. `provenance.ts` is a pure module
        // (zero imports), so we alias straight to its source (outside node_modules →
        // ts-loader transpiles it) rather than pulling the whole components lib +
        // React. Exact ($) match so only this pure subpath resolves here.
        '@spaarke/communication-components/logic/connections/provenance$': path.resolve(
          __dirname,
          '../shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts'
        ),
        // Task 096 (owner 2026-10-04: "use our shared UI components so it looks consistent"): the
        // Word pane's Email tab mounts the SAME compose engine the Spaarke email page mounts
        // (`EmailComposer`), through its pane wrapper `SendEmailPane` (ADR-045: every send UX goes
        // through a thin wrapper over the one engine). Exact ($) match to the WRAPPER FILE only —
        // never the `@spaarke/ui-components` barrel, which would pull in the library's Xrm-bound
        // components (the ADR-012 Path A reason this package does not consume the barrel). The
        // wrapper's import closure (22 files) has no Xrm/host dependency; its third-party imports
        // (react, Fluent v9, lexical) resolve from THIS package's node_modules — see the first rule
        // under `module.rules`.
        // Task 099 (ADR-012 amended 2026-10-05 / ADR-044): the ONE shared `cleanGuid` (`utils/guid.ts` — a pure
        // module, zero imports), by exact alias — replaces this package's former local copy.
        '@spaarke/ui-components/guid$': path.resolve(
          __dirname,
          '../shared/Spaarke.UI.Components/src/utils/guid.ts'
        ),
        '@spaarke/ui-components/send-email-pane$': path.resolve(
          __dirname,
          '../shared/Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailPane.tsx'
        ),
        // Task 100 (owner decision C, ADR-012 amended 2026-10-05): the "+ New" form's Assigned To picker is the
        // shared host-agnostic `LookupField` (`onSearch` injected; imports Fluent, react-icons, LookupTypes and the
        // shared thin scrollbar — no Xrm). Exact ($) match to the component FILE, never the barrel.
        '@spaarke/ui-components/lookup-field$': path.resolve(
          __dirname,
          '../shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx'
        ),
      },
    },
    module: {
      rules: [
        {
          // Task 096: the aliased shared compose sources live outside this package, where no node_modules is
          // installed in CI. Their bare imports resolve from THIS package's node_modules FIRST, so the shared
          // engine binds to the add-in's own single copy of react / react-dom / Fluent / lexical (two React
          // copies break hooks) even on a machine that has the shared library installed. Scoped to requests
          // ISSUED by the shared source (Rule.resolve), so the add-in's own dependency resolution — including
          // any nested package versions — is untouched.
          include: path.resolve(__dirname, '../shared/Spaarke.UI.Components/src'),
          resolve: {
            modules: [path.resolve(__dirname, 'node_modules'), 'node_modules'],
          },
        },
        {
          test: /\.tsx?$/,
          use: {
            loader: 'ts-loader',
            options: {
              transpileOnly: true, // Skip type checking during build
            },
          },
          exclude: /node_modules/,
        },
        {
          test: /\.css$/,
          use: [
            mode === 'production' ? MiniCssExtractPlugin.loader : 'style-loader',
            'css-loader',
          ],
        },
        {
          test: /\.(png|jpg|jpeg|gif|svg|ico)$/,
          type: 'asset/resource',
          generator: {
            filename: 'assets/[name][ext]',
          },
        },
      ],
    },
    plugins: [
      // Outlook taskpane HTML
      new HtmlWebpackPlugin({
        template: './outlook/taskpane/taskpane.html',
        filename: 'outlook/taskpane.html',
        chunks: ['outlook/taskpane'],
      }),
      // Outlook taskpane TEST HTML (for browser testing with mock Office.js)
      new HtmlWebpackPlugin({
        template: './outlook/taskpane/taskpane-test.html',
        filename: 'outlook/taskpane-test.html',
        chunks: ['outlook/taskpane'],
      }),
      // Word taskpane HTML
      new HtmlWebpackPlugin({
        template: './word/taskpane/taskpane.html',
        filename: 'word/taskpane.html',
        chunks: ['word/taskpane'],
      }),
      // Outlook commands HTML
      new HtmlWebpackPlugin({
        template: './outlook/commands/commands.html',
        filename: 'outlook/commands.html',
        chunks: ['outlook/commands'],
      }),
      // Word commands HTML
      new HtmlWebpackPlugin({
        template: './word/commands/commands.html',
        filename: 'word/commands.html',
        chunks: ['word/commands'],
      }),
      // Copy manifests and assets
      new CopyWebpackPlugin({
        patterns: [
          { from: './public/index.html', to: 'index.html' },
          {
            // OfficeNaaStrategy's legacy-client fallback MSAL popup redirect target
            // (replaces the deprecated self-built `auth-dialog.html` — task 072).
            // Static, config-free — see the file's own header comment for why it
            // deliberately does NOT instantiate its own MSAL client.
            from: './public/auth-callback.html',
            to: 'auth-callback.html',
          },
          {
            // task 037 (FR-17): the Word ribbon commands' Dialog API notification surface.
            // Static, config-free, same pattern as auth-callback.html above — no Office.js
            // bootstrap, no bundling. Emitted alongside word/commands.html so the same base
            // URL substitution (ADDIN_BASE_URL) that resolves the manifest's Commands.Url also
            // resolves this page when word/commands/index.ts builds its displayDialogAsync URL
            // from window.location.origin at runtime (no separate substitution needed here).
            from: './word/commands/notify.html',
            to: 'word/commands-notify.html',
          },
          {
            // Unified JSON manifest (email-communication-solution-r4 task 072 / FR-25) —
            // single source of truth for BOTH dev and production builds. Retires the
            // divergent `outlook-manifest.xml` (XML v1.0.19) + orphaned `manifest.prod.json`
            // (never referenced by this build). Parameterize app IDs + base URL at build time.
            from: './outlook/manifest.json',
            to: 'outlook/manifest.json',
            transform: (content) => {
              let manifest = content.toString();
              // Replace hardcoded app ID in "id" and "webApplicationInfo.id"
              manifest = manifest.replace(
                /"id":\s*"c1258e2d-1688-49d2-ac99-a7485ebd9995"/g,
                `"id": "${ENV_CONFIG.ADDIN_CLIENT_ID}"`
              );
              // Replace hardcoded resource URI (api://{BFF_API_CLIENT_ID})
              manifest = manifest.replace(
                /"resource":\s*"api:\/\/[a-f0-9-]+"/,
                `"resource": "api://${ENV_CONFIG.BFF_API_CLIENT_ID}"`
              );
              // Replace the manifest's dev-authored base URL with the resolved
              // per-mode ADDIN_BASE_URL (localhost for dev, deployed SWA for prod).
              manifest = manifest.split('https://localhost:3000').join(ENV_CONFIG.ADDIN_BASE_URL);
              return manifest;
            },
          },
          {
            // Legacy XML (OfficeApp) manifest for Word — retained for M365 Admin
            // Center upload + as the sideload rollback path until the unified JSON
            // manifest below is verified on both Word desktop and Word on the web
            // (project spaarkeai-word-add-in-r1 task 011 / FR-05). Do not delete
            // ahead of that verification. Parameterized to the same unified form
            // as the Outlook manifest above — no hardcoded SWA origin. The
            // dev-authored source uses the `https://localhost:3000` placeholder
            // (parity with the Outlook manifest's convention); this substitutes
            // the resolved per-mode `ADDIN_BASE_URL` (localhost for dev, deployed
            // SWA for prod) at build time. Previously hardcoded the production SWA
            // origin unconditionally (`https://icy-desert-0bfdbb61e...`), so dev
            // builds pointed at production — that bug is what this transform fixes.
            from: './word/word-manifest.xml',
            to: 'word/manifest.xml',
            transform: (content) => content.toString().split('https://localhost:3000').join(ENV_CONFIG.ADDIN_BASE_URL),
          },
          // (Task 078) The standalone `word/manifest.json` output was RETIRED here. It was a dev-sideload copy
          // whose top-level `id` webpack rewrote to ADDIN_CLIENT_ID — the same package id as the Outlook JSON —
          // so the two could never be installed side by side. Word's unified manifest now ships ONLY inside the
          // combined package (dist/spaarke/, SpaarkeUnifiedPackagePlugin below); word/manifest.json remains the
          // SOURCE of Word's half. The Word XML above is unchanged — it is the live registration.
          {
            // Legacy XML (OfficeApp/MailApp) manifest for Outlook — the format the M365 admin
            // center "Integrated apps" accepts directly (the unified manifest.json is dev-sideload
            // only, `manifestVersion: devPreview`). Served at /outlook/outlook-manifest.xml so admins
            // can upload it by file OR URL. Self-contained absolute URLs; the transform is a harmless
            // no-op (kept for parity with the Word copy).
            from: './outlook/outlook-manifest.xml',
            to: 'outlook/outlook-manifest.xml',
            noErrorOnMissing: true,
            transform: (content) => content.toString().split('https://localhost:3000').join(ENV_CONFIG.ADDIN_BASE_URL),
          },
          { from: './shared/assets', to: 'assets', noErrorOnMissing: true },
          // Mock Office.js for browser testing
          { from: './outlook/taskpane/mock-office.js', to: 'outlook/mock-office.js', noErrorOnMissing: true },
          { from: './staticwebapp.config.json', to: 'staticwebapp.config.json', noErrorOnMissing: true },
        ],
      }),
      // Define environment variables for client-side code
      new webpack.DefinePlugin({
        'process.env.ADDIN_CLIENT_ID': JSON.stringify(ENV_CONFIG.ADDIN_CLIENT_ID),
        'process.env.TENANT_ID': JSON.stringify(ENV_CONFIG.TENANT_ID),
        'process.env.BFF_API_CLIENT_ID': JSON.stringify(ENV_CONFIG.BFF_API_CLIENT_ID),
        'process.env.BFF_API_BASE_URL': JSON.stringify(ENV_CONFIG.BFF_API_BASE_URL),
        'process.env.SMARTTODO_CODEPAGE_URL': JSON.stringify(ENV_CONFIG.SMARTTODO_CODEPAGE_URL),
        'process.env.ORG_URL': JSON.stringify(ENV_CONFIG.ORG_URL),
        'process.env.SPAARKE_APP_NAME': JSON.stringify(ENV_CONFIG.SPAARKE_APP_NAME),
        'process.env.ADDIN_EMAIL_TAB_ENABLED': JSON.stringify(ENV_CONFIG.ADDIN_EMAIL_TAB_ENABLED),
        'process.env.FALLBACK_REDIRECT_URI': JSON.stringify(ENV_CONFIG.FALLBACK_REDIRECT_URI),
        'process.env.BUILD_DATE': JSON.stringify(BUILD_DATE),
        // Task 089: the pane footer shows the app-package version, not a hand-maintained literal.
        'process.env.ADDIN_PACKAGE_VERSION': JSON.stringify(UNIFIED_PACKAGE.VERSION),
      }),
      // Task 078: the combined Outlook + Word app package → dist/spaarke/.
      new SpaarkeUnifiedPackagePlugin(),
      ...(mode === 'production'
        ? [
            new MiniCssExtractPlugin({
              filename: '[name].css',
            }),
          ]
        : []),
    ],
    devServer: {
      static: {
        directory: path.join(__dirname, 'dist'),
      },
      port: 3000,
      https: await getHttpsOptions(),
      headers: {
        'Access-Control-Allow-Origin': '*',
      },
      hot: true,
      allowedHosts: 'all',
    },
    optimization: {
      splitChunks: {
        chunks: 'all',
        cacheGroups: {
          vendor: {
            test: /[\\/]node_modules[\\/]/,
            name: 'vendors',
            // 'initial', not 'all': packages reached only through a lazy import (the Email tab's compose engine
            // and its rich-text editor) stay in that lazy chunk instead of the startup `vendors` bundle every pane
            // loads (task 096 review).
            chunks: 'initial',
          },
        },
      },
    },
  };
};

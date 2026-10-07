/**
 * Custom webpack config merged on top of pcf-scripts defaults (featureconfig.json: pcfAllowCustomWebpack "on").
 *
 * unified-access-control-r2 task 166 f1: added so this control can be rebuilt at all. Same cause and same remedy as
 * the six Communication* controls (task 092, master build/test baseline repair — see
 * CommunicationActions/webpack.config.js item 5 for the full rationale): the `@spaarke/ui-components` root barrel
 * reaches SprkChat's chat-file attachment, whose `import('pdfjs-dist')` / `import('mammoth')` a single-chunk PCF build
 * must still resolve, and pcf-scripts' babel-loader crashes on `pdfjs-dist/build/pdf.mjs` ("Cannot read properties of
 * null (reading 'declarations')"). This control never renders <SprkChat>, so both packages resolve to the shared LOUD
 * stubs (they throw if ever reached), not to `false`. `@spaarke/sdap-client` is NOT stubbed: this control uses it.
 */
const path = require('path');

module.exports = {
  resolve: {
    alias: {
      // Loud stubs, not `false` — see ../shared/stubs/pdfjsDistUnreachable.js for why.
      'pdfjs-dist$': path.resolve(__dirname, '../shared/stubs/pdfjsDistUnreachable.js'),
      'mammoth$': path.resolve(__dirname, '../shared/stubs/mammothUnreachable.js'),
    },
  },
};

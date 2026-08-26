const FLUENT_ALIAS = 'FluentUIReactv940';
// SAFE subset only: every symbol the compat packages import from these is
// verified present on the umbrella's RUNTIME export surface.
// Deliberately EXCLUDED (symbols missing at runtime -> would crash on mount):
//   react-utilities, react-shared-contexts, react-motion-components-preview
const GRANULAR = [
  'react-components',
  'react-theme', 'react-tabster', 'react-motion',
  'react-portal', 'react-input', 'react-field',
  'react-popover', 'react-positioning',
];
const map = { react: 'React', 'react-dom': 'ReactDOM' };
GRANULAR.forEach(p => { map[`@fluentui/${p}`] = FLUENT_ALIAS; });

module.exports = {
  optimization: { usedExports: true, sideEffects: true, innerGraph: true, providedExports: true },
  externals: [
    map,
    function (ctx, cb) {
      if (/^@fluentui\/react-components\//.test(ctx.request || '')) return cb(null, FLUENT_ALIAS);
      cb();
    },
  ],
  module: {
    rules: [
      { test: /[\/]node_modules[\/]@fluentui[\/]react-icons[\/]/, sideEffects: false },
      { test: /[\/]node_modules[\/]@fluentui[\/]react-(calendar|datepicker)-compat[\/]/, sideEffects: false },
    ],
  },
};

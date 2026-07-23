/**
 * Babel config for babel-jest — transforms the ESM `.js` in
 * @fluentui/react-charting + d3 that Jest pulls in (see jest.config.cjs
 * transformIgnorePatterns). `.cjs` because this package is `"type": "module"`.
 */
module.exports = {
  presets: [['@babel/preset-env', { targets: { node: 'current' } }], '@babel/preset-react', '@babel/preset-typescript'],
};

const path = require('path');
const webpack = require('webpack');

module.exports = {
  mode: 'production',
  cache: { type: 'filesystem' },
  entry: './src/index.tsx',
  output: {
    filename: 'bundle.js',
    path: path.resolve(__dirname, 'out'),
    publicPath: '',
    clean: true,
  },
  resolve: {
    extensions: ['.ts', '.tsx', '.js', '.jsx'],
    // Force bare module resolution (react, react-dom, etc.) through THIS
    // code page's node_modules FIRST. Without this, the shared lib's
    // node_modules provides a second React copy, breaking hooks/context
    // in components like ReactFlow that depend on a single React instance.
    modules: [path.resolve(__dirname, 'node_modules'), 'node_modules'],
    alias: {
      '@spaarke/auth': path.resolve(__dirname, '../../shared/Spaarke.Auth/dist'),
      '@spaarke/ui-components': path.resolve(__dirname, '../../shared/Spaarke.UI.Components/dist'),
    },
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: {
          loader: 'esbuild-loader',
          options: { loader: 'tsx', target: 'es2020' },
        },
        exclude: /node_modules/,
      },
      {
        // Required for @xyflow/react styles
        test: /\.css$/,
        use: ['style-loader', 'css-loader'],
      },
    ],
  },
  optimization: {
    splitChunks: false,
  },
  plugins: [
    new webpack.optimize.LimitChunkCountPlugin({ maxChunks: 1 }),
  ],
  // NO externals — Code Pages bundle everything (unlike PCF platform library approach)
  devServer: {
    static: path.resolve(__dirname),
    port: 3001,
    hot: true,
  },
};

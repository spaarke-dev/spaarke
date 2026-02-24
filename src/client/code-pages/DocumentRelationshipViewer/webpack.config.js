const path = require('path');

module.exports = {
  mode: 'production',
  entry: './src/index.tsx',
  output: {
    filename: 'bundle.js',
    path: path.resolve(__dirname, 'out'),
    clean: true,
  },
  resolve: {
    extensions: ['.ts', '.tsx', '.js', '.jsx'],
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: 'ts-loader',
        exclude: /node_modules/,
      },
      {
        // Required for @xyflow/react styles
        test: /\.css$/,
        use: ['style-loader', 'css-loader'],
      },
    ],
  },
  // NO externals — Code Pages bundle everything (unlike PCF platform library approach)
  devServer: {
    static: path.resolve(__dirname),
    port: 3001,
    hot: true,
  },
};

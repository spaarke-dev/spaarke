// Jest setup for UniversalQuickCreate PCF control
require('@testing-library/jest-dom');

// Polyfills for streaming APIs (not available in jsdom)
const { TextEncoder, TextDecoder } = require('util');
const { ReadableStream } = require('stream/web');

global.TextEncoder = TextEncoder;
global.TextDecoder = TextDecoder;
global.ReadableStream = ReadableStream;

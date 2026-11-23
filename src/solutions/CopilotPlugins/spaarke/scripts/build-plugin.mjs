#!/usr/bin/env node
// Builds dist/spaarke-plugin.zip: manifest.json and the icons at the zip root, skills/<name>/... beside them.
// Runs the gate first and refuses to build an invalid package. Pure Node (zlib), so it works the same on Windows and Linux.
// Entries are sorted and carry a fixed timestamp, so the same sources always give the same zip.

import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateRawSync } from 'node:zlib';

import { validatePackage } from './validate-plugin.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const out = path.join(root, 'dist', 'spaarke-plugin.zip');

const CRC_TABLE = Uint32Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) {
    c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  }
  return c >>> 0;
});

function crc32(buffer) {
  let c = 0xffffffff;
  for (const byte of buffer) {
    c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

function filesUnder(dir, prefix) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
    e.isDirectory() ? filesUnder(path.join(dir, e.name), `${prefix}${e.name}/`) : [{ name: `${prefix}${e.name}`, file: path.join(dir, e.name) }],
  );
}

export function zipEntries(packageRoot) {
  const appPackage = readdirSync(path.join(packageRoot, 'appPackage'), { withFileTypes: true })
    .filter((e) => e.isFile())
    .map((e) => ({ name: e.name, file: path.join(packageRoot, 'appPackage', e.name) }));
  const skills = filesUnder(path.join(packageRoot, 'skills'), 'skills/');
  return [...appPackage, ...skills].sort((a, b) => (a.name < b.name ? -1 : 1));
}

export function buildZip(entries) {
  const DOS_TIME = 0;
  const DOS_DATE = (0 << 9) | (1 << 5) | 1; // 1980-01-01
  const local = [];
  const central = [];
  let offset = 0;
  for (const { name, file } of entries) {
    const data = readFileSync(file);
    const deflated = deflateRawSync(data);
    const store = deflated.length >= data.length;
    const body = store ? data : deflated;
    const method = store ? 0 : 8;
    const nameBytes = Buffer.from(name, 'utf8');
    const crc = crc32(data);

    const head = Buffer.alloc(30);
    head.writeUInt32LE(0x04034b50, 0);
    head.writeUInt16LE(20, 4); // version needed
    head.writeUInt16LE(0x0800, 6); // UTF-8 names
    head.writeUInt16LE(method, 8);
    head.writeUInt16LE(DOS_TIME, 10);
    head.writeUInt16LE(DOS_DATE, 12);
    head.writeUInt32LE(crc, 14);
    head.writeUInt32LE(body.length, 18);
    head.writeUInt32LE(data.length, 22);
    head.writeUInt16LE(nameBytes.length, 26);
    local.push(head, nameBytes, body);

    const dir = Buffer.alloc(46);
    dir.writeUInt32LE(0x02014b50, 0);
    dir.writeUInt16LE(20, 4); // version made by
    dir.writeUInt16LE(20, 6); // version needed
    dir.writeUInt16LE(0x0800, 8);
    dir.writeUInt16LE(method, 10);
    dir.writeUInt16LE(DOS_TIME, 12);
    dir.writeUInt16LE(DOS_DATE, 14);
    dir.writeUInt32LE(crc, 16);
    dir.writeUInt32LE(body.length, 20);
    dir.writeUInt32LE(data.length, 24);
    dir.writeUInt16LE(nameBytes.length, 28);
    dir.writeUInt32LE(offset, 42);
    central.push(dir, nameBytes);

    offset += head.length + nameBytes.length + body.length;
  }
  const centralSize = central.reduce((n, b) => n + b.length, 0);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(centralSize, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...local, ...central, end]);
}

const errors = validatePackage(root);
if (errors.length > 0) {
  console.error(`Refusing to build: the package failed validation (${errors.length}):`);
  for (const e of errors) {
    console.error(`  - ${e}`);
  }
  process.exit(1);
}

const entries = zipEntries(root);
mkdirSync(path.dirname(out), { recursive: true });
writeFileSync(out, buildZip(entries));
console.log(`Wrote ${path.relative(process.cwd(), out) || out} (${entries.length} entries):`);
for (const e of entries) {
  console.log(`  ${e.name}`);
}

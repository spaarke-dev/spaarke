// Comment-aware TDZ scanner — strips block + line comments before regex matching.
import fs from 'fs';
import path from 'path';

const rootDirs = [
  'src/solutions/LegalWorkspace/src',
  'src/solutions/SpaarkeAi/src',
  'src/solutions/DailyBriefing/src',
  'src/client/code-pages/PlaybookBuilder/src',
  'src/client/shared/Spaarke.UI.Components/src',
  'src/client/shared/Spaarke.DailyBriefing.Components/src',
  'src/client/shared/Spaarke.AI.Widgets/src',
  'src/client/shared/Spaarke.Events.Components/src',
  'src/client/shared/Spaarke.SmartTodo.Components/src',
];

function* walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (
        ['node_modules', 'dist', '__tests__', '__mocks__', 'examples', 'test', 'tests'].includes(
          e.name,
        )
      )
        continue;
      yield* walk(p);
    } else if (
      e.isFile() &&
      /\.(tsx?|jsx?)$/.test(e.name) &&
      !e.name.endsWith('.d.ts') &&
      !/\.(test|spec)\.(tsx?|jsx?)$/.test(e.name)
    ) {
      yield p;
    }
  }
}

// Strip block + line comments while preserving line breaks (so line numbers stay aligned).
function stripComments(src) {
  let out = '';
  let i = 0;
  const N = src.length;
  let inString = null; // '"' | "'" | '`'
  let inLine = false;
  let inBlock = false;
  while (i < N) {
    const c = src[i];
    const c2 = src[i + 1];
    if (inLine) {
      if (c === '\n') {
        out += c;
        inLine = false;
      } else {
        out += ' ';
      }
      i++;
      continue;
    }
    if (inBlock) {
      if (c === '*' && c2 === '/') {
        out += '  ';
        i += 2;
        inBlock = false;
      } else if (c === '\n') {
        out += c;
        i++;
      } else {
        out += ' ';
        i++;
      }
      continue;
    }
    if (inString) {
      if (c === '\\' && i + 1 < N) {
        out += c + src[i + 1];
        i += 2;
        continue;
      }
      if (c === inString) inString = null;
      out += c;
      i++;
      continue;
    }
    if (c === '/' && c2 === '/') {
      inLine = true;
      out += '  ';
      i += 2;
      continue;
    }
    if (c === '/' && c2 === '*') {
      inBlock = true;
      out += '  ';
      i += 2;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') inString = c;
    out += c;
    i++;
  }
  return out;
}

const VALID_IDENT = /^[A-Za-z_$][A-Za-z0-9_$]*$/;
let findings = 0;
const findingsList = [];

for (const rd of rootDirs) {
  if (!fs.existsSync(rd)) continue;
  for (const f of walk(rd)) {
    const rawText = fs.readFileSync(f, 'utf8');
    const text = stripComments(rawText);
    const lines = text.split(/\r?\n/);
    const declLine = new Map();
    lines.forEach((ln, i) => {
      const m = ln.match(/^\s*(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=/);
      if (m && !declLine.has(m[1])) declLine.set(m[1], i + 1);
    });
    const regex = /(use(?:Callback|Memo|Effect|LayoutEffect|ImperativeHandle))\s*\([\s\S]*?,\s*\[([^\]]*?)\]\s*\)/g;
    let match;
    while ((match = regex.exec(text)) !== null) {
      const hookStart = text.slice(0, match.index).split(/\r?\n/).length;
      const depsRaw = match[2];
      const deps = depsRaw
        .split(',')
        .map((s) => s.trim())
        .filter((s) => VALID_IDENT.test(s));
      for (const d of deps) {
        const dl = declLine.get(d);
        if (dl !== undefined && dl > hookStart) {
          findingsList.push({ file: f, hookLine: hookStart, dep: d, declLine: dl });
          findings++;
        }
      }
    }
  }
}

if (findings === 0) {
  console.log('✓ Zero TDZ findings across scanned packages.');
} else {
  console.log(`TDZ findings (${findings}):`);
  for (const x of findingsList) {
    console.log(`  ${path.relative('.', x.file)}:${x.hookLine}  deps:[${x.dep}]  declared at line ${x.declLine}`);
  }
}

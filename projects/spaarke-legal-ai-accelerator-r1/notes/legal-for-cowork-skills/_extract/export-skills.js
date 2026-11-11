// Export the skills of the "Legal for Cowork" plugin as JSON.
//
// Run in the browser DevTools console (F12 → Console) on any
// https://copilot.cloud.microsoft/cowork page while signed in.
// It walks each skill page (#/customize/skills/<name>), converts the
// Instructions panel to markdown and saves legal-for-cowork-skills.json
// to your Downloads folder. Then run split-skills.ps1 to write one .md per skill.
//
// To refresh after a plugin update, edit NAMES/VERSION if the list changed.
(async () => {
  const VERSION = '1.0.10';
  const NAMES = ['analyze-related-contracts', 'build-case-chronology', 'check-cross-references',
    'check-defined-terms', 'check-execution-readiness', 'compare-across-jurisdictions',
    'compare-key-terms', 'create-playbook', 'draft-new-contract', 'draft-legal-memo',
    'draft-minutes-and-resolutions', 'explain-legal-concepts', 'extract-obligations-and-deadlines',
    'identify-regulatory-gaps', 'improve-legal-prompt', 'edit-contract', 'legal-reasoning',
    'review-contract', 'summarize-legal-matters', 'summarize-redlines'];
  const sleep = ms => new Promise(r => setTimeout(r, ms));

  function toMarkdown(node) {
    function inline(n) {
      if (n.nodeType === 3) return n.textContent.replace(/\s+/g, ' ');
      if (n.nodeType !== 1) return '';
      const t = n.tagName, inner = () => [...n.childNodes].map(inline).join('');
      if (t === 'CODE') return '`' + n.textContent + '`';
      if (t === 'STRONG' || t === 'B') { const s = inner(); return s.trim() ? '**' + s.trim() + '**' + (/\s$/.test(s) ? ' ' : '') : s; }
      if (t === 'EM' || t === 'I') { const s = inner(); return s.trim() ? '*' + s.trim() + '*' : s; }
      if (t === 'A') return '[' + inner() + '](' + n.getAttribute('href') + ')';
      if (t === 'BR') return '  \n';
      if (t === 'DEL' || t === 'S') return '~~' + inner() + '~~';
      return inner();
    }
    function list(n, depth) {
      const ordered = n.tagName === 'OL'; let i = +(n.getAttribute('start') || 1), out = '';
      for (const li of n.children) {
        if (li.tagName !== 'LI') continue;
        const marker = ordered ? (i++) + '. ' : '- ', ind = '   '.repeat(depth);
        let text = '', sub = '';
        for (const c of li.childNodes) {
          if (c.nodeType === 1 && (c.tagName === 'UL' || c.tagName === 'OL')) sub += list(c, depth + 1);
          else if (c.nodeType === 1 && c.tagName === 'P') text += (text ? '\n' + ind + '   ' : '') + inline(c).trim();
          else if (c.nodeType === 1 && c.tagName === 'PRE') sub += ind + '   ```\n' + c.textContent.replace(/\n$/, '').split('\n').map(l => ind + '   ' + l).join('\n') + '\n' + ind + '   ```\n';
          else text += inline(c);
        }
        out += ind + marker + text.trim() + '\n' + sub;
      }
      return out;
    }
    function block(n, depth) {
      if (n.nodeType === 3) { const s = n.textContent.trim(); return s ? s + '\n\n' : ''; }
      if (n.nodeType !== 1) return '';
      const t = n.tagName;
      if (/^H[1-6]$/.test(t)) return '#'.repeat(+t[1]) + ' ' + inline(n).trim() + '\n\n';
      if (t === 'P') return inline(n).trim() + '\n\n';
      if (t === 'PRE') return '```\n' + n.textContent.replace(/\n$/, '') + '\n```\n\n';
      if (t === 'HR') return '---\n\n';
      if (t === 'BLOCKQUOTE') return [...n.childNodes].map(c => block(c, depth)).join('').trim().split('\n').map(l => l ? '> ' + l : '>').join('\n') + '\n\n';
      if (t === 'UL' || t === 'OL') return list(n, depth) + (depth === 0 ? '\n' : '');
      if (t === 'TABLE') {
        const rows = [...n.querySelectorAll('tr')].map(r => [...r.children].map(c => inline(c).trim().replace(/\|/g, '\\|')));
        if (!rows.length) return '';
        let out = '| ' + rows[0].join(' | ') + ' |\n|' + rows[0].map(() => '---').join('|') + '|\n';
        rows.slice(1).forEach(r => out += '| ' + r.join(' | ') + ' |\n');
        return out + '\n';
      }
      const hasBlock = [...n.children].some(c => /^(P|H[1-6]|UL|OL|PRE|TABLE|DIV|SECTION|BLOCKQUOTE|HR)$/.test(c.tagName));
      if (!hasBlock) { const s = inline(n).trim(); return s ? s + '\n\n' : ''; }
      return [...n.childNodes].map(c => block(c, depth)).join('');
    }
    return block(node, 0).replace(/\n{3,}/g, '\n\n').trim() + '\n';
  }

  async function grab(name) {
    location.hash = '#/customize/plugins/x';
    await sleep(300);
    location.hash = '#/customize/skills/' + name;
    let ins;
    for (let k = 0; k < 60; k++) {
      await sleep(250);
      const title = [...document.querySelectorAll('main h1, main h2')].find(e => e.textContent.trim() === name);
      ins = [...document.querySelectorAll('main h2')].find(e => e.textContent.trim() === 'Instructions');
      if (title && ins) break;
    }
    if (!ins) throw new Error('Instructions panel not found');
    const more = [...document.querySelectorAll('main button')].find(b => b.innerText.trim() === 'Show more');
    if (more) { more.click(); await sleep(500); }
    ins = [...document.querySelectorAll('main h2')].find(e => e.textContent.trim() === 'Instructions');
    const section = ins.parentElement.parentElement;
    const texts = [...section.parentElement.children].filter(c => c !== section).map(c => c.innerText.trim()).filter(Boolean);
    const description = (texts.find(x => !x.startsWith(name + '\n')) || '').replace(/\n*Show less$/, '').trim();
    return { name, description, instructions: toMarkdown(section.children[1]) };
  }

  const skills = [], failed = [];
  for (const n of NAMES) {
    try { skills.push(await grab(n)); console.log('ok', n); }
    catch (e) { failed.push(n); console.warn('FAILED', n, e); }
  }
  const out = { plugin: 'Legal for Cowork', publisher: 'Microsoft Corporation', version: VERSION,
    extracted: new Date().toISOString().slice(0, 10), skills };
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([JSON.stringify(out, null, 2)], { type: 'application/json' }));
  a.download = 'legal-for-cowork-skills.json';
  a.click();
  console.log(`Saved ${skills.length} skills` + (failed.length ? `; FAILED: ${failed.join(', ')}` : ''));
})();

"""
Accuracy re-verification for task 010.

METHOD (the point of this rewrite): for each ADR, extract the artifacts the ADR
ITSELF NAMES -- backticked type/file/entity identifiers -- and check those against
the real tree. The first pass guessed identifier names and produced a false
negative on ADR-047; this reads the ADR for what to look for instead.

Emits, per ADR: named artifacts found / not found, and the resulting evidence tier.
Judgement stays with the human reading the output -- this only supplies evidence.
"""
import glob, re, io, os, json, collections

SKIP_DIRS = {'node_modules', 'bin', 'obj', 'dist', '.git', '.vs', 'coverage'}

# ---------- index the tree once ----------
file_names = set()      # basenames, with and without extension
rel_paths = set()       # forward-slash relative paths
for top in ('src', 'tests', 'scripts'):
    for root, dirs, files in os.walk(top):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            file_names.add(f)
            file_names.add(os.path.splitext(f)[0])
            rel_paths.add(os.path.join(root, f).replace(os.sep, '/'))

all_paths_blob = '\n'.join(rel_paths)

# Symbol index: class/interface/record/type names declared in .cs and .ts/.tsx
symbols = set()
decl_cs = re.compile(r'\b(?:class|interface|record|struct|enum)\s+([A-Z][A-Za-z0-9_]*)')
decl_ts = re.compile(r'\b(?:class|interface|type|const|function)\s+([A-Za-z_][A-Za-z0-9_]*)')
for p in rel_paths:
    if p.endswith(('.cs', '.ts', '.tsx')):
        try:
            t = io.open(p, encoding='utf-8', errors='ignore').read()
        except Exception:
            continue
        rx = decl_cs if p.endswith('.cs') else decl_ts
        symbols.update(rx.findall(t))

# Dataverse entity/table names anywhere in source
entity_blob = set()
for p in rel_paths:
    if p.endswith(('.cs', '.ts', '.tsx', '.json', '.xml')):
        try:
            t = io.open(p, encoding='utf-8', errors='ignore').read()
        except Exception:
            continue
        entity_blob.update(re.findall(r'\bsprk_[a-z0-9_]+', t))


def exists(tok):
    """Is this named artifact present anywhere real in the tree?"""
    if tok.startswith('sprk_'):
        return tok in entity_blob
    if '/' in tok:                       # a path fragment
        return tok.strip('/') in all_paths_blob
    if tok.endswith(('.cs', '.ts', '.tsx', '.ps1', '.yml', '.json')):
        return tok in file_names
    return tok in symbols or tok in file_names


# ---------- per-ADR extraction ----------
# Only tokens that plausibly name a code artifact. Deliberately conservative:
# prose in backticks (e.g. `MUST`) must not be counted as a missing artifact.
TOKEN = re.compile(r'`([^`\n]{3,80})`')
CODEISH = re.compile(
    r'^(?:'
    r'sprk_[a-z0-9_]+'                                  # dataverse entity
    r'|I?[A-Z][A-Za-z0-9]*(?:Service|Store|Client|Factory|Provider|Handler|Executor'
    r'|Manager|Repository|Resolver|Builder|Job|Worker|Filter|Middleware|Context'
    r'|Registry|Engine|Renderer|Producer|Shell|Bus|Ledger|Options|Tests?)'   # typed suffixes
    r'|[A-Za-z0-9_.]+\.(?:cs|ts|tsx|ps1|yml|json)'      # file
    r'|(?:src|tests|scripts)/[A-Za-z0-9_./*-]+'         # path
    r')$'
)

rows = []
for path in sorted(glob.glob('.claude/adr/ADR-*.md')):
    adr = re.search(r'ADR-(\d{3})', os.path.basename(path)).group(1)
    text = io.open(path, encoding='utf-8', errors='ignore').read()
    title = re.sub(r'^ADR-\d+:\s*', '',
                   text.split('\n', 1)[0].replace('# ', '').replace(' (Concise)', '')).strip()

    toks = set()
    for raw in TOKEN.findall(text):
        t = raw.strip().split('(')[0].strip()          # drop call parens
        t = t.split('<')[0].strip()                    # drop generics
        if CODEISH.match(t):
            toks.add(t)

    found = sorted(t for t in toks if exists(t))
    missing = sorted(t for t in toks if not exists(t))
    total = len(toks)
    ratio = (len(found) / total) if total else None

    if total == 0:
        tier = 'NO-NAMED-ARTIFACTS'      # ADR names nothing checkable -> needs human read
    elif ratio == 1.0:
        tier = 'ALL-PRESENT'
    elif ratio >= 0.7:
        tier = 'MOSTLY-PRESENT'
    elif ratio >= 0.3:
        tier = 'PARTIAL'
    else:
        tier = 'MOSTLY-ABSENT'

    rows.append({
        'adr': adr, 'title': title, 'total': total,
        'found': len(found), 'tier': tier,
        'missing': missing[:8],
    })

json.dump(rows, io.open('/tmp/adr_verify.json', 'w', encoding='utf-8'), indent=1)

order = ['MOSTLY-ABSENT', 'PARTIAL', 'NO-NAMED-ARTIFACTS', 'MOSTLY-PRESENT', 'ALL-PRESENT']
print('%-5s %-46s %5s %6s  %s' % ('ADR', 'TITLE', 'NAMED', 'FOUND', 'TIER'))
for t in order:
    for r in sorted(rows, key=lambda x: -x['total']):
        if r['tier'] == t:
            print('%-5s %-46s %5d %6d  %s' % (r['adr'], r['title'][:46], r['total'], r['found'], r['tier']))

print()
c = collections.Counter(r['tier'] for r in rows)
for t in order:
    print('%-20s %d' % (t, c[t]))

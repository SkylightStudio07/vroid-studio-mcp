#!/usr/bin/env python3
"""Index an Il2CppDumper dump.cs into a searchable JSONL + summary files.

Usage:
    python3 index_dump.py <dump.cs> <out_dir>

Produces:
    types.jsonl   one JSON object per type (namespace, name, kind, base, index, members)
    methods.tsv   namespace<TAB>type<TAB>method<TAB>rva<TAB>offset<TAB>va<TAB>slot
"""
import json
import re
import sys
import os

TYPE_RE = re.compile(
    r'^(?P<attrs>(?:\[[^\]]*\]\s*)*)'
    r'(?P<kind>public|internal|private|protected)?\s*'
    r'(?P<mods>(?:sealed |abstract |static |partial |readonly )*)'
    r'(?P<decl>class|struct|interface|enum)\s+'
    r'(?P<name>[A-Za-z0-9_.<>`]+)'
    r'(?P<base>\s*:\s*[^\n{]+)?'
    r'\s*//\s*TypeDefIndex:\s*(?P<idx>\d+)'
)

METHOD_RE = re.compile(
    r'^(?P<indent>\s*)'
    r'(?P<attrs>(?:\[[^\]]*\]\s*)*)'
    r'//\s*RVA:\s*(?P<rva>0x[0-9A-Fa-f]+|-1)\s+'
    r'Offset:\s*(?P<off>0x[0-9A-Fa-f]+|-1)'
    r'(?:\s+VA:\s*(?P<va>0x[0-9A-Fa-f]+))?'
    r'(?:\s+Slot:\s*(?P<slot>\d+))?\s*$'
)

MEMBER_RE = re.compile(
    r'^\t(?P<attrs>(?:\[[^\]]*\]\s*)*)'
    r'(?P<decl>(?:public|private|protected|internal|static|virtual|override|abstract|sealed|readonly|const|extern|unsafe|new|async|\s)*)'
    r'(?P<sig>.+?)\s*(?:\{[^}]*\})?\s*;?\s*$'
)


def parse(path):
    ns = ''
    cur = None
    pending_attrs = []
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.rstrip('\n')
            if line.startswith('// Namespace:'):
                ns = line.split(':', 1)[1].strip()
                continue
            if line.startswith('// Image') or line.startswith('// TypeDefIndex'):
                continue

            m = METHOD_RE.match(line)
            if m and cur is not None:
                # method sig is the following non-empty line
                continue

            tm = TYPE_RE.match(line)
            if tm:
                if cur:
                    yield cur
                decl = tm.group('decl')
                name = tm.group('name')
                base = (tm.group('base') or '').strip().lstrip(':').strip()
                cur = {
                    'namespace': ns,
                    'name': name,
                    'full_name': (ns + '.' + name) if ns else name,
                    'kind': decl,
                    'base': base,
                    'index': int(tm.group('idx')),
                    'file': tm.group('attrs') + tm.group('decl') + ' ' + name + (' : ' + base if base else ''),
                    'fields': [],
                    'properties': [],
                    'methods': [],
                }
                continue

            if cur is not None and line.startswith('\t'):
                # member region
                if line.strip().startswith('//'):
                    continue
                cm = re.match(r'^\t(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|readonly|const|extern|unsafe|new|async|partial)\s+)*'
                              r'[A-Za-z_][A-Za-z0-9_.<>,\[\]\? ]*\s+[A-Za-z_][A-Za-z0-9_]*\s*;', line)
                if cm and ';' in line and '(' not in line.split(';')[0]:
                    cur['fields'].append(line.strip())
                elif '(' in line:
                    cur['methods'].append(line.strip())
                else:
                    cur['properties'].append(line.strip())
                continue
            if cur is not None and line.startswith('}'):
                yield cur
                cur = None
                continue
    if cur:
        yield cur


def main():
    src, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    types_path = os.path.join(outdir, 'types.jsonl')
    methods_path = os.path.join(outdir, 'methods.tsv')
    n = 0
    with open(types_path, 'w', encoding='utf-8') as tf, \
         open(methods_path, 'w', encoding='utf-8') as mf:
        for t in parse(src):
            tf.write(json.dumps(t, ensure_ascii=False) + '\n')
            n += 1
            for meth in t['methods']:
                mf.write('\t'.join([t['namespace'], t['name'], meth]) + '\n')
    print(f'indexed {n} types -> {types_path}')


if __name__ == '__main__':
    main()

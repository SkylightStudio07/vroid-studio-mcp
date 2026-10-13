#!/usr/bin/env python3
"""Query helper over types.jsonl.

Usage:
  python3 q.py ns <namespace-prefix>
  python3 q.py find <substr>            # search full_name
  python3 q.py type <exact-full-name>   # print full type incl members
  python3 q.py members <full-name> <substr>
"""
import json
import sys
import os

TYPES = os.path.join(os.path.dirname(__file__), '..', 'output', 'il2cpp', 'index', 'types.jsonl')


def load():
    with open(TYPES, encoding='utf-8') as f:
        return [json.loads(l) for l in f]


def main():
    cmd = sys.argv[1]
    arg = sys.argv[2]
    ts = load()
    if cmd == 'ns':
        for t in ts:
            if t['namespace'] == arg or t['namespace'].startswith(arg):
                print(f"{t['kind']:9} {t['full_name']} : {t['base']}  [{t['index']}]")
    elif cmd == 'find':
        for t in ts:
            if arg.lower() in t['full_name'].lower():
                print(f"{t['kind']:9} {t['full_name']} : {t['base']}  [{t['index']}]")
    elif cmd == 'type':
        for t in ts:
            if t['full_name'] == arg:
                print(json.dumps(t, indent=2, ensure_ascii=False))
    elif cmd == 'members':
        sub = sys.argv[3] if len(sys.argv) > 3 else ''
        for t in ts:
            if t['full_name'] == arg:
                for k in ('fields', 'properties', 'methods'):
                    for m in t[k]:
                        if sub.lower() in m.lower():
                            print(f"{k[:-1]:10} {m}")


if __name__ == '__main__':
    main()

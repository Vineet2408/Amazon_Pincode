from pathlib import Path
import re

root = Path(__file__).resolve().parent
codes = (root / 'pincode.txt').read_text(encoding='utf-8-sig').splitlines()
assert codes and all(re.fullmatch(r'[1-9][0-9]{5}', code) for code in codes)
assert len(codes) == len(set(codes)), 'Source list contains duplicates'
size, extra = divmod(len(codes), 4)
offset = 0
parts = []
for i in range(4):
    count = size + (i < extra)
    part = codes[offset:offset + count]
    path = root / f'pincode_{i+1}.txt'
    path.write_text('\n'.join(part) + '\n', encoding='utf-8')
    parts.append(path.read_text(encoding='utf-8').splitlines())
    offset += count
    print(f'{path.name}: {count} pincodes')
assert [code for part in parts for code in part] == codes
assert sum(map(len, parts)) == len(set(code for part in parts for code in part))
print(f'Verified: all {len(codes)} codes included exactly once across four files.')

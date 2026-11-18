"""Check current Markdown links; archived snapshots intentionally retain historical links."""
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]


def main():
    files = [ROOT / 'README.md', ROOT / 'THIRD_PARTY_NOTICES.md']
    for directory in ('docs', 'data', 'models', 'training', 'experiments', 'src/backend'):
        files.extend(file for file in (ROOT / directory).rglob('*.md')
                     if not any(part in {'archive', 'private', 'legacy', 'results', '__pycache__'}
                                for part in file.relative_to(ROOT).parts))
    failures = []
    count = 0
    for file in sorted(set(files)):
        source = file.read_text(encoding='utf-8-sig')
        # Code examples may contain illustrative Markdown rather than live links.
        source = re.sub(r'```.*?```', '', source, flags=re.S)
        for match in re.finditer(r'!?\[[^\]\n]*\]\(([^)\n]+)\)', source):
            target = match.group(1).strip().split(' "')[0].strip('<>')
            if urlsplit(target).scheme or target.startswith('#'):
                continue
            target = unquote(target.split('#', 1)[0])
            count += 1
            if target and not (file.parent / target).resolve().exists():
                failures.append(f'{file.relative_to(ROOT)} -> {target}')
    for failure in failures:
        print(f'BROKEN: {failure}')
    print(f'{len(files)} documents, {count} local links, {len(failures)} broken')
    return bool(failures)


if __name__ == '__main__':
    raise SystemExit(main())

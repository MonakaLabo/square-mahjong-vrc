# VCC / ALCOM 用の一覧(VPM のリスト)を作る。GitHub Actions(release.yml)から呼ぶ。
#   python3 .github/build_listing.py <出力フォルダ>
# リリースに載っている zip をすべて読み、中の package.json と zip の SHA256 から index.json を作る。
import hashlib
import io
import json
import os
import subprocess
import sys
import urllib.request
import zipfile

REPO = os.environ.get('GITHUB_REPOSITORY', 'MonakaLabo/square-mahjong-vrc')
OWNER, NAME = REPO.split('/')
LISTING_URL = f'https://{OWNER.lower()}.github.io/{NAME}/index.json'
out = sys.argv[1] if len(sys.argv) > 1 else 'site'
os.makedirs(out, exist_ok=True)

releases = json.loads(subprocess.check_output(['gh', 'api', f'repos/{REPO}/releases', '--paginate']))
packages = {}
for rel in releases:
    if rel.get('draft'):
        continue
    for asset in rel.get('assets', []):
        if not asset['name'].endswith('.zip'):
            continue
        url = asset['browser_download_url']
        data = urllib.request.urlopen(url).read()
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            pkg = json.loads(z.read('package.json').decode('utf-8-sig'))
        pkg['url'] = url
        pkg['zipSHA256'] = hashlib.sha256(data).hexdigest()
        packages.setdefault(pkg['name'], {'versions': {}})['versions'][pkg['version']] = pkg
        print('added', pkg['name'], pkg['version'])

listing = {
    'name': 'MonakaLabo',
    'id': 'jp.monakalabo.vpm',
    'url': LISTING_URL,
    'author': 'SavannaMonaka (MonakaLabo)',
    'packages': packages,
}
with open(os.path.join(out, 'index.json'), 'w', encoding='utf-8') as f:
    json.dump(listing, f, ensure_ascii=False, indent=2)

# 人が開いたとき用の案内ページ
with open(os.path.join(out, 'index.html'), 'w', encoding='utf-8') as f:
    f.write(f'''<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>MonakaLabo VPM</title>
<body style="font-family:sans-serif;max-width:40em;margin:2em auto;padding:0 1em;line-height:1.7">
<h1>MonakaLabo VPM</h1>
<p>VCC / ALCOM の「リポジトリを追加」に、次の URL を入れてください。</p>
<p><code style="background:#eee;padding:.3em .5em">{LISTING_URL}</code></p>
<p><a href="vcc://vpm/addRepo?url={LISTING_URL}">VCC に追加する</a></p>
<ul>{''.join(f"<li>{n}: {', '.join(sorted(p['versions']))}</li>" for n, p in packages.items())}</ul>
<p><a href="https://github.com/{REPO}">GitHub</a></p>
</body></html>''')
print('wrote', os.path.join(out, 'index.json'))

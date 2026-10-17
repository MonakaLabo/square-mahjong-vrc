# TextMeshPro の日本語フォントに入れる文字の一覧を作る。
#   python make-font-chars.py
# 卓の表示に使う文字(スクリプトの文字列の中身)と、ASCII・牌・記号を集めて Runtime/Fonts/SqmCharacters.txt に書く。
# Font Asset Creator の「Characters from File」でこのファイルを選ぶ。文言を足したら作り直す。
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..')
SOURCES = ['Runtime/Scripts/Table/SqmTable.cs', 'Editor/SqmBuilder.cs']
STRING = re.compile(r'"((?:[^"\\]|\\.)*)"')

chars = set(chr(c) for c in range(0x20, 0x7f))
for f in SOURCES:
    src = open(os.path.join(ROOT, f), encoding='utf8').read()
    for m in STRING.finditer(src):
        chars.update(m.group(1))
chars.update('、。・「」『』()（）〜ー…！？：%²³▶◀□■●○×＋−')
chars.update('東南西北白發中風林火山萬筒索算一二三四五六七八九')
# ユーザー名(VRChat の表示名)のために、ひらがな・カタカナ・全角英数記号はすべて入れる(漢字の名前は入らないことがある)
chars.update(chr(c) for c in range(0x3041, 0x3097))
chars.update(chr(c) for c in range(0x30A1, 0x30FB))
chars.update(chr(c) for c in range(0xFF01, 0xFF5F))
chars.update('ーゝゞヽヾ〇々')
text = ''.join(sorted(c for c in chars if c not in '\r\n\t'))
out = os.path.join(ROOT, 'Runtime', 'Fonts', 'SqmCharacters.txt')
os.makedirs(os.path.dirname(out), exist_ok=True)
open(out, 'w', encoding='utf8').write(text)
print(len(text), '文字 →', out)
print(''.join(c for c in text if ord(c) > 0x7f))

# ユーザー名の表示に使う「動的フォールバック」用に、フォントを間引いて小さくする。
#   python make-fallback-font.py <元のフォント.otf/.ttf> [出力先.otf]
# 例: python make-fallback-font.py Assets/Fonts/SourceHanSerif-Medium.otf Assets/Fonts/SqmFallback-Medium.otf
#
# 残す文字: ASCII・ラテン文字の拡張・ギリシャ文字・キリル文字・記号類(矢印・囲み文字・図形・罫線など)・
#           かな・全角英数・JIS 第1/第2水準の漢字(と Windows の拡張漢字)。元のフォントに無い文字は入らない。
# 源ノ明朝(約 24MB)なら 4MB くらいになる。
# SIL Open Font License のフォントは、元の名前(予約フォント名)のまま改変版を配れないので、名前を「SqmFallback」に変える。
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont

sys.stdout.reconfigure(encoding='utf-8')
if len(sys.argv) < 2:
    print(__doc__ or 'python make-fallback-font.py <元のフォント> [出力先]')
    sys.exit(1)
src = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(src), 'SqmFallback-Medium' + os.path.splitext(src)[1])

codes = set(range(0x20, 0x7F))
ranges = [
    (0x00A0, 0x024F),   # ラテン文字の拡張
    (0x0370, 0x03FF),   # ギリシャ文字
    (0x0400, 0x04FF),   # キリル文字
    (0x2000, 0x2BFF),   # 句読点・上付き・通貨・文字様記号・数字の形・矢印・数学記号・技術記号・囲み英数字・罫線・図形・その他の記号・装飾記号
    (0x3000, 0x30FF),   # CJK の記号・ひらがな・カタカナ
    (0x3190, 0x31FF),   # 漢文用の記号・カタカナの拡張
    (0x3200, 0x33FF),   # 囲み文字・CJK 互換(㈱ ㌔ など)
    (0xFE30, 0xFE4F),   # CJK 互換形
    (0xFF00, 0xFFEF),   # 全角・半角形
]
for a, b in ranges:
    codes.update(range(a, b + 1))
# JIS 第1・第2水準の漢字(と Windows の拡張漢字): cp932 で表せる CJK 統合漢字
for c in range(0x4E00, 0xA000):
    try:
        chr(c).encode('cp932')
        codes.add(c)
    except UnicodeEncodeError:
        pass
for c in range(0xF900, 0xFB00):   # CJK 互換漢字
    try:
        chr(c).encode('cp932')
        codes.add(c)
    except UnicodeEncodeError:
        pass

font = TTFont(src)
have = set(font.getBestCmap().keys())
keep = sorted(codes & have)
opts = subset.Options()
opts.layout_features = []   # 縦書きなどの置き換え表は TextMeshPro では使わないので捨てる
opts.name_IDs = ['*']
opts.notdef_outline = True
opts.desubroutinize = False   # 輪郭の共通部分(サブルーチン)は残す(外すと大きくなる)
s = subset.Subsetter(opts)
s.populate(unicodes=keep)
s.subset(font)

# 名前を変える(SIL OFL の予約フォント名を使わない)
family = 'SqmFallback'
for rec in font['name'].names:
    if rec.nameID in (1, 16):
        rec.string = family
    elif rec.nameID in (4,):
        rec.string = family + ' Medium'
    elif rec.nameID in (6,):
        rec.string = family + '-Medium'
    elif rec.nameID in (3,):
        rec.string = family + '-Medium;subset'
if 'CFF ' in font:
    cff = font['CFF '].cff
    cff.fontNames = [family + '-Medium']
font.save(out)
print(f'{len(keep)} 文字 → {out} ({os.path.getsize(out) / 1e6:.1f} MB、元は {os.path.getsize(src) / 1e6:.1f} MB)')

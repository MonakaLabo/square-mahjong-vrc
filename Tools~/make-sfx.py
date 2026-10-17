# 仮の効果音を作る(合成音)。 python make-sfx.py
# Runtime/Audio/sfx_*.wav に書く。本番の音ができたら、同じ名前で置き換える。
#   discard 打牌 / call ポン・チー・カン / riichi リーチ / win ロン・ツモ / end 局の終わり・流局 / turn 自分の番 / click 確認など
import os
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Runtime', 'Audio')
SR = 44100


def env(n, attack=0.004, decay=6.0):
    t = np.arange(n) / SR
    a = np.clip(t / attack, 0, 1)
    return a * np.exp(-t * decay)


def tone(freq, dur, decay=6.0, harm=(1.0, 0.35, 0.12), attack=0.004):
    n = int(SR * dur)
    t = np.arange(n) / SR
    y = sum(h * np.sin(2 * np.pi * freq * (k + 1) * t) for k, h in enumerate(harm))
    return y * env(n, attack, decay)


def place(buf, y, at):
    i = int(SR * at)
    end = min(len(buf), i + len(y))
    buf[i:end] += y[:end - i]


def make(dur):
    return np.zeros(int(SR * dur))


def save(name, y, gain=0.8):
    y = y / (np.max(np.abs(y)) + 1e-9) * gain
    data = (y * 32767).astype(np.int16)
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, 'sfx_' + name + '.wav'), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print('sfx_' + name + '.wav', round(len(y) / SR, 2), 's')


rng = np.random.default_rng(7)

# 打牌: 牌を卓に置く「カッ」(短いノイズ + 低めの木の音)
b = make(0.12)
n = int(SR * 0.03)
place(b, rng.standard_normal(n) * env(n, 0.0005, 120), 0)
place(b, tone(820, 0.1, 45, (1.0, 0.5, 0.2)), 0)
save('discard', b, 0.7)

# 鳴き: 2音の明るいチャイム
b = make(0.45)
place(b, tone(880, 0.35, 9), 0)
place(b, tone(1318.5, 0.35, 9), 0.08)
save('call', b)

# リーチ: 上がっていく3音 + 余韻
b = make(0.9)
for i, f in enumerate([659.3, 880, 1174.7]):
    place(b, tone(f, 0.7, 5, (1.0, 0.3, 0.1, 0.05)), i * 0.09)
save('riichi', b)

# 和了: 和音の駆け上がり(サイバーっぽく少し矩形波まじり)
b = make(1.6)
for i, f in enumerate([523.3, 659.3, 784, 1046.5, 1318.5]):
    place(b, tone(f, 1.3, 3.2, (1.0, 0.45, 0.25, 0.15, 0.1)), i * 0.07)
place(b, tone(261.6, 1.4, 2.5, (1.0, 0.5, 0.3)), 0.35)
save('win', b)

# 局の終わり・流局: 下がる2音
b = make(1.1)
place(b, tone(784, 0.9, 4), 0)
place(b, tone(523.3, 0.9, 3.5), 0.18)
save('end', b, 0.7)

# 自分の番: やわらかい1音(いまは録音の notice を使っている。このスクリプトを流すと仮の音に戻る)
b = make(0.35)
place(b, tone(1046.5, 0.3, 12, (1.0, 0.2)), 0)
save('turn', b, 0.55)

# 確認など: 短いクリック
b = make(0.08)
place(b, tone(1760, 0.06, 70, (1.0, 0.3)), 0)
save('click', b, 0.6)

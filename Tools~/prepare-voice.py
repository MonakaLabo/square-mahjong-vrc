# 録音した声を、パッケージの声と同じ形にそろえる。
#   python prepare-voice.py <録音のフォルダ> [出力先(既定: Runtime/Audio/Voice)]
# モノラル・16bit・44100Hz にし、前後の無音を切り(前後に 20ms 残す)、いちばん大きい所を -3dB(23197)にそろえる。
import array
import glob
import os
import sys
import wave

PEAK = 23197
THRESH = 0.02      # 無音とみなす大きさ(いちばん大きい所に対する割合)
PAD = 0.02         # 前後に残す秒数


def read(path):
    w = wave.open(path)
    ch, rate, width, n = w.getnchannels(), w.getframerate(), w.getsampwidth(), w.getnframes()
    raw = w.readframes(n)
    w.close()
    vals = []
    for i in range(0, len(raw), width):
        b = raw[i:i + width]
        if width == 1:
            v = (b[0] - 128) / 128
        else:
            v = int.from_bytes(b, 'little', signed=True) / float(1 << (8 * width - 1))
        vals.append(v)
    mono = [sum(vals[i:i + ch]) / ch for i in range(0, len(vals), ch)]
    if rate != 44100:
        sys.exit(f'{path}: {rate}Hz は未対応です(44100Hz にしてください)')
    return mono, rate


def main():
    src = sys.argv[1]
    here = os.path.dirname(os.path.abspath(__file__))
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, '..', 'Runtime', 'Audio', 'Voice')
    for path in sorted(glob.glob(os.path.join(src, '*.wav'))):
        mono, rate = read(path)
        peak = max(abs(v) for v in mono) or 1.0
        idx = [i for i, v in enumerate(mono) if abs(v) >= peak * THRESH]
        pad = int(PAD * rate)
        a, b = max(0, idx[0] - pad), min(len(mono), idx[-1] + pad + 1)
        g = PEAK / 32768.0 / peak
        pcm = array.array('h', [max(-32768, min(32767, int(round(v * g * 32768)))) for v in mono[a:b]])
        dst = os.path.join(out, os.path.basename(path))
        w = wave.open(dst, 'wb')
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate)
        w.writeframes(pcm.tobytes())
        w.close()
        print(f'{os.path.basename(path)}: {len(mono) / rate:.2f}s → {(b - a) / rate:.2f}s')


main()

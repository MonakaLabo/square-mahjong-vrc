# エンジンの答え合わせ

`Runtime/Scripts/Engine/SqmEngine.cs`(UdonSharp)が、ブラウザ版(MonakaLabo/baka-mahjong の js/engine)と
同じシード・同じ操作で、1手ずつまったく同じ状態になることを確かめる。
NPC(`SqmNpc.cs`)も、同じ局面でブラウザ版の NPC と同じ手を選ぶかを毎回確かめる。Unity は要らない。

1. 答えを作る(ブラウザ版で NPC 8人の試合を打ち、操作と状態のハッシュを書き出す)

   ```bash
   cd golden
   node make-golden.mjs 40 C:/Users/Savanna/Documents/CLAUDE/Game/BakaMahjong
   ```

2. C# で同じ操作をして照合する(.NET SDK が要る。Unity / UdonSharp の代わりに Stubs.cs でコンパイルする)

   ```bash
   cd EngineHarness
   dotnet run -c Release
   ```

食い違ったら、C# 側の状態の文字列が表示される。JS 側の同じ時点は `node make-golden.mjs --dump <ファイル> <操作の番号>` で見られる。
状態の文字列の形(`canon`)は、make-golden.mjs と Program.cs で同じにしてある。

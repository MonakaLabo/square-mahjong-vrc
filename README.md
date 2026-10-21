# 平方麻雀 for VRChat

**8人・1316枚・なんでも倍の麻雀「平方麻雀」を、VRChat のワールドに置いて遊べるギミックです。**
最大8人で卓を囲み、空いた席は NPC が打ちます。1人でも、NPC 7人を相手に遊べます。

SavannaMonaka が、Claude(Anthropic の AI)といっしょに作りました。

*A VRChat world gimmick for "Square Mahjong" — an 8-player, 1,316-tile mahjong variant. Up to 8 players; empty seats are filled by NPCs. (UI is Japanese only.)*

- ルールブック・ブラウザ版: <https://monakalabo.github.io/square-mahjong/>(ルールブック v0.8)
- バージョン: 0.3.8

---

## できること

- **8人で対局**：席に着いた人どうしで同期して打てます。空いている席・抜けた人の席は NPC が打ちます(10人の性格つき)
- **途中参加・ホストの交代**：途中から来た人も試合に追いつけます。ホストが抜けても、座っている人の中から次のホストが選ばれて続きます
- **上空のホロ**：局・残り枚数(円のゲージ)・供託と本場・ドラ表示牌・ロン牌・セット時間/現局時間・8席の見取り図。和了・流局・終局の結果画面も出ます
- **手元のパッド**(座った本人にだけ見えます)
  - 対局：手牌を選んで切る・リーチ・ツモ・ロン・ポン・チー・カン・パス
  - 点数：8席の点数と順位。終局すると結果の表(持ち点・ウマ・最終)
  - ルール：開始前にホストが12項目を設定(全員のパッドに反映)
  - 設定：効果音・声の音量、パッドを掴む、席を立つ / ホストは新しい試合・Reset・参加者を立たせる
- **声と効果音**：リーチ・ポン・チー・カン・ロン・ツモなどの宣言の声(宣言した席から聞こえます)
- **卓の牌でも打てる**：卓の上の自分の手牌を押して選び、卓の「確定」で切ることもできます

## 動作環境

- Unity 2022.3(VRChat が指定するバージョン)
- VRChat Worlds SDK **3.10 以降**(UdonSharp 同梱。ネットワークイベントの引数を使うため、古い SDK では動きません)
- TextMeshPro(Unity 標準。初回に「Import TMP Essential Resources」が必要です)
- PC 向けに作っています。Quest(Android)での動作は確かめていません

---

## ワールドへの入れ方

### 1. パッケージを入れる

**VCC / ALCOM から入れる(おすすめ)**

1. VCC(VRChat Creator Companion)または ALCOM を開く
2. 設定の「Packages」→「Add Repository」(ALCOM は「パッケージ」→「リポジトリを追加」)に、次の URL を入れる

   ```
   https://monakalabo.github.io/square-mahjong-vrc/index.json
   ```

   (<https://monakalabo.github.io/square-mahjong-vrc/> を開いて「VCC に追加する」を押しても追加できます)
3. ワールドのプロジェクトの「Manage Project」で **Square-Mahjong** を追加する

新しいバージョンが出たら、同じ画面から更新できます。

**手で入れる場合**

[Releases](https://github.com/MonakaLabo/square-mahjong-vrc/releases) から zip をダウンロードして、ワールドのプロジェクトの `Packages/jp.monakalabo.square-mahjong` に展開します。

Unity を開くと、U# のコンパイルが自動で走ります。Console に赤いエラーが出ていなければ準備完了です。

### 2. TextMeshPro の準備(初回だけ)

`Window > TextMeshPro > Import TMP Essential Resources` を実行します(`Assets/TextMesh Pro/` があれば済んでいます)。

### 3. フォントを用意する

卓とパッドの文字には日本語フォントを、点数には7セグのフォントを使います。どちらも無料で使えるフォントです。

| 使う所 | おすすめのフォント | 入手先 |
|---|---|---|
| 卓・パッドの文字 | 源ノ明朝(Source Han Serif)の **Medium** | <https://github.com/adobe-fonts/source-han-serif/releases> の日本語版(SubsetOTF/JP) |
| ホロの文字(太め) | 源ノ明朝の **Bold** | 同上 |
| 点数・時間(7セグ) | **DSEG7 Classic** の Bold | <https://github.com/keshikan/DSEG/releases> |

1. フォントのファイル(`.otf` / `.ttf`)を `Assets/Fonts/` などに置く
   - 源ノ明朝は可変フォント版(名前に VF が付くもの)ではなく、ふつうの版を使ってください
   - DSEG を置くときは、同梱の `DSEG-LICENSE.txt` も一緒に置いてください(ライセンスの決まりです)
2. メニュー **`Tools > 平方麻雀 > TMP フォントを作る`** を実行する
   - 何も選ばずに実行すると、DSEG のフォントを作ります
   - 源ノ明朝は、Project ウィンドウで `.otf` を選んでから実行します(Medium と Bold で1回ずつ)
   - できた TMP フォント(`〇〇_SDF`)は、元のフォントと同じ所に保存されます
3. メニュー **`Tools > 平方麻雀 > 動的フォールバックを作る`** を実行する(何も選ばなくて大丈夫です)
   - 漢字や記号を含むユーザー名も表示できるようにするための、予備のフォントです
   - パッケージに同梱した **SqmFallback-Medium**(源ノ明朝から、JIS 第1・第2水準の漢字・かな・記号など約 8,800 字を残して小さくしたもの)を使います。**ワールドの容量は約 3MB 増えます**
   - TMP フォントを作り直したときは、このメニューももう一度実行してください

卓を置くとき、フォントは名前で自動的に選ばれます。

- 名前に `SourceHan` / `Mincho` / `JP` などを含むもの → 卓・パッドの文字
- そのうち `Bold` / `Heavy` を含むもの → ホロの文字
- 名前に `DSEG` を含むもの → 点数・時間

### 4. 卓を置く

1. 卓を置きたいシーンを開く(床と VRCWorld があるシーン)
2. メニュー **`Tools > 平方麻雀 > 卓をシーンに置く`** を実行する
3. シーンに `SquareMahjongTable` ができます。好きな位置へ動かしてください(回転・移動しても大丈夫です)

卓の直径はおよそ 2.5m です。上空のホロは卓の上 約1m に浮くので、天井の高さに気をつけてください。周りには、8人が立てるくらいの広さ(半径 2m 程度)をあけておくと遊びやすいです。

> フォントを入れ替えたときや、パッケージを更新したときは、今の卓を削除して、もう一度「卓をシーンに置く」を実行してください。

### 5. 試す

- Unity の Play(ClientSim)で、1人で NPC と打てます
- 複数人の確認は、VRChat SDK の **Build & Test** で `Number of Clients` を 2 以上にすると、1台の PC で試せます

---

## 遊び方

1. **席に着く**：空いている席のパッドの **JOIN** を押します。最初に座った人が **ホスト** になります
2. **ルールを決める**(ホスト)：パッドの「ルール」タブで、開始前に設定できます
3. **始める**(ホスト)：パッドの **NEW GAME** を押します。空いている席は NPC が打ちます
   - だれも座っていないときに NEW GAME を押すと、NPC だけの対局を観戦できます
4. **打つ**：自分の番になると音が鳴り、パッドに「あなたの番です。」と出ます
   - 切る牌を選んで(このゲームでは基本は2枚)、**確定** を押します
   - リーチ・ツモ・ロン・ポン・チー・カンは、できるときだけパッドにボタンが出ます。候補が複数あるときは、押すと候補を選べます
5. **確認**：和了や流局のときはホロが結果画面に切り替わります。座っている全員がパッドの **確認** を押すと続きます
6. **終局**：パッドが点数タブに切り替わり、結果の表が出ます。ホストが NEW GAME を押すと次の試合です

### 困ったとき

- **表示がおかしい・ずれた気がする**：卓の中央の **Resync** を押すと、自分の手元だけ最初から追いつき直します
- **試合をやめたい**：卓の中央の **Reset**(人が座っているときはホストだけ)。パッドも元の位置に戻ります
- **席に着いたまま反応がない人がいる**：ホストはパッドの「設定」タブから、その人を席から立たせられます(その席は NPC が引き継ぎます)
- **パッドを動かしたい**：「設定」タブの「パッドを掴めるようにする」を ON にすると、自分のパッドを掴めます。元の場所の近くで離すとくっつきます

---

## しくみ(作る人向け)

- 試合を進めるのはホスト(同期の持ち主)だけです。ホストは操作を小さな番号にした記録を配り、ほかの人は同じ記録を手元のエンジンで再生します。盤面そのものは送らないので、通信は軽く済みます
- ルールエンジンと NPC は、ブラウザ版(JavaScript)を UdonSharp に移植したものです。同じシード・同じ操作で、ブラウザ版と1手ずつ同じ結果になることを確かめています(`Tests~/`)
- 主なスクリプト
  - `Runtime/Scripts/Engine/` … ルールエンジン(`SqmEngine`)と NPC(`SqmNpc`)
  - `Runtime/Scripts/Table/` … 卓の進行・同期・表示(`SqmTable`)、途中参加用の記録(`SqmArchive`)
  - `Runtime/Scripts/Pad/` … 手元のパッド(`SqmPad`・`SqmPadBody`・`SqmPadKey`)
  - `Editor/` … 卓を組み立てるメニュー(`SqmBuilder`)、TMP フォントを作るメニュー(`SqmFontMaker`)
- 設計のメモ: [Documentation~/DESIGN.md](Documentation~/DESIGN.md)、変更履歴: [CHANGELOG.md](CHANGELOG.md)

---

## クレジット

- 企画・ルール・デザイン・声: **SavannaMonaka**(MonakaLabo)
- プログラム: SavannaMonaka with Claude(Anthropic)
- フォント
  - 源ノ明朝 / Source Han Serif © Adobe — SIL Open Font License 1.1(各自で入手。同梱の SqmFallback-Medium は、これを間引いて名前を変えたもの。`Runtime/Fonts/SqmFallback-OFL.txt`)
  - DSEG © keshikan — SIL Open Font License 1.1(各自で入手)

## 利用規約

無料で使えます。くわしくは [LICENSE.md](LICENSE.md) を見てください。

- ✅ 自分のワールドへの設置(パブリックワールドも OK)、自分のワールド用の改変
- ❌ 再配布・販売(改変したものも含む)
- ❌ 声・効果音・画像などの素材を取り出して、ほかの目的に使うこと

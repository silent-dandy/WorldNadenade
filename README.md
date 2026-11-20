# World Nadenade

VRChat ワールド向けのなでなで音ギミック「World Nadenade」（Silent Comfort）を、VPM（VCC / ALCOM）で配布するためのリポジトリです。

ワールドに置くだけで、頭をなでたときに音が鳴ります。アバターの改変は要らず、Android / Quest の方にも聞こえます。

## 導入

1. VCC / ALCOM の **Settings > Packages > Add Repository** に、次の URL を追加します。
   ```
   https://silent-dandy.github.io/vpm/index.json
   ```
   [https://silent-dandy.github.io/vpm/](https://silent-dandy.github.io/vpm/) の **Add to VCC** ボタンからも追加できます。
2. 同じ画面で、tmp-fallback-fonts-jp のリポジトリ `https://vpm.narazaka.net/` も追加します。
3. プロジェクトの Manage Project から **World Nadenade** を追加します。

使い方は、パッケージに入っている `README.md` のクイックスタートと、マニュアルをご覧ください。

- マニュアル：https://silent-dandy.github.io/world-nadenade/
- 利用規約：https://silent-dandy.github.io/world-nadenade/terms.html
- お問い合わせ：X @silent_dandy_vr（https://x.com/silent_dandy_vr）

## 利用規約について

このリポジトリはソースを公開していますが、オープンソースではありません。利用は[利用規約](https://silent-dandy.github.io/world-nadenade/terms.html)に従ってください。改変は自由にできますが、改変したものを含めて再配布・販売はできません（第7条）。

## このリポジトリの仕組み（管理用）

開発は別の非公開リポジトリで行い、リリースのときに `Packages/com.silentcomfort.worldnadenade/` をそこからコピーします。このリポジトリで直接パッケージを編集しないでください。

1. 開発用のリポジトリで `package.json` の `version` を上げ、変更を `CHANGELOG.md` に書く
2. パッケージのフォルダをこのリポジトリへコピーしてコミットし、push する
3. **Actions > Build Release > Run workflow** を実行する。`package.json` の `version` と同じ名前のタグと Release ができ、zip が置かれる
4. 一覧のリポジトリ（[silent-dandy/vpm](https://github.com/silent-dandy/vpm)）で **Actions > Build Repo Listing > Run workflow** を実行する

- リポジトリ変数 `PACKAGE_NAME` に `com.silentcomfort.worldnadenade` を設定してある前提で動く
- 同じバージョン番号では二度とリリースしない。失敗してタグだけ残ったときは、Release とタグを消してから実行し直す
- `.github/workflows/release.yml` は VRChat 公式の [template-package](https://github.com/vrchat-community/template-package) のもの

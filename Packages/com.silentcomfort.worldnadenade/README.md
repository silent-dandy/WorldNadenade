# World Nadenade

ワールドに置くだけで使える、VRChat ワールド向けのなでなで音ギミックです（Silent Comfort）。

## 概要

- **アバターの改変は要りません。** 標準の手・指の判定だけで、頭をなでたときに音が鳴ります
- **Android / Quest の方にも聞こえます。** 音をワールド側で鳴らすので、アバターの音が鳴らない環境でも届きます
- **聞くかどうかは各自が選びます。** 初期設定はオフで、パネルやタブレットでオンにした人の端末でだけ鳴ります。静かに過ごしたい人の邪魔をしません
- **なで方に音がついてきます。** 手の速さで音量と高さが変わり、手のひらと手の甲で音色が変わります
- **音色はなでる人が選びます。** 選んだ音色は周りの人にも届き、人ごとに違う音でなでられます

## 動作環境

- VRChat SDK - Worlds 3.10.6 以降（UdonSharp を含む）
- TextMesh Pro VRC Fallback Font JP（パッケージ名 tmp-fallback-fonts-jp。VPM で World Nadenade と一緒に入ります）
- TextMesh Pro（TMP Essentials）

パネルの文字は TextMesh Pro VRC Fallback Font JP の Empty SDF で表示し、日本語は VRChat に内蔵のフォントで表示されます。ワールドにフォントを含めないので、容量は増えません。

## 初回の準備

1. **Window > TextMeshPro > Import TMP Essential Resources** で TMP Essentials を入れます（入れてあれば不要です）。
   先にパネルを置いて「TMP Importer」の画面が出たときは、**Import TMP Essentials** だけを押します（Import TMP Examples & Extras は要りません）。
2. **Tools > TextMesh Pro VRC Fallback Font JPを設定** を実行します。TMP の初期フォントが Empty SDF になり、パネルの文字が表示されます。

## クイックスタート

Prefab は Project ウィンドウの **Packages > World Nadenade** の中にあります。次の3つを置けば使えます。

### 1. 本体を置く

`WorldNadenade.prefab`

- Hierarchy の好きな場所に **1ワールドに1個だけ** 置きます（2個あると音が二重に鳴ります）。2個目を置くと、削除するか尋ねるダイアログが出ます
- パネルやタブレットより **先に置く** と、あとで置いたパネルが自動で接続されます（後から置いた場合も、置いた時点で未接続のパネルがまとめて接続されます）
- 位置と回転は自由です。**Scale は 1 のまま** にしてください（頭の判定の大きさが変わります）
- 32人分の頭の判定（`Heads` の下の Head）が入っています。ワールドの定員が32人より多いときは、Head を複製して定員以上にします

### 2. 壁の操作パネルを置く

`Prefabs/ControlPanel/NadeControlPanel_Smoke_TabletNote.prefab`

- 壁など、操作パネルを出したい場所に置き、表示面を部屋の側に向けます
- 置くと、NadeControlPanel の `Manager` 欄に 1 の WorldNadenade が **自動で入ります**。空のままだと、パネルに「未接続」と表示されて操作できません（入らなかったときは「パネルの接続」を見てください）
- 大きさは、置いたパネルの Scale で調整します
- 明るい壁には `NadeControlPanel_Paper_TabletNote.prefab` が合います。`_TabletNote` の付いた版は、使い方の画面にタブレットの戻し方が書いてあります。タブレットを置かないワールドでは、付いていない版を使ってください

### 3. 置き台付きのタブレットを置く

`Prefabs/Tablet/NadeTablet_Smoke_Gray_CradleWoodLight.prefab`

- 置き台ごと壁に付けます。**置き台の原点（背板の裏面）を壁の表面に合わせ**、正面（-Z）を部屋の側に向けます
- 高さは、立っても座っても手が届く位置（床から 1.0〜1.2 m 程度）が目安です
- 置き台の子の NadeTablet にある NadeTabletPanel の `Manager` 欄にも、WorldNadenade が **自動で入ります**
- タブレットは、VR では人差し指の先で画面に触れて操作します。置き台のボタンを押すと、持ち出したタブレットが置き台に戻ります
- 色違いがあります。ファイル名は「画面のデザイン（Smoke / Paper）＿本体の色（Gray / Sand / Blue / Lavender）＿置き台の色」の順です

### パネルの接続

操作パネル・タブレット・デバッグ表示パネルは、`Manager` 欄に WorldNadenade が入っていると使えます。

- **自動で接続**：シーンに WorldNadenade が1個あれば、パネルを置いた（ドロップ・複製・貼り付け）ときに `Manager` 欄が空なら自動で入ります。入っている欄は書き換えません
- **外れたときも自動で接続し直し**：Hierarchy が変わったとき・Prefab が読み込み直されたとき（パッケージの更新など）・シーンを開いたときに、`Manager` 欄が空になったパネルを接続し直し、Console に知らせます。シーンを保存すると残ります
- **ボタンで接続**：`Manager` 欄が空のパネルを選ぶと、インスペクターに「シーンの WorldNadenade に接続」ボタンが出ます。WorldNadenade のインスペクターにも、接続の状況と「未接続のパネルをこの WorldNadenade に接続」ボタンがあります
- **まとめて接続**：**Tools > Silent Comfort > World Nadenade > Connect Panels** で、シーンの未接続のパネルをすべて接続します
- **ビルドの前の確認**：Build & Test やアップロードの前に、未接続のパネルがあれば「接続して続ける / 中止する / 接続せずに続ける」を選べます
- WorldNadenade が2個以上あるときは、接続先を決められないので自動では接続せず、エラーを表示します。非表示の WorldNadenade は接続先にしません
- 自動接続がオンの間は、`Manager` 欄を空にしても、次に Hierarchy が変わったときに入り直します。わざと空にしておきたいときは、自動接続をオフにします
- 自動接続（置いたとき・接続し直し）を使わないときは、**Tools > Silent Comfort > World Nadenade > Auto Connect Panels** のチェックを外します（この PC の設定として保存されます）。手で `Manager` 欄に WorldNadenade をドラッグしても構いません

### 4. 動作を確かめる

1. Build & Test でワールドに入ります
2. パネルかタブレットで、なで音をオンにします
3. 一人で確かめるときは「自分なで」もオンにして、自分の頭をなでます

## 注意

- **パッケージの中の Prefab を直接編集しないでください。** パッケージを更新すると元に戻ります。シーンに置いたものを変更してください
- **WorldNadenade を非アクティブにしないでください。** なで音のオン・オフは、訪れた人がそれぞれパネルやタブレットで行います
- 聞くには各自がオンにする必要があります。インポスターやフォールバックで表示されている相手には反応しないことがあります。指のボーンが無いアバターでは、常に手のひらの音になります

## 資料

- マニュアル：https://silent-dandy.github.io/world-nadenade/
- 利用規約：`Documents/TERMS.md`（最新版は https://silent-dandy.github.io/world-nadenade/terms.html ）
- お問い合わせ：X @silent_dandy_vr（https://x.com/silent_dandy_vr）

クレジットの表記は任意です。表記する場合は `World Nadenade / Silent Comfort` をおすすめします。

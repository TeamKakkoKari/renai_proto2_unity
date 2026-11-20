# ことばを紡ぐ恋愛シミュレーション（プロトタイプ2）Unity版

Godot版「プロトタイプ2 v9」をUnityで開いて作業できるように移植したプロジェクトです。
ゲーム内容・データ・評価ロジックはGodot版と同一です。

## 開き方

1. Unity Hub を開く
2. 「プロジェクトを開く（Add / Open）」で、このフォルダ（renai_proto2_unity）を選択
3. 推奨バージョン: **Unity 2022.3 LTS**（それより新しいUnityでも開けますが、初回に変換が入ります）
4. 初回はインポートに少し時間がかかります
5. `Assets/Scenes/Main.unity` を開いて、上部の ▶（再生）ボタンで実行できます

※ 画面比率は 1280×720（16:9）想定です。Gameビューの比率を 16:9 にするとGodot版と同じ見た目になります。

### もし Main.unity が開けない場合

1. 空のシーンを新規作成
2. 空の GameObject を1つ作る
3. その GameObject に `Assets/Scripts/MainGame.cs` をアタッチして再生

（MainGame が Canvas や UI をすべて自動で組み立てるため、これだけでも動作します）

## 遊び方（Godot版と同じ）

- タイトル → マップで地域を選択 → 物語が始まります
- 質問の場面では、制限時間（60秒）以内に散らばった単語を押して文章を組み立て、「発言する」を押します
- 文法（語順・助詞・語尾）・話題の合致・中身のある発言かどうかで好感度が変わります
- 画面クリック / Enter / Space で文章送り、上部バー「ログ」でバックログ表示
- 好感度は自動保存されます（Unityの persistentDataPath 内 affection.json）

## データの編集（Godot版と同一形式）

`Assets/Resources/data/` のJSONを書き換えるだけで内容を変更できます。コードの変更は不要です。

- `words.json` … 単語（カテゴリ・タグ）
- `characters.json` … キャラ・質問・反応セリフ・表情差分
- `stories.json` … 物語の台本とエンディング
- `stages.json` … 地域（背景色・説明）
- `evaluation.json` … 評価の数値と制限時間

立ち絵・表情・ワールドマップ画像は `Assets/Resources/assets/`、フォントは `Assets/Resources/fonts/`（M PLUS Rounded 1c / SIL Open Font License）にあります。

## スクリプト構成

- `Assets/Scripts/MainGame.cs` … 画面・物語エンジン・UI（Godot版 main.gd の移植）
- `Assets/Scripts/Evaluator.cs` … 発言の評価ロジック（Godot版 evaluator.gd の移植）
- `Assets/Scripts/JsonParser.cs` … 外部パッケージ不要でJSONを読むための小さなパーサ

## Godot版との違い

- マップ画面は、3Dモデルをリアルタイム表示する代わりに、真上から描いたワールドマップ画像＋地域ボタンで再現しています（見た目と操作は同等です）
- パネルの角丸など、装飾の一部は簡易再現です
- 検証について: 評価ロジックはGodot版と30ケースで出力（スコア・理由文まで）完全一致を確認済み、C#コードのコンパイル確認も済んでいます。ただしUnityエディタ自体での起動確認は未実施のため、初回再生時に問題があれば知らせてください

# 🕶 Meta VR Glasses Unity Template
This template provides a VR-ready project with the Meta XR SDKs needed to develop for **Meta VR Glasses**.

## Runeveil — 星暮れの遺跡

### Unity 版

Unity で [`Assets/Scenes/Runeveil.unity`](Assets/Scenes/Runeveil.unity) を開き、Play を押します。このシーンが通常のビルドで最初に起動します。

- **Meta VR Glasses**では手と視線のトラッキングを有効にします。親指と人差し指をつまんでガイドの形を描き、撃ちたい箇所を見ながら指を離します。`Start duel` と `Fight again` は見ながら短くピンチします。
- **Editor でマウスを使う**ときは、`Runeveil Duel` の Inspector で `Desktop Preview In Editor` を有効にします。左ドラッグで描いて離した位置へ撃ち、右クリックで照準を固定・解除できます。`Start duel` のクリックか Enter キーで本戦を開始します。
- 拳が上がったら、胸の結晶に雷（Z）を当てると攻撃を中断できます。横線で張るバリアは次の攻撃を一度防ぎます。

### WebXR 版

親指と人差し指で模様を描き、指を離した瞬間の視線でゴーレムを狙うWebXRゲームです。公開版はログイン不要です。PCではドラッグで模様を描けます。

[公開版で遊ぶ](https://runeveil-golem-duel.yasei-no-otoko.chatgpt.site/) · [起動方法と魔法の描き方](web/README.md)

### 公開版とUSBローカル版の比較

2026年11月18日、同じMeta VR GlassesのHorizon Browser 156.0.0.38で、公開URLとUSB経由の `https://localhost:51916/` をそれぞれ直接開いて確認しました。

両方ともゲームはトップレベルのHTTPSページで動き、ブラウザーの実効ポリシーは `xr-spatial-tracking` を許可していました。公開URLは所有者限定のときにChatGPTへのログインを求め、一般公開への変更後は未ログインのGlassesとChromeの両方でゲームを表示しました。

| Glassesで確認した項目 | USBローカル | 公開Sites |
|---|---|---|
| 「VRでプレイ」で手と視線を必須にした起動 | `NotSupportedError` | `NotSupportedError` |
| 手を必須、視線を任意にした診断用VRセッション | 起動 | 起動 |
| その90フレームで指先が取得できた回数 | 89 | 79 |
| その90フレームで視線が取得できた回数 | 0 | 0 |

公開先を変えても、このGlassesとブラウザーでは視線必須の本戦を開始できませんでした。表は実験機能を既定値に戻した状態での結果です。指を離した瞬間の視線で雷が胸に当たり、拳を止める挙動はIWERのGaze + Handsで確認しました。

公開版の静的ファイル14件を個別に取得し、すべてローカルの本番ビルドとSHA-256が一致しました。公開URLへの直接ナビゲーションでは配信側がHTMLにスクリプトを追加しますが、ゲームはトップレベルにあります。確認時の公開ページの応答には `Permissions-Policy`、`Content-Security-Policy`、いずれのReport-Only版、`X-Frame-Options` もありませんでした。

別オリジンのテストページに公開版を埋めた場合、Chrome 157ではiframeに `allow="xr-spatial-tracking"` がないと `isSessionSupported('immersive-vr')` が `SecurityError` になりました。権限を委譲すると例外は消え、XR端末未接続のMacでは `false` が返りました。上の試遊リンクは公開版を直接開きます。

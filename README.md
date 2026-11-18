# 🕶 Meta VR Glasses Unity Template
This template provides a VR-ready project with the Meta XR SDKs needed to develop for **Meta VR Glasses**.

## Runeveil — 星暮れの遺跡

親指と人差し指で模様を描き、指を離した瞬間の視線でゴーレムを狙うWebXRゲームです。公開版はログイン不要です。PCではドラッグで模様を描けます。

[公開版で遊ぶ](https://runeveil-golem-duel.yasei-no-otoko.chatgpt.site/) · [起動方法と魔法の描き方](web/README.md)

### Unity 6.7でGlasses用APKを作る

Unity Hubで **6000.7.0b9** と **Android Build Support**（SDK、NDK、OpenJDKを含む）を導入し、Git LFSのファイルを取得してからこのリポジトリを開きます。起動シーンは `Assets/Scenes/Runeveil.unity` です。EditorのビルドターゲットをAndroidにして、メニューの **Runeveil → Build Glasses APK** を選びます。Editorを閉じた状態ならUnity CLIからも同じAPKを作れます。

```sh
git lfs pull
unity build . --target Android --execute-method Runeveil.Editor.RuneveilBuild.BuildApk
metavr app install Builds/Runeveil/Runeveil.apk
metavr app launch com.runeveil.golemduel
```

Glassesで眼球追跡の使用を許可し、頭を正面に向けたまま目を左右に動かします。ピンチして指で描いた模様を、離した瞬間の視線へ撃ちます。横線のバリアは視線を失っても張れます。

Unity版はMeta XR SDK 209の `OVRPlugin.GetEyeGazeInteractionState` で両眼を合わせた照準の有効性を読み、同じフレームの手指姿勢と組み合わせます。開発用APKの `[RuneveilInput]` ログは視線が有効だったフレーム数と、頭に対して視線が動いたことを確認できたフレーム数を集計します。視線座標は記録しません。[MetaのUnity向け視線インタラクション資料](https://developers.meta.com/vr/documentation/unity/move-eye-tracking-interactions/)

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

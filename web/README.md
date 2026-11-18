# Runeveil ― 星暮れの遺跡

指で魔法を描き、目線で巨大な石のゴーレムを狙うWebXRゲームです。Meta VR Glassesの没入型ブラウザーと、MacのChromeで動くMeta IWERを対象にしています。

## 起動

Node.js 24とnpmで、このディレクトリから実行します。

```sh
npm ci
npm run dev
```

コマンドが表示したHTTPSアドレスを開きます。Macではマウス操作ですぐに練習できます。「VRでプレイ」を選ぶと没入モードを開始します。IWERの機種は `metaVRGlasses` です。入力メニューで **Gaze + Hands** を選びます。実機ではWebXRを利用できる安全なオリジンから開き、手と視線の使用を許可してください。[MetaのWebXR試遊ガイド](https://developers.meta.com/vr/documentation/iwsdk/guides/02-testing-experience/)

## 遊び方

- 親指と人差し指をつまみ、片手で一筆書きします。離した瞬間に見ていた点へ攻撃が飛びます。描いた線が薄く消えたら、もう一度描けます。
- 火球は円、雷はZ、氷槍はV、岩弾は三角、風刃はS、バリアは横線です。画面の「魔導書」で描き順を確認できます。
- 拳が上がったら、胸の結晶を見て雷を撃つと攻撃が止まります。横線でバリアを張ると、次の拳を一度だけ防ぎます。練習では生命力を消費しません。
- 「決闘を始める」を選ぶと本戦です。VRではボタンを見て、短くつまんで離します。勝敗が決まると同じ場所で再戦できます。
- 通常のブラウザーではドラッグして描き、離した位置に撃ちます。胸などを先に右クリックすると照準が固定され、別の場所に模様を描けます。再度右クリックで解除します。

## ビルドと検証

```sh
npm run typecheck
npm test
npm run build
```

`dist/` が静的配信する本番ビルドです。IWERは開発時にだけ挿入されます。実機で確認する際は本番ビルドをHTTPSで配信します。チェックでは頭を正面に固定し、腕を見ながらZを描き、指を離す直前に胸に視線を移します。離した後に腕へ戻しても、照準は胸に残ります。

USB接続のGlassesで本番を開くときは、ビルド後に次を実行します。端末ブラウザーで `https://localhost:51916/` を開くと、このMacが配信するファイルを読み込みます。

```sh
metavr adb reverse tcp:51916 tcp:51916
npm run preview
```

## SDKの参照メモ

2026年11月18日確認。IWSDK 1.1.0とIWER 2.8.0を使用します。Metaの導入ガイドと、配布版の視線ソースが欠けたときの扱いには差があります。RuneveilはWebXRセッションに `hand-tracking` と `gaze-tracking` を必須として要求し、セッションの視線ソースを毎回検査します。頭の方向だけを返す環境を見分けるため、セッション開始後は一度、目だけで左右を見ます。[MetaのGlasses向けガイド](https://developers.meta.com/vr/documentation/iwsdk/guides/get-started-glasses/) · [IWSDKのGaze and Pinch](https://iwsdk.dev/concepts/xr-input/gaze.html)

指先は描画中の手の `index-finger-tip` から取得します。正常なピンチ解放の `select` イベントで、そのイベントのフレームから独立した視線ソースの姿勢と指先を同時取得します。追跡中断時の `selectend` は描画を破棄します。実機のHorizon BrowserとIWERで使う入力経路は共通です。[WebXR Device API](https://www.w3.org/TR/webxr/#xrinputsources) · [WebXR Hand Input](https://www.w3.org/TR/webxr-hand-input-1/)

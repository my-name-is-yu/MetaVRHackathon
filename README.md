# 🕶 Meta VR Glasses Unity Template
This template provides a VR-ready project with the Meta XR SDKs needed to develop for **Meta VR Glasses**.

## Runeveil — 星暮れの遺跡

### Unity 版

Unity で [`Assets/Scenes/Runeveil.unity`](Assets/Scenes/Runeveil.unity) を開き、Play を押します。このシーンが通常のビルドで最初に起動します。

- **Meta VR Glasses**では手と視線のトラッキングを有効にします。親指と人差し指をつまんでガイドの形を描き、撃ちたい箇所を見ながら指を離します。`Start duel` と `Fight again` は見ながら短くピンチします。
- **Editor でマウスを使う**ときは、`Runeveil Duel` の Inspector で `Desktop Preview In Editor` を有効にします。左ドラッグで描いて離した位置へ撃ち、右クリックで照準を固定・解除できます。`Start duel` のクリックか Enter キーで本戦を開始します。
- **Editor で視線なしに試す**ときは、`Runeveil Duel` の Inspector で `Assume Gaze On Target` を有効にします。XR（Meta XR Simulator や Link）でプレイ中、視線は常に光っている弱点を見ている扱いになり、`Start duel` と `Fight again` はどこを見ていても短くピンチすれば押せます。ビルドには含まれません。
- 弱点は胸・額・左右の拳にあるピンクの結晶のうち、光っている1か所です。弱点に当てると結晶が割れ、0.8秒後に別の部位が光ります（直前と同じ場所にはなりません）。弱点に当たったかは、指が離れ始めた瞬間に光っていた場所で決まり、ダメージは弾が着弾した瞬間に入ります。
- 弱点を見ていないとき（視線が取れないときも含む）は、攻撃魔法がMuseの胴体に当たり、ダメージが半分になります。
- 闇堕ちしたMuseが拳を振り上げたら、光っている結晶を見ます。0.35秒見続けると照準の輪が閉じて4つの菱形が現れ、開いた弱点への攻撃に8ダメージが加わります。視線を外すと注視は解除されます。
- 雷（Z）は輪が閉じる前でも光っている弱点に当たれば拳を止められます。Museは3秒間膝をつき、その間は弱点へのダメージが2倍になります。
- 横線で張るバリアは1秒で消え、その間に来た攻撃を一度防ぎます。振り下ろしの直前に張る必要があります。防ぐと拳が1.5秒その場に残り、次の攻撃が遅れます。
- 魔法ごとにクールタイムがあります（火球・氷槍・風刃1.5秒、雷3秒、岩弾4秒、バリア3.5秒）。残り時間は画面下中央に出ます。
- Museの体力が半分を切ると怒り状態になり、棘の帯が赤く染まって攻撃のテンポが上がります。

### Glasses用のAPKをビルドする

Unity Hubで **Unity 6000.7.0b9** と **Android Build Support**（SDK、NDK、OpenJDKを含む）を導入します。Git LFSで素材を取得し、Editorを閉じた状態で次を実行します。

```sh
git lfs pull
unity build . --target Android --execute-method Runeveil.Editor.RuneveilBuild.BuildApk
metavr app install Builds/Runeveil/Runeveil.apk
metavr app launch com.runeveil.golemduel
```

Androidに切り替えたEditorの **Runeveil → Build Glasses APK** からも同じ開発用APKを作れます。

### 視線を確認する

Glassesの権限画面で眼球追跡を許可します。装着して頭を正面に向けたまま、目だけを左右に動かしてください。ピンチでZを描き、指を離す直前に胸の結晶を見ます。拳を振り上げたときなら、命中すると攻撃が中断されます。シミュレーターでは機種に **Meta VR Glasses**、入力に **Look and Pinch** を選びます。

開発用APKは、視線サンプルの有効性と、頭の向きに対して視線が動いたことを確認したフレーム数を5秒ごとに集計します。詠唱の結果は魔法・命中部位・結果で記録します。ログに眼球や手指の座標は含めません。[MetaのUnity向け視線入力ガイド](https://developers.meta.com/vr/documentation/unity/move-eye-tracking-interactions/)

# 🕶 Meta VR Glasses Unity Template
This template provides a VR-ready project with the Meta XR SDKs needed to develop for **Meta VR Glasses**.

## Runeveil — 星暮れの遺跡

### Unity 版

Unity で [`Assets/Scenes/Runeveil.unity`](Assets/Scenes/Runeveil.unity) を開き、Play を押します。このシーンが通常のビルドで最初に起動します。

- **Meta VR Glasses**では手と視線のトラッキングを有効にします。親指と人差し指をつまんでガイドの形を描き、撃ちたい箇所を見ながら指を離します。`Start duel` と `Fight again` は見ながら短くピンチします。
- **Editor でマウスを使う**ときは、`Runeveil Duel` の Inspector で `Desktop Preview In Editor` を有効にします。左ドラッグで描いて離した位置へ撃ち、右クリックで照準を固定・解除できます。`Start duel` のクリックか Enter キーで本戦を開始します。
- Museが拳を振り上げ始めたら、胸の宝石、Museの右手（向かって左）、頭の左右の角をそれぞれ狙えます。反対側の拳（向かって右）は振り上がる途中で弱点に加わります。同じ箇所を0.35秒見続けると照準の輪が閉じ、その箇所を見て指を離すと攻撃に8ダメージが加わります。視線を外すか別の箇所に移すと、注視は最初からになります。
- 雷（Z）は輪が閉じる前でも胸に当たれば拳を止められます。横線でバリアを張ると次の一撃を防げます。

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

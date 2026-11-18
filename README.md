# Save Muse!
This Unity game is based on Meta's VR Glasses template and uses Meta XR SDKs for **Meta VR Glasses**.

## Unity instructions

### Unity 版

Unity で [`Assets/Scenes/Runeveil.unity`](Assets/Scenes/Runeveil.unity) を開き、Play を押します。このシーンが通常のビルドで最初に起動します。

- **Meta VR Glasses**では親指と人差し指をつまんでガイドの形を描き、指を離して撃ちます。視線で弱点を狙えます。視線が取れないときは攻撃が胴体に当たり、メニューでは頭をボタンに向けて短くピンチすると `Start duel` と `Fight again` を選べます。
- **Editor でマウスを使う**ときは、`Runeveil Duel` の Inspector で `Desktop Preview In Editor` を有効にします。左ドラッグで描いて離した位置へ撃ち、右クリックで照準を固定・解除できます。`Start duel` のクリックか Enter キーで本戦を開始します。
- **Editor で視線なしに試す**ときは、`Runeveil Duel` の Inspector で `Assume Gaze On Target` を有効にします。XR（Meta XR Simulator や Link）でプレイ中、視線は常に光っている弱点を見ている扱いになり、`Start duel` と `Fight again` はどこを見ていても短くピンチすれば押せます。ビルドには含まれません。
- 本戦はハート5個で始まります。Museの体力は120です。
- 弱点は胸の宝石、Museの右手、左右の角、振り上げた拳の5か所です。普段は光っている部位を狙えます。狙いは指を離し始めた瞬間に決まり、弾が着くとダメージが入ります。命中した弱点が割れると、0.8秒後に別の部位が光ります。
- 弱点に狙いが合わないときは、視線を取得できない場合も含めて胴体への命中になります。胴体に入るダメージは、魔法の基本ダメージと溜めによる追加分を足した値の55%です。
- 火球・雷・氷槍・風刃は、ピンチしたまま同じ形を繰り返すと最大Lv3まで溜められます。溜めの追加ダメージはLv1で0、Lv2で6、Lv3で12です。弱点への命中では、注視やダウンによる補正を計算したあとに加算します。
- 闇堕ちしたMuseが拳を振り上げたら、5か所のうち撃ちたい部位を0.35秒見続けます。照準の輪が閉じて4つの菱形が現れたら、その部位が露呈し、攻撃に8ダメージが加わります。別の部位に視線を移すと、そこで新たに注視が始まります。
- 雷（Z）は、同じ予備動作の間に弱点を狙って指を離し始めると、着弾したときに拳を止めます。予備動作が終わる直前でも、指を離したことを確定する時間（XRでは0.12秒）と雷が飛ぶ時間（約0.10秒）だけ判定が待ちます。Museが膝をつく3秒間は、溜めの追加分を除いた弱点ダメージが2倍になります。
- 横線で張るバリアは1.5秒間、攻撃を一度防ぎます。拳を振り上げたら横線を描き始め、振り下ろしまで残り約1.3秒で弧が強く光ったら指を離します。防ぐと拳が2.2秒間止まって結晶が光り、最初の着弾が弱点ダメージになります。
- 魔法ごとにクールタイムがあります（火球・氷槍・風刃1.5秒、雷3秒、岩弾4秒、バリア3.5秒）。残り時間は画面下中央に出ます。
- Museの体力が半分以下になると、次の予備動作から赤い強打に切り替わります。通常の一撃でハートを1個、強打で2個失います。予備動作はどちらも2.4秒で、次の予備動作までの間隔は通常2秒、怒り時は1.5秒です。
- Museを倒すとファンファーレが鳴り、目の前のMuseが元の白い姿に戻ります。勝利ランクは残りハート5個でS、3〜4個でA、1〜2個でBです。

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

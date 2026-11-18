# 🕶 Meta VR Glasses Unity Template
This template provides a VR-ready project with the Meta XR SDKs needed to develop for **Meta VR Glasses**.

## Runeveil — 星暮れの遺跡

### Unity 版

Unity で [`Assets/Scenes/Runeveil.unity`](Assets/Scenes/Runeveil.unity) を開き、Play を押します。このシーンが通常のビルドで最初に起動します。

- **Meta VR Glasses**では手と視線のトラッキングを有効にします。親指と人差し指をつまんでガイドの形を描き、撃ちたい箇所を見ながら指を離します。`Start duel` と `Fight again` は見ながら短くピンチします。
- **Editor でマウスを使う**ときは、`Runeveil Duel` の Inspector で `Desktop Preview In Editor` を有効にします。左ドラッグで描いて離した位置へ撃ち、右クリックで照準を固定・解除できます。`Start duel` のクリックか Enter キーで本戦を開始します。
- 拳が上がったら、胸の結晶に雷（Z）を当てると攻撃を中断できます。横線で張るバリアは次の攻撃を一度防ぎます。

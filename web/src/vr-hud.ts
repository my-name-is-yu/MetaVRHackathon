import * as T from 'three';
import type { Duel } from './duel.ts';

function panel(width: number, height: number, meters: number) {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d')!;
  const texture = new T.CanvasTexture(canvas);
  texture.colorSpace = T.SRGBColorSpace;
  const mesh = new T.Mesh(
    new T.PlaneGeometry(meters, (meters * height) / width),
    new T.MeshBasicMaterial({
      map: texture,
      transparent: true,
      depthWrite: false,
      depthTest: false,
      toneMapped: false,
    }),
  );
  mesh.renderOrder = 201;
  const update = (draw: (ctx: CanvasRenderingContext2D) => void) => {
    ctx.clearRect(0, 0, width, height);
    draw(ctx);
    texture.needsUpdate = true;
  };
  return { mesh, update };
}

function box(
  c: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number,
  bg: string,
  edge?: string,
) {
  c.fillStyle = bg;
  c.beginPath();
  c.roundRect(x, y, w, h, r);
  c.fill();
  if (edge) {
    c.lineWidth = 3;
    c.strokeStyle = edge;
    c.stroke();
  }
}

function label(
  c: CanvasRenderingContext2D,
  s: string,
  x: number,
  y: number,
  size: number,
  color: string,
) {
  c.fillStyle = color;
  c.font = '600 ' + size + 'px system-ui, sans-serif';
  c.textAlign = 'center';
  c.textBaseline = 'middle';
  c.shadowColor = '#302439';
  c.shadowBlur = 5;
  c.fillText(s, x, y);
  c.shadowBlur = 0;
}

export function createVrHud(scene: T.Scene) {
  const head = new T.Group();
  const wrist = panel(640, 104, 0.16);
  const boss = panel(720, 112, 0.73);
  const hearts = panel(300, 88, 0.26);
  const message = panel(780, 80, 0.72);
  const action = panel(536, 106, 0.46);
  const lesson = panel(536, 202, 0.46);
  head.add(boss.mesh, hearts.mesh, message.mesh, action.mesh, lesson.mesh);
  scene.add(head, wrist.mesh);
  boss.mesh.position.set(0, 0.39, -1.55);
  hearts.mesh.position.set(-0.48, -0.32, -1.35);
  message.mesh.position.set(0, 0.255, -1.48);
  action.mesh.position.set(-0.41, -0.18, -1.45);
  lesson.mesh.position.set(-0.41, -0.008, -1.45);
  lesson.update((c) => {
    box(c, 4, 4, 528, 194, 20, '#32273de5', '#a695a4');
    label(c, '詠唱の見本', 268, 43, 31, '#ffe4b4');
    label(c, '○ 火球　 Z 雷　 V 氷槍', 268, 100, 31, '#ffe5dc');
    label(c, '△ 岩弾　 S 風刃　 ― バリア', 268, 152, 29, '#c3f3df');
  });
  const castRay = new T.Raycaster();
  const offset = new T.Vector3();
  const inverseHead = new T.Quaternion();
  let previous = '';
  function update(xr: boolean, camera: T.Camera, hand: T.Vector3 | null, duel: Duel, note: string) {
    head.visible = xr;
    wrist.mesh.visible = xr && hand !== null;
    if (!xr) return;
    camera.getWorldPosition(head.position);
    camera.getWorldQuaternion(head.quaternion);
    if (hand) {
      // Keep the two labels together just inside the drawing hand. Adapt their
      // angular size at close range to keep both legible in the Glasses view.
      inverseHead.copy(head.quaternion).invert();
      offset.copy(hand).sub(head.position).applyQuaternion(inverseHead);
      const depth = -offset.z;
      wrist.mesh.visible = depth > 0.12;
      const hudDepth = Math.max(0.28, depth + 0.025);
      const width = Math.min(0.19, hudDepth * 0.39);
      wrist.mesh.scale.setScalar(width / 0.16);
      offset.x = T.MathUtils.clamp(offset.x * 0.62, -0.48 * hudDepth, 0.48 * hudDepth);
      offset.y = T.MathUtils.clamp(offset.y - 0.04, -0.46 * hudDepth, 0.44 * hudDepth);
      offset.z = -hudDepth;
      wrist.mesh.position.copy(offset.applyQuaternion(head.quaternion)).add(head.position);
      wrist.mesh.quaternion.copy(head.quaternion);
    }
    action.mesh.visible = duel.phase !== 'battle';
    lesson.mesh.visible = duel.phase === 'practice';
    message.mesh.visible = !!note;
    head.updateMatrixWorld(true);
    const key = [duel.phase, duel.hearts, duel.bossHP, duel.shield, note].join('|');
    if (key === previous) return;
    previous = key;
    boss.update((c) => {
      label(c, '苔冠のゴーレム', 360, 30, 30, '#fff1d4');
      box(c, 18, 63, 684, 32, 16, '#38293ce8', '#eab97b');
      if (duel.bossHP) box(c, 24, 70, (672 * duel.bossHP) / 120, 18, 9, '#ffc565');
    });
    hearts.update((c) => {
      label(c, '♥'.repeat(duel.hearts) + '♡'.repeat(3 - duel.hearts), 150, 45, 67, '#ffdbca');
    });
    wrist.update((c) => {
      box(c, 4, 5, 278, 94, 27, duel.shield ? '#346755' : '#2e303be8', '#8fe8c4');
      box(c, 292, 5, 343, 94, 27, '#382938e8', '#e4ba92');
      label(c, '◇ バリア', 143, 52, 40, '#a4f9d4');
      label(c, '✦ 攻撃魔法', 463, 52, 40, '#ffe1aa');
    });
    message.update((c) => {
      if (!note) return;
      box(c, 8, 5, 764, 70, 25, '#32263ce0');
      label(c, note, 390, 40, 27, '#ffeace');
    });
    action.update((c) => {
      box(c, 4, 4, 528, 98, 23, '#49374aee', '#f2ce97');
      const title =
        duel.phase === 'practice'
          ? '決闘を始める'
          : duel.phase === 'won'
            ? '勝利！　再戦する'
            : '敗北　 再戦する';
      label(c, title + '  ›', 268, 54, 40, '#ffe8c9');
    });
  }
  function actionHit(ray: T.Ray) {
    if (!head.visible || !action.mesh.visible) return false;
    castRay.ray.copy(ray);
    return castRay.intersectObject(action.mesh, false).length > 0;
  }
  return { update, actionHit };
}

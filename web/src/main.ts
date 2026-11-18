import * as T from 'three';
import { createArena } from './arena.ts';
import { Duel } from './duel.ts';
import { createPreviewGlove, createStroke } from './focus.ts';
import { glyphs, matchRune, type Point2, type Spell } from './runes.ts';
import { spells } from './spells.ts';
import { createVrHud } from './vr-hud.ts';
import { startXR } from './xr.ts';

const $ = <E extends HTMLElement = HTMLElement>(id: string) => document.getElementById(id) as E;
const game = $('game'),
  viewport = $('viewport'),
  load = $('load-state');
const book = $<HTMLDialogElement>('spellbook');
const vr = $<HTMLButtonElement>('enter-vr'),
  start = $<HTMLButtonElement>('start-duel');
const bossbar = document.querySelector<HTMLElement>('.boss-track')!;
const defense = document.querySelector('.defense')!,
  offense = document.querySelector('.offense')!;
const duel = new Duel();

let arena: ReturnType<typeof createArena>, hud: ReturnType<typeof createVrHud>;
let stroke: ReturnType<typeof createStroke>, glove: T.Group;
let camera: T.Camera,
  xr = false,
  activeStroke = false;
let samples: Point2[] = [],
  lastKey = '',
  notice = '',
  noticeUntil = 0,
  castUntil = 0;
let desktopPointer: number | null = null;
let sound = false,
  audio: AudioContext | undefined;
let gameplayReady = false;
const origin = new T.Vector3(),
  previous = new T.Vector3();
const right = new T.Vector3(),
  up = new T.Vector3(),
  delta = new T.Vector3(),
  viewQ = new T.Quaternion();
const mouseNdc = new T.Vector2(0, 0),
  screenRay = new T.Raycaster();
const flatPoint = new T.Vector3(),
  restHand = new T.Vector3(),
  pinned = new T.Vector2();
const gloveOffset = new T.Vector3(0.0605, -0.0463, 0.0303);
const releasedAt = new T.Vector3();
let holdGloveUntil = 0;
let locked = false;
const pulse = (text: string, seconds = 1.5) => {
  notice = text;
  noticeUntil = performance.now() + seconds * 1000;
};
function tone(hz: number, duration: number, shape: OscillatorType = 'sine') {
  if (!sound || !audio || audio.state !== 'running') return;
  const oscillator = audio.createOscillator(),
    gain = audio.createGain();
  oscillator.type = shape;
  oscillator.frequency.setValueAtTime(hz, audio.currentTime);
  oscillator.frequency.exponentialRampToValueAtTime(hz * 0.62, audio.currentTime + duration);
  gain.gain.setValueAtTime(0.055, audio.currentTime);
  gain.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + duration);
  oscillator.connect(gain).connect(audio.destination);
  oscillator.start();
  oscillator.stop(audio.currentTime + duration);
}
$('audio').onclick = async () => {
  audio ??= new AudioContext();
  if (!sound) await audio.resume();
  sound = !sound;
  $('audio').setAttribute('aria-pressed', String(sound));
  $('audio').setAttribute('aria-label', sound ? '効果音をオフ' : '効果音をオン');
  tone(480, 0.12);
};
$('open-book').onclick = () => {
  cancel();
  book.showModal();
};
$('close-book').onclick = () => book.close();
book.addEventListener('click', (e) => {
  if (e.target === book) book.close();
});
$('spell-list').innerHTML = Object.entries(spells)
  .map(
    ([id, s]) =>
      '<div class="spell-tile"><canvas aria-hidden="true" width="106" height="80" data-rune="' +
      id +
      '"></canvas><span>' +
      s.name +
      '</span></div>',
  )
  .join('');
const diagrams = Array.from(document.querySelectorAll<HTMLCanvasElement>('canvas[data-rune]'));
let diagramFrame = -1;
function drawDiagrams(t: number) {
  const tick = matchMedia('(prefers-reduced-motion: reduce)').matches
    ? 36
    : Math.floor((t % 2400) / 45);
  if (!book.open || tick === diagramFrame) return;
  diagramFrame = tick;
  for (const canvas of diagrams) {
    const spell = canvas.dataset.rune as Spell,
      points = glyphs[spell],
      ctx = canvas.getContext('2d')!;
    const project = (point: Point2) => [53 + point.x * 57, 40 - point.y * 57] as const;
    ctx.clearRect(0, 0, 106, 80);
    ctx.lineWidth = 4;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    const path = (count: number, opacity: number) => {
      ctx.globalAlpha = opacity;
      ctx.strokeStyle = spells[spell].color;
      ctx.beginPath();
      points.slice(0, count).forEach((p, i) => {
        if (!i) ctx.moveTo(...project(p));
        else ctx.lineTo(...project(p));
      });
      ctx.stroke();
    };
    path(points.length, 0.24);
    path(Math.ceil(points.length * Math.min(1, tick / 36)), 1);
    ctx.globalAlpha = 1;
    ctx.fillStyle = '#fff2d9';
    ctx.beginPath();
    ctx.arc(...project(points[0]), 3.5, 0, Math.PI * 2);
    ctx.fill();
  }
}

function sync() {
  const key = [
    duel.phase,
    duel.hearts,
    duel.bossHP,
    duel.shield,
    duel.windup >= 0,
    duel.interruptions,
    duel.blocks,
    locked,
  ].join('|');
  if (key === lastKey) return;
  lastKey = key;
  Object.assign(game.dataset, {
    phase: duel.phase,
    hearts: String(duel.hearts),
    bossHp: String(duel.bossHP),
    shield: String(duel.shield),
    windup: String(duel.windup >= 0),
    interrupts: String(duel.interruptions),
    blocks: String(duel.blocks),
  });
  $('boss-fill').style.width = (duel.bossHP / 120) * 100 + '%';
  bossbar.setAttribute('aria-valuenow', String(duel.bossHP));
  $('hearts').innerHTML = [0, 1, 2]
    .map(
      (i) =>
        '<svg aria-hidden="true" class="' +
        (i >= duel.hearts ? 'empty' : '') +
        '"><use href="#heart"/></svg>',
    )
    .join('');
  $('hearts').setAttribute('aria-label', '生命力 ' + duel.hearts + ' / 3');
  defense.classList.toggle('active', duel.shield);
  const label = { practice: '練習', battle: '決闘', won: '勝利', lost: '敗北' }[duel.phase];
  $('mode-tag').textContent = label;
  start.hidden = duel.phase === 'battle';
  start.firstChild!.textContent = duel.phase === 'practice' ? '決闘を始める ' : 'もう一度戦う ';
  const title = {
    practice: '詠唱の練習',
    battle: 'ゴーレムとの決闘',
    won: '勝利！ 遺跡に静けさが戻った',
    lost: '敗北　もう一度、挑戦しよう',
  };
  $('lesson-title').textContent = title[duel.phase];
  $('lesson-text').textContent =
    duel.phase === 'practice'
      ? locked
        ? '照準を固定中。ドラッグで魔法を描く。右クリックで固定を解除。'
        : '○で火球、Zで雷。ドラッグで描いて離すと発射。右クリックで照準固定。'
      : duel.phase === 'battle'
        ? '拳が上がったら胸に雷を撃つか、横線を描いてバリアを展開。'
        : '雷で拳を止めた回数 ' + duel.interruptions + '　 •　 防いだ回数 ' + duel.blocks;
}

function startBattle() {
  if (!gameplayReady) return;
  clearStroke();
  duel.reset('battle');
  arena.reset();
  lastKey = '';
  delete game.dataset.lastSpell;
  delete game.dataset.lastResult;
  delete game.dataset.lastTarget;
  pulse('決闘開始');
  tone(493, 0.32);
  sync();
}
start.onclick = startBattle;

function clearStroke() {
  activeStroke = false;
  samples.length = 0;
  stroke?.clear();
}
function cancel() {
  if (activeStroke) game.dataset.lastResult = 'cancel';
  clearStroke();
}
function begin(point: T.Vector3, view: T.Camera) {
  if (!gameplayReady || book.open) return;
  clearStroke();
  activeStroke = true;
  origin.copy(point);
  previous.copy(point);
  view.getWorldQuaternion(viewQ);
  right.set(1, 0, 0).applyQuaternion(viewQ);
  up.set(0, 1, 0).applyQuaternion(viewQ);
  samples = [{ x: 0, y: 0 }];
}
function sample(point: T.Vector3) {
  if (!activeStroke) return;
  if (samples.length >= 1024) {
    cancel();
    return;
  }
  if (previous.distanceToSquared(point) < 0.000004) return;
  delta.subVectors(point, origin);
  samples.push({ x: delta.dot(right), y: delta.dot(up) });
  stroke.segment(previous, point);
  previous.copy(point);
}
function release(point: T.Vector3, gaze: T.Ray | null) {
  if (!activeStroke) return;
  sample(point);
  if (!activeStroke) return;
  const spell = matchRune(samples);
  clearStroke();
  if (!spell && gaze && hud.actionHit(gaze)) {
    startBattle();
    return;
  }
  if (duel.phase === 'won' || duel.phase === 'lost') return;
  if (!spell) {
    game.dataset.lastResult = 'invalid';
    pulse('模様をもう一度描いてください');
    return;
  }
  if (spell !== 'barrier' && !gaze) {
    game.dataset.lastResult = 'cancel';
    pulse('目だけで左右を見て、視線を再取得してください');
    return;
  }
  const hit = gaze && spell !== 'barrier' ? arena.aim(gaze) : { part: 'miss' as const, point };
  const result = duel.cast(spell, hit.part);
  if (!result) return;
  Object.assign(game.dataset, { lastSpell: spell, lastTarget: hit.part, lastResult: result });
  arena.cast(
    spell,
    point,
    hit.point,
    {
      head: camera.getWorldPosition(new T.Vector3()),
      forward: camera.getWorldDirection(new T.Vector3()),
    },
    hit.part,
  );
  castUntil = performance.now() + 400;
  if (!xr) {
    releasedAt.copy(point).add(gloveOffset);
    holdGloveUntil = performance.now() + 380;
  }
  const freq: Record<Spell, number> = {
    fire: 230,
    ice: 830,
    lightning: 660,
    rock: 100,
    wind: 440,
    barrier: 525,
  };
  tone(freq[spell], spell === 'barrier' ? 0.4 : 0.22, spell === 'lightning' ? 'triangle' : 'sine');
  if (result === 'interrupt') pulse('結晶に命中！ 拳を止めた');
  else if (result === 'shield') pulse('バリアを展開');
  else if (hit.part === 'core') pulse('結晶に命中');
  else {
    notice = '';
    noticeUntil = 0;
  }
  if (duel.bossHP === 0) pulse('勝利！', 3);
  sync();
}

function updatePointer(e: PointerEvent | MouseEvent) {
  const bounds = viewport.getBoundingClientRect();
  mouseNdc.set(
    (2 * (e.clientX - bounds.left)) / bounds.width - 1,
    1 - (2 * (e.clientY - bounds.top)) / bounds.height,
  );
}
function rayAt(ndc: T.Vector2) {
  camera.updateMatrixWorld();
  screenRay.setFromCamera(ndc, camera);
  return screenRay.ray;
}
function handPoint() {
  const ray = rayAt(mouseNdc);
  camera.getWorldDirection(delta);
  return ray.at(0.57 / Math.max(0.1, ray.direction.dot(delta)), flatPoint);
}
viewport.oncontextmenu = (e) => {
  e.preventDefault();
  if (!gameplayReady || xr) return;
  updatePointer(e);
  locked = !locked;
  if (locked) pinned.copy(mouseNdc);
  pulse(locked ? '照準を固定' : '照準の固定を解除');
  sync();
};
viewport.onpointerdown = (e) => {
  if (!gameplayReady || xr || e.button !== 0 || desktopPointer !== null) return;
  updatePointer(e);
  desktopPointer = e.pointerId;
  viewport.setPointerCapture(e.pointerId);
  begin(handPoint(), camera);
};
viewport.onpointermove = (e) => {
  if (xr || !gameplayReady || (desktopPointer !== null && e.pointerId !== desktopPointer)) return;
  updatePointer(e);
  if (desktopPointer !== null) sample(handPoint());
};
viewport.onpointerup = (e) => {
  if (xr || desktopPointer !== e.pointerId) return;
  updatePointer(e);
  const point = handPoint().clone();
  release(point, rayAt(locked ? pinned : mouseNdc));
  desktopPointer = null;
};
viewport.onlostpointercapture = viewport.onpointercancel = () => {
  if (desktopPointer !== null) cancel();
  desktopPointer = null;
};
window.addEventListener('blur', () => {
  if (!xr) {
    desktopPointer = null;
    cancel();
  }
});
sync();
start.disabled = true;
startXR(viewport, {
  ready(scene, view) {
    camera = view;
    arena = createArena(scene);
    hud = createVrHud(scene);
    stroke = createStroke(scene);
    glove = createPreviewGlove(scene);
    gameplayReady = true;
    load.hidden = true;
    start.disabled = false;
  },
  frame(dt, view, hand, gaze) {
    if (!gameplayReady) return;
    camera = view;
    const now = performance.now();
    if (book.open) drawDiagrams(now);
    const impact = book.open ? null : duel.step(Math.min(0.1, dt));
    if (impact) {
      arena.impact(impact === 'blocked');
      if (impact === 'blocked') {
        pulse('バリアが拳を防いだ');
        tone(340, 0.35);
      } else if (duel.phase !== 'practice') {
        pulse(duel.phase === 'lost' ? '敗北' : '被弾！');
        tone(110, 0.25);
      }
    }
    const aim = xr ? gaze : rayAt(locked ? pinned : mouseNdc);
    arena.update(
      dt,
      duel.windup,
      duel.shield,
      duel.phase,
      aim !== null && arena.aim(aim).part === 'core',
    );
    glove.visible = !xr;
    if (!xr) {
      if (desktopPointer !== null) glove.position.copy(handPoint()).add(gloveOffset);
      else if (now < holdGloveUntil) glove.position.copy(releasedAt);
      else {
        restHand.set(
          0.3 + 0.004 * Math.sin(now / 1000),
          -0.175 + 0.002 * Math.cos(now / 550),
          -0.43,
        );
        camera.localToWorld(restHand);
        if (glove.position.lengthSq() === 0) glove.position.copy(restHand);
        else glove.position.lerp(restHand, 1 - Math.exp(-14 * dt));
      }
      camera.getWorldQuaternion(glove.quaternion);
    }
    const activeNote = now < noticeUntil ? notice : '';
    const callout = $('callout');
    if (callout.textContent !== activeNote) callout.textContent = activeNote;
    callout.classList.toggle('visible', !!activeNote);
    offense.classList.toggle('active', now < castUntil && game.dataset.lastSpell !== 'barrier');
    hud.update(xr, view, hand, duel, activeNote);
    sync();
  },
  begin,
  sample,
  release,
  cancel,
  session(active) {
    xr = active;
    game.dataset.xr = String(active);
    desktopPointer = null;
    locked = false;
    cancel();
    if (active) pulse('頭を正面に向け、目だけで左右を見てください', 4);
  },
  error(message) {
    pulse(message, 6);
  },
})
  .then(({ enter }) => {
    vr.disabled = false;
    vr.onclick = () => {
      book.close();
      void enter();
    };
  })
  .catch((error: unknown) => {
    load.hidden = false;
    load.textContent = '3D画面を開始できませんでした。ページを再読み込みしてください。';
    console.error(error);
  });

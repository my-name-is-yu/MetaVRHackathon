import { buildSessionInit, createSystem, World } from '@iwsdk/core';
import projectOptions from 'virtual:iwsdk-project';
import { Quaternion, Ray, Vector3, type Camera, type Scene } from 'three';

export interface XRCallbacks {
  ready(scene: Scene, camera: Camera): void;
  frame(dt: number, camera: Camera, hand: Vector3 | null, gaze: Ray | null): void;
  begin(point: Vector3, camera: Camera): void;
  sample(point: Vector3): void;
  release(point: Vector3, gaze: Ray | null): void;
  cancel(): void;
  session(active: boolean): void;
  error(message: string): void;
}

type GlassesSession = XRSession & { trackedSources?: Iterable<XRInputSource> };

function trackingSources(session: GlassesSession): XRInputSource[] {
  return [...new Set([...session.inputSources, ...(session.trackedSources ?? [])])];
}

function fingertip(frame: XRFrame, ref: XRReferenceSpace, hand?: XRInputSource): Vector3 | null {
  const joint = hand?.hand?.get('index-finger-tip');
  const pose = joint && frame.getJointPose?.(joint, ref);
  return pose ? new Vector3().copy(pose.transform.position) : null;
}

const forward = (pose: XRPose) => new Vector3(0, 0, -1).applyQuaternion(
  new Quaternion().copy(pose.transform.orientation)
);

export async function startXR(container: HTMLElement, callbacks: XRCallbacks) {
  const world = await World.create(container, projectOptions);
  const { camera, renderer, scene } = world;
  let active: XRSession | null = null;
  let viewerSpace: XRReferenceSpace | null = null;
  let drawingHand: XRInputSource | undefined;
  let pointingHand: XRInputSource | undefined;
  let gazeSource: XRInputSource | undefined;
  let independentGaze = false;
  let requesting = false;

  function fitPreview() {
    if (renderer.xr.isPresenting) return;
    const { clientWidth: width, clientHeight: height } = container;
    if (width < 1 || height < 1) return;
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
    renderer.setSize(width, height);
  }
  const resize = new ResizeObserver(fitPreview);
  resize.observe(container);
  window.addEventListener('resize', fitPreview);
  world.addCleanup(() => {
    resize.disconnect();
    window.removeEventListener('resize', fitPreview);
  });
  fitPreview();

  function cancel() {
    if (!drawingHand) return;
    drawingHand = undefined;
    callbacks.cancel();
  }

  function readGaze(frame: XRFrame, ref: XRReferenceSpace, sources: XRInputSource[]): Ray | null {
    const source = sources.find(input => input.targetRayMode === 'gaze');
    if (gazeSource !== source) {
      gazeSource = source;
      independentGaze = false;
    }
    if (!source || !viewerSpace) return null;
    let eyes: XRPose | null;
    let head: XRPose | null;
    try {
      eyes = frame.getPose(source.targetRaySpace, ref) ?? null;
      head = frame.getPose(viewerSpace, ref) ?? null;
    } catch (error) {
      if (error instanceof DOMException) return null;
      throw error;
    }
    if (!eyes || !head) {
      independentGaze = false;
      return null;
    }

    // A runtime can map a valid "gaze" pose onto the headset. Require evidence
    // that this source moves independently, then allow normal straight-ahead aim.
    const direction = forward(eyes);
    if (direction.angleTo(forward(head)) > Math.PI / 120) independentGaze = true;
    if (!independentGaze) return null;
    return new Ray(new Vector3().copy(eyes.transform.position), direction);
  }

  function onStart(event: XRInputSourceEvent) {
    const ref = world.xrReferenceSpace;
    if (drawingHand || active?.visibilityState !== 'visible' || !ref || !event.inputSource.hand) return;
    const point = fingertip(event.frame, ref, event.inputSource);
    if (!point) return;
    drawingHand = pointingHand = event.inputSource;
    callbacks.begin(point, camera);
  }

  function onRelease(event: XRInputSourceEvent) {
    if (drawingHand !== event.inputSource) return;
    const ref = world.xrReferenceSpace;
    if (!ref || !active || active.visibilityState !== 'visible') return cancel();
    const point = fingertip(event.frame, ref, drawingHand);
    if (!point) return cancel();
    const gaze = readGaze(event.frame, ref, trackingSources(active));
    drawingHand = undefined;
    callbacks.release(point, gaze);
  }

  // Successful hand release emits "select" followed by "selectend". A lost
  // hand produces only "selectend", and must never launch the pending spell.
  function onEnd(event: XRInputSourceEvent) {
    if (event.inputSource === drawingHand) cancel();
  }

  function onSources(event: XRInputSourcesChangeEvent) {
    if (drawingHand && event.removed.includes(drawingHand)) cancel();
    if (pointingHand && event.removed.includes(pointingHand)) pointingHand = undefined;
    if (gazeSource && event.removed.includes(gazeSource)) {
      gazeSource = undefined;
      independentGaze = false;
    }
  }

  function onVisibility() {
    if (active?.visibilityState !== 'visible') {
      cancel();
      independentGaze = false;
    }
  }

  function attachSession() {
    const session = renderer.xr.getSession();
    if (!session || active === session) return;
    active = world.session = session;
    session.addEventListener('selectstart', onStart);
    session.addEventListener('select', onRelease);
    session.addEventListener('selectend', onEnd);
    session.addEventListener('inputsourceschange', onSources);
    session.addEventListener('visibilitychange', onVisibility);
    session.requestReferenceSpace('viewer').then(space => {
      if (active === session) viewerSpace = space;
    }).catch(() => {
      if (active === session) callbacks.error('視線の基準座標を取得できませんでした。VRを終了して再度お試しください。');
    });
    callbacks.session(true);
  }

  function detachSession() {
    if (!active) return;
    active.removeEventListener('selectstart', onStart);
    active.removeEventListener('select', onRelease);
    active.removeEventListener('selectend', onEnd);
    active.removeEventListener('inputsourceschange', onSources);
    active.removeEventListener('visibilitychange', onVisibility);
    cancel();
    active = null;
    viewerSpace = null;
    world.session = undefined;
    pointingHand = undefined;
    gazeSource = undefined;
    independentGaze = false;
    camera.position.set(0, 1.6, 0);
    camera.rotation.set(0, 0, 0);
    requestAnimationFrame(fitPreview);
    callbacks.session(false);
  }

  renderer.xr.addEventListener('sessionstart', attachSession);
  renderer.xr.addEventListener('sessionend', detachSession);

  callbacks.ready(scene, camera);
  class DuelFrames extends createSystem() {
    update(dt: number) {
      const frame = world.xrFrame;
      const ref = world.xrReferenceSpace;
      if (!active || !frame || !ref) {
        callbacks.frame(dt, camera, null, null);
        return;
      }
      if (active.visibilityState !== 'visible') {
        cancel();
        callbacks.frame(0, camera, null, null);
        return;
      }
      const sources = trackingSources(active);
      const hands = sources.filter(source => source.hand);
      let point: Vector3 | null = null;
      if (drawingHand) {
        point = sources.includes(drawingHand) ? fingertip(frame, ref, drawingHand) : null;
        if (point) callbacks.sample(point);
        else cancel();
      } else {
        // Keep the most recently used hand's HUD steady while both are visible.
        const ordered = [pointingHand, hands.find(hand => hand.handedness === 'right'), ...hands];
        for (const hand of ordered) {
          if (!hand || !hands.includes(hand)) continue;
          point = fingertip(frame, ref, hand);
          if (point) {
            pointingHand = hand;
            break;
          }
        }
      }
      callbacks.frame(dt, camera, point, readGaze(frame, ref, sources));
    }
  }
  world.registerSystem(DuelFrames);

  async function enter() {
    if (active || requesting) return;
    requesting = true;
    let session: XRSession | undefined;
    try {
      if (!navigator.xr) throw new Error('このブラウザはWebXRに対応していません。');
      session = await navigator.xr.requestSession('immersive-vr', buildSessionInit(world.xrDefaults ?? {}));
      renderer.xr.setReferenceSpaceType('local-floor');
      await renderer.xr.setSession(session);
    } catch (cause) {
      if (session) await session.end().catch(() => {});
      const detail = cause instanceof Error ? cause.message : String(cause);
      callbacks.error('VRを開始できません。手・視線トラッキングに対応する環境と許可を確認してください。 ' + detail);
    } finally {
      requesting = false;
    }
  }

  return { enter };
}

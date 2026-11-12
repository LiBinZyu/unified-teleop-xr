import {
  AmbientLight, ArrowHelper, Box3, DirectionalLight, GridHelper, Group,
  Mesh, MeshStandardMaterial, PerspectiveCamera, Quaternion, Scene, SphereGeometry,
  Vector3, WebGLRenderer,
} from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { FBXLoader } from "three/addons/loaders/FBXLoader.js";
import leftFBX from "./assets/LeftHand.fbx";
import rightFBX from "./assets/RightHand.fbx";
import { applyHandShader, loadFingerMask } from "./hand-shader.js";
import { joints, validJoint, visuals } from "./protocol.js";

const axes = [
  { dir: new Vector3(0, 0, -1), color: "#ff756d" },
  { dir: new Vector3(-1, 0, 0), color: "#69da8e" },
  { dir: new Vector3(0, 1, 0), color: "#77baff" },
];
const labelOffset = new Vector3(0, 0.07, 0);
const origin = new Vector3();
const fbxToJoint = new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), Math.PI);

function readRosPose(data, offset, position, orientation) {
  // ROS X forward/Y left/Z up → Three X right/Y up/Z back. This is a proper rotation.
  position.set(-data[offset + 1], data[offset + 2], -data[offset]);
  orientation.set(-data[offset + 4], data[offset + 5], -data[offset + 3], data[offset + 6]).normalize();
}

function createHand(side, fingerMask) {
  const left = side === "left";
  const bytes = left ? leftFBX : rightFBX;
  const prefix = left ? "L_" : "R_";
  const object = new FBXLoader().parse(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), "");
  // FBX stores centimeters. Unit conversion stays outside the skinned hierarchy.
  object.scale.setScalar(0.01);
  object.updateMatrixWorld(true);
  const bones = joints.map((name) => {
    const bone = object.getObjectByName(prefix + name);
    if (!bone?.isBone) throw new Error(`FBX missing ${prefix + name}`);
    return { bone, position: bone.position.clone(), rotation: bone.quaternion.clone() };
  });
  const wrist = bones[0].bone;
  applyHandShader(object, wrist, fingerMask);
  const position = new Vector3();
  const orientation = new Quaternion();
  const inverseParent = new Quaternion();

  return {
    object, wrist,
    update(data) {
      for (const [index, entry] of bones.entries()) {
        const { bone } = entry;
        if (validJoint(data, index)) {
          readRosPose(data, index * 8 + 1, position, orientation);
          // Raw FBX joint forward is +Z. The Three representation of XR joint forward is -Z.
          orientation.multiply(fbxToJoint);
          bone.position.copy(bone.parent.worldToLocal(position));
          bone.parent.getWorldQuaternion(inverseParent).invert();
          bone.quaternion.copy(orientation.premultiply(inverseParent));
        } else {
          bone.position.copy(entry.position);
          bone.quaternion.copy(entry.rotation);
        }
        bone.updateMatrixWorld(true);
      }
    },
  };
}

function createAxes(color, size) {
  const object = new Group();
  const center = new Mesh(new SphereGeometry(size * 0.045, 10, 8), new MeshStandardMaterial({ color }));
  object.add(center);
  for (const axis of axes) {
    object.add(new ArrowHelper(axis.dir, origin, size, axis.color, size * 0.19, size * 0.1));
  }
  return object;
}

export function createScene(viewport, labels, empty, fitButton) {
  let renderer;
  try {
    renderer = new WebGLRenderer({ alpha: true, antialias: true, powerPreference: "low-power" });
  } catch {
    empty.querySelector("span").textContent = "WebGL unavailable";
    fitButton.disabled = true;
    return { sync() {}, fit() {} };
  }
  renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
  viewport.prepend(renderer.domElement);
  const scene = new Scene();
  const camera = new PerspectiveCamera(42, 1, 0.01, 100);
  const orbit = new OrbitControls(camera, renderer.domElement);
  orbit.enableDamping = !matchMedia("(prefers-reduced-motion: reduce)").matches;
  orbit.dampingFactor = 0.1;
  orbit.maxDistance = 65;
  orbit.minDistance = 0.09;
  const viewDirection = new Vector3(1.55, 0.93, 2.28).normalize();
  const grid = new GridHelper(10, 40, "#ffffff", "#ffffff");
  grid.material.transparent = true;
  grid.material.opacity = 0.16;
  scene.add(grid, new AmbientLight("#eef2f6", 2));
  const key = new DirectionalLight("#fff5ea", 2);
  key.position.set(3, 5, 4);
  const fill = new DirectionalLight("#b5dfff", 1.6);
  fill.position.set(-3, 2, -3);
  scene.add(key, fill);
  const objects = new Map();
  let dirty = true;
  const fingerMask = loadFingerMask(() => { dirty = true; });
  let width = 0;
  let height = 0;

  for (const visual of visuals.filter(({ kind }) => kind !== "input")) {
    let item;
    if (visual.kind === "hand") {
      const side = visual.topic.split("/")[0];
      const hand = createHand(side, fingerMask);
      item = { ...hand, anchor: hand.wrist };
    } else {
      const object = createAxes(visual.color, visual.topic === "eye/pose" ? 0.11 : 0.16);
      item = {
        object, anchor: object,
        update(data) { readRosPose(data, 0, object.position, object.quaternion); },
      };
    }
    item.object.visible = false;
    item.label = document.createElement("span");
    item.label.className = "scene-label";
    item.label.textContent = visual.label;
    item.label.hidden = true;
    labels.append(item.label);
    scene.add(item.object);
    objects.set(visual.topic, item);
  }

  function fit() {
    const bounds = new Box3();
    const position = new Vector3();
    for (const { object, anchor } of objects.values()) {
      if (object.visible) bounds.expandByPoint(anchor.getWorldPosition(position));
    }
    const hasObjects = !bounds.isEmpty();
    const target = hasObjects ? bounds.getCenter(new Vector3()) : new Vector3(0, 1.13, -0.15);
    const span = hasObjects ? bounds.getSize(new Vector3()).length() : 1.4;
    const verticalFov = camera.fov * Math.PI / 180;
    const frustum = Math.tan(verticalFov / 2) * Math.min(camera.aspect, 1);
    const distance = Math.max(0.9, (span * 0.62 + 0.18) / frustum);
    orbit.target.copy(target);
    camera.position.copy(target).addScaledVector(viewDirection, Math.min(distance, 65));
    orbit.update();
    dirty = true;
  }

  const projected = new Vector3();
  function render() {
    requestAnimationFrame(render);
    orbit.update();
    if (!dirty || !width || !height) return;
    dirty = false;
    renderer.render(scene, camera);
    for (const { object, anchor, label } of objects.values()) {
      if (!object.visible) {
        label.hidden = true;
        continue;
      }
      anchor.getWorldPosition(projected).add(labelOffset).project(camera);
      label.hidden = projected.z < -1 || projected.z > 1 || Math.abs(projected.x) > 1 || Math.abs(projected.y) > 1;
      if (!label.hidden) {
        label.style.left = `${(projected.x + 1) * width / 2}px`;
        label.style.top = `${(1 - projected.y) * height / 2}px`;
      }
    }
  }

  orbit.addEventListener("change", () => { dirty = true; });
  fitButton.addEventListener("click", fit);
  new ResizeObserver(() => {
    width = viewport.clientWidth;
    height = viewport.clientHeight;
    if (!width || !height) return;
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
    renderer.setSize(width, height, false);
    dirty = true;
  }).observe(viewport);
  fit();
  render();

  return {
    fit,
    sync(active) {
      let visible = 0;
      for (const [topic, item] of objects) {
        const sample = active.get(topic);
        if (sample && sample !== item.sample) {
          item.update(sample.data);
          dirty = true;
        }
        if (item.object.visible !== Boolean(sample)) {
          item.object.visible = Boolean(sample);
          dirty = true;
        }
        if (sample) visible++;
        item.sample = sample;
      }
      empty.hidden = visible > 0;
      return visible;
    },
  };
}

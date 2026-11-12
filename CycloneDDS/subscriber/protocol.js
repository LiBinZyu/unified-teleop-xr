// Topics are relative to rt/xr/. See XRPublishConfiguration and XRPublishEncoding.
export const inputTopic = "controller/input";
export const visuals = [
  { topic: "head/pose", label: "Head", kind: "pose", color: "#ffffff" },
  { topic: "eye/pose", label: "Eye", kind: "pose", color: "#ffcc83" },
  { topic: "left/controller_pose", label: "Left controller", kind: "pose", side: "left", color: "#64d2ff" },
  { topic: "right/controller_pose", label: "Right controller", kind: "pose", side: "right", color: "#83e9a2" },
  { topic: "left/hand_joints", label: "Left hand", kind: "hand", color: "#64d2ff" },
  { topic: "right/hand_joints", label: "Right hand", kind: "hand", color: "#83e9a2" },
  { topic: inputTopic, label: "Input overlay", kind: "input" },
];

const fingers = ["Index", "Middle", "Ring", "Little"];
const segments = ["Metacarpal", "Proximal", "Intermediate", "Distal", "Tip"];
export const joints = [
  "Wrist", "Palm",
  ...segments.filter((segment) => segment !== "Intermediate").map((segment) => "Thumb" + segment),
  ...fingers.flatMap((finger) => segments.map((segment) => finger + segment)),
];

export const controls = [
  { id: "left-stick", label: "Stick", side: "left", key: "axes", indices: [0, 1] },
  { id: "left-click", label: "Stick click", side: "left", key: "buttons", indices: [4] },
  { id: "left-trigger", label: "Trigger", side: "left", key: "axes", indices: [2] },
  { id: "left-grip", label: "Grip", side: "left", key: "axes", indices: [3] },
  { id: "x", label: "X", side: "left", key: "buttons", indices: [0] },
  { id: "y", label: "Y", side: "left", key: "buttons", indices: [1] },
  { id: "right-stick", label: "Stick", side: "right", key: "axes", indices: [4, 5] },
  { id: "right-click", label: "Stick click", side: "right", key: "buttons", indices: [5] },
  { id: "right-trigger", label: "Trigger", side: "right", key: "axes", indices: [6] },
  { id: "right-grip", label: "Grip", side: "right", key: "axes", indices: [7] },
  { id: "a", label: "A", side: "right", key: "buttons", indices: [2] },
  { id: "b", label: "B", side: "right", key: "buttons", indices: [3] },
];

export const poseFields = ["x", "y", "z", "qx", "qy", "qz", "qw"];
const twistFields = ["linear.x", "linear.y", "linear.z", "angular.x", "angular.y", "angular.z"];
const joyFields = controls.flatMap(({ label, side, key, indices }) => indices.map((index, axis) => ({
  label: `${side === "left" ? "Left" : "Right"} ${label.length === 1 ? label : label.toLowerCase()}${indices.length === 2 ? (axis ? " Y" : " X") : ""}`,
  key, index,
})));

function arrayFields(names) {
  return names.map((label, index) => ({ label, index }));
}

const handFields = arrayFields(joints.flatMap((joint) => ["state", ...poseFields].map((key) => `${joint}.${key}`)));
const poseChannels = arrayFields(poseFields);
const twistChannels = arrayFields(twistFields);
const stateChannel = [{ label: "data" }];

export function fieldsFor(topic) {
  if (topic === inputTopic) return joyFields;
  if (topic.endsWith("pose")) return poseChannels;
  if (topic.endsWith("velocity")) return twistChannels;
  if (topic.endsWith("hand_joints")) return handFields;
  if (topic.endsWith("tracking_state")) return stateChannel;
  return [];
}

export function valueOf(data, field) {
  if (field.key) return data?.[field.key]?.[field.index];
  if (field.index !== undefined) {
    // Discard the zero placeholders accompanying untracked hand joints.
    if (data?.length === 208 && field.index % 8 && !(data[field.index - field.index % 8] & 2)) {
      return undefined;
    }
    return data?.[field.index];
  }
  return data;
}

export function controlValues(data, control) {
  return control.indices.map((index) => data?.[control.key]?.[index]);
}

export function controllerTracked(state) {
  const poseBits = 3; // Unity InputTrackingState.Position | Rotation
  return (state & poseBits) === poseBits;
}

export function validPose(values, start = 0) {
  if (!Array.isArray(values) || values.length < start + 7) return false;
  for (let i = start; i < start + 7; i++) {
    if (!Number.isFinite(values[i])) return false;
  }
  return Math.hypot(values[start + 3], values[start + 4], values[start + 5], values[start + 6]) > 1e-8;
}

export function validJoint(values, index) {
  return (values?.[index * 8] & 2) !== 0 && validPose(values, index * 8 + 1);
}

export function hasVisual(kind, data) {
  if (kind === "pose") return validPose(data);
  if (kind === "hand") return data?.length === 208 && validJoint(data, 0);
  return controls.some((control) => controlValues(data, control).some(Number.isFinite));
}

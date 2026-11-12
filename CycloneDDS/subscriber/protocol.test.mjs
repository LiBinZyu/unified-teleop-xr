import assert from "node:assert/strict";
import { test } from "node:test";
import { controls, controlValues, fieldsFor, hasVisual, joints, validJoint, valueOf } from "./protocol.js";

test("a Joy field at zero or below is available and both stick axes keep their wire order", () => {
  const joy = { axes: [-0.75, 0], buttons: [0] };
  assert.equal(hasVisual("input", joy), true);
  assert.equal(hasVisual("input", { axes: [], buttons: [] }), false);
  assert.deepEqual(controlValues(joy, controls[0]), [-0.75, 0]);
  assert.deepEqual(controlValues(joy, controls[6]), [undefined, undefined]);
  assert.deepEqual(fieldsFor("controller/input").slice(0, 2).map((field) => valueOf(joy, field)), [-0.75, 0]);
});

test("a 26-joint hand requires a valid wrist and masks invalid joint coordinates", () => {
  assert.equal(joints.length, 26);
  const frame = Array(208).fill(0);
  frame[0] = 2;
  frame[7] = 1;
  assert.equal(hasVisual("hand", frame), true);
  assert.equal(validJoint(frame, 1), false);
  const fields = fieldsFor("left/hand_joints");
  assert.equal(valueOf(frame, fields[1]), 0);
  assert.equal(valueOf(frame, fields[8]), 0);
  assert.equal(valueOf(frame, fields[9]), undefined);
  assert.equal(hasVisual("hand", frame.slice(0, -1)), false);
  frame[0] = 0;
  assert.equal(hasVisual("hand", frame), false);
});

test("an identity rotation at the origin is available but a zero quaternion is not", () => {
  assert.equal(hasVisual("pose", [0, 0, 0, 0, 0, 0, 1]), true);
  assert.equal(hasVisual("pose", [0, 0, 0, 0, 0, 0, 0]), false);
  assert.equal(hasVisual("pose", [null, 0, 0, 0, 0, 0, 1]), false);
});

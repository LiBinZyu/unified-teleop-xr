import { createController } from "./controller.js";
import { createMonitor } from "./monitor.js";
import { controllerTracked, hasVisual, inputTopic, visuals } from "./protocol.js";
import { createScene } from "./scene.js";

const get = (id) => document.getElementById(id);
const scene = createScene(get("viewport"), get("scene-labels"), get("scene-empty"), get("fit-view"));
const controller = createController(get("device"), get("controller-render"), get("controller-mask"));
const monitor = createMonitor(get);
const rows = new Map();
const host = get("connection");
const hostText = host.querySelector("span");

let lastEvent;
let latest = new Map();
let fitted = false;

for (const visual of visuals) {
  const row = document.createElement("label");
  row.className = "stream-toggle";
  const title = document.createElement("span");
  title.textContent = visual.label;
  const toggle = document.createElement("input");
  toggle.type = "checkbox";
  toggle.className = "switch";
  toggle.setAttribute("role", "switch");
  toggle.setAttribute("aria-label", visual.label);
  toggle.disabled = true;
  row.append(title, toggle);
  get(`${visual.kind}-switches`).append(row);
  const item = { row, toggle, enabled: true };
  rows.set(visual.topic, item);
  toggle.addEventListener("change", () => {
    item.enabled = toggle.checked;
    updateVisuals();
  });
}

function setStatus(state, text, detail = "") {
  host.dataset.state = state;
  if (hostText.textContent !== text) hostText.textContent = text;
  if (host.title !== detail) host.title = detail;
}

function updateVisuals() {
  const enabled = new Map();
  const tracking = {
    left: controllerTracked(latest.get("left/controller_tracking_state")?.data),
    right: controllerTracked(latest.get("right/controller_tracking_state")?.data),
  };
  for (const { topic, kind, side } of visuals) {
    const sample = latest.get(topic);
    let available = hasVisual(kind, sample?.data);
    if (side) available &&= tracking[side];
    if (kind === "input") available &&= tracking.left || tracking.right;
    const { row, toggle, enabled: preference } = rows.get(topic);
    toggle.disabled = !available;
    toggle.checked = available && preference;
    const tooltip = `/xr/${topic}${available ? "" : " · No data"}`;
    if (row.title !== tooltip) row.title = tooltip;
    if (toggle.checked) enabled.set(topic, sample);
  }
  const shown = scene.sync(enabled);
  if (!fitted && shown) {
    scene.fit();
    fitted = true;
  }
  controller.update(latest.get(inputTopic)?.data, enabled.has(inputTopic), tracking);
}

function ingest(streams) {
  const next = new Map();
  for (const [topic, sample] of Object.entries(streams)) {
    const existing = latest.get(topic);
    next.set(topic, existing?.seq === sample.seq ? existing : sample);
  }
  latest = next;
  updateVisuals();
  monitor.ingest(latest, performance.now());
}

function connectDDS() {
  const stream = new EventSource("/events");
  stream.onmessage = ({ data }) => {
    lastEvent = performance.now();
    const packet = JSON.parse(data);
    ingest(packet.streams);
    if (packet.error) setStatus("error", "DDS error", packet.error);
    else if (latest.size) setStatus("live", `DDS ${packet.domain} · Live`);
    else setStatus("waiting", `DDS ${packet.domain} · Idle`);
  };
  stream.onerror = () => {
    ingest({});
    setStatus("error", "Disconnected");
  };
  setInterval(() => {
    if (lastEvent !== undefined && performance.now() - lastEvent > 2_000) {
      ingest({});
      setStatus("error", "Disconnected");
      lastEvent = undefined;
    }
  }, 500);
}

updateVisuals();
connectDDS();

import picoRender from "./assets/picoultracontroller_render.png";
import questRender from "./assets/questtouchpluscontroller_render.png";
import picoMask from "./assets/picoultra_controller_mask.svg";
import questMask from "./assets/quest_touch_plus_controller_mask.svg";
import { controls, controlValues } from "./protocol.js";

// SVG paths are numbered in the original 1024×1024 assets. Unlisted paths are system buttons.
const devices = {
  pico: {
    render: picoRender, mask: picoMask,
    paths: [4, 5, 6, 2, 0, 1, 11, 10, 12, 8, 7, 13],
    labels: [[165, 119], [317, 119], [420, 306], [246, 589], [150, 390], [276, 221],
      [859, 119], [707, 119], [604, 306], [778, 589], [874, 390], [748, 221]],
  },
  quest: {
    render: questRender, mask: questMask,
    paths: [4, 5, 6, 2, 0, 1, 11, 12, 13, 9, 7, 8],
    labels: [[151, 152], [303, 152], [416, 324], [208, 594], [150, 397], [272, 234],
      [873, 152], [721, 152], [608, 324], [816, 594], [874, 397], [752, 234]],
  },
};

const SVG = "http://www.w3.org/2000/svg";
function svgElement(tag, attributes = {}) {
  const node = document.createElementNS(SVG, tag);
  for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, value);
  return node;
}

function createBadge(svg, path, x, y, control) {
  const badge = svgElement("g", { class: "reading-badge", "data-live": "false" });
  const lines = control.indices.length;
  const b = path.getBBox();
  const line = svgElement("line", { x1: b.x + b.width / 2, y1: b.y + b.height / 2, x2: x, y2: y });
  const width = lines === 2 ? 148 : control.id.endsWith("-click") ? 128 : 94;
  const height = lines === 1 ? 43 : 74;
  badge.append(line, svgElement("rect", { x: x - width / 2, y: y - height / 2, width, height, rx: 12 }));
  const text = Array.from({ length: lines }, (_, i) => svgElement("text", {
    x, y: y + (lines === 1 ? 10 : -6 + i * 32), "text-anchor": "middle",
  }));
  badge.append(...text);
  svg.append(badge);
  return { path, badge, text };
}

function format(value, kind) {
  if (!Number.isFinite(value)) return "—";
  return kind === "buttons" ? String(value) : (Math.abs(value) < 0.005 ? "0.00" : value.toFixed(2));
}

export function createController(select, image, layer) {
  const artwork = image.parentElement;
  for (const type of ["contextmenu", "dragstart"]) {
    artwork.addEventListener(type, (event) => event.preventDefault());
  }
  let marks, data, enabled;
  let tracking = { left: false, right: false };

  function draw() {
    for (const [i, item] of controls.entries()) {
      const values = controlValues(enabled && tracking[item.side] ? data : null, item);
      const text = values.map((value) => format(value, item.key));
      const shown = values.some(Number.isFinite);
      const mark = marks[i];
      mark.path.dataset.live = mark.badge.dataset.live = String(shown);
      mark.path.dataset.positive = String(values.some((value) => Number.isFinite(value) && value > 0));
      for (const [index, node] of mark.text.entries()) {
        node.textContent = text.length === 2 ? `${index === 0 ? "X" : "Y"} ${text[index]}`
          : item.id.endsWith("-click") ? `Click ${text[0]}` : text[0];
      }
    }
  }

  function setDevice() {
    const device = devices[select.value] ?? devices.pico;
    image.src = device.render;
    // The two checked-in SVGs contain only static paths. All initial paths are hidden by CSS.
    const svg = new DOMParser().parseFromString(device.mask, "image/svg+xml").documentElement;
    layer.replaceChildren(document.importNode(svg, true));
    const liveSvg = layer.firstElementChild;
    const paths = liveSvg.querySelectorAll("path");
    marks = controls.map((item, i) => createBadge(liveSvg, paths[device.paths[i]], ...device.labels[i], item));
    draw();
  }

  select.addEventListener("change", setDevice);
  setDevice();
  return {
    update(next, visible, nextTracking) {
      if (data === next && enabled === visible && tracking.left === nextTracking.left && tracking.right === nextTracking.right) return;
      data = next;
      enabled = visible;
      tracking = nextTracking;
      draw();
    },
  };
}

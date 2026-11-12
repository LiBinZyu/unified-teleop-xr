import { fieldsFor, valueOf } from "./protocol.js";

const colors = ["#79beff", "#87e9a7", "#ffb37c", "#f18bd8", "#fae480", "#b4a0ff", "#75e5df", "#f28f93"];
const maxAge = 120_000;
const cap = 3_700;
const fieldKey = (topic, field) => `${topic}:${field.key ?? ""}:${field.index ?? ""}`;
const format = (value) => Math.abs(value) >= 10_000 || (value && Math.abs(value) < .001)
  ? value.toExponential(2) : Number(value.toFixed(3)).toString();

export function createMonitor(get) {
  const panel = get("monitor");
  const toggle = get("monitor-toggle");
  const list = get("stream-list");
  const search = get("stream-filter");
  const canvas = get("plot");
  const ctx = canvas.getContext("2d");
  const pause = get("pause-plot");
  const timeRange = get("time-range");
  const dataMin = get("data-min");
  const dataMax = get("data-max");
  const legend = get("plot-legend");
  const cursor = get("plot-cursor");
  const history = new Map();
  const selected = new Map();
  const groups = new Map();
  let active = new Map();
  let pausedAt = null;
  let pausedHistory = null;
  let nextColor = 0;
  let seeded = false;
  let cursorX = null;

  function setOpen(value) {
    if (value === !panel.hidden) return;
    panel.hidden = !value;
    toggle.setAttribute("aria-pressed", String(value));
    if (value) draw();
  }

  toggle.addEventListener("click", () => setOpen(panel.hidden));
  panel.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    setOpen(false);
    toggle.focus();
  });
  pause.addEventListener("click", () => {
    pausedAt = pausedAt === null ? performance.now() : null;
    pausedHistory = pausedAt === null ? null : new Map(
      [...history].map(([topic, samples]) => [topic, samples.slice()]),
    );
    pause.setAttribute("aria-pressed", String(pausedAt !== null));
    pause.textContent = pausedAt === null ? "Pause" : "Resume";
    draw();
  });

  function updateLegend() {
    legend.replaceChildren();
    for (const [id, series] of selected) {
      const button = document.createElement("button");
      button.type = "button";
      button.title = `Remove /xr/${series.topic} · ${series.field.label}`;
      const swatch = document.createElement("i");
      swatch.style.background = series.color;
      const prefix = series.topic.replace("/pose", "").replace("/controller_pose", " · controller");
      button.append(swatch, `${prefix} · ${series.field.label} ×`);
      button.addEventListener("click", () => {
        selected.delete(id);
        syncGroups();
        updateLegend();
        draw();
      });
      legend.append(button);
    }
  }

  function addSeries(topic, field) {
    const id = fieldKey(topic, field);
    if (!selected.has(id)) {
      selected.set(id, { topic, field, color: colors[nextColor++ % colors.length] });
    }
  }

  function fillGroup(topic, group, filter) {
    if (!group.details.open && !filter) return;
    group.fields.replaceChildren();
    for (const field of fieldsFor(topic)) {
      if (filter && !topic.toLowerCase().includes(filter) && !field.label.toLowerCase().includes(filter)) continue;
      const key = fieldKey(topic, field);
      const label = document.createElement("label");
      const box = document.createElement("input");
      box.type = "checkbox";
      box.checked = selected.has(key);
      box.disabled = !active.has(topic) && !box.checked;
      box.addEventListener("change", () => {
        if (box.checked) addSeries(topic, field);
        else selected.delete(key);
        if (!active.has(topic)) {
          box.disabled = !box.checked;
          if (![...selected.values()].some((series) => series.topic === topic)) syncGroups();
        }
        updateLegend();
        draw();
      });
      label.append(box, field.label);
      group.fields.append(label);
    }
  }

  function syncGroups() {
    const filter = search.value.trim().toLowerCase();
    const wanted = new Set([...active.keys(), ...[...selected.values()].map(({ topic }) => topic)]);
    for (const [topic, group] of groups) {
      if (wanted.has(topic)) continue;
      group.details.remove();
      groups.delete(topic);
    }
    let shown = 0;
    for (const topic of [...wanted].sort()) {
      if (!groups.has(topic)) {
        const details = document.createElement("details");
        details.className = "stream-group";
        const summary = document.createElement("summary");
        const fields = document.createElement("div");
        const group = { details, summary, fields };
        details.append(summary, fields);
        details.addEventListener("toggle", () => {
          if (details.open && !fields.childElementCount) fillGroup(topic, group, search.value.trim().toLowerCase());
        });
        groups.set(topic, group);
        list.append(details);
      }
      const group = groups.get(topic);
      const live = active.has(topic);
      const matches = !filter || `/xr/${topic}`.toLowerCase().includes(filter)
        || fieldsFor(topic).some((field) => field.label.toLowerCase().includes(filter));
      group.details.hidden = !matches;
      group.details.dataset.live = String(live);
      group.summary.textContent = `/xr/${topic}${live ? "" : " · Offline"}`;
      if (matches) {
        shown++;
        if (filter) group.details.open = true;
        fillGroup(topic, group, filter.startsWith("/xr/") ? filter.slice(4) : filter);
      }
    }
    get("no-streams").hidden = shown > 0;
    get("no-streams").textContent = filter ? "No matches" : "No live streams";
    get("stream-count").textContent = `${active.size} live`;
  }

  function trim(now, samples) {
    if (samples.length < cap && now - samples[0].t < maxAge + 2_000) return;
    let first = 0;
    const limit = now - maxAge - 500;
    while (first < samples.length - 1 && samples[first].t < limit) first++;
    samples.splice(0, Math.max(first, samples.length - cap + 150, 0));
  }

  function ingest(streams, now) {
    const previous = active;
    active = streams;
    let changed = previous.size !== active.size;
    for (const topic of previous.keys()) {
      if (!active.has(topic)) {
        history.get(topic)?.push({ t: now, data: null });
        changed = true;
      }
    }
    for (const [topic, sample] of streams) {
      if (previous.get(topic) === sample) continue;
      changed ||= !previous.has(topic);
      if (!history.has(topic)) history.set(topic, []);
      const samples = history.get(topic);
      samples.push({ t: now, data: sample.data });
      trim(now, samples);
    }
    for (const [topic, samples] of history) {
      if (!active.has(topic) && now - samples.at(-1).t > maxAge + 2_000) history.delete(topic);
    }
    if (!seeded && active.size) {
      seeded = true;
      const topic = active.has("head/pose") ? "head/pose" : active.keys().next().value;
      for (const field of fieldsFor(topic).slice(0, 3)) addSeries(topic, field);
      updateLegend();
    }
    if (changed) syncGroups();
  }

  function getRange() {
    const time = Math.min(120, Math.max(1, timeRange.valueAsNumber || 15));
    const min = dataMin.valueAsNumber;
    const max = dataMax.valueAsNumber;
    const invalid = Number.isFinite(min) && Number.isFinite(max) && min >= max;
    for (const input of [dataMin, dataMax]) input.setAttribute("aria-invalid", String(invalid));
    get("range-error").hidden = !invalid;
    return { time, min: invalid ? NaN : min, max: invalid ? NaN : max };
  }

  function draw() {
    if (panel.hidden || !ctx) return;
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    if (w < 90 || h < 65) return;
    const dpr = Math.min(devicePixelRatio || 1, 2);
    const pixelWidth = Math.round(w * dpr);
    const pixelHeight = Math.round(h * dpr);
    if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
      canvas.width = pixelWidth;
      canvas.height = pixelHeight;
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    const range = getRange();
    const end = pausedAt ?? performance.now();
    const start = end - range.time * 1_000;
    const box = { left: 56, right: w - 12, top: 12, bottom: h - 24 };
    const spanX = box.right - box.left;
    const spanY = box.bottom - box.top;
    const points = [];
    let low = Infinity;
    let high = -Infinity;
    for (const series of selected.values()) {
      const data = [];
      for (const sample of (pausedHistory ?? history).get(series.topic) ?? []) {
        if (sample.t < start) continue;
        if (sample.t > end) break;
        const value = valueOf(sample.data, series.field);
        if (Number.isFinite(value)) {
          low = Math.min(low, value);
          high = Math.max(high, value);
          data.push([sample.t, value]);
        } else {
          data.push(null);
        }
      }
      points.push({ ...series, data });
    }
    const hasPoints = Number.isFinite(low);
    const pad = hasPoints ? Math.max(Math.abs(high - low) * 0.08, Math.abs(low) * 0.015, .001) : 1;
    const floor = Number.isFinite(range.min) ? range.min : hasPoints ? low - pad : -1;
    const ceil = Number.isFinite(range.max) ? range.max : hasPoints ? high + pad : 1;
    const yMin = floor < ceil ? floor : Number.isFinite(range.min) ? floor : ceil - 1;
    const yMax = floor < ceil ? ceil : Number.isFinite(range.max) ? ceil : floor + 1;
    const xOf = (t) => box.left + ((t - start) / (end - start)) * spanX;
    const yOf = (value) => box.bottom - ((value - yMin) / (yMax - yMin)) * spanY;

    ctx.font = "11px Segoe UI, sans-serif";
    ctx.lineWidth = 1;
    for (let i = 0; i <= 4; i++) {
      const ratio = i / 4;
      const y = box.top + ratio * spanY;
      const x = box.left + ratio * spanX;
      ctx.strokeStyle = "#ffffff19";
      ctx.beginPath();
      ctx.moveTo(box.left, y);
      ctx.lineTo(box.right, y);
      ctx.moveTo(x, box.top);
      ctx.lineTo(x, box.bottom);
      ctx.stroke();
      ctx.fillStyle = "#ffffffa6";
      ctx.textAlign = "right";
      ctx.fillText(format(yMax - ratio * (yMax - yMin)), box.left - 7, y + 3);
      ctx.textAlign = i === 4 ? "right" : "center";
      ctx.fillText(`${format((ratio - 1) * range.time)}s`, x, box.bottom + 16);
    }
    ctx.save();
    ctx.beginPath();
    ctx.rect(box.left, box.top, spanX, spanY);
    ctx.clip();
    for (const series of points) {
      ctx.strokeStyle = ctx.fillStyle = series.color;
      ctx.lineWidth = 1.5;
      let previous = null;
      let last = null;
      ctx.beginPath();
      for (const point of series.data) {
        if (!point) {
          previous = null;
          continue;
        }
        const x = xOf(point[0]);
        const y = yOf(point[1]);
        if (previous) ctx.lineTo(x, y);
        else ctx.moveTo(x, y);
        previous = point;
        last = [x, y];
      }
      ctx.stroke();
      if (last) {
        ctx.beginPath();
        ctx.arc(...last, 2, 0, Math.PI * 2);
        ctx.fill();
      }
    }
    const hover = cursorX !== null && cursorX >= box.left && cursorX <= box.right && hasPoints;
    if (hover) {
      ctx.strokeStyle = "#ffffff73";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(cursorX, box.top);
      ctx.lineTo(cursorX, box.bottom);
      ctx.stroke();
    }
    ctx.restore();
    if (hover) {
      const target = start + ((cursorX - box.left) / spanX) * (end - start);
      const lines = [`${((target - end) / 1000).toFixed(2)} s`];
      for (const series of points.slice(0, 6)) {
        let nearest = null;
        for (const point of series.data) {
          if (point && (!nearest || Math.abs(point[0] - target) < Math.abs(nearest[0] - target))) nearest = point;
        }
        if (nearest && Math.abs(nearest[0] - target) < Math.max(150, range.time * 9)) {
          lines.push(`${series.topic} · ${series.field.label} = ${format(nearest[1])}`);
        }
      }
      cursor.textContent = lines.join("\n");
    }
    cursor.hidden = !hover;
    get("plot-empty").hidden = hasPoints;
    get("plot-empty").textContent = selected.size ? "No data in range" : "Select fields to plot";
  }

  canvas.addEventListener("pointermove", (event) => { cursorX = event.offsetX; draw(); });
  canvas.addEventListener("pointerleave", () => { cursorX = null; draw(); });
  search.addEventListener("input", syncGroups);
  for (const input of [timeRange, dataMin, dataMax]) input.addEventListener("input", draw);
  setInterval(draw, 80);

  return { ingest };
}

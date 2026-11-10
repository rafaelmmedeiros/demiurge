// DEMIURGE dashboard: presentation only. It renders the WorldView pushed by the server every tick
// and sends god-mode commands over HTTP. It never changes the world itself.
// Concept: Dashboard and state streaming — docs/CONCEPTS.md#dashboard-e-streaming-de-estado
"use strict";

// ---------------------------------------------------------------- i18n
// Every user-facing string comes from i18n/<lang>.json. Domain identifiers (event types, pain
// codes, places, reasons) stay English in data and are mapped to labels here.
const i18n = {
  lang: "en",
  dict: {},
  async load(lang) {
    const res = await fetch(`i18n/${lang}.json`);
    this.dict = await res.json();
    this.lang = lang;
    document.documentElement.lang = lang;
    try { localStorage.setItem("demiurge.lang", lang); } catch { /* storage may be blocked */ }
  },
  initial() {
    let saved = null;
    try { saved = localStorage.getItem("demiurge.lang"); } catch { /* ignore */ }
    if (saved === "pt-BR" || saved === "en") return saved;
    return (navigator.language || "en").toLowerCase().startsWith("pt") ? "pt-BR" : "en";
  },
};

/** Translate a key, filling {placeholders}. Unknown keys fall back to the raw identifier. */
function t(key, params = {}) {
  const template = i18n.dict[key] ?? key.split(".").slice(1).join(".");
  return template.replace(/\{(\w+)\}/g, (_, name) => (params[name] ?? `{${name}}`));
}

function applyStaticTexts() {
  document.querySelectorAll("[data-i18n]").forEach(el => { el.textContent = t(el.dataset.i18n); });
}

// ---------------------------------------------------------------- state
const state = { view: null, prevPositions: new Map(), animStart: 0, feed: [] };
const FEED_MAX = 50;
const NOISY = new Set(["TickStarted", "TickEnded", "NeedsUpdated", "TravelProgressed", "MarketRestocked"]);

const $ = id => document.getElementById(id);
const agentLabel = id => t("ui.agentName", { id });
const placeLabel = id => t(`place.${id}`);

// ---------------------------------------------------------------- events → text
const AGENT_FIELDS = ["agentId", "thief", "victim", "speaker", "listener", "witness"];
const PLACE_FIELDS = ["place", "from", "to"];

function describeEvent(e) {
  const p = { ...e };
  for (const f of AGENT_FIELDS) if (f in p) p[f] = agentLabel(p[f]);
  for (const f of PLACE_FIELDS) if (f in p) p[f] = placeLabel(p[f]);
  if (p.reason) p.reason = t(`reason.${p.reason}`);
  if (p.cause) p.cause = t(`cause.${p.cause}`);
  if (p.item) p.item = t(`item.${p.item}`);
  if (p.action) p.action = t(`action.${p.action.$type}`);
  return t(`event.${e.$type}`, p);
}

function eventClass(type) {
  if (type === "Stole" || type === "TheftWitnessed" || type === "AgentDied") return "crime";
  if (type === "Talked") return "social";
  return "";
}

// ---------------------------------------------------------------- rendering
function renderHeader(v) {
  $("tick").textContent = v.tick;
  $("simTime").textContent = t("ui.simTime", { day: Math.floor(v.tick / 24) + 1, hour: String(v.tick % 24).padStart(2, "0") });
  const run = $("runState");
  run.dataset.state = v.paused ? "paused" : "running";
  run.textContent = t(v.paused ? "ui.paused" : "ui.running");
  $("btnToggle").textContent = t(v.paused ? "ui.resume" : "ui.pause");
}

function bar(value, color) {
  return `<span class="bar"><i style="width:${Math.max(0, Math.min(100, value))}%;background:${color}"></i></span>${value}`;
}

function renderAgents(v) {
  const rows = v.agents.map(a => {
    const where = a.place ? placeLabel(a.place)
      : t("ui.travelling", { from: placeLabel(a.from), to: placeLabel(a.to), left: a.ticksRemaining });
    const tr = document.createElement("tr");
    if (!a.alive) tr.className = "dead";
    tr.innerHTML = `<td></td><td></td><td>${bar(a.hunger, "var(--bad)")}</td><td>${bar(a.energy, "var(--ok)")}</td><td>${a.food}</td><td>${a.money}</td>`;
    tr.children[0].textContent = agentLabel(a.id) + (a.alive ? "" : ` (${t("ui.dead")})`);
    tr.children[1].textContent = where;
    return tr;
  });
  $("agentRows").replaceChildren(...rows);
  $("marketLine").textContent = t("ui.market", { stock: v.market.foodStock, price: v.market.foodPrice });
}

const SEV_COLORS = ["#8b93a3", "#9ece6a", "#e0af68", "#ff9e64", "#f7768e", "#ff5370"];

function renderPains(v) {
  const items = v.pains.map(p => {
    const li = document.createElement("li");
    li.innerHTML = `<span class="sev"></span> <strong></strong> <span class="code"></span><br><span class="meta muted"></span><div class="sample"></div>`;
    const sev = li.querySelector(".sev");
    sev.textContent = p.severity;
    sev.style.background = SEV_COLORS[p.severity] ?? SEV_COLORS[0];
    li.querySelector("strong").textContent = t(`pain.${p.code}`);
    li.querySelector(".code").textContent = p.code;
    li.querySelector(".meta").textContent = t("ui.painMeta", {
      count: p.count, first: p.firstTick, last: p.lastTick, status: t(`status.${p.status}`), category: t(`category.${p.category}`),
    });
    li.querySelector(".sample").textContent = p.sample; // harness text, English
    return li;
  });
  $("painList").replaceChildren(...items);
  $("noPains").hidden = items.length > 0;
}

function renderFeed() {
  const showAll = $("showAll").checked;
  const items = state.feed
    .filter(e => showAll || !NOISY.has(e.$type))
    .slice(-FEED_MAX)
    .reverse()
    .map(e => {
      const li = document.createElement("li");
      li.className = eventClass(e.$type);
      const time = document.createElement("span");
      time.className = "t";
      time.textContent = e.tick;
      li.append(time, document.createTextNode(describeEvent(e))); // textContent: agent text is never HTML
      return li;
    });
  $("feed").replaceChildren(...items);
}

function pushEvents(events) {
  state.feed.push(...events);
  if (state.feed.length > 2000) state.feed.splice(0, state.feed.length - 2000);
}

// ---------------------------------------------------------------- map (canvas)
const canvas = $("map");
const ctx = canvas.getContext("2d");
const PAD = 40;

function toCanvas(x, y) {
  const w = canvas.clientWidth, h = canvas.clientHeight;
  return [PAD + (x / 100) * (w - 2 * PAD), PAD + (y / 100) * (h - 2 * PAD)];
}

/** Target position of every agent for the current view: at a place (in a small ring) or along an edge. */
function targetPositions(v) {
  const places = new Map(v.places.map(p => [p.id, p]));
  const byPlace = new Map();
  for (const a of v.agents) if (a.alive && a.place) byPlace.set(a.place, [...(byPlace.get(a.place) ?? []), a.id]);

  const out = new Map();
  for (const a of v.agents) {
    if (!a.alive) continue;
    if (a.place) {
      const p = places.get(a.place);
      const group = byPlace.get(a.place);
      const i = group.indexOf(a.id), n = group.length;
      const angle = (2 * Math.PI * i) / Math.max(n, 1);
      const r = n > 1 ? 7 : 0;
      out.set(a.id, [p.x + r * Math.cos(angle) * 0.6, p.y + r * Math.sin(angle)]);
    } else {
      const from = places.get(a.from), to = places.get(a.to);
      const progress = a.travelTicks > 0 ? (a.travelTicks - a.ticksRemaining) / a.travelTicks : 0;
      out.set(a.id, [from.x + (to.x - from.x) * progress, from.y + (to.y - from.y) * progress]);
    }
  }
  return out;
}

function resizeCanvas() {
  const dpr = window.devicePixelRatio || 1;
  canvas.width = canvas.clientWidth * dpr;
  canvas.height = canvas.clientHeight * dpr;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function draw(now) {
  const v = state.view;
  ctx.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
  if (v) {
    const places = new Map(v.places.map(p => [p.id, p]));
    ctx.lineWidth = 1;
    ctx.font = "11px system-ui";
    for (const e of v.edges) {
      const [x1, y1] = toCanvas(places.get(e.a).x, places.get(e.a).y);
      const [x2, y2] = toCanvas(places.get(e.b).x, places.get(e.b).y);
      ctx.strokeStyle = "#2b313d";
      ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
      ctx.fillStyle = "#8b93a3";
      ctx.fillText(`${e.ticks}h`, (x1 + x2) / 2 + 4, (y1 + y2) / 2 - 4);
    }
    for (const p of v.places) {
      const [x, y] = toCanvas(p.x, p.y);
      ctx.fillStyle = "#232937"; ctx.strokeStyle = "#7aa2f7";
      ctx.beginPath(); ctx.arc(x, y, 26, 0, 2 * Math.PI); ctx.fill(); ctx.stroke();
      ctx.fillStyle = "#e3e6ec"; ctx.font = "12px system-ui"; ctx.textAlign = "center";
      ctx.fillText(placeLabel(p.id), x, y + 42);
      ctx.textAlign = "start";
    }

    // Interpolate each pawn from its previous to its current position over one tick interval.
    const alpha = Math.min(1, (now - state.animStart) / Math.max(v.tickIntervalMs, 1));
    const targets = targetPositions(v);
    for (const [id, [tx, ty]] of targets) {
      const [px, py] = state.prevPositions.get(id) ?? [tx, ty];
      const [x, y] = toCanvas(px + (tx - px) * alpha, py + (ty - py) * alpha);
      ctx.fillStyle = `hsl(${(id * 67) % 360} 70% 60%)`;
      ctx.beginPath(); ctx.arc(x, y, 8, 0, 2 * Math.PI); ctx.fill();
      ctx.fillStyle = "#111"; ctx.font = "bold 10px system-ui"; ctx.textAlign = "center";
      ctx.fillText(String(id), x, y + 3.5);
      ctx.textAlign = "start";
    }
  }
  requestAnimationFrame(draw);
}

// ---------------------------------------------------------------- data flow
function currentPositions() {
  // Where pawns are drawn right now: becomes the start point of the next animation.
  const v = state.view;
  if (!v) return new Map();
  const alpha = Math.min(1, (performance.now() - state.animStart) / Math.max(v.tickIntervalMs, 1));
  const out = new Map();
  for (const [id, [tx, ty]] of targetPositions(v)) {
    const [px, py] = state.prevPositions.get(id) ?? [tx, ty];
    out.set(id, [px + (tx - px) * alpha, py + (ty - py) * alpha]);
  }
  return out;
}

function applyView(view, events) {
  const sameWorld = state.view && state.view.worldId === view.worldId;
  state.prevPositions = sameWorld ? currentPositions() : new Map();
  state.animStart = performance.now();
  if (!sameWorld) state.feed = [];
  state.view = view;
  if (events) pushEvents(events);
  render();
}

function render() {
  const v = state.view;
  if (!v) return;
  renderHeader(v);
  renderAgents(v);
  renderPains(v);
  renderFeed();
}

async function post(path, body) {
  const res = await fetch(path, {
    method: "POST",
    headers: body ? { "Content-Type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${path}: ${res.status}`);
  return res.json();
}

async function loadInitial() {
  const view = await (await fetch("api/world")).json();
  const events = await (await fetch(`api/events?sinceTick=${Math.max(0, view.tick - 30)}&limit=1000`)).json();
  applyView(view, events);
  $("seed").value = view.seed;
}

function setConnection(stateName) {
  const el = $("connection");
  el.dataset.state = stateName;
  el.textContent = t(`ui.${stateName}`);
}

async function main() {
  await i18n.load(i18n.initial());
  $("lang").value = i18n.lang;
  applyStaticTexts();
  resizeCanvas();
  window.addEventListener("resize", resizeCanvas);
  requestAnimationFrame(draw);

  $("lang").addEventListener("change", async e => { await i18n.load(e.target.value); applyStaticTexts(); render(); setConnection($("connection").dataset.state); });
  $("showAll").addEventListener("change", renderFeed);
  $("btnToggle").addEventListener("click", async () => applyView(await post(`api/god/${state.view?.paused ? "resume" : "pause"}`)));
  $("btnStep").addEventListener("click", async () => { await post("api/god/step"); });
  $("newWorld").addEventListener("submit", async e => {
    e.preventDefault();
    applyView(await post("api/god/worlds", { seed: Number($("seed").value), agentCount: Number($("agents").value) }), []);
  });

  const connection = new signalR.HubConnectionBuilder().withUrl("hubs/world").withAutomaticReconnect().build();
  connection.on("tick", msg => applyView(msg.world, msg.events));
  connection.onreconnecting(() => setConnection("connecting"));
  connection.onreconnected(() => { setConnection("connected"); loadInitial(); });
  connection.onclose(() => setConnection("disconnected"));

  setConnection("connecting");
  await connection.start();
  setConnection("connected");
  await loadInitial();
}

main().catch(err => { console.error(err); setConnection("disconnected"); });

import * as THREE from "three";
import * as OBC from "@thatopen/components";
import * as FRAGS from "@thatopen/fragments";

// ── State ──
let components;
let world;
let fragments;
let ifcLoader;
let hider;

const status = document.getElementById("status");
const container = document.getElementById("container");

// ── Scene Setup ──
async function initScene() {
  components = new OBC.Components();
  const worlds = components.get(OBC.Worlds);
  world = worlds.create();

  world.scene = new OBC.SimpleScene(components);
  world.scene.setup();
  world.scene.three.background = null;

  world.renderer = new OBC.SimpleRenderer(components, container);
  world.camera = new OBC.OrthoPerspectiveCamera(components);

  components.init();
  components.get(OBC.Grids).create(world);

  // IFC loader
  ifcLoader = components.get(OBC.IfcLoader);
  await ifcLoader.setup({
    autoSetWasm: false,
    wasm: {
      path: "https://unpkg.com/web-ifc@0.0.75/",
      absolute: true,
    },
  });

  // Fragments
  fragments = components.get(OBC.FragmentsManager);
  const workerUrl = "https://thatopen.github.io/engine_fragment/resources/worker.mjs";
  const fetched = await fetch(workerUrl);
  const blob = await fetched.blob();
  const file = new File([blob], "worker.mjs", { type: "text/javascript" });
  const url = URL.createObjectURL(file);
  fragments.init(url);

  world.camera.controls.addEventListener("update", () => fragments.core.update());
  fragments.list.onItemSet.add(({ value: model }) => {
    model.useCamera(world.camera.three);
    world.scene.three.add(model.object);
    fragments.core.update(true);
  });

  // Hider for isolate/reset
  hider = components.get(OBC.Hider);
}

// ── Load IFC Model ──
async function loadModel() {
  status.textContent = "Loading model...";
  try {
    const response = await fetch("/model.ifc");
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.arrayBuffer();
    const buffer = new Uint8Array(data);
    await ifcLoader.load(buffer, false, "model");
    status.textContent = "Model loaded.";
  } catch (err) {
    status.textContent = `Error loading model: ${err.message}`;
    console.error(err);
  }
}

// ── WebSocket ──
function connectWebSocket() {
  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  const ws = new WebSocket(`${protocol}//${location.host}/ws`);

  ws.onopen = () => console.log("WebSocket connected");
  ws.onclose = () => {
    console.log("WebSocket disconnected, reconnecting in 2s...");
    setTimeout(connectWebSocket, 2000);
  };
  ws.onerror = (err) => console.error("WebSocket error:", err);

  ws.onmessage = (event) => {
    try {
      const msg = JSON.parse(event.data);
      handleCommand(msg);
    } catch (err) {
      console.error("Invalid WebSocket message:", err);
    }
  };
}

// ── Command Handlers ──
function handleCommand(msg) {
  switch (msg.action) {
    case "highlight":
      highlightElements(msg.globalIds);
      break;
    case "isolate":
      isolateElements(msg.globalIds);
      break;
    case "reset":
      resetView();
      break;
    case "camera-fit":
      fitCamera(msg.globalIds);
      break;
    case "reload":
      reloadModel();
      break;
    default:
      console.warn("Unknown action:", msg.action);
  }
}

function findFragmentIdsByGlobalIds(globalIds) {
  const idSet = new Set(globalIds);
  const selectionMap = new Map();

  for (const [, model] of fragments.list) {
    if (!model.object) continue;
    model.object.traverse((child) => {
      if (!child.isMesh) return;
      const frag = child;
      if (!frag.fragment) return;
      for (const [itemId, expressIds] of frag.fragment.ids) {
        if (idSet.has(itemId)) {
          if (!selectionMap.has(frag.fragment.id)) {
            selectionMap.set(frag.fragment.id, new Set());
          }
          for (const eid of expressIds) {
            selectionMap.get(frag.fragment.id).add(eid);
          }
        }
      }
    });
  }
  return selectionMap;
}

function highlightElements(globalIds) {
  // Reset first, then dim everything except the highlighted elements.
  // This uses the same isolate mechanism as a fallback — a proper color
  // overlay (e.g., tinted material) can replace this once the fragment
  // color API is confirmed at runtime.
  resetView();
  if (!globalIds || globalIds.length === 0) return;

  const selectionMap = findFragmentIdsByGlobalIds(globalIds);
  if (selectionMap.size > 0) {
    hider.set(false);
    hider.set(true, selectionMap);
  }
  status.textContent = `Highlighted ${globalIds.length} element(s)`;
}

function isolateElements(globalIds) {
  if (!globalIds || globalIds.length === 0) return;
  hider.set(false);
  const selectionMap = findFragmentIdsByGlobalIds(globalIds);
  hider.set(true, selectionMap);
  status.textContent = `Isolated ${globalIds.length} element(s)`;
}

function resetView() {
  hider.set(true);
  status.textContent = "View reset.";
}

async function fitCamera(globalIds) {
  if (!globalIds || globalIds.length === 0) return;
  const selectionMap = findFragmentIdsByGlobalIds(globalIds);
  const box = new THREE.Box3();
  for (const [fragId, expressIds] of selectionMap) {
    world.scene.three.traverse((child) => {
      if (!child.isMesh || !child.fragment) return;
      if (child.fragment.id === fragId) {
        const meshBox = new THREE.Box3().setFromObject(child);
        box.union(meshBox);
      }
    });
  }
  if (!box.isEmpty()) {
    await world.camera.fit(box);
  }
  status.textContent = `Camera fitted to ${globalIds.length} element(s)`;
}

async function reloadModel() {
  status.textContent = "Reloading model...";
  try {
    await fragments.disposeModel("model");
  } catch { /* ignore if no model */ }
  await loadModel();
}

// ── Init ──
async function main() {
  await initScene();
  await loadModel();
  connectWebSocket();
}

main().catch(console.error);

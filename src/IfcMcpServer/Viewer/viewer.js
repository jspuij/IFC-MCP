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

// Build a modelIdMap { modelId: localIds } from GlobalIds
// This is the format hider.set() and hider.isolate() expect.
async function buildModelIdMap(globalIds) {
  const modelIdMap = {};
  for (const [modelId, model] of fragments.list) {
    const localIds = await model.getLocalIdsByGuids(globalIds);
    if (localIds && (localIds.size > 0 || localIds.length > 0)) {
      modelIdMap[modelId] = localIds;
    }
  }
  return modelIdMap;
}

async function highlightElements(globalIds) {
  await resetView();
  if (!globalIds || globalIds.length === 0) return;

  const selectedMap = await buildModelIdMap(globalIds);
  if (Object.keys(selectedMap).length === 0) return;

  // setColor(localIds: number[] | undefined, color: Color)
  // resetColor(localIds: number[] | undefined)
  const dimColor = new THREE.Color(0.5, 0.5, 0.5);
  for (const [modelId, model] of fragments.list) {
    // Dim all elements
    await model.setColor(undefined, dimColor);
    // Reset color on selected elements so they stand out
    if (selectedMap[modelId]) {
      const localIds = selectedMap[modelId];
      const idsArray = Array.isArray(localIds) ? localIds : [...localIds];
      await model.resetColor(idsArray);
    }
  }
  status.textContent = `Highlighted ${globalIds.length} element(s)`;
}

async function isolateElements(globalIds) {
  if (!globalIds || globalIds.length === 0) return;

  const modelIdMap = await buildModelIdMap(globalIds);
  if (Object.keys(modelIdMap).length > 0) {
    await hider.isolate(modelIdMap);
  }
  status.textContent = `Isolated ${globalIds.length} element(s)`;
}

async function resetView() {
  await hider.set(true);
  for (const [, model] of fragments.list) {
    // resetColor(undefined) resets all items
    await model.resetColor(undefined);
  }
  status.textContent = "View reset.";
}

async function fitCamera(globalIds) {
  if (!globalIds || globalIds.length === 0) return;

  const modelIdMap = await buildModelIdMap(globalIds);
  // Compute bounding box from all visible meshes of the target elements
  const box = new THREE.Box3();
  for (const [modelId, model] of fragments.list) {
    if (!modelIdMap[modelId] || !model.object) continue;
    model.object.traverse((child) => {
      if (!child.isMesh || !child.visible) return;
      const meshBox = new THREE.Box3().setFromObject(child);
      box.union(meshBox);
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

// Minimal WebGPU canvas module for Blazor WebAssembly
// All business logic is in C# - this file only handles WebGPU API calls

// ============================================================================
// Constants & Shaders
// ============================================================================

const FRAME_BUFFER_SIZE = Float32Array.BYTES_PER_ELEMENT * 36; // projection + view matrices + camera position

let gpuOperation = Promise.resolve();

function enqueueGpuOperation(operation) {
    const next = gpuOperation.then(operation);
    gpuOperation = next.catch(() => { });
    return next;
}

function requireDevice(operation) {
    if (!device) {
        throw new Error(`WebGPU device is unavailable during ${operation}.`);
    }
    return device;
}

function notifyDotNet(method, ...args) {
    const callback = dotNetRef?.invokeMethodAsync(method, ...args);
    callback?.catch(error => {
        if (!isDisposing) console.error(`BugViewer callback ${method} failed:`, error);
    });
}

// WGSL Shaders (moved to top for clarity)
const GRID_SHADER = `
  fn PristineGrid(uv: vec2f, lineWidth: vec2f) -> f32 {
      let uvDDXY = vec4f(dpdx(uv), dpdy(uv));
      let uvDeriv = vec2f(length(uvDDXY.xz), length(uvDDXY.yw));
      let invertLine: vec2<bool> = lineWidth > vec2f(0.5);
      let targetWidth: vec2f = select(lineWidth, 1 - lineWidth, invertLine);
      let drawWidth: vec2f = clamp(targetWidth, uvDeriv, vec2f(0.5));
      let lineAA: vec2f = uvDeriv * 1.5;
      var gridUV: vec2f = abs(fract(uv) * 2.0 - 1.0);
      gridUV = select(1 - gridUV, gridUV, invertLine);
      var grid2: vec2f = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);
      grid2 *= saturate(targetWidth / drawWidth);
      grid2 = mix(grid2, targetWidth, saturate(uvDeriv * 2.0 - 1.0));
      grid2 = select(grid2, 1.0 - grid2, invertLine);
      return mix(grid2.x, 1.0, grid2.y);
  }
  struct VertexIn { @location(0) pos: vec3f, @location(1) uv: vec2f }
  struct VertexOut { @builtin(position) pos: vec4f, @location(0) uv: vec2f }
  struct Camera { projection: mat4x4f, view: mat4x4f, cameraPosition: vec3f }
  @group(0) @binding(0) var<uniform> camera: Camera;
  struct GridArgs { lineColor: vec4f, baseColor: vec4f, lineWidth: vec2f, spacing: f32 }
  @group(1) @binding(0) var<uniform> gridArgs: GridArgs;
  @vertex fn vertexMain(in: VertexIn) -> VertexOut { var out: VertexOut; out.pos = camera.projection * camera.view * vec4f(in.pos, 1.0); out.uv = in.uv - vec2f(50.0, 50.0); return out; }
  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f { var grid = PristineGrid(in.uv * gridArgs.spacing, gridArgs.lineWidth); return mix(gridArgs.baseColor, gridArgs.lineColor, grid); }
`;

const BACKGROUND_GRADIENT_SHADER = `
  struct BackgroundGradient {
    color0: vec4f,
    color1: vec4f,
    color2: vec4f,
    color3: vec4f,
    angles: vec4f,
    cameraPolarAngle: f32,
    verticalSpan: f32,
    padding: vec2f
  }
  @group(0) @binding(0) var<uniform> gradient: BackgroundGradient;

  struct VertexOut {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f
  }

  @vertex fn vertexMain(@builtin(vertex_index) vertexIndex: u32) -> VertexOut {
    let positions = array<vec2f, 6>(
      vec2f(-1.0, -1.0), vec2f(1.0, -1.0), vec2f(-1.0, 1.0),
      vec2f(-1.0, 1.0), vec2f(1.0, -1.0), vec2f(1.0, 1.0));
    let position = positions[vertexIndex];
    var out: VertexOut;
    out.position = vec4f(position, 0.0, 1.0);
    out.uv = position * 0.5 + vec2f(0.5);
    return out;
  }

  fn blend(startColor: vec4f, endColor: vec4f, startAngle: f32, endAngle: f32, polarAngle: f32) -> vec4f {
    let amount = clamp((polarAngle - startAngle) / max(endAngle - startAngle, 0.00001), 0.0, 1.0);
    return mix(startColor, endColor, amount);
  }

  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    // OrbitCamera.PolarAngle describes the camera's position around the target,
    // so the center viewing ray has the opposite polar angle. WebGPU screen UV
    // increases toward the top, where the visible ray has the higher angle.
    let polarAngle = -gradient.cameraPolarAngle + (in.uv.y - 0.5) * gradient.verticalSpan;
    var color: vec4f;
    if (polarAngle <= gradient.angles.x) {
      color = gradient.color0;
    } else if (polarAngle <= gradient.angles.y) {
      color = blend(gradient.color0, gradient.color1, gradient.angles.x, gradient.angles.y, polarAngle);
    } else if (polarAngle <= gradient.angles.z) {
      color = blend(gradient.color1, gradient.color2, gradient.angles.y, gradient.angles.z, polarAngle);
    } else if (polarAngle <= gradient.angles.w) {
      color = blend(gradient.color2, gradient.color3, gradient.angles.z, gradient.angles.w, polarAngle);
    } else {
      color = gradient.color3;
    }
    return vec4f(color.rgb, 1.0);
  }
`;

const MESH_SHADER = `
  struct Camera { projection: mat4x4f, view: mat4x4f, cameraPosition: vec3f }
  @group(0) @binding(0) var<uniform> camera: Camera;

  struct LightUniforms {
    lightDir: vec3f,
    ambient: f32,
    specularPower: f32,
    headlampIntensity: f32,
    directionalLightIntensity: f32,
    headlampFocus: f32
  }
  @group(1) @binding(0) var<uniform> light: LightUniforms;

  struct MeshUniforms { color: vec4f }
  @group(1) @binding(1) var<uniform> meshUniforms: MeshUniforms;

  struct VertexIn { @location(0) pos: vec3f, @location(1) primitiveSurfaceNormal: vec3f }
  struct VertexOut {
    @builtin(position) pos: vec4f,
    @location(0) worldPos: vec3f,
    @location(1) primitiveSurfaceNormal: vec3f
  }

  @vertex fn vertexMain(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    out.pos = camera.projection * camera.view * vec4f(in.pos, 1.0);
    out.worldPos = in.pos;
    out.primitiveSurfaceNormal = in.primitiveSurfaceNormal;
    return out;
  }

  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    var normal = normalize(cross(dpdx(in.worldPos), dpdy(in.worldPos)));
    if (dot(in.primitiveSurfaceNormal, in.primitiveSurfaceNormal) > 1e-12) {
      normal = normalize(in.primitiveSurfaceNormal);
    }
    let lightDir = normalize(light.lightDir);

    let viewDir = normalize(camera.cameraPosition - in.worldPos);
    let halfDir = normalize(lightDir + viewDir);

    // Diffuse
    let diffuse = light.directionalLightIntensity * max(dot(normal, lightDir), 0.0);
    // Specular
    let specAngle = max(dot(normal, halfDir), 0.0);
    let specular = light.directionalLightIntensity * pow(specAngle, light.specularPower);
    // Mesh winding can reverse derivative normals, but a viewer headlamp should illuminate the rendered face.
    let headlampAngle = abs(dot(normal, viewDir));
    let headlampDiffuse = light.headlampIntensity * pow(headlampAngle, light.headlampFocus);
    let headlampSpecular = light.headlampIntensity * pow(headlampAngle, light.specularPower);

    let finalColor = meshUniforms.color.rgb * (light.ambient + diffuse + headlampDiffuse)
      + vec3f(1.0) * (specular + headlampSpecular);
    return vec4f(finalColor, meshUniforms.color.a);
  }
`;

const MESH_SHADER_VERTEX_COLOR = `
  struct Camera { projection: mat4x4f, view: mat4x4f, cameraPosition: vec3f }
  @group(0) @binding(0) var<uniform> camera: Camera;

  struct LightUniforms {
    lightDir: vec3f,
    ambient: f32,
    specularPower: f32,
    headlampIntensity: f32,
    directionalLightIntensity: f32,
    headlampFocus: f32
  }
  @group(1) @binding(0) var<uniform> light: LightUniforms;

  struct VertexIn {
    @location(0) pos: vec3f,
    @location(1) color: vec4f,
    @location(2) primitiveSurfaceNormal: vec3f
  }
  struct VertexOut {
    @builtin(position) pos: vec4f,
    @location(0) worldPos: vec3f,
    @location(1) @interpolate(flat) color: vec4f,
    @location(2) primitiveSurfaceNormal: vec3f
  }
  @vertex fn vertexMain(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    out.pos = camera.projection * camera.view * vec4f(in.pos, 1.0);
    out.worldPos = in.pos;
    out.color = in.color;
    out.primitiveSurfaceNormal = in.primitiveSurfaceNormal;
    return out;
  }
  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    var normal = normalize(cross(dpdx(in.worldPos), dpdy(in.worldPos)));
    if (dot(in.primitiveSurfaceNormal, in.primitiveSurfaceNormal) > 1e-12) {
      normal = normalize(in.primitiveSurfaceNormal);
    }
    let lightDir = normalize(light.lightDir);

    let viewDir = normalize(camera.cameraPosition - in.worldPos);
    let halfDir = normalize(lightDir + viewDir);

    let diffuse = light.directionalLightIntensity * max(dot(normal, lightDir), 0.0);
    let specAngle = max(dot(normal, halfDir), 0.0);
    let specular = light.directionalLightIntensity * pow(specAngle, light.specularPower);
    // Mesh winding can reverse derivative normals, but a viewer headlamp should illuminate the rendered face.
    let headlampAngle = abs(dot(normal, viewDir));
    let headlampDiffuse = light.headlampIntensity * pow(headlampAngle, light.headlampFocus);
    let headlampSpecular = light.headlampIntensity * pow(headlampAngle, light.specularPower);

    let finalColor = in.color.rgb * (light.ambient + diffuse + headlampDiffuse)
      + vec3f(1.0) * (specular + headlampSpecular);
    return vec4f(finalColor, in.color.a);
  }
`;

const BILLBOARD_LINE_SHADER = `
  struct Camera { projection: mat4x4f, view: mat4x4f, cameraPosition: vec3f }
  @group(0) @binding(0) var<uniform> camera: Camera;
  struct VertexIn {
    @location(0) pos: vec3f,
    @location(1) color: vec4f,
    @location(2) thickness: f32,
    @location(3) uv: vec2f,
    @location(4) endPos: vec3f,
    @location(5) fade: f32
  }
  struct VertexOut {
    @builtin(position) clipPos: vec4f,
    @location(0) color: vec4f,
    @location(1) uvY: f32,
    @location(2) fade: f32
  }
  @vertex fn vertexMain(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    let viewStart = camera.view * vec4f(in.pos, 1.0);
    let viewEnd = camera.view * vec4f(in.endPos, 1.0);
    let rawDir = viewEnd.xy - viewStart.xy;
    let dist = max(length(rawDir), 1e-6);
    let viewDir = rawDir / dist;
    let perp = vec2f(-viewDir.y, viewDir.x);
    let axial = clamp(in.uv.x, 0.0, 1.0);
    let capOffset = in.uv.x - axial;
    let interpPos = mix(viewStart, viewEnd, vec4f(axial, axial, axial, axial));
    let offsetPerp = perp * (in.thickness * in.uv.y);
    let offsetTan = viewDir * (in.thickness * capOffset);
    let finalXY = interpPos.xy + offsetPerp + offsetTan;
    let finalPos = vec4f(finalXY, interpPos.z, interpPos.w);
    out.clipPos = camera.projection * finalPos;
    out.color = in.color;
    out.uvY = in.uv.y;
    out.fade = in.fade;
    return out;
  }
  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    var alpha = in.color.a;
    if (in.fade > 0.0) {
      let dist = abs(in.uvY);
      let t = clamp(1.0 - dist / (0.5 * in.fade), 0.0, 1.0);
      alpha = alpha * t;
    }
    return vec4f(in.color.rgb, alpha);
  }
`;

// One instance represents one line segment. The vertex shader generates the
// same body quad and six-triangle rounded caps previously expanded in C#.
const INSTANCED_BILLBOARD_LINE_SHADER = `
  struct Camera { projection: mat4x4f, view: mat4x4f, cameraPosition: vec3f }
  @group(0) @binding(0) var<uniform> camera: Camera;
  struct SegmentIn {
    @location(0) startPos: vec3f,
    @location(1) endPos: vec3f,
    @location(2) color: vec4f,
    @location(3) thickness: f32,
    @location(4) fade: f32
  }
  struct VertexOut {
    @builtin(position) clipPos: vec4f,
    @location(0) color: vec4f,
    @location(1) uvY: f32,
    @location(2) fade: f32
  }
  fn stadiumUv(vertexIndex: u32) -> vec2f {
    let bodyVertices = array<vec2f, 6>(
      vec2f(0.0, -0.5), vec2f(0.0, 0.5), vec2f(1.0, -0.5),
      vec2f(0.0, 0.5), vec2f(1.0, 0.5), vec2f(1.0, -0.5));
    if (vertexIndex < 6u) {
      return bodyVertices[vertexIndex];
    }

    let capVertex = vertexIndex - 6u;
    let triangle = capVertex / 3u;
    let corner = capVertex % 3u;
    if (corner == 0u) {
      return select(vec2f(0.0, 0.0), vec2f(1.0, 0.0), triangle >= 6u);
    }

    let angleStep = 0.5235987755982988; // PI / 6
    let pointIndex = triangle % 6u + corner - 1u;
    let startCap = triangle < 6u;
    let angle = select(
      -1.5707963267948966 + f32(pointIndex) * angleStep,
       1.5707963267948966 + f32(pointIndex) * angleStep,
      startCap);
    let axial = select(1.0 + cos(angle) * 0.5, cos(angle) * 0.5, startCap);
    return vec2f(axial, sin(angle) * 0.5);
  }
  @vertex fn vertexMain(@builtin(vertex_index) vertexIndex: u32, input: SegmentIn) -> VertexOut {
    var out: VertexOut;
    let uv = stadiumUv(vertexIndex);
    let viewStart = camera.view * vec4f(input.startPos, 1.0);
    let viewEnd = camera.view * vec4f(input.endPos, 1.0);
    let rawDir = viewEnd.xy - viewStart.xy;
    let rawLength = length(rawDir);
    let viewDir = select(vec2f(1.0, 0.0), rawDir / rawLength, rawLength > 1e-6);
    let perp = vec2f(-viewDir.y, viewDir.x);
    let axial = clamp(uv.x, 0.0, 1.0);
    let capOffset = uv.x - axial;
    let interpPos = mix(viewStart, viewEnd, vec4f(axial));
    let finalXY = interpPos.xy
      + perp * (input.thickness * uv.y)
      + viewDir * (input.thickness * capOffset);
    out.clipPos = camera.projection * vec4f(finalXY, interpPos.z, interpPos.w);
    out.color = input.color;
    out.uvY = uv.y;
    out.fade = input.fade;
    return out;
  }
  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    var alpha = in.color.a;
    if (in.fade > 0.0) {
      let dist = abs(in.uvY);
      let t = clamp(1.0 - dist / (0.5 * in.fade), 0.0, 1.0);
      alpha = alpha * t;
    }
    return vec4f(in.color.rgb, alpha);
  }
`;

const BILLBOARD_SHADER = `
  struct Camera { projection: mat4x4f, view: mat4x4f, cameraPosition: vec3f }
  @group(0) @binding(0) var<uniform> camera: Camera;
  @group(1) @binding(0) var sampler0: sampler;
  @group(1) @binding(1) var texture0: texture_2d<f32>;
  struct VertexIn {
    @location(0) pos: vec3f,
    @location(1) uv: vec2f,
    @location(2) aspectRatio: f32,
    @location(3) scale: f32,
    @location(4) relativeAnchor: vec2f
  }
  struct VertexOut { @builtin(position) pos: vec4f, @location(0) uv: vec2f }
  @vertex fn vertexMain(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    let offset = vec3f(
      (in.uv.x - in.relativeAnchor.x) * 2.0 * in.scale * in.aspectRatio,
      (in.uv.y - in.relativeAnchor.y) * 2.0 * in.scale,
      0.0);
    let right = vec3f(camera.view[0][0], camera.view[1][0], camera.view[2][0]);
    let up = vec3f(camera.view[0][1], camera.view[1][1], camera.view[2][1]);
    let world_pos = in.pos + right * offset.x + up * offset.y;
    out.pos = camera.projection * camera.view * vec4f(world_pos, 1.0);
    out.uv = in.uv;
    return out;
  }
  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    let color = textureSample(texture0, sampler0, in.uv);
    if (color.a < 0.1) { discard; }
    return color;
  }
`;

// ============================================================================
// Global State (WebGPU resources that can't be in C#)
// ============================================================================

let canvas = null;
let context = null;
let device = null;
let dotNetRef = null;
let resizeObserver = null;
let renderFrameId = 0;
let isDisposing = false;

// Frame timing
const frameMs = new Array(20);
let frameMsIndex = 0;

// Matrices
const frameArrayBuffer = new ArrayBuffer(FRAME_BUFFER_SIZE);
const projectionMatrix = new Float32Array(frameArrayBuffer, 0, 16);
const viewMatrix = new Float32Array(frameArrayBuffer, 16 * Float32Array.BYTES_PER_ELEMENT, 16);
const cameraPosition = new Float32Array(frameArrayBuffer, 32 * Float32Array.BYTES_PER_ELEMENT, 3);

// GPU resources
let frameUniformBuffer = null;
let frameBindGroupLayout = null;
let frameBindGroup = null;
let instancedLinePipeline = null;

// Background-gradient resources. Colors are packed as four vec4f values,
// followed by four polar-angle stops and the current camera/view span.
const BACKGROUND_GRADIENT_BUFFER_SIZE = Float32Array.BYTES_PER_ELEMENT * 24;
const backgroundGradientUniformArray = new ArrayBuffer(BACKGROUND_GRADIENT_BUFFER_SIZE);
const backgroundGradientColors = new Float32Array(backgroundGradientUniformArray, 0, 16);
const backgroundGradientAngles = new Float32Array(backgroundGradientUniformArray, 16 * Float32Array.BYTES_PER_ELEMENT, 4);
const backgroundGradientCamera = new Float32Array(backgroundGradientUniformArray, 20 * Float32Array.BYTES_PER_ELEMENT, 2);
let backgroundGradientUniformBuffer = null;
let backgroundGradientBindGroup = null;
let backgroundGradientPipeline = null;

// Render targets
let msaaColorTexture = null;
let depthTexture = null;
let colorAttachment = null;
let renderPassDescriptor = null;

// Lighting resources
let lightUniformArray = new ArrayBuffer(8 * Float32Array.BYTES_PER_ELEMENT); // 3 (vec3f) + ambient + specular + headlamp + directional light + headlamp focus
const lightDirection = new Float32Array(lightUniformArray, 0, 3);
const lightAmbient = new Float32Array(lightUniformArray, 12, 1);
const lightSpecularPower = new Float32Array(lightUniformArray, 16, 1);
const lightHeadlampIntensity = new Float32Array(lightUniformArray, 20, 1);
const lightDirectionalIntensity = new Float32Array(lightUniformArray, 24, 1);
const lightHeadlampFocus = new Float32Array(lightUniformArray, 28, 1);
let lightUniformBuffer = null;
let lightBindGroupLayout = null;
let lightBindGroup = null;


// Grid resources
let gridPipeline = null;
let gridVertexBuffer = null;
let gridIndexBuffer = null;
let gridUniformBuffer = null;
let gridBindGroup = null;
let gridBindGroupLayout = null;
const gridUniformArray = new ArrayBuffer(16 * Float32Array.BYTES_PER_ELEMENT);
const gridLineColor = new Float32Array(gridUniformArray, 0, 4);
const gridBaseColor = new Float32Array(gridUniformArray, 16, 4);
const gridLineWidth = new Float32Array(gridUniformArray, 32, 2);
const gridSpacingUniform = new Float32Array(gridUniformArray, 40, 1);

// Grid configuration (updated from C#)
let gridSize = 20.0;
let gridSpacing = 1.0;
let zIsUp = false;
let gridDepthWriteEnabled = false;  // New variable to control depth writing
let gridIsTransparent = false;

// Coordinate axes
let coordinateThickness = 1.0;
let coordinateAxes = null;
let axisExtent = gridSize;

// Render settings (updated from C#)
let colorFormat = 'bgra8unorm';
let depthFormat = 'depth24plus';
let sampleCount = 4;
let backgroundGradientNegativePolarColor = [0, 0, 0, 1];
let backgroundGradientFirstIntermediatePolarColor = [0, 0, 0, 1];
let backgroundGradientFirstIntermediatePolarAngle = -Math.PI / 6;
let backgroundGradientSecondIntermediatePolarColor = [0, 0, 0, 1];
let backgroundGradientSecondIntermediatePolarAngle = Math.PI / 6;
let backgroundGradientPositivePolarColor = [0, 0, 0, 1];
let cameraPolarAngle = 0;
let backgroundGradientVerticalSpan = Math.PI / 3;

function updateBackgroundGradientUniforms() {
    const stops = [
        { angle: -Math.PI / 2, color: backgroundGradientNegativePolarColor },
        { angle: backgroundGradientFirstIntermediatePolarAngle, color: backgroundGradientFirstIntermediatePolarColor },
        { angle: backgroundGradientSecondIntermediatePolarAngle, color: backgroundGradientSecondIntermediatePolarColor },
        { angle: Math.PI / 2, color: backgroundGradientPositivePolarColor }
    ].sort((left, right) => left.angle - right.angle);

    stops.forEach((stop, index) => {
        backgroundGradientColors.set(stop.color, index * 4);
        backgroundGradientAngles[index] = stop.angle;
    });
    backgroundGradientCamera.set([cameraPolarAngle, backgroundGradientVerticalSpan]);

    if (device && backgroundGradientUniformBuffer) {
        device.queue.writeBuffer(backgroundGradientUniformBuffer, 0, backgroundGradientUniformArray);
    }
}

// Scene objects (maintained in sync with C#)
const meshes = [];
const lines = [];
const textBillboards = [];
let meshFacesVisible = true;
let usePrimitiveSurfaceNormals = false;

// ============================================================================
// Initialization
// ============================================================================

export function initGPU_Canvas(dotnet, canvasEl, options, initialViewMatrix, initialCameraPosition) {
    return enqueueGpuOperation(() => initGPUCanvasCore(dotnet, canvasEl, options,
        initialViewMatrix, initialCameraPosition));
}

async function initGPUCanvasCore(dotnet, canvasEl, options, initialViewMatrix, initialCameraPosition) {
    isDisposing = false;
    dotNetRef = dotnet;
    canvas = canvasEl;
    context = canvas.getContext('webgpu');

    colorFormat = navigator.gpu?.getPreferredCanvasFormat?.() || 'bgra8unorm';

    // Set initial view matrix from parameter
    viewMatrix.set(initialViewMatrix);
    cameraPosition.set(initialCameraPosition);

    // Apply options
    await updateDisplayOptionsCore(options);

    // Set up resize observer
    setupResizeObserver();

    // Initialize WebGPU
    try {
        await initWebGPU();

        // Initialize render targets BEFORE starting render loop
        if (canvas.width > 0 && canvas.height > 0) {
            allocateRenderTargets(canvas.width, canvas.height);
        }

        startRenderLoop();
        startFrameTimer();
        notifyDotNet('OnWebGpuReady');
    } catch (error) {
        notifyDotNet('OnWebGpuError', error.message);
        throw error;
    }
}

async function initWebGPU() {
    if (!navigator.gpu)
        throw new Error('WebGPU is not available in this browser.');

    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter)
        throw new Error('No compatible WebGPU adapter was found.');

    const requiredFeatures = [];
    if (adapter.features.has('texture-compression-bc')) requiredFeatures.push('texture-compression-bc');
    if (adapter.features.has('texture-compression-etc2')) requiredFeatures.push('texture-compression-etc2');

    device = await adapter.requestDevice({ requiredFeatures });
    const initializedDevice = device;
    initializedDevice.lost.then(info => {
        if (isDisposing || device !== initializedDevice) return;
        renderingPaused = true;
        if (renderFrameId) {
            cancelAnimationFrame(renderFrameId);
            renderFrameId = 0;
        }
        if (frameIntervalId) {
            clearInterval(frameIntervalId);
            frameIntervalId = 0;
        }
        device = null;
        const reason = info?.message || info?.reason || 'The WebGPU device was lost.';
        notifyDotNet('OnWebGpuError', `WebGPU device lost: ${reason}`);
    }).catch(error => {
        if (!isDisposing)
            notifyDotNet('OnWebGpuError', `WebGPU device-loss handler failed: ${error.message}`);
    });
    context.configure({
        device,
        format: colorFormat,
        alphaMode: 'opaque',
        viewFormats: [`${colorFormat}-srgb`]
    });

    // Create frame uniform buffer
    frameUniformBuffer = device.createBuffer({
        size: FRAME_BUFFER_SIZE,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
    });

    frameBindGroupLayout = device.createBindGroupLayout({
        label: 'Frame BGL',
        entries: [{
            binding: 0,
            visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
            buffer: {}
        }]
    });

    frameBindGroup = device.createBindGroup({
        label: 'Frame BG',
        layout: frameBindGroupLayout,
        entries: [{ binding: 0, resource: { buffer: frameUniformBuffer } }]
    });

    // Create lighting uniform buffer and bind group
    lightUniformBuffer = device.createBuffer({
        size: lightUniformArray.byteLength,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(lightUniformBuffer, 0, lightUniformArray);

    lightBindGroupLayout = device.createBindGroupLayout({
        label: 'Light BGL',
        entries: [{
            binding: 0,
            visibility: GPUShaderStage.FRAGMENT,
            buffer: {}
        }]
    });

    lightBindGroup = device.createBindGroup({
        label: 'Light BG',
        layout: lightBindGroupLayout,
        entries: [{ binding: 0, resource: { buffer: lightUniformBuffer } }]
    });

    await initBackgroundGradient();
    await initGrid();
    await initInstancedLinePipeline();
    await initCoordinateAxes();
}

async function initInstancedLinePipeline() {
    const module = device.createShaderModule({
        label: 'Instanced Line Shader',
        code: INSTANCED_BILLBOARD_LINE_SHADER
    });

    instancedLinePipeline = await device.createRenderPipelineAsync({
        label: 'Instanced Line Pipeline',
        layout: device.createPipelineLayout({ bindGroupLayouts: [frameBindGroupLayout] }),
        vertex: {
            module,
            entryPoint: 'vertexMain',
            buffers: [{
                arrayStride: 48,
                stepMode: 'instance',
                attributes: [
                    { shaderLocation: 0, offset: 0, format: 'float32x3' },
                    { shaderLocation: 1, offset: 12, format: 'float32x3' },
                    { shaderLocation: 2, offset: 24, format: 'float32x4' },
                    { shaderLocation: 3, offset: 40, format: 'float32' },
                    { shaderLocation: 4, offset: 44, format: 'float32' }
                ]
            }]
        },
        fragment: {
            module,
            entryPoint: 'fragmentMain',
            targets: [{
                format: `${colorFormat}-srgb`,
                blend: {
                    color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                    alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' }
                }
            }]
        },
        depthStencil: {
            format: depthFormat,
            depthWriteEnabled: false,
            depthCompare: 'less-equal'
        },
        multisample: { count: sampleCount },
        primitive: { topology: 'triangle-list', cullMode: 'none' }
    });
}

async function initBackgroundGradient() {
    backgroundGradientUniformBuffer = device.createBuffer({
        label: 'Background Gradient Uniform Buffer',
        size: BACKGROUND_GRADIENT_BUFFER_SIZE,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
    });

    const bindGroupLayout = device.createBindGroupLayout({
        label: 'Background Gradient BGL',
        entries: [{ binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: {} }]
    });

    backgroundGradientBindGroup = device.createBindGroup({
        label: 'Background Gradient BG',
        layout: bindGroupLayout,
        entries: [{ binding: 0, resource: { buffer: backgroundGradientUniformBuffer } }]
    });

    const module = device.createShaderModule({
        label: 'Background Gradient Shader',
        code: BACKGROUND_GRADIENT_SHADER
    });

    backgroundGradientPipeline = await device.createRenderPipelineAsync({
        label: 'Background Gradient Pipeline',
        layout: device.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] }),
        vertex: { module, entryPoint: 'vertexMain' },
        fragment: {
            module,
            entryPoint: 'fragmentMain',
            targets: [{ format: `${colorFormat}-srgb` }]
        },
        // This pipeline is used in the same pass as the scene pipelines,
        // which always has a depth attachment. It neither tests nor writes
        // depth, but it must still declare the pass's depth format.
        depthStencil: {
            format: depthFormat,
            depthWriteEnabled: false,
            depthCompare: 'always'
        },
        primitive: { topology: 'triangle-list', cullMode: 'none' },
        multisample: { count: sampleCount }
    });

    updateBackgroundGradientUniforms();
}

async function initGrid() {
    // Create grid pipeline
    const bindGroupLayout = device.createBindGroupLayout({
        label: 'Grid BGL',
        entries: [{ binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: {} }]
    });

    const module = device.createShaderModule({ label: 'Grid Shader', code: GRID_SHADER });

    gridPipeline = await device.createRenderPipelineAsync({
        label: 'Grid Pipeline',
        layout: device.createPipelineLayout({ bindGroupLayouts: [frameBindGroupLayout, bindGroupLayout] }),
        vertex: {
            module,
            entryPoint: 'vertexMain',
            buffers: [{
                arrayStride: 20,
                attributes: [
                    { shaderLocation: 0, offset: 0, format: 'float32x3' },
                    { shaderLocation: 1, offset: 12, format: 'float32x2' }
                ]
            }]
        },
        fragment: {
            module,
            entryPoint: 'fragmentMain',
            targets: [{
                format: `${colorFormat}-srgb`,
                blend: {
                    color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                    alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' }
                }
            }]
        },
        depthStencil: {
            format: depthFormat,
            depthWriteEnabled: !gridIsTransparent,
            depthCompare: 'less-equal'
        },
        multisample: { count: sampleCount }
    });

    // Create grid uniform buffer
    if (!gridUniformBuffer) {
        gridUniformBuffer = device.createBuffer({
            size: gridUniformArray.byteLength,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
        });
    }


    if (!gridBindGroup) {
        gridBindGroup = device.createBindGroup({
            label: 'Grid BG',
            layout: bindGroupLayout,
            entries: [{ binding: 0, resource: { buffer: gridUniformBuffer } }]
        });
    }


    createGridGeometry();
    updateGridUniforms();
}

async function initCoordinateAxes() {

    const axisData = createAxisGeometry();
    const posBuffer = createBuffer(axisData.vertices, GPUBufferUsage.VERTEX);
    const colorBuffer = createBuffer(axisData.colors, GPUBufferUsage.VERTEX);
    const thicknessBuffer = createBuffer(axisData.thickness, GPUBufferUsage.VERTEX);
    const uvBuffer = createBuffer(axisData.uvs, GPUBufferUsage.VERTEX);
    const endPosBuffer = createBuffer(axisData.endPositions, GPUBufferUsage.VERTEX);
    const fadeBuffer = createBuffer(axisData.fades, GPUBufferUsage.VERTEX);
    const indexBuffer = createBuffer(axisData.indices, GPUBufferUsage.INDEX, Uint16Array);

    const shaderModule = device.createShaderModule({ label: 'Coordinate Axes Shader', code: BILLBOARD_LINE_SHADER });

    const vertexBufferLayout = [
        { arrayStride: 12, attributes: [{ shaderLocation: 0, offset: 0, format: 'float32x3' }] },
        { arrayStride: 16, attributes: [{ shaderLocation: 1, offset: 0, format: 'float32x4' }] },
        { arrayStride: 4, attributes: [{ shaderLocation: 2, offset: 0, format: 'float32' }] },
        { arrayStride: 8, attributes: [{ shaderLocation: 3, offset: 0, format: 'float32x2' }] },
        { arrayStride: 12, attributes: [{ shaderLocation: 4, offset: 0, format: 'float32x3' }] },
        { arrayStride: 4, attributes: [{ shaderLocation: 5, offset: 0, format: 'float32' }] }
    ];

    const pipeline = await device.createRenderPipelineAsync({
        label: 'Coordinate Axes Pipeline',
        layout: device.createPipelineLayout({ bindGroupLayouts: [frameBindGroupLayout] }),
        vertex: { module: shaderModule, entryPoint: 'vertexMain', buffers: vertexBufferLayout },
        fragment: {
            module: shaderModule,
            entryPoint: 'fragmentMain',
            targets: [{
                format: `${colorFormat}-srgb`,
                blend: {
                    color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                    alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' }
                }
            }]
        },
        depthStencil: {
            format: depthFormat,
            depthWriteEnabled: false, // Axes are transparent and should not write to depth
            depthCompare: 'less-equal'
        },
        multisample: { count: sampleCount },
        primitive: { topology: 'triangle-list', cullMode: 'none' }
    });

    coordinateAxes = {
        posBuffer,
        colorBuffer,
        thicknessBuffer,
        uvBuffer,
        endPosBuffer,
        fadeBuffer,
        indexBuffer,
        indexCount: axisData.indices.length,
        pipeline
    };
}

function createAxisGeometry() {
    const vertices = [];
    const colors = [];
    const thickness = [];
    const uvs = [];
    const endPositions = [];
    const fades = [];
    const indices = [];

    const lineThickness = coordinateThickness;
    const axes = [
        { start: [0, 0, 0], end: [axisExtent, 0, 0], color: [1, 0, 0, 1], fade: 0 },
        { start: [0, 0, 0], end: [-axisExtent, 0, 0], color: [0.5, 0, 0, 1], fade: 1 },
        { start: [0, 0, 0], end: [0, axisExtent, 0], color: [0, 1, 0, 1], fade: 0 },
        { start: [0, 0, 0], end: [0, -axisExtent, 0], color: [0, 0.5, 0, 1], fade: 1 },
        { start: [0, 0, 0], end: [0, 0, axisExtent], color: [0, 0, 1, 1], fade: 0 },
        { start: [0, 0, 0], end: [0, 0, -axisExtent], color: [0, 0, 0.5, 1], fade: 1 }
    ];

    let vertexOffset = 0;
    for (const axis of axes) {
        for (let i = 0; i < 4; i++) {
            vertices.push(...axis.start);
            colors.push(...axis.color);
            thickness.push(lineThickness);
            endPositions.push(...axis.end);
            fades.push(axis.fade);
        }
        uvs.push(0, -0.5, 1, -0.5, 0, 0.5, 1, 0.5);
        indices.push(
            vertexOffset + 0, vertexOffset + 1, vertexOffset + 2,
            vertexOffset + 1, vertexOffset + 3, vertexOffset + 2
        );
        vertexOffset += 4;
    }

    return {
        vertices: new Float32Array(vertices),
        colors: new Float32Array(colors),
        thickness: new Float32Array(thickness),
        uvs: new Float32Array(uvs),
        endPositions: new Float32Array(endPositions),
        fades: new Float32Array(fades),
        indices: new Uint16Array(indices)
    };
}

function createGridGeometry() {
    // Destroy existing buffers if they exist
    if (gridVertexBuffer) gridVertexBuffer.destroy();
    if (gridIndexBuffer) gridIndexBuffer.destroy();

    var yNeg = zIsUp ? -gridSize : -0.01;
    var zNeg = zIsUp ? -0.01 : -gridSize;
    var yPos = zIsUp ? gridSize : -0.01;
    var zPos = zIsUp ? -0.01 : gridSize;
    // Create grid geometry
    const vertexArray = new Float32Array([
        -gridSize, yNeg, zNeg, 0, 0,
        gridSize, yNeg, zNeg, 100, 0,
        -gridSize, yPos, zPos, 0, 100,
        gridSize, yPos, zPos, 100, 100,
    ]);

    gridVertexBuffer = device.createBuffer({
        size: vertexArray.byteLength,
        usage: GPUBufferUsage.VERTEX,
        mappedAtCreation: true
    });
    new Float32Array(gridVertexBuffer.getMappedRange()).set(vertexArray);
    gridVertexBuffer.unmap();

    const indexArray = new Uint32Array([0, 1, 2, 1, 2, 3]);
    gridIndexBuffer = device.createBuffer({
        size: indexArray.byteLength,
        usage: GPUBufferUsage.INDEX,
        mappedAtCreation: true
    });
    new Uint32Array(gridIndexBuffer.getMappedRange()).set(indexArray);
    gridIndexBuffer.unmap();
}

function updateGridUniforms() {
    const scale = 100 / gridSize;
    const factor = 1 / (scale * gridSpacing);
    gridSpacingUniform[0] = factor;
    device.queue.writeBuffer(gridUniformBuffer, 0, gridUniformArray);
}

// ============================================================================
// Rendering
// ============================================================================

let renderingPaused = false;

function startRenderLoop() {
    function frameCallback() {
        if (!device || isDisposing) {
            renderFrameId = 0;
            return;
        }
        renderFrameId = requestAnimationFrame(frameCallback);
        if (renderingPaused) return;
        const frameStart = performance.now();

        device.queue.writeBuffer(frameUniformBuffer, 0, frameArrayBuffer);
        renderFrame();

        frameMs[frameMsIndex++ % frameMs.length] = performance.now() - frameStart;
    }

    if (renderFrameId) cancelAnimationFrame(renderFrameId);
    renderFrameId = requestAnimationFrame(frameCallback);
}

function renderFrame() {
    const renderPass = getRenderPassDescriptor();
    if (!renderPass) return; // Skip frame if render targets aren't ready

    const encoder = device.createCommandEncoder();
    const pass = encoder.beginRenderPass(renderPass);

    // Draw the full-screen, polar-angle-aware gradient before scene geometry.
    // It does not write depth, leaving the cleared depth texture available for
    // the normal opaque and transparent scene passes.
    if (backgroundGradientPipeline && backgroundGradientBindGroup) {
        pass.setPipeline(backgroundGradientPipeline);
        pass.setBindGroup(0, backgroundGradientBindGroup);
        pass.draw(6);
    }

    // ========================================================================
    // 1. Opaque Pass: Draw all opaque objects first.
    // Depth test and depth write are enabled.
    // ========================================================================

    // Draw opaque meshes
    for (const mesh of meshes.filter(m => m.visible && !m.isTransparent)) {
        if (!mesh.pipeline || !mesh.vertexBuffer || !mesh.indexBuffer) continue;

        pass.setPipeline(mesh.pipeline);
        pass.setBindGroup(0, frameBindGroup);

        if (mesh.singleColor && mesh.bindGroup) {
            pass.setBindGroup(1, mesh.bindGroup);
        }

        pass.setVertexBuffer(0, mesh.vertexBuffer);
        if (!mesh.singleColor && mesh.colorBuffer) {
            pass.setVertexBuffer(1, mesh.colorBuffer);
        }
        pass.setVertexBuffer(mesh.singleColor ? 1 : 2, mesh.primitiveSurfaceNormalBuffer);
        if (!mesh.singleColor) {
            pass.setBindGroup(1, lightBindGroup);
        }

        pass.setIndexBuffer(mesh.indexBuffer, mesh.indexFormat);
        pass.drawIndexed(mesh.indexCount);
    }

    // Draw grid if it's opaque
    if (gridPipeline && !gridIsTransparent) {
        pass.setPipeline(gridPipeline);
        pass.setBindGroup(0, frameBindGroup);
        pass.setBindGroup(1, gridBindGroup);
        pass.setVertexBuffer(0, gridVertexBuffer);
        pass.setIndexBuffer(gridIndexBuffer, 'uint32');
        pass.drawIndexed(6);
    }

    // ========================================================================
    // 2. Transparent Pass: Draw all transparent objects, sorted back-to-front.
    // Depth test is enabled, but depth write is disabled.
    // ========================================================================

    const transparentDrawables = [];

    // Add transparent grid
    if (gridPipeline && gridIsTransparent) {
        transparentDrawables.push({
            // The grid is at the origin, so its depth is determined by the view matrix's translation
            depth: (viewMatrix[12] * viewMatrix[12] + viewMatrix[13] * viewMatrix[13] + viewMatrix[14] * viewMatrix[14]),
            draw: () => {
                pass.setPipeline(gridPipeline);
                pass.setBindGroup(0, frameBindGroup);
                pass.setBindGroup(1, gridBindGroup);
                pass.setVertexBuffer(0, gridVertexBuffer);
                pass.setIndexBuffer(gridIndexBuffer, 'uint32');
                pass.drawIndexed(6);
            }
        });
    }

    // Add coordinate axes
    if (coordinateThickness && coordinateAxes) {
        transparentDrawables.push({
            depth: (viewMatrix[12] * viewMatrix[12] + viewMatrix[13] * viewMatrix[13] + viewMatrix[14] * viewMatrix[14]),
            draw: () => {
                pass.setPipeline(coordinateAxes.pipeline);
                pass.setBindGroup(0, frameBindGroup);
                pass.setVertexBuffer(0, coordinateAxes.posBuffer);
                pass.setVertexBuffer(1, coordinateAxes.colorBuffer);
                pass.setVertexBuffer(2, coordinateAxes.thicknessBuffer);
                pass.setVertexBuffer(3, coordinateAxes.uvBuffer);
                pass.setVertexBuffer(4, coordinateAxes.endPosBuffer);
                pass.setVertexBuffer(5, coordinateAxes.fadeBuffer);
                pass.setIndexBuffer(coordinateAxes.indexBuffer, 'uint16');
                pass.drawIndexed(coordinateAxes.indexCount);
            }
        });
    }

    // Add transparent meshes
    for (const mesh of meshes.filter(m => m.visible && m.isTransparent)) {
        if (!mesh.pipeline || !mesh.vertexBuffer || !mesh.indexBuffer) continue;
        const viewSpacePos = transformPoint(mesh.center, viewMatrix);
        transparentDrawables.push({
            depth: viewSpacePos[2],
            draw: () => {
                pass.setPipeline(mesh.pipeline);
                pass.setBindGroup(0, frameBindGroup);
                if (mesh.singleColor && mesh.bindGroup) pass.setBindGroup(1, mesh.bindGroup);
                pass.setVertexBuffer(0, mesh.vertexBuffer);
                if (!mesh.singleColor && mesh.colorBuffer) pass.setVertexBuffer(1, mesh.colorBuffer);
                pass.setVertexBuffer(mesh.singleColor ? 1 : 2, mesh.primitiveSurfaceNormalBuffer);
                if (!mesh.singleColor) pass.setBindGroup(1, lightBindGroup);
                pass.setIndexBuffer(mesh.indexBuffer, mesh.indexFormat);
                pass.drawIndexed(mesh.indexCount);
            }
        });
    }

    // Add lines
    for (const line of lines) {
        if (!instancedLinePipeline || !line.instanceBuffer || line.instanceCount === 0) continue;
        const viewSpacePos = transformPoint(line.center, viewMatrix);
        transparentDrawables.push({
            depth: viewSpacePos[2],
            draw: () => {
                pass.setPipeline(instancedLinePipeline);
                pass.setBindGroup(0, frameBindGroup);
                pass.setVertexBuffer(0, line.instanceBuffer);
                pass.draw(42, line.instanceCount);
            }
        });
    }

    // Add text billboards
    for (const billboard of textBillboards) {
        if (!billboard.pipeline || !billboard.vertexBuffer || !billboard.indexBuffer) continue;
        const viewSpacePos = transformPoint(billboard.position, viewMatrix);
        transparentDrawables.push({
            depth: viewSpacePos[2],
            draw: () => {
                pass.setPipeline(billboard.pipeline);
                pass.setBindGroup(0, frameBindGroup);
                pass.setBindGroup(1, billboard.bindGroup);
                pass.setVertexBuffer(0, billboard.vertexBuffer);
                pass.setIndexBuffer(billboard.indexBuffer, 'uint16');
                pass.drawIndexed(billboard.indexCount);
            }
        });
    }

    // Sort transparent objects from back to front (descending depth)
    transparentDrawables.sort((a, b) => b.depth - a.depth);

    // Execute draw calls
    for (const drawable of transparentDrawables) {
        drawable.draw();
    }

    pass.end();
    device.queue.submit([encoder.finish()]);
}

function getRenderPassDescriptor() {
    // Ensure render targets are allocated
    if (!colorAttachment || !renderPassDescriptor) {
        if (canvas.width > 0 && canvas.height > 0) {
            allocateRenderTargets(canvas.width, canvas.height);
        } else {
            // Return null to skip this frame if canvas isn't ready
            return null;
        }
    }

    const colorView = context.getCurrentTexture().createView({ format: `${colorFormat}-srgb` });
    if (sampleCount > 1) {
        colorAttachment.resolveTarget = colorView;
    } else {
        colorAttachment.view = colorView;
    }
    return renderPassDescriptor;
}

// ============================================================================
// Resize Handling
// ============================================================================

function setupResizeObserver() {
    resizeObserver?.disconnect();
    resizeObserver = new ResizeObserver((entries) => {
        for (let entry of entries) {
            if (entry.target !== canvas) continue;

            let width, height;
            if (entry.devicePixelContentBoxSize) {
                const size = entry.devicePixelContentBoxSize[0];
                width = size.inlineSize;
                height = size.blockSize;
            } else if (entry.contentBoxSize) {
                const s = Array.isArray(entry.contentBoxSize) ? entry.contentBoxSize[0] : entry.contentBoxSize;
                width = s.inlineSize;
                height = s.blockSize;
            } else {
                width = entry.contentRect.width;
                height = entry.contentRect.height;
            }

            if (width === 0 || height === 0) return;
            enqueueGpuOperation(() => {
                if (isDisposing || !canvas) return;
                canvas.width = width;
                canvas.height = height;
                notifyDotNet('OnCanvasResized', width, height);
                if (device) allocateRenderTargets(width, height);
            }).catch(error => {
                if (!isDisposing) notifyDotNet('OnWebGpuError', `WebGPU resize failed: ${error.message}`);
            });
        }
    });

    resizeObserver.observe(canvas);
}

function allocateRenderTargets(width, height) {
    const size = { width, height };

    if (msaaColorTexture) msaaColorTexture.destroy();
    if (sampleCount > 1) {
        msaaColorTexture = device.createTexture({
            size,
            sampleCount,
            format: `${colorFormat}-srgb`,
            usage: GPUTextureUsage.RENDER_ATTACHMENT
        });
    }

    if (depthTexture) depthTexture.destroy();
    depthTexture = device.createTexture({
        size,
        sampleCount,
        format: depthFormat,
        usage: GPUTextureUsage.RENDER_ATTACHMENT
    });

    colorAttachment = {
        view: sampleCount > 1 ? msaaColorTexture.createView() : undefined,
        resolveTarget: undefined,
        clearValue: { r: 0, g: 0, b: 0, a: 1.0 },
        loadOp: 'clear',
        storeOp: sampleCount > 1 ? 'discard' : 'store'
    };

    renderPassDescriptor = {
        colorAttachments: [colorAttachment],
        depthStencilAttachment: {
            view: depthTexture.createView(),
            depthClearValue: 1.0,
            depthLoadOp: 'clear',
            depthStoreOp: 'discard'
        }
    };
}

// ============================================================================
// Updates from C#
// ============================================================================

export function writeViewMatrix(matrixArray, polarAngle, cameraPositionArray) {
    return enqueueGpuOperation(() => writeViewMatrixCore(matrixArray, polarAngle, cameraPositionArray));
}

function writeViewMatrixCore(matrixArray, polarAngle, cameraPositionArray) {
    viewMatrix.set(matrixArray);
    cameraPosition.set(cameraPositionArray);
    if (typeof polarAngle === 'number') {
        cameraPolarAngle = polarAngle;
        updateBackgroundGradientUniforms();
    }
}

export function writeProjectionMatrix(matrixArray) {
    return enqueueGpuOperation(() => writeProjectionMatrixCore(matrixArray));
}

function writeProjectionMatrixCore(matrixArray) {
    projectionMatrix.set(matrixArray);
}

export function updateDisplayOptions(options) {
    return enqueueGpuOperation(() => updateDisplayOptionsCore(options));
}

async function updateDisplayOptionsCore(options) {
    let gridChanged = false;
    let needsGridPipelineRecreation = false;
    if (zIsUp !== options.zIsUp) {
        zIsUp = options.zIsUp;
        gridChanged = true;
    }
    if (typeof options.sampleCount === 'number') sampleCount = options.sampleCount;

    // Handle coordinate axes visibility
    if (typeof options.coordinateThickness === 'number' && coordinateThickness !== options.coordinateThickness) {
        coordinateThickness = options.coordinateThickness;
        if (device) {
            if (coordinateAxes) {
                coordinateAxes.posBuffer?.destroy();
                coordinateAxes.colorBuffer?.destroy();
                coordinateAxes.thicknessBuffer?.destroy();
                coordinateAxes.uvBuffer?.destroy();
                coordinateAxes.endPosBuffer?.destroy();
                coordinateAxes.fadeBuffer?.destroy();
                coordinateAxes.indexBuffer?.destroy();
                coordinateAxes = null;
            }
            if (coordinateThickness > 0.0) {
                await initCoordinateAxes();
            }
        }
    }

    // Update lighting uniforms
    if (options.lightDir) lightDirection.set(options.lightDir);
    if (typeof options.ambient === 'number') lightAmbient[0] = options.ambient;
    if (typeof options.specularPower === 'number') lightSpecularPower[0] = options.specularPower;
    if (typeof options.headlampIntensity === 'number') lightHeadlampIntensity[0] = options.headlampIntensity;
    if (typeof options.directionalLightIntensity === 'number') lightDirectionalIntensity[0] = options.directionalLightIntensity;
    if (typeof options.headlampFocus === 'number') lightHeadlampFocus[0] = options.headlampFocus;
    if (device) {
        device.queue.writeBuffer(lightUniformBuffer, 0, lightUniformArray);
    }

    // Update grid uniforms
    if (options.baseColor) {
        const newIsTransparent = options.baseColor[3] < 1.0;
        if (newIsTransparent !== gridIsTransparent) {
            gridIsTransparent = newIsTransparent;
            needsGridPipelineRecreation = true;
        }
        gridBaseColor.set(options.baseColor);
    }
    if (options.lineColor) gridLineColor.set(options.lineColor);
    if (typeof options.lineWidthX === 'number' && typeof options.lineWidthY === 'number') {
        gridLineWidth.set([options.lineWidthX, options.lineWidthY]);
    }

    if (typeof options.gridSize === 'number' && options.gridSize !== gridSize) {
        gridSize = options.gridSize;
        axisExtent = gridSize;  // Update axis extent to match grid size
        gridChanged = true;
        // Recreate coordinate axes with new extent if they exist
        if (coordinateAxes) {
            coordinateAxes.posBuffer?.destroy();
            coordinateAxes.colorBuffer?.destroy();
            coordinateAxes.thicknessBuffer?.destroy();
            coordinateAxes.uvBuffer?.destroy();
            coordinateAxes.endPosBuffer?.destroy();
            coordinateAxes.fadeBuffer?.destroy();
            coordinateAxes.indexBuffer?.destroy();
            coordinateAxes = null;
            if (coordinateThickness > 0.0) {
                await initCoordinateAxes();
            }
        }
    }
    if (typeof options.gridSpacing === 'number' && options.gridSpacing !== gridSpacing) {
        gridSpacing = options.gridSpacing;
        gridChanged = true;
    }

    if (device) {
        if (needsGridPipelineRecreation) {
            await initGrid(); // This recreates pipeline and geometry
        } else if (gridChanged) {
            createGridGeometry();
            updateGridUniforms();
        } else if (gridUniformBuffer) {
            device.queue.writeBuffer(gridUniformBuffer, 0, gridUniformArray);
        }
    }


    if (options.backgroundGradientNegativePolarColor) {
        backgroundGradientNegativePolarColor = options.backgroundGradientNegativePolarColor;
    }
    if (options.backgroundGradientFirstIntermediatePolarColor) {
        backgroundGradientFirstIntermediatePolarColor = options.backgroundGradientFirstIntermediatePolarColor;
    }
    if (typeof options.backgroundGradientFirstIntermediatePolarAngle === 'number') {
        backgroundGradientFirstIntermediatePolarAngle = options.backgroundGradientFirstIntermediatePolarAngle;
    }
    if (options.backgroundGradientSecondIntermediatePolarColor) {
        backgroundGradientSecondIntermediatePolarColor = options.backgroundGradientSecondIntermediatePolarColor;
    }
    if (typeof options.backgroundGradientSecondIntermediatePolarAngle === 'number') {
        backgroundGradientSecondIntermediatePolarAngle = options.backgroundGradientSecondIntermediatePolarAngle;
    }
    if (options.backgroundGradientPositivePolarColor) {
        backgroundGradientPositivePolarColor = options.backgroundGradientPositivePolarColor;
    }
    if (typeof options.cameraPolarAngle === 'number') cameraPolarAngle = options.cameraPolarAngle;
    if (typeof options.backgroundGradientVerticalSpan === 'number') {
        backgroundGradientVerticalSpan = options.backgroundGradientVerticalSpan;
    }
    updateBackgroundGradientUniforms();
}

// ============================================================================
// Scene Management (Mesh, Lines, Billboards)
// ============================================================================

export function addMesh(meshData) {
    return enqueueGpuOperation(() => addMeshCore(meshData));
}

async function addMeshCore(meshData) {
    requireDevice(`adding mesh '${meshData.id}'`);
    const { id, vertices, indices, colors, primitiveSurfaceNormals, singleColor } = meshData;

    const vertexBuffer = createBuffer(vertices, GPUBufferUsage.VERTEX);
    let maximumIndex = 0;
    for (const index of indices) {
        maximumIndex = Math.max(maximumIndex, index);
    }
    const indexFormat = maximumIndex > 0xFFFF ? 'uint32' : 'uint16';
    const indexBuffer = createBuffer(indices, GPUBufferUsage.INDEX,
        indexFormat === 'uint32' ? Uint32Array : Uint16Array);
    const suppliedPrimitiveSurfaceNormals = primitiveSurfaceNormals?.length === vertices.length
        ? new Float32Array(primitiveSurfaceNormals)
        : new Float32Array(vertices.length);
    const activePrimitiveSurfaceNormals = usePrimitiveSurfaceNormals
        ? suppliedPrimitiveSurfaceNormals
        : new Float32Array(vertices.length);
    const primitiveSurfaceNormalBuffer = createBuffer(activePrimitiveSurfaceNormals,
        GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST);

    // Calculate bounding box and center for sorting
    let min = [Infinity, Infinity, Infinity];
    let max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < vertices.length; i += 3) {
        min[0] = Math.min(min[0], vertices[i]);
        min[1] = Math.min(min[1], vertices[i + 1]);
        min[2] = Math.min(min[2], vertices[i + 2]);
        max[0] = Math.max(max[0], vertices[i]);
        max[1] = Math.max(max[1], vertices[i + 1]);
        max[2] = Math.max(max[2], vertices[i + 2]);
    }
    const center = [(min[0] + max[0]) / 2, (min[1] + max[1]) / 2, (min[2] + max[2]) / 2];

    let colorBuffer = null;
    let bindGroup = null;
    let meshBindGroupLayout = null;
    let isTransparent = false;
    let shaderCode = null;
    let pipelineLayout = null;

    if (singleColor) {
        shaderCode = MESH_SHADER;
        isTransparent = colors.length >= 4 && colors[3] < 1.0;

        colorBuffer = createBuffer(colors, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST);

        meshBindGroupLayout = device.createBindGroupLayout({
            label: `Mesh ${id} BGL`,
            entries: [
                { binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: {} }, // Light uniforms
                { binding: 1, visibility: GPUShaderStage.FRAGMENT, buffer: {} }  // Mesh color
            ]
        });

        bindGroup = device.createBindGroup({
            label: `Mesh ${id} BG`,
            layout: meshBindGroupLayout,
            entries: [
                { binding: 0, resource: { buffer: lightUniformBuffer } },
                { binding: 1, resource: { buffer: colorBuffer } }
            ]
        });
        pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [frameBindGroupLayout, meshBindGroupLayout] });
    } else {
        shaderCode = MESH_SHADER_VERTEX_COLOR;
        colorBuffer = createBuffer(colors, GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST);
        // Check if any vertex has transparency to correctly flag the mesh
        isTransparent = false;
        for (let i = 3; i < colors.length; i += 4) {
            if (colors[i] < 1.0) {
                isTransparent = true;
                break;
            }
        }
        // For vertex-colored meshes, the bind group layout is just the light BGL
        meshBindGroupLayout = lightBindGroupLayout;
        pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [frameBindGroupLayout, lightBindGroupLayout] });
    }

    const shaderModule = device.createShaderModule({ code: shaderCode });

    const vertexBufferLayout = [
        { arrayStride: 12, attributes: [{ shaderLocation: 0, offset: 0, format: 'float32x3' }] }
    ];

    if (!singleColor) {
        vertexBufferLayout.push({
            arrayStride: 16,
            attributes: [{ shaderLocation: 1, offset: 0, format: 'float32x4' }]
        });
    }
    vertexBufferLayout.push({
        arrayStride: 12,
        attributes: [{ shaderLocation: singleColor ? 1 : 2, offset: 0, format: 'float32x3' }]
    });

    const pipeline = await device.createRenderPipelineAsync({
        label: `Mesh ${id} Pipeline`,
        layout: pipelineLayout,
        vertex: { module: shaderModule, entryPoint: 'vertexMain', buffers: vertexBufferLayout },
        fragment: {
            module: shaderModule,
            entryPoint: 'fragmentMain',
            targets: [{
                format: `${colorFormat}-srgb`,
                blend: {
                    color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                    alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' }
                }
            }]
        },
        depthStencil: {
            format: depthFormat,
            depthWriteEnabled: !isTransparent,
            depthCompare: 'less-equal'
        },
        multisample: { count: sampleCount },
        primitive: { topology: 'triangle-list', cullMode: 'back' }
    });

    meshes.push({
        id,
        center, // Store center for sorting
        vertexBuffer,
        colorBuffer,
        primitiveSurfaceNormalBuffer,
        primitiveSurfaceNormals: suppliedPrimitiveSurfaceNormals,
        indexBuffer,
        indexFormat,
        bindGroup,
        singleColor,
        isTransparent,
        visible: meshFacesVisible,
        indexCount: indices.length,
        pipeline
    });
}

export function addMeshes(meshArray) {
    return enqueueGpuOperation(() => addMeshesCore(meshArray));
}

async function addMeshesCore(meshArray) {
    for (const mesh of meshArray) {
        await addMeshCore(mesh);
    }
}

export function removeMeshes(meshIds) {
    return enqueueGpuOperation(() => removeMeshesCore(meshIds));
}

function removeMeshesCore(meshIds) {
    for (const id of meshIds) {
        const index = meshes.findIndex(mesh => mesh.id === id);
        if (index >= 0) removeMeshCore(index);
    }
}

export function setMeshesVisible(meshIds, visible) {
    return enqueueGpuOperation(() => setMeshesVisibleCore(meshIds, visible));
}

function setMeshesVisibleCore(meshIds, visible) {
    const ids = new Set(meshIds);
    for (const mesh of meshes) {
        if (ids.has(mesh.id)) mesh.visible = visible;
    }
}

export function setMeshFaceDisplay(visible, useSurfaceNormals) {
    return enqueueGpuOperation(() => setMeshFaceDisplayCore(visible, useSurfaceNormals));
}

function setMeshFaceDisplayCore(visible, useSurfaceNormals) {
    const gpuDevice = requireDevice('updating mesh face display');
    meshFacesVisible = Boolean(visible);
    usePrimitiveSurfaceNormals = Boolean(useSurfaceNormals);
    for (const mesh of meshes) {
        mesh.visible = meshFacesVisible;
        const normals = usePrimitiveSurfaceNormals
            ? mesh.primitiveSurfaceNormals
            : new Float32Array(mesh.primitiveSurfaceNormals.length);
        gpuDevice.queue.writeBuffer(mesh.primitiveSurfaceNormalBuffer, 0, normals);
    }
}

export function removeMesh(index) {
    return enqueueGpuOperation(() => removeMeshCore(index));
}

function removeMeshCore(index) {
    const mesh = meshes[index];
    if (!mesh) return;
    mesh.vertexBuffer?.destroy();
    mesh.colorBuffer?.destroy();
    mesh.primitiveSurfaceNormalBuffer?.destroy();
    mesh.indexBuffer?.destroy();
    meshes.splice(index, 1);
}

export function changeMeshColor(colorChangeData) {
    return enqueueGpuOperation(() => changeMeshColorCore(colorChangeData));
}

function changeMeshColorCore(colorChangeData) {
    const gpuDevice = requireDevice('changing a mesh color');
    const { index, color } = colorChangeData;
    const mesh = meshes[index];
    if (mesh && mesh.singleColor && mesh.colorBuffer) {
        gpuDevice.queue.writeBuffer(mesh.colorBuffer, 0, new Float32Array(color));
        if (color.length >= 4) {
            mesh.isTransparent = color[3] < 1.0;
        }
    }
}

export function changeMeshColors(colorChangeData) {
    return enqueueGpuOperation(() => changeMeshColorsCore(colorChangeData));
}

function changeMeshColorsCore(colorChangeData) {
    const gpuDevice = requireDevice('changing per-triangle mesh colors');
    const { meshId, colors } = colorChangeData;
    const mesh = meshes.find(candidate => candidate.id === meshId);
    if (!mesh) throw new Error(`Mesh '${meshId}' was not found in the WebGPU scene.`);
    if (mesh.singleColor || !mesh.colorBuffer)
        throw new Error(`Mesh '${meshId}' was not created with per-triangle coloring.`);

    gpuDevice.queue.writeBuffer(mesh.colorBuffer, 0, new Float32Array(colors));
    mesh.isTransparent = false;
    for (let i = 3; i < colors.length; i += 4) {
        if (colors[i] < 1.0) {
            mesh.isTransparent = true;
            break;
        }
    }
}

export function clearAllMeshes() {
    return enqueueGpuOperation(clearAllMeshesCore);
}

function clearAllMeshesCore() {
    for (const mesh of meshes) {
        mesh.vertexBuffer?.destroy();
        mesh.colorBuffer?.destroy();
        mesh.primitiveSurfaceNormalBuffer?.destroy();
        mesh.indexBuffer?.destroy();
    }
    meshes.length = 0;
}

export function addLines(lineData) {
    return enqueueGpuOperation(() => addLinesCore(lineData));
}

async function addLinesCore(lineData) {
    requireDevice(`adding line '${lineData.id}'`);
    const { id, segments, center } = lineData;
    const instanceCount = segments.length / 12;
    if (instanceCount === 0) return;

    const instanceBuffer = createBuffer(segments, GPUBufferUsage.VERTEX);

    lines.push({
        id,
        center,
        instanceBuffer,
        instanceCount
    });
}

export function addLinesBatch(lineDataArray) {
    return enqueueGpuOperation(() => addLinesBatchCore(lineDataArray));
}

async function addLinesBatchCore(lineDataArray) {
    for (const lineData of lineDataArray) {
        await addLinesCore(lineData);
    }
}

export function removeLines(lineId) {
    return enqueueGpuOperation(() => removeLinesCore(lineId));
}

function removeLinesCore(lineId) {
    const index = lines.findIndex(candidate => candidate.id === lineId);
    if (index < 0) return;
    const line = lines[index];
    line.instanceBuffer?.destroy();
    lines.splice(index, 1);
}

export function removeLinesBatch(lineIds) {
    return enqueueGpuOperation(() => removeLinesBatchCore(lineIds));
}

function removeLinesBatchCore(lineIds) {
    for (const id of lineIds) removeLinesCore(id);
}

export function pauseRendering() {
    return enqueueGpuOperation(pauseRenderingCore);
}

function pauseRenderingCore() {
    renderingPaused = true;
}

export function resumeRendering() {
    return enqueueGpuOperation(resumeRenderingCore);
}

function resumeRenderingCore() {
    renderingPaused = false;
}

export function clearAllLines() {
    return enqueueGpuOperation(clearAllLinesCore);
}

function clearAllLinesCore() {
    for (const line of lines) {
        line.instanceBuffer?.destroy();
    }
    lines.length = 0;
}

export function addTextBillboard(billboardData) {
    return enqueueGpuOperation(() => addTextBillboardCore(billboardData));
}

async function addTextBillboardCore(billboardData) {
    requireDevice(`adding text billboard '${billboardData.id}'`);
    const { id, text, position, backgroundColor, textColor, scale = 0.5, relativeX = 0.5, relativeY = 0.5 } = billboardData;
    const anchorX = Math.min(1, Math.max(0, relativeX));
    const anchorY = Math.min(1, Math.max(0, relativeY));

    // Create a canvas to render the text
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    ctx.font = 'bold 24px sans-serif';
    const textMetrics = ctx.measureText(text);
    canvas.width = Math.ceil(textMetrics.width) + 20;
    canvas.height = 30;
    const aspectRatio = canvas.width / canvas.height;

    // Background
    ctx.fillStyle = `rgba(${Math.floor(backgroundColor[0] * 255)}, ${Math.floor(backgroundColor[1] * 255)}, ${Math.floor(backgroundColor[2] * 255)}, ${backgroundColor[3]})`;
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    // Text
    ctx.fillStyle = `rgba(${Math.floor(textColor[0] * 255)}, ${Math.floor(textColor[1] * 255)}, ${Math.floor(textColor[2] * 255)}, ${textColor[3]})`;
    ctx.font = 'bold 24px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(text, canvas.width / 2, canvas.height / 2);

    // Create ImageBitmap for reliable texture copying
    const bitmap = await createImageBitmap(canvas);

    const texture = device.createTexture({
        size: [canvas.width, canvas.height],
        format: 'rgba8unorm',
        usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST | GPUTextureUsage.RENDER_ATTACHMENT
    });

    device.queue.copyExternalImageToTexture(
        { source: bitmap, flipY: true },
        { texture, premultipliedAlpha: false },
        [canvas.width, canvas.height]
    );

    bitmap.close();

    // Create billboard geometry
    const vertices = new Float32Array([
        position[0], position[1], position[2], 0, 1, aspectRatio, scale, anchorX, anchorY,
        position[0], position[1], position[2], 1, 1, aspectRatio, scale, anchorX, anchorY,
        position[0], position[1], position[2], 0, 0, aspectRatio, scale, anchorX, anchorY,
        position[0], position[1], position[2], 1, 0, aspectRatio, scale, anchorX, anchorY,
    ]);

    const vertexBuffer = createBuffer(vertices, GPUBufferUsage.VERTEX);
    const indexBuffer = createBuffer(new Uint16Array([0, 1, 2, 1, 3, 2]), GPUBufferUsage.INDEX, Uint16Array);

    const sampler = device.createSampler({
        magFilter: 'linear',
        minFilter: 'linear',
        addressModeU: 'clamp-to-edge',
        addressModeV: 'clamp-to-edge'
    });

    const bindGroupLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: {} }
        ]
    });

    const bindGroup = device.createBindGroup({
        layout: bindGroupLayout,
        entries: [
            { binding: 0, resource: sampler },
            { binding: 1, resource: texture.createView() }
        ]
    });

    const shaderModule = device.createShaderModule({ code: BILLBOARD_SHADER });

    const pipeline = await device.createRenderPipelineAsync({
        layout: device.createPipelineLayout({ bindGroupLayouts: [frameBindGroupLayout, bindGroupLayout] }),
        vertex: {
            module: shaderModule,
            entryPoint: 'vertexMain',
            buffers: [{
                arrayStride: 36,
                attributes: [
                    { shaderLocation: 0, offset: 0, format: 'float32x3' },
                    { shaderLocation: 1, offset: 12, format: 'float32x2' },
                    { shaderLocation: 2, offset: 20, format: 'float32' },
                    { shaderLocation: 3, offset: 24, format: 'float32' },
                    { shaderLocation: 4, offset: 28, format: 'float32x2' }
                ]
            }]
        },
        fragment: {
            module: shaderModule,
            entryPoint: 'fragmentMain',
            targets: [{
                format: `${colorFormat}-srgb`,
                blend: {
                    color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                    alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' }
                }
            }]
        },
        depthStencil: {
            format: depthFormat,
            depthWriteEnabled: false, // Transparent objects test depth but don't write to it
            depthCompare: 'less-equal'
        },
        multisample: { count: sampleCount }
    });

    textBillboards.push({
        id,
        position, // Store position for sorting
        vertexBuffer,
        indexBuffer,
        bindGroup,
        texture,
        sampler,
        indexCount: 6,
        pipeline
    });
}

export function removeTextBillboard(index) {
    return enqueueGpuOperation(() => removeTextBillboardCore(index));
}

function removeTextBillboardCore(index) {
    const billboard = textBillboards[index];
    if (!billboard) return;
    billboard.vertexBuffer?.destroy();
    billboard.indexBuffer?.destroy();
    billboard.texture?.destroy();
    textBillboards.splice(index, 1);
}

export function clearAllTextBillboards() {
    return enqueueGpuOperation(clearAllTextBillboardsCore);
}

function clearAllTextBillboardsCore() {
    for (const billboard of textBillboards) {
        billboard.vertexBuffer?.destroy();
        billboard.indexBuffer?.destroy();
        billboard.texture?.destroy();
    }
    textBillboards.length = 0;
}

// ============================================================================
// Frame Timing Callback
// ============================================================================

let frameIntervalId = 0;

function startFrameTimer() {
    frameIntervalId = setInterval(() => {
        let avg = 0;
        for (const v of frameMs) {
            if (v === undefined) return;
            avg += v;
        }
        const ms = avg / frameMs.length;
        notifyDotNet('OnFrameMsUpdate', ms);
    }, 1000);
}

// ============================================================================
// Utility Functions
// ============================================================================

function transformPoint(point, matrix) {
    const x = point[0], y = point[1], z = point[2];
    const w = matrix[3] * x + matrix[7] * y + matrix[11] * z + matrix[15] || 1.0;
    return [
        (matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12]) / w,
        (matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13]) / w,
        (matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14]) / w
    ];
}

function createBuffer(data, usage, ArrayType = Float32Array, operation = 'creating a GPU buffer') {
    const gpuDevice = requireDevice(operation);
    const typedArray = data instanceof ArrayType ? data : new ArrayType(data);
    // Align buffer size to 4 bytes because createBuffer with mappedAtCreation=true
    // requires the size to be a multiple of 4 on many WebGPU implementations.
    const byteLength = typedArray.byteLength;
    const alignedSize = (byteLength + 3) & ~3; // round up to next multiple of 4

    const buffer = gpuDevice.createBuffer({
        size: alignedSize,
        usage,
        mappedAtCreation: true
    });

    // Copy raw bytes into the mapped range. Use Uint8Array so this works for any typed array.
    const mappedRange = buffer.getMappedRange();
    new Uint8Array(mappedRange).set(new Uint8Array(typedArray.buffer, typedArray.byteOffset, typedArray.byteLength));
    buffer.unmap();
    return buffer;
}

export function getBoundingClientRect(element) {
    const rect = element.getBoundingClientRect();
    return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height
    };
}

// ============================================================================
// Cleanup
// ============================================================================

export function disposeWebGPU_Canvas() {
    isDisposing = true;
    return enqueueGpuOperation(disposeWebGPUCanvasCore);
}

function disposeWebGPUCanvasCore() {
        resizeObserver?.disconnect();
        resizeObserver = null;
        if (renderFrameId) {
            cancelAnimationFrame(renderFrameId);
            renderFrameId = 0;
        }
        if (frameIntervalId) {
            clearInterval(frameIntervalId);
            frameIntervalId = 0;
        }

        clearAllMeshesCore();
        clearAllLinesCore();
        clearAllTextBillboardsCore();

        coordinateAxes?.posBuffer?.destroy();
        coordinateAxes?.colorBuffer?.destroy();
        coordinateAxes?.thicknessBuffer?.destroy();
        coordinateAxes?.uvBuffer?.destroy();
        coordinateAxes?.endPosBuffer?.destroy();
        coordinateAxes?.fadeBuffer?.destroy();
        coordinateAxes?.indexBuffer?.destroy();
        gridVertexBuffer?.destroy();
        gridIndexBuffer?.destroy();
        gridUniformBuffer?.destroy();
        backgroundGradientUniformBuffer?.destroy();
        lightUniformBuffer?.destroy();
        frameUniformBuffer?.destroy();
        msaaColorTexture?.destroy();
        depthTexture?.destroy();

        coordinateAxes = null;
        gridVertexBuffer = null;
        gridIndexBuffer = null;
        gridUniformBuffer = null;
        gridBindGroup = null;
        gridBindGroupLayout = null;
        gridPipeline = null;
        backgroundGradientUniformBuffer = null;
        backgroundGradientBindGroup = null;
        backgroundGradientPipeline = null;
        lightUniformBuffer = null;
        lightBindGroup = null;
        lightBindGroupLayout = null;
        frameUniformBuffer = null;
        frameBindGroup = null;
        frameBindGroupLayout = null;
        msaaColorTexture = null;
        depthTexture = null;
        colorAttachment = null;
        renderPassDescriptor = null;
        instancedLinePipeline = null;
        renderingPaused = true;
        context?.unconfigure?.();
        device?.destroy();
        device = null;
        context = null;
        canvas = null;
        dotNetRef = null;
}

// Minimal WebGPU canvas module for Blazor WebAssembly
// All business logic is in C# - this file only handles WebGPU API calls

// ============================================================================
// Constants & Shaders
// ============================================================================

const FRAME_BUFFER_SIZE = Float32Array.BYTES_PER_ELEMENT * 32; // projection + view matrices

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
  struct Camera { projection: mat4x4f, view: mat4x4f }
  @group(0) @binding(0) var<uniform> camera: Camera;
  struct GridArgs { lineColor: vec4f, baseColor: vec4f, lineWidth: vec2f, spacing: f32 }
  @group(1) @binding(0) var<uniform> gridArgs: GridArgs;
  @vertex fn vertexMain(in: VertexIn) -> VertexOut { var out: VertexOut; out.pos = camera.projection * camera.view * vec4f(in.pos, 1.0); out.uv = in.uv - vec2f(50.0, 50.0); return out; }
  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f { var grid = PristineGrid(in.uv * gridArgs.spacing, gridArgs.lineWidth); return mix(gridArgs.baseColor, gridArgs.lineColor, grid); }
`;

const MESH_SHADER = `
  struct Camera { projection: mat4x4f, view: mat4x4f }
  @group(0) @binding(0) var<uniform> camera: Camera;

  struct LightUniforms {
    lightDir: vec3f,
    ambient: f32,
    specularPower: f32
  }
  @group(1) @binding(0) var<uniform> light: LightUniforms;

  struct MeshUniforms { color: vec4f }
  @group(1) @binding(1) var<uniform> meshUniforms: MeshUniforms;

  struct VertexIn { @location(0) pos: vec3f }
  struct VertexOut { @builtin(position) pos: vec4f, @location(0) worldPos: vec3f }

  @vertex fn vertexMain(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    out.pos = camera.projection * camera.view * vec4f(in.pos, 1.0);
    out.worldPos = in.pos;
    return out;
  }

  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    let normal = normalize(cross(dpdx(in.worldPos), dpdy(in.worldPos)));
    let lightDir = normalize(light.lightDir);

    // View space position and view direction (camera at origin in view space)
    let viewPos = (camera.view * vec4f(in.worldPos, 1.0)).xyz;
    let viewDir = normalize(-viewPos);
    let halfDir = normalize(lightDir + viewDir);

    // Diffuse
    let diffuse = max(dot(normal, lightDir), 0.0);
    // Specular
    let specAngle = max(dot(normal, halfDir), 0.0);
    let specular = pow(specAngle, light.specularPower);

    let finalColor = meshUniforms.color.rgb * (light.ambient + diffuse) + vec3f(1.0) * specular;
    return vec4f(finalColor, meshUniforms.color.a);
  }
`;

const MESH_SHADER_VERTEX_COLOR = `
  struct Camera { projection: mat4x4f, view: mat4x4f }
  @group(0) @binding(0) var<uniform> camera: Camera;

  struct LightUniforms {
    lightDir: vec3f,
    ambient: f32,
    specularPower: f32
  }
  @group(1) @binding(0) var<uniform> light: LightUniforms;

  struct VertexIn {
    @location(0) pos: vec3f,
    @location(1) color: vec4f
  }
  struct VertexOut {
    @builtin(position) pos: vec4f,
    @location(0) worldPos: vec3f,
    @location(1) @interpolate(flat) color: vec4f
  }
  @vertex fn vertexMain(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    out.pos = camera.projection * camera.view * vec4f(in.pos, 1.0);
    out.worldPos = in.pos;
    out.color = in.color;
    return out;
  }
  @fragment fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    let normal = normalize(cross(dpdx(in.worldPos), dpdy(in.worldPos)));
    let lightDir = normalize(light.lightDir);

    // View space position and view direction
    let viewPos = (camera.view * vec4f(in.worldPos, 1.0)).xyz;
    let viewDir = normalize(-viewPos);
    let halfDir = normalize(lightDir + viewDir);

    let diffuse = max(dot(normal, lightDir), 0.0);
    let specAngle = max(dot(normal, halfDir), 0.0);
    let specular = pow(specAngle, light.specularPower);

    let finalColor = in.color.rgb * (light.ambient + diffuse) + vec3f(1.0) * specular;
    return vec4f(finalColor, in.color.a);
  }
`;

const BILLBOARD_LINE_SHADER = `
  struct Camera { projection: mat4x4f, view: mat4x4f }
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

const BILLBOARD_SHADER = `
  struct Camera { projection: mat4x4f, view: mat4x4f }
  @group(0) @binding(0) var<uniform> camera: Camera;
  @group(1) @binding(0) var sampler0: sampler;
  @group(1) @binding(1) var texture0: texture_2d<f32>;
  struct VertexIn { @location(0) pos: vec3f, @location(1) uv: vec2f }
  struct VertexOut { @builtin(position) pos: vec4f, @location(0) uv: vec2f }
  @vertex fn vertexMain(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    let size = 1.0;
    let offset = vec3f((in.uv.x - 0.5) * 2.0 * size, (in.uv.y - 0.5) * 2.0 * size, 0.0);
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

// Frame timing
const frameMs = new Array(20);
let frameMsIndex = 0;

// Matrices
const frameArrayBuffer = new ArrayBuffer(FRAME_BUFFER_SIZE);
const projectionMatrix = new Float32Array(frameArrayBuffer, 0, 16);
const viewMatrix = new Float32Array(frameArrayBuffer, 16 * Float32Array.BYTES_PER_ELEMENT, 16);

// GPU resources
let frameUniformBuffer = null;
let frameBindGroupLayout = null;
let frameBindGroup = null;

// Render targets
let msaaColorTexture = null;
let depthTexture = null;
let colorAttachment = null;
let renderPassDescriptor = null;

// Lighting resources
let lightUniformArray = new ArrayBuffer(8 * Float32Array.BYTES_PER_ELEMENT); // 3 (vec3f) + 1 (f32) + 1 (f32) + 3 padding
const lightDirection = new Float32Array(lightUniformArray, 0, 3);
const lightAmbient = new Float32Array(lightUniformArray, 12, 1);
const lightSpecularPower = new Float32Array(lightUniformArray, 16, 1);
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
let clearColor = { r: 0, g: 0, b: 0, a: 1.0 };

// Scene objects (maintained in sync with C#)
const meshes = [];
const lines = [];
const textBillboards = [];

// ============================================================================
// Initialization
// ============================================================================

export async function initGPU_Canvas(dotnet, canvasEl, options, initialViewMatrix) {
    dotNetRef = dotnet;
    canvas = canvasEl;
    context = canvas.getContext('webgpu');

    colorFormat = navigator.gpu?.getPreferredCanvasFormat?.() || 'bgra8unorm';

    // Set initial view matrix from parameter
    viewMatrix.set(initialViewMatrix);

    // Apply options
    await updateDisplayOptions(options);

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
        dotNetRef.invokeMethodAsync('OnWebGpuReady');
    } catch (error) {
        dotNetRef.invokeMethodAsync('OnWebGpuError', error.message);
        throw error;
    }
}

async function initWebGPU() {
    const adapter = await navigator.gpu.requestAdapter();
    const requiredFeatures = [];
    if (adapter.features.has('texture-compression-bc')) requiredFeatures.push('texture-compression-bc');
    if (adapter.features.has('texture-compression-etc2')) requiredFeatures.push('texture-compression-etc2');

    device = await adapter.requestDevice({ requiredFeatures });
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


    await initGrid();
    await initCoordinateAxes();
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

function startRenderLoop() {
    function frameCallback() {
        requestAnimationFrame(frameCallback);
        const frameStart = performance.now();

        device.queue.writeBuffer(frameUniformBuffer, 0, frameArrayBuffer);
        renderFrame();

        frameMs[frameMsIndex++ % frameMs.length] = performance.now() - frameStart;
    }

    requestAnimationFrame(frameCallback);
}

function renderFrame() {
    const renderPass = getRenderPassDescriptor();
    if (!renderPass) return; // Skip frame if render targets aren't ready

    const encoder = device.createCommandEncoder();
    const pass = encoder.beginRenderPass(renderPass);

    // ========================================================================
    // 1. Opaque Pass: Draw all opaque objects first.
    // Depth test and depth write are enabled.
    // ========================================================================

    // Draw opaque meshes
    for (const mesh of meshes.filter(m => !m.isTransparent)) {
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
        if (!mesh.singleColor) {
            pass.setBindGroup(1, lightBindGroup);
        }

        pass.setIndexBuffer(mesh.indexBuffer, 'uint16');
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
    for (const mesh of meshes.filter(m => m.isTransparent)) {
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
                pass.setIndexBuffer(mesh.indexBuffer, 'uint16');
                pass.drawIndexed(mesh.indexCount);
            }
        });
    }

    // Add lines
    for (const line of lines) {
        if (!line.pipeline || !line.posBuffer || !line.indexBuffer) continue;
        const viewSpacePos = transformPoint(line.center, viewMatrix);
        transparentDrawables.push({
            depth: viewSpacePos[2],
            draw: () => {
                pass.setPipeline(line.pipeline);
                pass.setBindGroup(0, frameBindGroup);
                pass.setVertexBuffer(0, line.posBuffer);
                pass.setVertexBuffer(1, line.colorBuffer);
                pass.setVertexBuffer(2, line.thicknessBuffer);
                pass.setVertexBuffer(3, line.uvBuffer);
                pass.setVertexBuffer(4, line.endPosBuffer);
                pass.setVertexBuffer(5, line.fadeBuffer);
                pass.setIndexBuffer(line.indexBuffer, 'uint16');
                pass.drawIndexed(line.indexCount);
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
    const observer = new ResizeObserver((entries) => {
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

            canvas.width = width;
            canvas.height = height;

            // Notify C# to recompute projection matrix
            dotNetRef?.invokeMethodAsync('OnCanvasResized', width, height);

            if (device) {
                allocateRenderTargets(width, height);
            }
        }
    });

    observer.observe(canvas);
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
        clearValue: clearColor,
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

export function writeViewMatrix(matrixArray) {
    viewMatrix.set(matrixArray);
}

export function writeProjectionMatrix(matrixArray) {
    projectionMatrix.set(matrixArray);
}

export async function updateDisplayOptions(options) {
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


    // Update clear color
    if (options.clearColor) {
        clearColor = options.clearColor;
        if (colorAttachment) colorAttachment.clearValue = clearColor;
    }
}

// ============================================================================
// Scene Management (Mesh, Lines, Billboards)
// ============================================================================

export async function addMesh(meshData) {
    const { id, vertices, indices, colors, singleColor } = meshData;

    const vertexBuffer = createBuffer(vertices, GPUBufferUsage.VERTEX);
    const indexBuffer = createBuffer(indices, GPUBufferUsage.INDEX, Uint16Array);

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
        colorBuffer = createBuffer(colors, GPUBufferUsage.VERTEX);
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
        indexBuffer,
        bindGroup,
        singleColor,
        isTransparent,
        indexCount: indices.length,
        pipeline
    });
}

export function removeMesh(index) {
    const mesh = meshes[index];
    if (!mesh) return;
    mesh.vertexBuffer?.destroy();
    mesh.colorBuffer?.destroy();
    mesh.indexBuffer?.destroy();
    meshes.splice(index, 1);
}

export function changeMeshColor(colorChangeData) {
    const { index, color } = colorChangeData;
    const mesh = meshes[index];
    if (mesh && mesh.singleColor && mesh.colorBuffer) {
        device.queue.writeBuffer(mesh.colorBuffer, 0, new Float32Array(color));
        if (color.length >= 4) {
            mesh.isTransparent = color[3] < 1.0;
        }
    }
}

export function clearAllMeshes() {
    for (const mesh of meshes) {
        mesh.vertexBuffer?.destroy();
        mesh.colorBuffer?.destroy();
        mesh.indexBuffer?.destroy();
    }
    meshes.length = 0;
}

export async function addLines(lineData) {
    const { id, vertices, thickness, colors, fades } = lineData;

    // Calculate center for sorting
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

    // Geometry buffers are created from pre-computed data from C#
    const posBuffer = createBuffer(vertices, GPUBufferUsage.VERTEX);
    const colorBuffer = createBuffer(colors, GPUBufferUsage.VERTEX);
    const thicknessBuffer = createBuffer(thickness, GPUBufferUsage.VERTEX);
    const uvBuffer = createBuffer(lineData.uvs, GPUBufferUsage.VERTEX);
    const endPosBuffer = createBuffer(lineData.endPositions, GPUBufferUsage.VERTEX);
    const fadeBuffer = createBuffer(fades, GPUBufferUsage.VERTEX);
    const indexBuffer = createBuffer(lineData.indices, GPUBufferUsage.INDEX, Uint16Array);

    const shaderModule = device.createShaderModule({ label: `Line ${id} Shader`, code: BILLBOARD_LINE_SHADER });

    const vertexBufferLayout = [
        { arrayStride: 12, attributes: [{ shaderLocation: 0, offset: 0, format: 'float32x3' }] },
        { arrayStride: 16, attributes: [{ shaderLocation: 1, offset: 0, format: 'float32x4' }] },
        { arrayStride: 4, attributes: [{ shaderLocation: 2, offset: 0, format: 'float32' }] },
        { arrayStride: 8, attributes: [{ shaderLocation: 3, offset: 0, format: 'float32x2' }] },
        { arrayStride: 12, attributes: [{ shaderLocation: 4, offset: 0, format: 'float32x3' }] },
        { arrayStride: 4, attributes: [{ shaderLocation: 5, offset: 0, format: 'float32' }] }
    ];

    const pipeline = await device.createRenderPipelineAsync({
        label: `Line ${id} Pipeline`,
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
            depthWriteEnabled: false, // Transparent objects test depth but don't write to it
            depthCompare: 'less-equal'
        },
        multisample: { count: sampleCount },
        primitive: { topology: 'triangle-list', cullMode: 'none' }
    });

    lines.push({
        id,
        center, // Store center for sorting
        posBuffer,
        colorBuffer,
        thicknessBuffer,
        uvBuffer,
        endPosBuffer,
        fadeBuffer,
        indexBuffer,
        indexCount: lineData.indices.length,
        pipeline
    });
}

export function removeLines(index) {
    const line = lines[index];
    line.posBuffer?.destroy();
    line.colorBuffer?.destroy();
    line.thicknessBuffer?.destroy();
    line.uvBuffer?.destroy();
    line.endPosBuffer?.destroy();
    line.fadeBuffer?.destroy();
    line.indexBuffer?.destroy();
    lines.splice(index, 1);
}

export function clearAllLines() {
    for (const line of lines) {
        line.posBuffer?.destroy();
        line.colorBuffer?.destroy();
        line.thicknessBuffer?.destroy();
        line.uvBuffer?.destroy();
        line.endPosBuffer?.destroy();
        line.fadeBuffer?.destroy();
        line.indexBuffer?.destroy();
    }
    lines.length = 0;
}

export async function addTextBillboard(billboardData) {
    const { id, text, position, backgroundColor, textColor } = billboardData;

    // Create a canvas to render the text
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    ctx.font = 'bold 24px sans-serif';
    const textMetrics = ctx.measureText(text);
    canvas.width = Math.ceil(textMetrics.width) + 20;
    canvas.height = 30;

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
        position[0], position[1], position[2], 0, 1,
        position[0], position[1], position[2], 1, 1,
        position[0], position[1], position[2], 0, 0,
        position[0], position[1], position[2], 1, 0,
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
                arrayStride: 20,
                attributes: [
                    { shaderLocation: 0, offset: 0, format: 'float32x3' },
                    { shaderLocation: 1, offset: 12, format: 'float32x2' }
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
    const billboard = textBillboards[index];
    billboard.vertexBuffer?.destroy();
    billboard.indexBuffer?.destroy();
    billboard.texture?.destroy();
    textBillboards.splice(index, 1);
}

export function clearAllTextBillboards() {
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
        dotNetRef?.invokeMethodAsync('OnFrameMsUpdate', ms);
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

function createBuffer(data, usage, ArrayType = Float32Array) {
    const typedArray = data instanceof ArrayType ? data : new ArrayType(data);
    // Align buffer size to 4 bytes because createBuffer with mappedAtCreation=true
    // requires the size to be a multiple of 4 on many WebGPU implementations.
    const byteLength = typedArray.byteLength;
    const alignedSize = (byteLength + 3) & ~3; // round up to next multiple of 4

    const buffer = device.createBuffer({
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
    if (frameIntervalId) {
        clearInterval(frameIntervalId);
        frameIntervalId = 0;
    }

    // Clean up all GPU resources
    clearAllMeshes();
    clearAllLines();
    clearAllTextBillboards();

    gridVertexBuffer?.destroy();
    gridIndexBuffer?.destroy();
    gridUniformBuffer?.destroy();
    frameUniformBuffer?.destroy();
    msaaColorTexture?.destroy();
    depthTexture?.destroy();

    device = null;
    dotNetRef = null;
}

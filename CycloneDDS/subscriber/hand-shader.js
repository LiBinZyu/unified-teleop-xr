import { Color, Matrix4, MeshBasicMaterial, NearestFilter, ShaderMaterial, SRGBColorSpace, TextureLoader, Vector3, Vector4 } from "three";
import materialSource from "./assets/reference/Unity_Hand_Medium.mat";
import fingerMaskSource from "./assets/reference/CombinedFingerIndexHighlight.png";

function property(name) {
  const value = materialSource.match(new RegExp(`^\\s*- ${name}: (.+)$`, "m"))?.[1];
  if (!value) throw new Error(`Missing ${name} in Unity_Hand_Medium.mat`);
  return value;
}

function channels(name) {
  return Array.from(property(name).matchAll(/[rgba]:\s*([-\d.e+]+)/gi), ([, value]) => Number(value));
}

function color(name) {
  const [r, g, b, a] = channels(name);
  const linear = new Color().setRGB(r, g, b, SRGBColorSpace);
  return new Vector4(linear.r, linear.g, linear.b, a);
}

export function loadFingerMask(onLoad) {
  const texture = new TextureLoader().load(fingerMaskSource, onLoad);
  // Red is a bit mask. Point sampling and no color conversion preserve its bits.
  texture.magFilter = texture.minFilter = NearestFilter;
  texture.generateMipmaps = false;
  return texture;
}

const vertexShader = /* glsl */ `
  #include <common>
  #include <skinning_pars_vertex>

  uniform mat4 worldToWrist;
  varying vec2 handUv;
  varying vec3 wristPositionMeters;
  varying vec3 handViewNormal;
  varying vec3 handViewPosition;

  void main() {
    #include <beginnormal_vertex>
    #include <skinbase_vertex>
    #include <skinnormal_vertex>
    #include <defaultnormal_vertex>
    #include <begin_vertex>
    #include <skinning_vertex>
    #include <project_vertex>

    handUv = uv;
    handViewNormal = transformedNormal;
    handViewPosition = mvPosition.xyz;
    // FBX bone space is in centimeters. A wrist-relative fade follows the tracked hand.
    wristPositionMeters = (worldToWrist * modelMatrix * vec4(transformed, 1.0)).xyz * 0.01;
  }
`;

const fragmentShader = /* glsl */ `
  uniform vec4 mainColor;
  uniform vec4 edgeColor;
  uniform float edgePower;
  uniform vec3 fadeCenter;
  uniform vec3 fadeScale;
  uniform float fadeStart;
  uniform float fadeSize;
  uniform float noiseScale;
  uniform float noiseStrength;
  uniform sampler2D fingerMask;
  uniform vec4 fingerColors[5];

  varying vec2 handUv;
  varying vec3 wristPositionMeters;
  varying vec3 handViewNormal;
  varying vec3 handViewPosition;

  // Deterministic 2D hash used by the Shader Graph Simple Noise node.
  float noiseHash(vec2 cell) {
    uvec2 bits = uvec2(ivec2(cell));
    bits.y ^= 1103515245u;
    uint n = (bits.x + bits.y) * bits.y;
    n ^= n >> 5u;
    n *= 0x27d4eb2du;
    return float(n >> 8u) / 16777215.0;
  }

  float valueNoise(vec2 uv) {
    vec2 cell = floor(uv);
    vec2 f = fract(uv);
    f = f * f * (3.0 - 2.0 * f);
    float bottom = mix(noiseHash(cell), noiseHash(cell + vec2(1.0, 0.0)), f.x);
    float top = mix(noiseHash(cell + vec2(0.0, 1.0)), noiseHash(cell + vec2(1.0, 1.0)), f.x);
    return mix(bottom, top, f.y);
  }

  void main() {
    // EdgeHighlight, shared by RimLight and Unity_Hand_Noise.
    float facing = clamp(dot(normalize(handViewNormal), normalize(-handViewPosition)), 0.0, 1.0);
    vec4 rim = mix(mainColor, edgeColor, pow(1.0 - facing, edgePower));

    // FadeFromCenterWithNoise uses a reversed smoothstep in Shader Graph.
    vec2 noiseUv = handUv * noiseScale;
    float noise = 0.125 * valueNoise(noiseUv)
      + 0.25 * valueNoise(noiseUv * 0.5)
      + 0.5 * valueNoise(noiseUv * 0.25);
    float distanceToCenter = distance(wristPositionMeters / fadeScale, fadeCenter);
    float fade = 1.0 - smoothstep(0.0, 1.0,
      (distanceToCenter - fadeStart) / fadeSize + (2.0 * noise - 1.0) * noiseStrength);

    // AllFingerHighlights packs one membership bit per finger in R and the strength in G.
    vec2 packedMask = texture2D(fingerMask, handUv).rg;
    int fingers = int(round(packedMask.r * 255.0));
    vec4 highlight = vec4(0.0);
    for (int i = 0; i < 5; i++) {
      if ((fingers & (1 << i)) != 0) highlight += packedMask.g * fingerColors[i];
    }

    gl_FragColor = vec4(mix(rim.rgb, highlight.rgb, highlight.a), max(rim.a * fade, highlight.a));
    #include <colorspace_fragment>
  }
`;

export function applyHandShader(object, wrist, fingerMask) {
  const surface = new ShaderMaterial({
    name: "Unity Hand Medium",
    uniforms: {
      worldToWrist: { value: new Matrix4() },
      mainColor: { value: color("_MainColor") },
      edgeColor: { value: color("_EdgeColor") },
      edgePower: { value: Number(property("_EdgeHighlightPower")) },
      fadeCenter: { value: new Vector3(...channels("_FadeCenter").slice(0, 3)) },
      fadeScale: { value: new Vector3(...channels("_FadeScale").slice(0, 3)) },
      fadeStart: { value: Number(property("_FadeStart")) },
      fadeSize: { value: Number(property("_FadeSize")) },
      noiseScale: { value: Number(property("_NoiseScale")) },
      noiseStrength: { value: Number(property("_NoiseStrength")) },
      fingerMask: { value: fingerMask },
      fingerColors: { value: ["_ThumbColor", "_FingerColor_1", "_FingerColor_2", "_FingerColor_3", "_FingerColor_4"].map(color) },
    },
    vertexShader,
    fragmentShader,
    transparent: true,
    depthWrite: false,
    toneMapped: false,
  });

  // DepthOnly.shader writes the skinned front surface before the transparent color pass.
  const depth = new MeshBasicMaterial({ name: "Hand Depth Only", transparent: true, colorWrite: false });
  const skinnedMeshes = [];
  object.traverse((node) => {
    if (node.isSkinnedMesh) skinnedMeshes.push(node);
  });
  for (const mesh of skinnedMeshes) {
    mesh.material = surface;
    mesh.frustumCulled = false;
    mesh.renderOrder = 2;
    mesh.onBeforeRender = () => surface.uniforms.worldToWrist.value.copy(wrist.matrixWorld).invert();

    const depthMesh = mesh.clone(false);
    depthMesh.material = depth;
    depthMesh.renderOrder = 1;
    mesh.parent.add(depthMesh);
  }
}

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drop-in GPU SPH replacement for the old CPU BucketParticleSystemCustom.
/// The class name stays BucketParticleSystemCustom, so existing references keep working.
/// Bucket flow, outlet shape, tilt flow, water amount, visible liquid, viscosity, and paint bridge are kept.
/// Density, pressure, viscosity force, neighbor grid, emission recycling, and integration run on the GPU.
/// </summary>
public class BucketParticleSystemCustom : MonoBehaviour
{
    private enum EmissionAxis
    {
        LocalForward,
        LocalBack,
        LocalUp,
        LocalDown,
        LocalRight,
        LocalLeft,
        WorldDown
    }

    private enum OutletShape
    {
        Circle,
        Line,
        MultipleHoles,
        Random
    }

    private struct GPUParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float density;
        public float pressure;
    }

    [Header("References")]
    [Tooltip("Assign BucketParticleSystemCustomGPU.compute here.")]
    [SerializeField] private ComputeShader sphCompute;

    [Tooltip("Material using Custom/GPUSPHParticleInstanced shader.")]
    [SerializeField] private Material particleMaterial;

    [SerializeField] private Mesh particleMesh;
    [SerializeField] private Transform emitter;
    [SerializeField] private Transform tiltReference;
    [SerializeField] private Camera renderCamera;

    [Header("Outlet Direction - No Collider")]
    [SerializeField] private EmissionAxis emissionAxis = EmissionAxis.LocalDown;
    [SerializeField] private Vector3 emissionLocalOffset = Vector3.zero;
    [SerializeField] private bool showOutletGizmo = true;
    [SerializeField, Min(0.01f)] private float outletGizmoLength = 0.35f;

    [Header("Outlet Shape Algorithm")]
    [SerializeField] private OutletShape outletShape = OutletShape.Circle;
    [SerializeField, Min(0f)] private float outletShapeRadius = 0.035f;
    [SerializeField, Min(0.001f)] private float lineOutletLength = 0.18f;
    [SerializeField] private Vector3 outletLineLocalDirection = Vector3.right;
    [SerializeField, Range(2, 12)] private int multipleHoleCount = 3;
    [SerializeField, Min(0.001f)] private float multipleHoleSpacing = 0.06f;
    [SerializeField, Range(0f, 1f)] private float randomOutletJitter = 0.75f;
    [SerializeField] private bool outletShapeAffectsFlowRate = true;
    [SerializeField] private bool outletShapeAffectsSpread = true;

    [Header("Manual Paint Canvas - GPU Bridge")]
    [SerializeField] private Transform paintCanvas;
    [SerializeField] private PaintCanvasDrawer paintCanvasDrawer;
    [SerializeField] private bool useManualCanvasCollision = true;
    [SerializeField] private bool useUnityPlaneLocalSize = true;
    [SerializeField, Min(0.01f)] private float canvasWidth = 10f;
    [SerializeField, Min(0.01f)] private float canvasLength = 10f;
    [SerializeField] private bool drawOnlyWhenParticleCenterReachesCanvas = true;
    [SerializeField] private bool drawOnCanvasTexture = true;
    [SerializeField] private bool removeParticleAfterDrawing = true;
    [SerializeField, Min(0f)] private float minimumImpactSpeedToDraw = 0f;
    [SerializeField] private bool forceRemoveParticleOnCanvasHit = true;
    [SerializeField] private bool absorbParticleOnCanvasHit = true;
    [SerializeField] private bool stickParticlesOnCanvas = false;
    [SerializeField, Min(0f)] private float paintMarkLift = 0.003f;

    [Header("GPU Paint Bridge")]
    [Tooltip("Draws paint marks from the GPU stream direction without reading 250k particles back to CPU every frame.")]
    [SerializeField] private bool useGpuPaintBridge = true;

    [SerializeField, Min(0.01f)] private float paintBridgeInterval = 0.035f;
    [SerializeField, Range(1, 12)] private int paintBridgeMarksPerTick = 3;
    [SerializeField, Min(0.001f)] private float paintBridgeMarkSizeMultiplier = 1.0f;
    [SerializeField, Min(0f)] private float paintBridgeJitterRadius = 0.06f;

    [Header("Bucket Water Amount")]
    [SerializeField] private bool fillBucketOnStart = true;
    [SerializeField, Min(0f)] private float initialWaterAmount = 100f;
    [SerializeField, Min(0f)] private float currentWaterAmount = 100f;
    [SerializeField, Min(0.000001f)] private float waterPerParticle = 0.02f;
    [SerializeField] private bool stopWhenBucketEmpty = true;

    [Header("Visible Liquid Inside Bucket")]
    [SerializeField] private bool updateBucketLiquidVisual = true;
    [SerializeField] private Transform bucketLiquidVisual;
    [SerializeField] private bool autoCreateLiquidVisualIfMissing = true;
    [SerializeField] private Vector3 autoLiquidLocalPosition = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 autoLiquidLocalScale = new Vector3(0.65f, 0.035f, 0.65f);
    [SerializeField] private float emptyLiquidLocalYOffset = -0.35f;
    [SerializeField, Range(0.001f, 1f)] private float emptyLiquidScaleYMultiplier = 0.03f;
    [SerializeField] private bool hideLiquidVisualWhenEmpty = true;
    [SerializeField] private bool liquidUsesPaintColor = true;
    [SerializeField] private Color liquidColor = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);
    [SerializeField, Range(0f, 0.98f)] private float liquidVisualSmoothing = 0.15f;

    [Header("Emission")]
    [SerializeField] private bool emissionEnabled = true;
    [SerializeField, Min(1)] private int maxParticles = 250000;
    [SerializeField, Min(0f)] private float emissionRate = 18000f;
    [SerializeField] private bool emitOnlyWhenTilted = true;
    [SerializeField, Range(0f, 180f)] private float tiltStartAngle = 25f;
    [SerializeField, Range(0f, 180f)] private float tiltFullAngle = 80f;
    [SerializeField, Min(0f)] private float initialSpeedMin = 0.45f;
    [SerializeField, Min(0f)] private float initialSpeedMax = 1.20f;
    [SerializeField, Range(0f, 89f)] private float spreadAngle = 2f;
    [SerializeField, Range(0f, 2f)] private float inheritEmitterVelocity = 1f;
    [SerializeField, Min(0.001f)] private float sizeMin = 0.010f;
    [SerializeField, Min(0.001f)] private float sizeMax = 0.018f;
    [SerializeField] private Color startColor = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);

    [Header("Falling Stream Realism")]
    [Tooltip("Improves the visible falling particles before they reach the paint canvas: cohesive stream, slight natural wobble, lower random scatter, and smoother pulsed flow.")]
    [SerializeField] private bool useRealisticFallingStream = true;

    [Tooltip("Higher = particles stay as one coherent falling stream instead of exploding outward.")]
    [SerializeField, Range(0f, 1f)] private float streamCoherence = 0.82f;

    [Tooltip("Higher = the stream starts breaking into droplets. Keep low for smooth liquid paint/water.")]
    [SerializeField, Range(0f, 1f)] private float streamBreakup = 0.22f;

    [Tooltip("Small natural turbulence. Too high makes the stream noisy.")]
    [SerializeField, Range(0f, 1f)] private float streamTurbulence = 0.16f;

    [Tooltip("Subtle real pouring pulse. 0 = perfectly constant flow.")]
    [SerializeField, Range(0f, 0.6f)] private float flowPulseStrength = 0.10f;

    [SerializeField, Min(0f)] private float flowPulseFrequency = 2.1f;

    [Tooltip("Tiny direction wobble in degrees. Keep around 0.5-2 for realistic flow.")]
    [SerializeField, Range(0f, 8f)] private float streamWobbleAngle = 1.15f;

    [SerializeField, Min(0f)] private float streamWobbleFrequency = 1.35f;

    [Tooltip("Blends the initial outlet direction slightly toward world down so gravity-looking fall starts naturally.")]
    [SerializeField, Range(0f, 1f)] private float gravityDirectionBlend = 0.18f;

    [Tooltip("Boosts initial falling speed without increasing particle count.")]
    [SerializeField, Min(0.1f)] private float streamSpeedMultiplier = 1.15f;

    [Tooltip("Reduces outlet spread so the stream falls as a column, not as spray.")]
    [SerializeField, Range(0f, 1f)] private float forceNarrowOutletSpread = 0.72f;

    [Tooltip("Reduces Random Outlet Jitter at runtime. Useful because high jitter makes falling particles look fake.")]
    [SerializeField, Range(0f, 1f)] private float automaticJitterReduction = 0.78f;

    [Header("Rainbow Particles / Canvas Mixing")]
    [SerializeField] private bool useRainbowParticles = true;
    [SerializeField] private bool useRainbowPaintOnCanvas = true;
    [SerializeField, Range(0f, 1f)] private float rainbowSaturation = 1f;
    [SerializeField, Range(0f, 1f)] private float rainbowValue = 1f;
    [SerializeField, Min(0f)] private float rainbowParticleHueScale = 0.000015f;
    [SerializeField, Min(0f)] private float rainbowPaintWorldScale = 0.22f;
    [SerializeField] private float rainbowScrollSpeed = 0.25f;

    [Header("Paint Impact Algorithm")]
    [SerializeField, Range(0f, 1f)] private float paintViscosity = 0.45f;

    [Header("SPH Fluid Simulation - Full GPU")]
    [SerializeField] private bool useSPHSimulation = true;
    [SerializeField, Range(0f, 2f)] private float sphInfluence = 1f;
    [SerializeField, Min(0.01f)] private float sphSmoothingRadius = 0.16f;
    [SerializeField, Min(0.0001f)] private float sphParticleMass = 1f;
    [SerializeField, Min(0.0001f)] private float sphRestDensity = 120f;
    [SerializeField, Min(0f)] private float sphPressureStiffness = 0.04f;
    [SerializeField, Min(0f)] private float sphViscosityStrength = 0.08f;
    [SerializeField, Min(0f)] private float sphSurfaceTensionStrength = 0f;
    [SerializeField, Min(0.01f)] private float sphMaxAcceleration = 55f;
    [SerializeField, Min(1)] private int maxSPHParticles = 250000;

    [Header("Safe SPH Editing / Anti-Hang")]
    [Tooltip("Keep ON so Unity opens safely. Turn it OFF only after you finish changing the particle amount.")]
    [SerializeField] private bool startPausedForEditing = true;

    [Tooltip("Runtime allocation uses this count. Change Max Particles / Max SPH Particles while paused, then enable Apply Particle Count Now once.")]
    [SerializeField] private bool applyParticleCountNow = false;

    [Tooltip("If ON, Max SPH Particles follows Max Particles. If you want to type in Max SPH Particles directly, turn this OFF.")]
    [SerializeField] private bool syncMaxSPHParticlesWithMaxParticles = true;

    [Tooltip("Absolute safety cap. Keep 250000 unless your GPU handled more.")]
    [SerializeField, Min(1000)] private int maxAllowedParticles = 250000;

    [Tooltip("Shows how many particles are actually allocated right now. Read only during Play.")]
    [SerializeField] private int runtimeAllocatedParticles = 0;

    [Header("Flow Rate Algorithm")]
    [SerializeField] private bool useDynamicFlowRate = true;
    [SerializeField, Min(0f)] private float flowRateMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float outletSizeMultiplier = 1f;
    [SerializeField, Min(0.1f)] private float waterLevelPower = 1f;
    [SerializeField, Min(0.05f)] private float wateryViscosityResistance = 0.45f;
    [SerializeField, Min(0.05f)] private float thickViscosityResistance = 2.25f;
    [SerializeField, Min(1f)] private float maxCalculatedEmissionRate = 250000f;

    [Header("Manual Physics / GPU Integration")]
    [SerializeField, Min(0.0001f)] private float particleMass = 1f;
    [SerializeField, Min(0f)] private float gravityAcceleration = 9.81f;
    [SerializeField] private float gravityScale = 1f;
    [SerializeField, Min(0f)] private float drag = 0.01f;
    [SerializeField, Min(0f)] private float fixedDtOverride = 0.0035f;
    [SerializeField, Min(0.001f)] private float maxVelocity = 8f;

    [Header("GPU Simulation Bounds")]
    [SerializeField, Min(0.1f)] private float boundsSize = 7.5f;
    [SerializeField] private Vector3 boundsCenter = new Vector3(0f, 2.5f, 0f);
    [SerializeField, Range(0f, 1f)] private float boundaryDamping = 0.55f;
    [SerializeField, Range(8, 96)] private int gridResolution = 48;
    [SerializeField, Range(8, 256)] private int maxParticlesPerCell = 32;
    [SerializeField, Range(1, 8)] private int simulationStepsPerFrame = 1;
    [SerializeField] private bool pauseSimulation = false;
    [SerializeField] private bool resetOnPlay = true;

    [Header("Initial Fill Shape")]
    [SerializeField, Min(0.01f)] private float spawnRadius = 1.0f;
    [SerializeField, Min(0.01f)] private float spawnHeight = 1.8f;
    [SerializeField] private Vector3 spawnCenter = new Vector3(0f, 3.0f, 0f);
    [SerializeField] private Vector3 initialVelocity = Vector3.zero;

    [Header("Manual Ground Fallback")]
    [SerializeField] private bool collideWithGround = false;
    [SerializeField] private float groundY = 0f;
    [SerializeField, Range(0f, 1f)] private float collisionBounce = 0.35f;
    [SerializeField, Range(0f, 1f)] private float tangentDamping = 0.15f;
    [SerializeField, Min(0f)] private float collisionScatter = 0.10f;
    [SerializeField, Min(0.001f)] private float particleRadiusMultiplier = 0.5f;

    [Header("Particle Cleanup - GPU Bounds")]
    [SerializeField] private bool recycleWhenTooFar = true;
    [SerializeField, Min(1f)] private float maxDistanceFromEmitter = 35f;
    [SerializeField, Min(0.1f)] private float maxDistanceBelowCanvas = 15f;

    [Header("Rendering")]
    [SerializeField] private bool billboardToCamera = true;
    [SerializeField] private bool cullOffscreen = false;
    [SerializeField, Range(0f, 0.5f)] private float viewportCullMargin = 0.05f;
    [SerializeField] private bool castShadows = false;
    [SerializeField] private bool receiveShadows = false;
    [SerializeField] private bool useInstancedRendering = true;
    [SerializeField, Min(1)] private int renderEveryNthParticle = 1;
    [SerializeField, Min(1)] private int maxRenderedParticles = 250000;
    [SerializeField] private bool skipViewportCullingWhenManyParticles = true;
    [SerializeField, Min(1)] private int viewportCullingDisableThreshold = 12000;
    [SerializeField] private bool useSimulationBudget = false;
    [SerializeField, Min(1)] private int maxParticlesSimulatedPerFixedStep = 250000;
    [SerializeField] private bool autoTurboMode = false;
    [SerializeField, Min(1)] private int turboParticleThreshold = 250000;
    [SerializeField] private bool disableSPHInTurboMode = false;
    [SerializeField, Min(1)] private int turboRenderedParticleTarget = 250000;
    [SerializeField, Min(1)] private int turboCanvasCollisionStride = 1;
    [SerializeField, Min(1)] private int maxEmittedPerFixedStep = 1024;
    [SerializeField, Min(0.001f)] private float renderParticleSize = 0.020f;
    [SerializeField] private bool drawParticles = true;
    [SerializeField] private bool showStatsOnGUI = true;

    [Header("Runtime Settings UI")]
    [SerializeField] private bool showRuntimeSettingsUI = true;
    [SerializeField] private bool runtimeSettingsCollapsed = false;
    [SerializeField] private Rect runtimeSettingsWindowRect = new Rect(12f, 12f, 430f, 640f);
    [SerializeField, Range(0.55f, 1.35f)] private float runtimeSettingsWindowOpacity = 0.94f;
    [SerializeField, Range(10, 22)] private int runtimeSettingsFontSize = 13;
    [SerializeField] private Color runtimeSettingsBackgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.94f);
    [SerializeField] private Color runtimeSettingsTextColor = Color.white;
    [SerializeField] private Color runtimeSettingsAccentColor = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);
    [SerializeField] private bool uiShowQuickControls = true;
    [SerializeField] private bool uiShowVisibleThings = true;
    [SerializeField] private bool uiShowParticlesAndFlow = true;
    [SerializeField] private bool uiShowOutletSettings = true;
    [SerializeField] private bool uiShowRealisticStream = true;
    [SerializeField] private bool uiShowSPHSettings = true;
    [SerializeField] private bool uiShowRenderingSettings = true;
    [SerializeField] private bool uiShowColorSettings = true;
    [SerializeField] private bool uiShowWaterAndCanvas = true;
    [SerializeField] private bool uiShowPerformanceSettings = true;
    private Vector2 runtimeSettingsScroll;

    private const int THREADS = 256;
    private const int PARTICLE_STRIDE = 32;

    private int runtimeParticleCount;

    private ComputeBuffer particleBuffer;
    private ComputeBuffer cellCountsBuffer;
    private ComputeBuffer cellParticlesBuffer;
    private ComputeBuffer indirectArgsBuffer;
    private MaterialPropertyBlock mpb;
    private uint[] indirectArgs;
    private Bounds renderBounds;

    private int emitKernel;
    private int clearGridKernel;
    private int buildGridKernel;
    private int densityKernel;
    private int integrateKernel;
    private int cellsCount;
    private int emissionCursor;
    private float emissionAccumulator;
    private bool initialized;
    private bool waterWasInitialized;

    private bool liquidFullTransformCaptured;
    private Vector3 liquidFullLocalPosition;
    private Vector3 liquidFullLocalScale;
    private Renderer cachedLiquidRenderer;
    private Material liquidMaterialInstance;

    private Vector3 lastEmitterPosition;
    private Vector3 emitterVelocity;
    private float lastCalculatedFlowRate;
    private float lastTiltFactor;
    private float lastWaterLevelFactor;
    private float lastViscosityFlowFactor;
    private float paintBridgeTimer;

    public float CurrentWaterAmount => currentWaterAmount;
    public float InitialWaterAmount => initialWaterAmount;
    public bool IsBucketEmpty => currentWaterAmount <= 0f;
    public int ActiveParticleCount => runtimeParticleCount > 0 ? runtimeParticleCount : maxParticles;
    public float CurrentFlowRate => lastCalculatedFlowRate;
    public float CurrentTiltFactor => lastTiltFactor;
    public float CurrentWaterLevelFactor => lastWaterLevelFactor;
    public float CurrentWater01 => initialWaterAmount <= 0f ? 0f : Mathf.Clamp01(currentWaterAmount / initialWaterAmount);

    public void SetEmitter(Transform newEmitter)
    {
        emitter = newEmitter;
        lastEmitterPosition = GetEmitterPosition();
    }

    public void SetTiltReference(Transform newTiltReference)
    {
        tiltReference = newTiltReference;
    }

    public void SetPaintCanvas(Transform newPaintCanvas)
    {
        paintCanvas = newPaintCanvas;
        if (paintCanvas != null && paintCanvasDrawer == null)
        {
            paintCanvasDrawer = paintCanvas.GetComponent<PaintCanvasDrawer>();
        }
    }

    public void SetPaintCanvasDrawer(PaintCanvasDrawer newPaintCanvasDrawer)
    {
        paintCanvasDrawer = newPaintCanvasDrawer;
    }

    public void SetEmissionEnabled(bool value)
    {
        emissionEnabled = value;
    }

    public void RefillBucket()
    {
        currentWaterAmount = Mathf.Max(0f, initialWaterAmount);
        emissionAccumulator = 0f;
        waterWasInitialized = true;
        UpdateBucketLiquidVisual(true);
    }

    public void SetCurrentWaterAmount(float amount)
    {
        currentWaterAmount = Mathf.Clamp(amount, 0f, initialWaterAmount);
        if (currentWaterAmount <= 0f)
        {
            emissionAccumulator = 0f;
        }
        waterWasInitialized = true;
        UpdateBucketLiquidVisual(true);
    }

    public void AddWater(float amount)
    {
        currentWaterAmount = Mathf.Clamp(currentWaterAmount + Mathf.Max(0f, amount), 0f, initialWaterAmount);
        waterWasInitialized = true;
        UpdateBucketLiquidVisual(true);
    }

    public void EmitBurst(int count)
    {
        EnsureInitialized();
        if (count > 0)
        {
            DispatchEmit(Mathf.Min(count, GetRuntimeParticleCount()));
        }
    }

    public void ClearAll()
    {
        ResetParticles();
        RefillBucket();
        if (paintCanvasDrawer != null)
        {
            paintCanvasDrawer.ClearCanvas();
        }
    }

    public void ResetParticles()
    {
        EnsureInitialized();
        if (particleBuffer != null)
        {
            particleBuffer.SetData(CreateInitialParticles());
        }
        emissionCursor = 0;
        emissionAccumulator = 0f;
    }

    private void Reset()
    {
        emitter = transform;
        tiltReference = transform;
    }

    private void Start()
    {
        if (startPausedForEditing)
        {
            pauseSimulation = true;
        }

        EnsureInitialized();
        lastEmitterPosition = GetEmitterPosition();

        if (fillBucketOnStart || !waterWasInitialized)
        {
            RefillBucket();
        }

        if (resetOnPlay)
        {
            ResetParticles();
        }

        UpdateBucketLiquidVisual(true);
    }

    private void OnEnable()
    {
        EnsureInitialized();
        lastEmitterPosition = GetEmitterPosition();
        SetCommonBuffers();
        SetMaterialBuffers();
        UpdateBucketLiquidVisual(true);
    }

    private void Update()
    {
        EnsureInitialized();

        if (applyParticleCountNow)
        {
            applyParticleCountNow = false;
            RebuildParticleBuffersFromInspector();
            return;
        }

        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }

        float frameDt = Mathf.Max(Time.deltaTime, 0.00001f);
        UpdateEmitterVelocity(frameDt);

        if (!pauseSimulation)
        {
            int steps = Mathf.Max(1, simulationStepsPerFrame);
            float dt = fixedDtOverride > 0f ? fixedDtOverride : Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            for (int s = 0; s < steps; s++)
            {
                StepSimulation(dt);
            }
        }

        UpdateBucketLiquidVisual(false);

        if (drawOnCanvasTexture && useGpuPaintBridge)
        {
            UpdateGpuPaintBridge(frameDt);
        }

        if (drawParticles)
        {
            RenderParticles();
        }
    }

    private void EnsureInitialized()
    {
        if (initialized && particleBuffer != null)
        {
            return;
        }

        ReleaseBuffers();

        if (emitter == null)
        {
            emitter = transform;
        }

        if (tiltReference == null)
        {
            tiltReference = emitter;
        }

        if (paintCanvasDrawer == null && paintCanvas != null)
        {
            paintCanvasDrawer = paintCanvas.GetComponent<PaintCanvasDrawer>();
        }

        if (sphCompute == null)
        {
            Debug.LogError("BucketParticleSystemCustom GPU SPH: assign BucketParticleSystemCustomGPU.compute to Sph Compute.", this);
            enabled = false;
            return;
        }

        if (particleMaterial == null)
        {
            Debug.LogError("BucketParticleSystemCustom GPU SPH: assign a material using Custom/GPUSPHParticleInstanced shader.", this);
            enabled = false;
            return;
        }

        if (particleMesh == null)
        {
            particleMesh = CreateQuadMesh();
        }

        SyncAndClampParticleCounts();
        runtimeParticleCount = GetRuntimeParticleCount();
        runtimeAllocatedParticles = runtimeParticleCount;
        gridResolution = Mathf.Clamp(gridResolution, 8, 96);
        maxParticlesPerCell = Mathf.Clamp(maxParticlesPerCell, 8, 256);
        cellsCount = gridResolution * gridResolution * gridResolution;

        particleBuffer = new ComputeBuffer(runtimeParticleCount, PARTICLE_STRIDE, ComputeBufferType.Structured);
        cellCountsBuffer = new ComputeBuffer(cellsCount, sizeof(uint), ComputeBufferType.Structured);
        cellParticlesBuffer = new ComputeBuffer(cellsCount * maxParticlesPerCell, sizeof(uint), ComputeBufferType.Structured);
        indirectArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        particleBuffer.SetData(CreateInitialParticles());

        indirectArgs = new uint[5]
        {
            particleMesh != null ? particleMesh.GetIndexCount(0) : 0u,
            (uint)runtimeParticleCount,
            particleMesh != null ? particleMesh.GetIndexStart(0) : 0u,
            particleMesh != null ? particleMesh.GetBaseVertex(0) : 0u,
            0u
        };
        indirectArgsBuffer.SetData(indirectArgs);

        emitKernel = sphCompute.FindKernel("EmitFromOutlet");
        clearGridKernel = sphCompute.FindKernel("ClearGrid");
        buildGridKernel = sphCompute.FindKernel("BuildGrid");
        densityKernel = sphCompute.FindKernel("ComputeDensityPressure");
        integrateKernel = sphCompute.FindKernel("ComputeForcesIntegrate");

        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();
        }

        renderBounds = new Bounds(boundsCenter, Vector3.one * boundsSize * 1.35f);
        emissionCursor = 0;
        emissionAccumulator = 0f;
        lastEmitterPosition = GetEmitterPosition();
        emitterVelocity = Vector3.zero;

        particleMaterial.enableInstancing = true;
        SetCommonBuffers();
        SetMaterialBuffers();
        InitializeBucketLiquidVisualIfNeeded();

        initialized = true;
    }

    private GPUParticle[] CreateInitialParticles()
    {
        int count = GetRuntimeParticleCount();
        GPUParticle[] particles = new GPUParticle[count];

        // IMPORTANT:
        // Old version filled all 250k particles into a visible spawn volume immediately.
        // Because DrawMeshInstancedIndirect renders the whole buffer, those un-emitted particles
        // looked like a pink dust/cloud near the brush instead of a clean falling stream.
        // We keep particles inactive until EmitFromOutlet writes them.
        Vector3 inactivePosition = boundsCenter + Vector3.down * (boundsSize + 25f);

        for (int i = 0; i < count; i++)
        {
            particles[i].position = inactivePosition;
            particles[i].velocity = Vector3.zero;
            particles[i].density = -1f; // inactive marker used by compute + shader
            particles[i].pressure = 0f;
        }

        return particles;
    }

    private void StepSimulation(float dt)
    {
        SetSimulationParameters(dt);

        if (emissionEnabled && (!stopWhenBucketEmpty || currentWaterAmount > 0f))
        {
            float tiltFactor = emitOnlyWhenTilted ? GetTiltFactor() : 1f;
            float currentFlowRate = useDynamicFlowRate ? CalculateDynamicFlowRate(tiltFactor) : emissionRate * tiltFactor;
            currentFlowRate = ApplyRealisticFlowToRate(currentFlowRate, tiltFactor);
            lastCalculatedFlowRate = currentFlowRate;
            emissionAccumulator += currentFlowRate * dt;

            int requestedCount = Mathf.FloorToInt(emissionAccumulator);
            if (requestedCount > 0)
            {
                requestedCount = Mathf.Min(requestedCount, maxEmittedPerFixedStep, GetRuntimeParticleCount());

                if (stopWhenBucketEmpty)
                {
                    int affordable = Mathf.FloorToInt(currentWaterAmount / Mathf.Max(0.000001f, waterPerParticle));
                    requestedCount = Mathf.Min(requestedCount, affordable);
                }

                if (requestedCount > 0)
                {
                    DispatchEmit(requestedCount);
                    emissionAccumulator -= requestedCount;

                    if (stopWhenBucketEmpty)
                    {
                        currentWaterAmount = Mathf.Max(0f, currentWaterAmount - requestedCount * waterPerParticle);
                    }
                }
                else if (stopWhenBucketEmpty)
                {
                    currentWaterAmount = 0f;
                    emissionAccumulator = 0f;
                }
            }
        }
        else if (stopWhenBucketEmpty && currentWaterAmount <= 0f)
        {
            emissionAccumulator = 0f;
            lastCalculatedFlowRate = 0f;
            lastWaterLevelFactor = 0f;
        }

        int cellGroups = Mathf.CeilToInt(cellsCount / (float)THREADS);
        int particleGroups = Mathf.CeilToInt(GetRuntimeParticleCount() / (float)THREADS);

        sphCompute.Dispatch(clearGridKernel, cellGroups, 1, 1);
        sphCompute.Dispatch(buildGridKernel, particleGroups, 1, 1);

        if (useSPHSimulation)
        {
            sphCompute.Dispatch(densityKernel, particleGroups, 1, 1);
        }

        sphCompute.Dispatch(integrateKernel, particleGroups, 1, 1);
    }

    private void DispatchEmit(int emitCount)
    {
        if (emitCount <= 0 || sphCompute == null)
        {
            return;
        }

        Vector3 outletPosition = GetOutletEmissionPosition();
        Vector3 outletDirection = GetEmissionDirection().normalized;
        if (outletDirection.sqrMagnitude < 0.000001f)
        {
            outletDirection = Vector3.down;
        }

        outletDirection = GetRealisticOutletDirection(outletDirection);

        BuildOutletBasis(outletDirection, out Vector3 outletRight, out Vector3 outletUp);
        Vector3 shapeLineDirection = GetOutletLineDirectionWorld(outletRight, outletUp);
        Vector3 shapePerpendicular = Vector3.Cross(outletDirection, shapeLineDirection).normalized;
        if (shapePerpendicular.sqrMagnitude < 0.000001f)
        {
            shapePerpendicular = outletUp;
        }

        sphCompute.SetInt("_EmitCount", emitCount);
        sphCompute.SetInt("_EmissionCursor", emissionCursor);
        sphCompute.SetVector("_OutletPosition", outletPosition);
        sphCompute.SetVector("_OutletDirection", outletDirection);
        sphCompute.SetVector("_OutletRight", shapeLineDirection);
        sphCompute.SetVector("_OutletUp", shapePerpendicular);
        sphCompute.SetVector("_EmitterVelocity", emitterVelocity * inheritEmitterVelocity);
        GetRealisticOutletSpeeds(out float outletSpeedMin, out float outletSpeedMax);
        float effectiveJitter = GetRealisticOutletJitter();
        float effectiveSpreadAngle = GetRealisticSpreadAngle();

        sphCompute.SetFloat("_OutletRadius", Mathf.Max(0.0001f, outletShapeRadius));
        sphCompute.SetFloat("_OutletSpeedMin", outletSpeedMin);
        sphCompute.SetFloat("_OutletSpeedMax", outletSpeedMax);
        sphCompute.SetFloat("_RandomVelocityJitter", effectiveJitter);
        sphCompute.SetFloat("_RandomSeed", Time.time * 97.13f + emissionCursor * 0.017f);
        sphCompute.SetInt("_OutletShape", (int)outletShape);
        sphCompute.SetFloat("_LineOutletLength", Mathf.Max(0.001f, lineOutletLength));
        sphCompute.SetInt("_MultipleHoleCount", Mathf.Clamp(multipleHoleCount, 2, 12));
        sphCompute.SetFloat("_MultipleHoleSpacing", Mathf.Max(0.001f, multipleHoleSpacing));
        sphCompute.SetFloat("_ShapeJitter", effectiveJitter);
        sphCompute.SetFloat("_SpreadTan", Mathf.Tan(effectiveSpreadAngle * Mathf.Deg2Rad));
        sphCompute.SetFloat("_ParticleSizeMin", sizeMin);
        sphCompute.SetFloat("_ParticleSizeMax", sizeMax);

        int groups = Mathf.CeilToInt(emitCount / (float)THREADS);
        sphCompute.Dispatch(emitKernel, groups, 1, 1);

        emissionCursor = (emissionCursor + emitCount) % GetRuntimeParticleCount();
    }

    private void SetCommonBuffers()
    {
        SetKernelBuffers(emitKernel);
        SetKernelBuffers(clearGridKernel);
        SetKernelBuffers(buildGridKernel);
        SetKernelBuffers(densityKernel);
        SetKernelBuffers(integrateKernel);
    }

    private void SetKernelBuffers(int kernel)
    {
        if (kernel < 0 || sphCompute == null || particleBuffer == null)
        {
            return;
        }

        sphCompute.SetBuffer(kernel, "_Particles", particleBuffer);
        if (kernel != emitKernel)
        {
            sphCompute.SetBuffer(kernel, "_CellCounts", cellCountsBuffer);
            sphCompute.SetBuffer(kernel, "_CellParticles", cellParticlesBuffer);
        }
    }

    private void SetSimulationParameters(float dt)
    {
        float safeBoundsSize = Mathf.Max(0.1f, boundsSize);
        float halfBoundsSize = safeBoundsSize * 0.5f;
        float cellSize = safeBoundsSize / Mathf.Max(1, gridResolution);

        sphCompute.SetInt("_ParticleCount", GetRuntimeParticleCount());
        sphCompute.SetInt("_GridResolution", gridResolution);
        sphCompute.SetInt("_CellsCount", cellsCount);
        sphCompute.SetInt("_MaxParticlesPerCell", maxParticlesPerCell);
        sphCompute.SetFloat("_BoundsSize", safeBoundsSize);
        sphCompute.SetFloat("_HalfBoundsSize", halfBoundsSize);
        sphCompute.SetFloat("_CellSize", cellSize);
        sphCompute.SetFloat("_InvCellSize", 1f / Mathf.Max(0.0001f, cellSize));
        sphCompute.SetVector("_BoundsCenter", boundsCenter);
        sphCompute.SetFloat("_GroundY", groundY);
        sphCompute.SetInt("_CollideWithGround", collideWithGround ? 1 : 0);
        sphCompute.SetFloat("_DeltaTime", Mathf.Max(0.00001f, dt));
        sphCompute.SetFloat("_SmoothingRadius", Mathf.Max(0.01f, sphSmoothingRadius));
        sphCompute.SetFloat("_ParticleMass", Mathf.Max(0.0001f, sphParticleMass));
        sphCompute.SetFloat("_RestDensity", Mathf.Max(0.0001f, sphRestDensity));
        sphCompute.SetFloat("_PressureStiffness", Mathf.Max(0f, sphPressureStiffness) * sphInfluence);
        sphCompute.SetFloat("_Viscosity", GetEffectiveSPHViscosity());
        sphCompute.SetFloat("_SurfaceTension", GetEffectiveSurfaceTension());
        sphCompute.SetFloat("_Gravity", Mathf.Max(0f, gravityAcceleration * gravityScale));
        sphCompute.SetFloat("_Drag", GetEffectiveDrag());
        sphCompute.SetFloat("_MaxVelocity", Mathf.Max(0.001f, maxVelocity));
        sphCompute.SetFloat("_MaxAcceleration", Mathf.Max(0.001f, sphMaxAcceleration));
        sphCompute.SetFloat("_BoundaryDamping", Mathf.Clamp01(boundaryDamping));
    }

    private void SetMaterialBuffers()
    {
        if (particleMaterial == null || particleBuffer == null)
        {
            return;
        }

        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();
        }

        float size = renderParticleSize > 0f ? renderParticleSize : Mathf.Lerp(sizeMin, sizeMax, 0.5f);

        // Academic SPH visualization parameters.
        // The ComputeShader calculates density and pressure; the particle shader only visualizes them.
        float visualizationRestDensity = Mathf.Max(0.0001f, sphRestDensity);
        float pressureVisualizationScale = 0.04f;
        float densityVisualizationScale = 0.015f;

        particleMaterial.enableInstancing = true;
        particleMaterial.SetBuffer("_Particles", particleBuffer);
        particleMaterial.SetFloat("_Size", size);
        particleMaterial.SetColor("_Color", startColor);
        particleMaterial.SetFloat("_RestDensity", visualizationRestDensity);
        particleMaterial.SetFloat("_PressureScale", pressureVisualizationScale);
        particleMaterial.SetFloat("_DensityScale", densityVisualizationScale);
        particleMaterial.SetFloat("_UseRainbow", useRainbowParticles ? 1f : 0f);
        particleMaterial.SetFloat("_RainbowHueScale", rainbowParticleHueScale);
        particleMaterial.SetFloat("_RainbowWorldScale", rainbowPaintWorldScale);
        particleMaterial.SetFloat("_RainbowScrollSpeed", rainbowScrollSpeed);
        particleMaterial.SetFloat("_RainbowSaturation", rainbowSaturation);
        particleMaterial.SetFloat("_RainbowValue", rainbowValue);

        mpb.Clear();
        mpb.SetBuffer("_Particles", particleBuffer);
        mpb.SetFloat("_Size", size);
        mpb.SetColor("_Color", startColor);
        mpb.SetFloat("_RestDensity", visualizationRestDensity);
        mpb.SetFloat("_PressureScale", pressureVisualizationScale);
        mpb.SetFloat("_DensityScale", densityVisualizationScale);
        mpb.SetFloat("_UseRainbow", useRainbowParticles ? 1f : 0f);
        mpb.SetFloat("_RainbowHueScale", rainbowParticleHueScale);
        mpb.SetFloat("_RainbowWorldScale", rainbowPaintWorldScale);
        mpb.SetFloat("_RainbowScrollSpeed", rainbowScrollSpeed);
        mpb.SetFloat("_RainbowSaturation", rainbowSaturation);
        mpb.SetFloat("_RainbowValue", rainbowValue);
    }

    private void RenderParticles()
    {
        if (particleMaterial == null || particleMesh == null || indirectArgsBuffer == null)
        {
            return;
        }

        renderBounds = new Bounds(boundsCenter, Vector3.one * boundsSize * 1.35f);
        SetMaterialBuffers();

        Graphics.DrawMeshInstancedIndirect(
            particleMesh,
            0,
            particleMaterial,
            renderBounds,
            indirectArgsBuffer,
            0,
            mpb,
            castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows,
            gameObject.layer
        );
    }

    private void UpdateEmitterVelocity(float dt)
    {
        Vector3 currentPosition = GetEmitterPosition();
        emitterVelocity = (currentPosition - lastEmitterPosition) / Mathf.Max(0.00001f, dt);
        lastEmitterPosition = currentPosition;
    }

    private float GetTiltFactor()
    {
        Transform t = tiltReference != null ? tiltReference : (emitter != null ? emitter : transform);
        float tilt = Vector3.Angle(t.up, Vector3.up);
        return Mathf.Clamp01(Mathf.InverseLerp(tiltStartAngle, tiltFullAngle, tilt));
    }

    private float CalculateDynamicFlowRate(float tiltFactor)
    {
        if (currentWaterAmount <= 0f || initialWaterAmount <= 0f)
        {
            lastTiltFactor = Mathf.Clamp01(tiltFactor);
            lastWaterLevelFactor = 0f;
            lastViscosityFlowFactor = 0f;
            return 0f;
        }

        lastTiltFactor = Mathf.Clamp01(tiltFactor);
        float water01 = Mathf.Clamp01(currentWaterAmount / initialWaterAmount);
        lastWaterLevelFactor = Mathf.Pow(water01, waterLevelPower);

        float viscosityResistance = Mathf.Lerp(wateryViscosityResistance, thickViscosityResistance, Mathf.Clamp01(paintViscosity));
        lastViscosityFlowFactor = 1f / Mathf.Max(0.05f, viscosityResistance);

        float calculatedFlowRate =
            emissionRate *
            flowRateMultiplier *
            lastTiltFactor *
            lastWaterLevelFactor *
            outletSizeMultiplier *
            GetOutletShapeFlowFactor() *
            lastViscosityFlowFactor;

        return Mathf.Clamp(calculatedFlowRate, 0f, maxCalculatedEmissionRate);
    }

    private float ApplyRealisticFlowToRate(float baseRate, float tiltFactor)
    {
        if (!useRealisticFallingStream || baseRate <= 0f)
        {
            return baseRate;
        }

        if (flowPulseStrength <= 0f || flowPulseFrequency <= 0f)
        {
            return baseRate;
        }

        float time = Time.time;
        float sinePulse = Mathf.Sin(time * flowPulseFrequency * Mathf.PI * 2f) * 0.5f + 0.5f;
        float noisePulse = Mathf.PerlinNoise(time * flowPulseFrequency * 0.33f, 19.73f);
        float pulse = Mathf.Lerp(sinePulse, noisePulse, 0.45f);
        float centeredPulse = (pulse - 0.5f) * 2f;

        // Stable when the bucket just starts tilting, more alive when flow is strong.
        float tiltStability = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(tiltFactor));
        float breakupFactor = Mathf.Lerp(0.35f, 1f, streamBreakup);
        float multiplier = 1f + centeredPulse * flowPulseStrength * tiltStability * breakupFactor;

        return Mathf.Clamp(baseRate * Mathf.Max(0.15f, multiplier), 0f, maxCalculatedEmissionRate);
    }

    private Vector3 GetRealisticOutletDirection(Vector3 baseDirection)
    {
        if (!useRealisticFallingStream)
        {
            return baseDirection.normalized;
        }

        Vector3 direction = baseDirection.sqrMagnitude < 0.000001f ? Vector3.down : baseDirection.normalized;

        // A slight blend toward gravity makes the falling column look more natural while keeping the outlet direction.
        if (gravityDirectionBlend > 0f)
        {
            float tiltFactor = emitOnlyWhenTilted ? GetTiltFactor() : 1f;
            float blend = gravityDirectionBlend * Mathf.Lerp(0.25f, 1f, tiltFactor);
            direction = Vector3.Slerp(direction, Vector3.down, Mathf.Clamp01(blend)).normalized;
        }

        if (streamWobbleAngle <= 0f || streamWobbleFrequency <= 0f || streamTurbulence <= 0f)
        {
            return direction;
        }

        BuildOutletBasis(direction, out Vector3 right, out Vector3 up);

        float time = Time.time * streamWobbleFrequency;
        float wobbleX = Mathf.Sin(time * Mathf.PI * 2f + 0.73f);
        float wobbleY = Mathf.Sin(time * Mathf.PI * 1.37f + 2.11f);
        float noise = Mathf.PerlinNoise(time * 0.61f, 8.21f) * 2f - 1f;

        float angleX = streamWobbleAngle * streamTurbulence * (wobbleX * 0.65f + noise * 0.35f);
        float angleY = streamWobbleAngle * streamTurbulence * wobbleY * 0.55f;

        direction = Quaternion.AngleAxis(angleX, up) * direction;
        direction = Quaternion.AngleAxis(angleY, right) * direction;
        return direction.normalized;
    }

    private void GetRealisticOutletSpeeds(out float outletSpeedMin, out float outletSpeedMax)
    {
        float minSpeed = Mathf.Max(0f, initialSpeedMin);
        float maxSpeed = Mathf.Max(minSpeed + 0.0001f, initialSpeedMax);

        if (!useRealisticFallingStream)
        {
            outletSpeedMin = minSpeed;
            outletSpeedMax = maxSpeed;
            return;
        }

        float multiplier = Mathf.Max(0.1f, streamSpeedMultiplier);
        float center = (minSpeed + maxSpeed) * 0.5f * multiplier;
        float baseHalfRange = (maxSpeed - minSpeed) * 0.5f * multiplier;

        // Coherent stream = less random speed difference. Breakup = more droplet variation.
        float halfRange = baseHalfRange * Mathf.Lerp(0.35f, 1.35f, Mathf.Clamp01(streamBreakup));
        float pulse = flowPulseFrequency > 0f
            ? Mathf.Sin(Time.time * flowPulseFrequency * Mathf.PI * 2f + 1.7f) * 0.5f + 0.5f
            : 0.5f;

        float centerPulse = 1f + (pulse - 0.5f) * flowPulseStrength * 0.35f;
        center *= Mathf.Max(0.2f, centerPulse);

        outletSpeedMin = Mathf.Max(0f, center - halfRange);
        outletSpeedMax = Mathf.Max(outletSpeedMin + 0.0001f, center + halfRange);
    }

    private float GetRealisticOutletJitter()
    {
        float originalJitter = Mathf.Clamp01(randomOutletJitter);

        if (!useRealisticFallingStream)
        {
            return originalJitter;
        }

        // Prevent the high inspector jitter from exploding the falling stream outward.
        float reducedOriginal = originalJitter * (1f - Mathf.Clamp01(automaticJitterReduction));
        float naturalJitter = Mathf.Lerp(0.025f, 0.30f, Mathf.Clamp01(streamBreakup * 0.75f + streamTurbulence * 0.55f));

        return Mathf.Clamp01(Mathf.Max(reducedOriginal, naturalJitter));
    }

    private float GetRealisticSpreadAngle()
    {
        float baseSpread = Mathf.Clamp(GetOutletShapeSpreadAngle(), 0f, 89f);

        if (!useRealisticFallingStream)
        {
            return baseSpread;
        }

        float narrowedSpread = Mathf.Lerp(
            baseSpread,
            baseSpread * (1f - Mathf.Clamp01(forceNarrowOutletSpread)),
            Mathf.Clamp01(streamCoherence)
        );

        float breakupSpread = streamBreakup * 2.1f + streamTurbulence * 0.75f;
        return Mathf.Clamp(narrowedSpread + breakupSpread, 0f, 89f);
    }

    private float GetEffectiveSPHViscosity()
    {
        float viscosity = Mathf.Max(0f, sphViscosityStrength);

        if (!useRealisticFallingStream)
        {
            return viscosity;
        }

        // Extra cohesion helps the falling column behave like liquid instead of separated sparks.
        return viscosity * Mathf.Lerp(1f, 2.25f, Mathf.Clamp01(streamCoherence)) + streamCoherence * 0.012f;
    }

    private float GetEffectiveSurfaceTension()
    {
        float surfaceTension = Mathf.Max(0f, sphSurfaceTensionStrength);

        if (!useRealisticFallingStream)
        {
            return surfaceTension;
        }

        return surfaceTension + streamCoherence * 0.008f;
    }

    private float GetEffectiveDrag()
    {
        float effectiveDrag = Mathf.Max(0f, drag);

        if (!useRealisticFallingStream)
        {
            return effectiveDrag;
        }

        // A very small extra drag prevents particles from stretching into unrealistic long streaks.
        return effectiveDrag + Mathf.Lerp(0.001f, 0.012f, Mathf.Clamp01(streamBreakup));
    }

    private float GetOutletShapeFlowFactor()
    {
        if (!outletShapeAffectsFlowRate)
        {
            return 1f;
        }

        switch (outletShape)
        {
            case OutletShape.Line:
                return Mathf.Clamp(lineOutletLength / 0.18f, 0.45f, 2.5f);
            case OutletShape.MultipleHoles:
                return Mathf.Clamp(multipleHoleCount * 0.55f, 0.8f, 4f);
            case OutletShape.Random:
                return Random.Range(0.75f, 1.25f);
            case OutletShape.Circle:
            default:
                return 1f;
        }
    }

    private float GetOutletShapeSpreadAngle()
    {
        if (!outletShapeAffectsSpread)
        {
            return spreadAngle;
        }

        switch (outletShape)
        {
            case OutletShape.Line:
                return Mathf.Clamp(spreadAngle * 0.65f + 1.5f, 0f, 89f);
            case OutletShape.MultipleHoles:
                return Mathf.Clamp(spreadAngle * 0.9f + 0.5f, 0f, 89f);
            case OutletShape.Random:
                return Mathf.Clamp(spreadAngle * 1.65f + 2f, 0f, 89f);
            case OutletShape.Circle:
            default:
                return spreadAngle;
        }
    }

    private Vector3 GetEmitterPosition()
    {
        Transform t = emitter != null ? emitter : transform;
        return t.TransformPoint(emissionLocalOffset);
    }

    private Vector3 GetOutletEmissionPosition()
    {
        return GetEmitterPosition();
    }

    private Vector3 GetEmissionDirection()
    {
        Transform t = emitter != null ? emitter : transform;
        switch (emissionAxis)
        {
            case EmissionAxis.LocalForward: return t.forward;
            case EmissionAxis.LocalBack: return -t.forward;
            case EmissionAxis.LocalUp: return t.up;
            case EmissionAxis.LocalDown: return -t.up;
            case EmissionAxis.LocalRight: return t.right;
            case EmissionAxis.LocalLeft: return -t.right;
            case EmissionAxis.WorldDown: return Vector3.down;
            default: return -t.up;
        }
    }

    private Vector3 GetOutletLineDirectionLocal()
    {
        Vector3 direction = outletLineLocalDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.000001f)
        {
            direction = Vector3.right;
        }
        return direction.normalized;
    }

    private Vector3 GetOutletLineDirectionWorld(Vector3 fallbackRight, Vector3 fallbackUp)
    {
        Transform t = emitter != null ? emitter : transform;
        Vector3 world = t.TransformDirection(GetOutletLineDirectionLocal());
        world = Vector3.ProjectOnPlane(world, GetEmissionDirection());
        if (world.sqrMagnitude < 0.000001f)
        {
            world = fallbackRight.sqrMagnitude > 0.000001f ? fallbackRight : fallbackUp;
        }
        return world.normalized;
    }

    private void BuildOutletBasis(Vector3 direction, out Vector3 right, out Vector3 up)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;
        right = Vector3.Cross(reference, direction).normalized;
        up = Vector3.Cross(direction, right).normalized;
    }

    private void UpdateGpuPaintBridge(float dt)
    {
        if (!useManualCanvasCollision || paintCanvas == null || paintCanvasDrawer == null || lastCalculatedFlowRate <= 0.01f)
        {
            return;
        }

        paintBridgeTimer += dt;
        if (paintBridgeTimer < paintBridgeInterval)
        {
            return;
        }
        paintBridgeTimer = 0f;

        Vector3 origin = GetOutletEmissionPosition();
        Vector3 direction = GetEmissionDirection().normalized;
        if (direction.sqrMagnitude < 0.000001f)
        {
            return;
        }

        Vector3 canvasPoint = paintCanvas.position;
        Vector3 canvasNormal = GetCanvasNormal();
        float denom = Vector3.Dot(direction, canvasNormal);
        if (Mathf.Abs(denom) < 0.00001f)
        {
            return;
        }

        float t = Vector3.Dot(canvasPoint - origin, canvasNormal) / denom;
        if (t <= 0f)
        {
            return;
        }

        float impactSpeed = Mathf.Lerp(initialSpeedMin, initialSpeedMax, 0.5f) + emitterVelocity.magnitude * inheritEmitterVelocity;
        if (impactSpeed < minimumImpactSpeedToDraw)
        {
            return;
        }

        Vector3 hit = origin + direction * t;
        Vector3 splatterDirectionOnCanvas = Vector3.ProjectOnPlane(direction, canvasNormal).normalized;
        if (splatterDirectionOnCanvas.sqrMagnitude < 0.000001f)
        {
            splatterDirectionOnCanvas = Vector3.ProjectOnPlane(emitter != null ? emitter.forward : transform.forward, canvasNormal).normalized;
        }

        float impactAngle = Vector3.Angle(-direction, canvasNormal);
        float markSize = Mathf.Lerp(sizeMin, sizeMax, 0.5f) * paintBridgeMarkSizeMultiplier;
        int marks = Mathf.Clamp(paintBridgeMarksPerTick, 1, 12);
        for (int i = 0; i < marks; i++)
        {
            Vector2 jitter2 = Random.insideUnitCircle * paintBridgeJitterRadius;
            Vector3 jittered = hit + paintCanvas.right * jitter2.x + paintCanvas.forward * jitter2.y;
            if (IsPointInsideCanvas(jittered))
            {
                Color impactColor = useRainbowPaintOnCanvas ? GetRainbowPaintColor(jittered, i) : startColor;
                paintCanvasDrawer.DrawPaint(jittered, impactColor, markSize, impactSpeed, impactAngle, paintViscosity, splatterDirectionOnCanvas);
            }
        }
    }

    private Color GetRainbowPaintColor(Vector3 worldPoint, int seed)
    {
        float hue = Mathf.Repeat(
            worldPoint.x * rainbowPaintWorldScale +
            worldPoint.y * rainbowPaintWorldScale * 0.37f +
            worldPoint.z * rainbowPaintWorldScale * 0.61f +
            seed * 0.137f +
            Time.time * rainbowScrollSpeed,
            1f
        );

        Color color = Color.HSVToRGB(hue, rainbowSaturation, rainbowValue);
        color.a = startColor.a;
        return color;
    }

    private bool IsPointInsideCanvas(Vector3 worldPoint)
    {
        if (paintCanvas == null)
        {
            return false;
        }

        Vector3 localPoint = paintCanvas.InverseTransformPoint(worldPoint);
        float halfWidth = useUnityPlaneLocalSize ? 5f : canvasWidth * 0.5f;
        float halfLength = useUnityPlaneLocalSize ? 5f : canvasLength * 0.5f;
        return Mathf.Abs(localPoint.x) <= halfWidth && Mathf.Abs(localPoint.z) <= halfLength;
    }

    private Vector3 GetCanvasNormal()
    {
        if (paintCanvas == null)
        {
            return Vector3.up;
        }

        Vector3 normal = paintCanvas.up;
        return normal.sqrMagnitude < 0.000001f ? Vector3.up : normal.normalized;
    }

    private void InitializeBucketLiquidVisualIfNeeded()
    {
        if (!updateBucketLiquidVisual)
        {
            return;
        }

        if (bucketLiquidVisual == null && autoCreateLiquidVisualIfMissing)
        {
            Transform parent = emitter != null ? emitter : transform;
            GameObject liquidObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            liquidObject.name = "Visible Bucket Liquid";
            liquidObject.transform.SetParent(parent, false);
            liquidObject.transform.localPosition = autoLiquidLocalPosition;
            liquidObject.transform.localRotation = Quaternion.identity;
            liquidObject.transform.localScale = autoLiquidLocalScale;

            Collider liquidCollider = liquidObject.GetComponent<Collider>();
            if (liquidCollider != null)
            {
                Destroy(liquidCollider);
            }

            bucketLiquidVisual = liquidObject.transform;
        }

        if (bucketLiquidVisual == null)
        {
            return;
        }

        if (!liquidFullTransformCaptured)
        {
            liquidFullLocalPosition = bucketLiquidVisual.localPosition;
            liquidFullLocalScale = bucketLiquidVisual.localScale;
            liquidFullTransformCaptured = true;
        }

        if (cachedLiquidRenderer == null)
        {
            cachedLiquidRenderer = bucketLiquidVisual.GetComponent<Renderer>();
        }

        if (cachedLiquidRenderer != null && liquidMaterialInstance == null)
        {
            Material sourceMaterial = cachedLiquidRenderer.material;
            liquidMaterialInstance = sourceMaterial != null ? sourceMaterial : CreateFallbackMaterial();
            cachedLiquidRenderer.material = liquidMaterialInstance;
        }
    }

    private void UpdateBucketLiquidVisual(bool instant)
    {
        if (!updateBucketLiquidVisual)
        {
            return;
        }

        InitializeBucketLiquidVisualIfNeeded();
        if (bucketLiquidVisual == null)
        {
            return;
        }

        float water01 = CurrentWater01;
        if (hideLiquidVisualWhenEmpty)
        {
            bool shouldShow = water01 > 0.001f;
            if (bucketLiquidVisual.gameObject.activeSelf != shouldShow)
            {
                bucketLiquidVisual.gameObject.SetActive(shouldShow);
            }
            if (!shouldShow)
            {
                return;
            }
        }

        Vector3 targetPosition = liquidFullLocalPosition + Vector3.up * emptyLiquidLocalYOffset * (1f - water01);
        Vector3 targetScale = liquidFullLocalScale;
        targetScale.y = liquidFullLocalScale.y * Mathf.Lerp(emptyLiquidScaleYMultiplier, 1f, water01);

        if (instant || liquidVisualSmoothing <= 0f)
        {
            bucketLiquidVisual.localPosition = targetPosition;
            bucketLiquidVisual.localScale = targetScale;
        }
        else
        {
            float lerpSpeed = 1f - Mathf.Pow(liquidVisualSmoothing, Time.deltaTime * 60f);
            bucketLiquidVisual.localPosition = Vector3.Lerp(bucketLiquidVisual.localPosition, targetPosition, lerpSpeed);
            bucketLiquidVisual.localScale = Vector3.Lerp(bucketLiquidVisual.localScale, targetScale, lerpSpeed);
        }

        UpdateLiquidMaterialColor();
    }

    private void UpdateLiquidMaterialColor()
    {
        if (cachedLiquidRenderer == null)
        {
            return;
        }

        Material mat = liquidMaterialInstance != null ? liquidMaterialInstance : cachedLiquidRenderer.material;
        if (mat == null)
        {
            return;
        }

        Color finalColor = liquidUsesPaintColor ? startColor : liquidColor;
        finalColor.a = liquidColor.a;
        int id = DetectColorProperty(mat);
        if (id >= 0)
        {
            mat.SetColor(id, finalColor);
        }
    }

    private int DetectColorProperty(Material mat)
    {
        if (mat == null) return -1;
        if (mat.HasProperty("_BaseColor")) return Shader.PropertyToID("_BaseColor");
        if (mat.HasProperty("_Color")) return Shader.PropertyToID("_Color");
        if (mat.HasProperty("_TintColor")) return Shader.PropertyToID("_TintColor");
        return -1;
    }

    private Material CreateFallbackMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;
        Material mat = new Material(shader);
        mat.hideFlags = HideFlags.DontSave;
        return mat;
    }

    private Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Bucket GPU SPH Quad";
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }


    private int GetRuntimeParticleCount()
    {
        int desired = syncMaxSPHParticlesWithMaxParticles ? maxParticles : maxSPHParticles;
        return Mathf.Clamp(desired, 1, Mathf.Max(1, maxAllowedParticles));
    }

    private void SyncAndClampParticleCounts()
    {
        maxAllowedParticles = Mathf.Max(1000, maxAllowedParticles);
        maxParticles = Mathf.Clamp(maxParticles, 1, maxAllowedParticles);

        if (syncMaxSPHParticlesWithMaxParticles)
        {
            maxSPHParticles = maxParticles;
        }
        else
        {
            maxSPHParticles = Mathf.Clamp(maxSPHParticles, 1, maxAllowedParticles);
            maxParticles = maxSPHParticles;
        }
    }

    private void RebuildParticleBuffersFromInspector()
    {
        pauseSimulation = true;
        SyncAndClampParticleCounts();
        ReleaseBuffers();
        initialized = false;
        EnsureInitialized();
        ResetParticles();
        SetCommonBuffers();
        SetMaterialBuffers();
        Debug.Log("BucketParticleSystemCustom GPU SPH rebuilt safely with " + GetRuntimeParticleCount().ToString("N0") + " particles. Simulation is paused; turn Pause Simulation OFF when ready.", this);
    }

    private void OnValidate()
    {
        SyncAndClampParticleCounts();
        initialWaterAmount = Mathf.Max(0f, initialWaterAmount);
        currentWaterAmount = Mathf.Clamp(currentWaterAmount, 0f, initialWaterAmount);
        waterPerParticle = Mathf.Max(0.000001f, waterPerParticle);
        initialSpeedMax = Mathf.Max(initialSpeedMin, initialSpeedMax);
        sizeMax = Mathf.Max(sizeMin, sizeMax);
        rainbowSaturation = Mathf.Clamp01(rainbowSaturation);
        rainbowValue = Mathf.Clamp01(rainbowValue);
        rainbowParticleHueScale = Mathf.Max(0f, rainbowParticleHueScale);
        rainbowPaintWorldScale = Mathf.Max(0f, rainbowPaintWorldScale);
        streamCoherence = Mathf.Clamp01(streamCoherence);
        streamBreakup = Mathf.Clamp01(streamBreakup);
        streamTurbulence = Mathf.Clamp01(streamTurbulence);
        flowPulseStrength = Mathf.Clamp(flowPulseStrength, 0f, 0.6f);
        flowPulseFrequency = Mathf.Max(0f, flowPulseFrequency);
        streamWobbleAngle = Mathf.Clamp(streamWobbleAngle, 0f, 8f);
        streamWobbleFrequency = Mathf.Max(0f, streamWobbleFrequency);
        gravityDirectionBlend = Mathf.Clamp01(gravityDirectionBlend);
        streamSpeedMultiplier = Mathf.Max(0.1f, streamSpeedMultiplier);
        forceNarrowOutletSpread = Mathf.Clamp01(forceNarrowOutletSpread);
        automaticJitterReduction = Mathf.Clamp01(automaticJitterReduction);
        tiltFullAngle = Mathf.Max(tiltStartAngle + 0.001f, tiltFullAngle);
        canvasWidth = Mathf.Max(0.01f, canvasWidth);
        canvasLength = Mathf.Max(0.01f, canvasLength);
        outletShapeRadius = Mathf.Max(0f, outletShapeRadius);
        lineOutletLength = Mathf.Max(0.001f, lineOutletLength);
        multipleHoleCount = Mathf.Clamp(multipleHoleCount, 2, 12);
        multipleHoleSpacing = Mathf.Max(0.001f, multipleHoleSpacing);
        flowRateMultiplier = Mathf.Max(0f, flowRateMultiplier);
        outletSizeMultiplier = Mathf.Max(0.01f, outletSizeMultiplier);
        waterLevelPower = Mathf.Max(0.1f, waterLevelPower);
        wateryViscosityResistance = Mathf.Max(0.05f, wateryViscosityResistance);
        thickViscosityResistance = Mathf.Max(wateryViscosityResistance + 0.001f, thickViscosityResistance);
        maxCalculatedEmissionRate = Mathf.Max(1f, maxCalculatedEmissionRate);
        sphSmoothingRadius = Mathf.Max(0.01f, sphSmoothingRadius);
        sphParticleMass = Mathf.Max(0.0001f, sphParticleMass);
        sphRestDensity = Mathf.Max(0.0001f, sphRestDensity);
        sphMaxAcceleration = Mathf.Max(0.01f, sphMaxAcceleration);
        gridResolution = Mathf.Clamp(gridResolution, 8, 96);
        maxParticlesPerCell = Mathf.Clamp(maxParticlesPerCell, 8, 256);
        renderParticleSize = Mathf.Max(0.001f, renderParticleSize);
        boundsSize = Mathf.Max(0.1f, boundsSize);
        spawnRadius = Mathf.Max(0.01f, spawnRadius);
        spawnHeight = Mathf.Max(0.01f, spawnHeight);
        maxEmittedPerFixedStep = Mathf.Max(1, maxEmittedPerFixedStep);
    }

    private void OnDisable()
    {
        ReleaseBuffers();
        initialized = false;
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        ReleaseBuffer(ref particleBuffer);
        ReleaseBuffer(ref cellCountsBuffer);
        ReleaseBuffer(ref cellParticlesBuffer);
        ReleaseBuffer(ref indirectArgsBuffer);
    }

    private void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    private void OnGUI()
    {
        if (showRuntimeSettingsUI)
        {
            DrawRuntimeSettingsUI();
        }
        else if (showStatsOnGUI)
        {
            DrawCompactRuntimeStats();
        }
    }

    private void DrawRuntimeSettingsUI()
    {
        GUIStyle oldLabel = GUI.skin.label;
        GUIStyle oldButton = GUI.skin.button;
        GUIStyle oldToggle = GUI.skin.toggle;
        GUIStyle oldWindow = GUI.skin.window;

        int oldLabelFontSize = GUI.skin.label.fontSize;
        int oldButtonFontSize = GUI.skin.button.fontSize;
        int oldToggleFontSize = GUI.skin.toggle.fontSize;
        int oldWindowFontSize = GUI.skin.window.fontSize;

        Color oldColor = GUI.color;
        Color oldBackground = GUI.backgroundColor;
        Color oldContent = GUI.contentColor;

        GUI.skin.label.fontSize = runtimeSettingsFontSize;
        GUI.skin.button.fontSize = runtimeSettingsFontSize;
        GUI.skin.toggle.fontSize = runtimeSettingsFontSize;
        GUI.skin.window.fontSize = runtimeSettingsFontSize + 1;

        GUI.contentColor = runtimeSettingsTextColor;
        Color bg = runtimeSettingsBackgroundColor;
        bg.a = runtimeSettingsWindowOpacity;
        GUI.backgroundColor = bg;

        if (runtimeSettingsCollapsed)
        {
            Rect collapsedRect = new Rect(runtimeSettingsWindowRect.x, runtimeSettingsWindowRect.y, 235f, 42f);
            GUILayout.BeginArea(collapsedRect, GUI.skin.window);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Settings", GUILayout.Height(24f)))
            {
                runtimeSettingsCollapsed = false;
            }
            GUILayout.Label(GetRuntimeParticleCount().ToString("N0") + " particles");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (showStatsOnGUI)
            {
                DrawCompactRuntimeStats();
            }

            RestoreRuntimeGuiSkin(oldColor, oldBackground, oldContent, oldLabelFontSize, oldButtonFontSize, oldToggleFontSize, oldWindowFontSize);
            return;
        }

        runtimeSettingsWindowRect = GUI.Window(
            735421,
            runtimeSettingsWindowRect,
            DrawRuntimeSettingsWindow,
            "Bucket GPU SPH - Runtime Settings"
        );

        RestoreRuntimeGuiSkin(oldColor, oldBackground, oldContent, oldLabelFontSize, oldButtonFontSize, oldToggleFontSize, oldWindowFontSize);
    }

    private void RestoreRuntimeGuiSkin(
        Color oldColor,
        Color oldBackground,
        Color oldContent,
        int oldLabelFontSize,
        int oldButtonFontSize,
        int oldToggleFontSize,
        int oldWindowFontSize)
    {
        GUI.color = oldColor;
        GUI.backgroundColor = oldBackground;
        GUI.contentColor = oldContent;
        GUI.skin.label.fontSize = oldLabelFontSize;
        GUI.skin.button.fontSize = oldButtonFontSize;
        GUI.skin.toggle.fontSize = oldToggleFontSize;
        GUI.skin.window.fontSize = oldWindowFontSize;
    }

    private void DrawRuntimeSettingsWindow(int windowId)
    {
        GUI.contentColor = runtimeSettingsTextColor;

        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(pauseSimulation ? "Play" : "Pause", GUILayout.Height(28f)))
        {
            pauseSimulation = !pauseSimulation;
        }

        if (GUILayout.Button("Refill", GUILayout.Height(28f)))
        {
            RefillBucket();
        }

        if (GUILayout.Button("Clear", GUILayout.Height(28f)))
        {
            ClearAll();
        }

        if (GUILayout.Button("Collapse", GUILayout.Height(28f)))
        {
            runtimeSettingsCollapsed = true;
        }
        GUILayout.EndHorizontal();

        DrawCompactRuntimeStatsInsideWindow();

        int maxParticlesBeforeGUI = maxParticles;
        int maxSPHParticlesBeforeGUI = maxSPHParticles;
        int maxAllowedParticlesBeforeGUI = maxAllowedParticles;
        int gridResolutionBeforeGUI = gridResolution;
        int maxParticlesPerCellBeforeGUI = maxParticlesPerCell;

        runtimeSettingsScroll = GUILayout.BeginScrollView(runtimeSettingsScroll, GUILayout.Height(Mathf.Max(240f, runtimeSettingsWindowRect.height - 115f)));

        uiShowQuickControls = DrawFoldout("Quick Controls", uiShowQuickControls);
        if (uiShowQuickControls)
        {
            emissionEnabled = GUILayout.Toggle(emissionEnabled, "Emission Enabled");
            pauseSimulation = GUILayout.Toggle(pauseSimulation, "Pause Simulation");
            drawParticles = GUILayout.Toggle(drawParticles, "Draw Particles");
            showStatsOnGUI = GUILayout.Toggle(showStatsOnGUI, "Show Compact Stats");
            useSPHSimulation = GUILayout.Toggle(useSPHSimulation, "Use SPH Simulation");
            useRealisticFallingStream = GUILayout.Toggle(useRealisticFallingStream, "Realistic Falling Stream");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Emit Burst 250"))
            {
                EmitBurst(250);
            }
            if (GUILayout.Button("Reset Particles"))
            {
                ResetParticles();
            }
            GUILayout.EndHorizontal();
        }

        uiShowVisibleThings = DrawFoldout("Everything Visible / Hidden", uiShowVisibleThings);
        if (uiShowVisibleThings)
        {
            drawParticles = GUILayout.Toggle(drawParticles, "Show Falling Particles");
            updateBucketLiquidVisual = GUILayout.Toggle(updateBucketLiquidVisual, "Show Liquid Inside Bucket");
            showOutletGizmo = GUILayout.Toggle(showOutletGizmo, "Show Outlet Gizmo");
            drawOnCanvasTexture = GUILayout.Toggle(drawOnCanvasTexture, "Draw On Canvas Texture");
            useGpuPaintBridge = GUILayout.Toggle(useGpuPaintBridge, "Use GPU Paint Bridge");
            useManualCanvasCollision = GUILayout.Toggle(useManualCanvasCollision, "Use Manual Canvas Collision");
            collideWithGround = GUILayout.Toggle(collideWithGround, "Collide With Ground");
            cullOffscreen = GUILayout.Toggle(cullOffscreen, "Cull Offscreen Particles");
            billboardToCamera = GUILayout.Toggle(billboardToCamera, "Billboard To Camera");
        }

        uiShowParticlesAndFlow = DrawFoldout("Particles / Flow", uiShowParticlesAndFlow);
        if (uiShowParticlesAndFlow)
        {
            int oldMaxParticles = maxParticles;
            maxParticles = DrawIntSlider("Max Particles", maxParticles, 1000, maxAllowedParticles);
            maxSPHParticles = syncMaxSPHParticlesWithMaxParticles ? maxParticles : DrawIntSlider("Max SPH Particles", maxSPHParticles, 1000, maxAllowedParticles);
            syncMaxSPHParticlesWithMaxParticles = GUILayout.Toggle(syncMaxSPHParticlesWithMaxParticles, "Sync Max SPH With Max Particles");

            if (maxParticles != oldMaxParticles)
            {
                runtimeAllocatedParticles = GetRuntimeParticleCount();
            }

            if (GUILayout.Button("Apply Particle Count Safely"))
            {
                pauseSimulation = true;
                applyParticleCountNow = true;
            }

            emissionRate = DrawSlider("Emission Rate", emissionRate, 0f, maxCalculatedEmissionRate, "0");
            flowRateMultiplier = DrawSlider("Flow Rate Multiplier", flowRateMultiplier, 0f, 5f, "0.00");
            maxEmittedPerFixedStep = DrawIntSlider("Max Emitted Per Step", maxEmittedPerFixedStep, 1, 10000);
            waterPerParticle = DrawSlider("Water Per Particle", waterPerParticle, 0.000001f, 0.2f, "0.000000");
            initialSpeedMin = DrawSlider("Initial Speed Min", initialSpeedMin, 0f, 8f, "0.00");
            initialSpeedMax = DrawSlider("Initial Speed Max", Mathf.Max(initialSpeedMin, initialSpeedMax), initialSpeedMin, 12f, "0.00");
            spreadAngle = DrawSlider("Spread Angle", spreadAngle, 0f, 89f, "0.0");
            inheritEmitterVelocity = DrawSlider("Inherit Emitter Velocity", inheritEmitterVelocity, 0f, 2f, "0.00");
        }

        uiShowOutletSettings = DrawFoldout("Outlet Shape / Direction", uiShowOutletSettings);
        if (uiShowOutletSettings)
        {
            outletShape = (OutletShape)GUILayout.Toolbar((int)outletShape, new string[] { "Circle", "Line", "Holes", "Random" });
            emissionAxis = (EmissionAxis)GUILayout.Toolbar((int)emissionAxis, new string[] { "Fwd", "Back", "Up", "Down", "Right", "Left", "W.Down" });

            outletShapeRadius = DrawSlider("Outlet Radius", outletShapeRadius, 0f, 0.35f, "0.000");
            lineOutletLength = DrawSlider("Line Outlet Length", lineOutletLength, 0.001f, 1.2f, "0.000");
            multipleHoleCount = DrawIntSlider("Multiple Hole Count", multipleHoleCount, 2, 12);
            multipleHoleSpacing = DrawSlider("Multiple Hole Spacing", multipleHoleSpacing, 0.001f, 0.4f, "0.000");
            randomOutletJitter = DrawSlider("Random Outlet Jitter", randomOutletJitter, 0f, 1f, "0.00");
            outletShapeAffectsFlowRate = GUILayout.Toggle(outletShapeAffectsFlowRate, "Outlet Shape Affects Flow Rate");
            outletShapeAffectsSpread = GUILayout.Toggle(outletShapeAffectsSpread, "Outlet Shape Affects Spread");
        }

        uiShowRealisticStream = DrawFoldout("Realistic Falling Stream", uiShowRealisticStream);
        if (uiShowRealisticStream)
        {
            streamCoherence = DrawSlider("Stream Coherence", streamCoherence, 0f, 1f, "0.00");
            streamBreakup = DrawSlider("Stream Breakup", streamBreakup, 0f, 1f, "0.00");
            streamTurbulence = DrawSlider("Stream Turbulence", streamTurbulence, 0f, 1f, "0.00");
            flowPulseStrength = DrawSlider("Flow Pulse Strength", flowPulseStrength, 0f, 0.6f, "0.00");
            flowPulseFrequency = DrawSlider("Flow Pulse Frequency", flowPulseFrequency, 0f, 10f, "0.00");
            streamWobbleAngle = DrawSlider("Stream Wobble Angle", streamWobbleAngle, 0f, 8f, "0.00");
            streamWobbleFrequency = DrawSlider("Stream Wobble Frequency", streamWobbleFrequency, 0f, 10f, "0.00");
            gravityDirectionBlend = DrawSlider("Gravity Direction Blend", gravityDirectionBlend, 0f, 1f, "0.00");
            streamSpeedMultiplier = DrawSlider("Stream Speed Multiplier", streamSpeedMultiplier, 0.1f, 4f, "0.00");
            forceNarrowOutletSpread = DrawSlider("Force Narrow Outlet Spread", forceNarrowOutletSpread, 0f, 1f, "0.00");
            automaticJitterReduction = DrawSlider("Automatic Jitter Reduction", automaticJitterReduction, 0f, 1f, "0.00");
        }

        uiShowSPHSettings = DrawFoldout("SPH / Physics", uiShowSPHSettings);
        if (uiShowSPHSettings)
        {
            sphInfluence = DrawSlider("SPH Influence", sphInfluence, 0f, 2f, "0.00");
            sphSmoothingRadius = DrawSlider("Smoothing Radius", sphSmoothingRadius, 0.01f, 1.0f, "0.000");
            sphParticleMass = DrawSlider("Particle Mass", sphParticleMass, 0.0001f, 5f, "0.000");
            sphRestDensity = DrawSlider("Rest Density", sphRestDensity, 1f, 1000f, "0");
            sphPressureStiffness = DrawSlider("Pressure Stiffness", sphPressureStiffness, 0f, 1f, "0.000");
            sphViscosityStrength = DrawSlider("Viscosity Strength", sphViscosityStrength, 0f, 1f, "0.000");
            sphSurfaceTensionStrength = DrawSlider("Surface Tension", sphSurfaceTensionStrength, 0f, 0.4f, "0.000");
            sphMaxAcceleration = DrawSlider("Max Acceleration", sphMaxAcceleration, 0.01f, 250f, "0.0");
            gravityAcceleration = DrawSlider("Gravity Acceleration", gravityAcceleration, 0f, 25f, "0.00");
            gravityScale = DrawSlider("Gravity Scale", gravityScale, 0f, 5f, "0.00");
            drag = DrawSlider("Drag", drag, 0f, 0.25f, "0.000");
            fixedDtOverride = DrawSlider("Fixed DT Override", fixedDtOverride, 0.0001f, 0.05f, "0.0000");
            maxVelocity = DrawSlider("Max Velocity", maxVelocity, 0.001f, 35f, "0.00");
        }

        uiShowRenderingSettings = DrawFoldout("Rendering / Look", uiShowRenderingSettings);
        if (uiShowRenderingSettings)
        {
            renderParticleSize = DrawSlider("Render Particle Size", renderParticleSize, 0.001f, 0.15f, "0.000");
            sizeMin = DrawSlider("Emit Size Min", sizeMin, 0.001f, 0.12f, "0.000");
            sizeMax = DrawSlider("Emit Size Max", Mathf.Max(sizeMin, sizeMax), sizeMin, 0.2f, "0.000");
            castShadows = GUILayout.Toggle(castShadows, "Cast Shadows");
            receiveShadows = GUILayout.Toggle(receiveShadows, "Receive Shadows");
            boundsSize = DrawSlider("Bounds Size", boundsSize, 0.1f, 50f, "0.0");
            boundaryDamping = DrawSlider("Boundary Damping", boundaryDamping, 0f, 1f, "0.00");
            gridResolution = DrawIntSlider("Grid Resolution", gridResolution, 8, 96);
            maxParticlesPerCell = DrawIntSlider("Max Particles Per Cell", maxParticlesPerCell, 8, 256);
            simulationStepsPerFrame = DrawIntSlider("Simulation Steps Per Frame", simulationStepsPerFrame, 1, 8);
        }

        uiShowColorSettings = DrawFoldout("Colors / Rainbow / UI", uiShowColorSettings);
        if (uiShowColorSettings)
        {
            startColor = DrawColorSliders("Particle / Paint Color", startColor, true);
            liquidColor = DrawColorSliders("Liquid Color", liquidColor, true);
            liquidUsesPaintColor = GUILayout.Toggle(liquidUsesPaintColor, "Liquid Uses Paint Color");

            useRainbowParticles = GUILayout.Toggle(useRainbowParticles, "Use Rainbow Particles");
            useRainbowPaintOnCanvas = GUILayout.Toggle(useRainbowPaintOnCanvas, "Use Rainbow Paint On Canvas");
            rainbowSaturation = DrawSlider("Rainbow Saturation", rainbowSaturation, 0f, 1f, "0.00");
            rainbowValue = DrawSlider("Rainbow Value", rainbowValue, 0f, 1f, "0.00");
            rainbowParticleHueScale = DrawSlider("Rainbow Particle Hue Scale", rainbowParticleHueScale, 0f, 0.0002f, "0.000000");
            rainbowPaintWorldScale = DrawSlider("Rainbow Paint World Scale", rainbowPaintWorldScale, 0f, 2f, "0.000");
            rainbowScrollSpeed = DrawSlider("Rainbow Scroll Speed", rainbowScrollSpeed, 0f, 3f, "0.00");

            runtimeSettingsTextColor = DrawColorSliders("UI Text Color", runtimeSettingsTextColor, true);
            runtimeSettingsBackgroundColor = DrawColorSliders("UI Background Color", runtimeSettingsBackgroundColor, true);
            runtimeSettingsAccentColor = DrawColorSliders("UI Accent Color", runtimeSettingsAccentColor, true);
            runtimeSettingsWindowOpacity = DrawSlider("UI Opacity", runtimeSettingsWindowOpacity, 0.55f, 1.0f, "0.00");
            runtimeSettingsFontSize = DrawIntSlider("UI Font Size", runtimeSettingsFontSize, 10, 22);
        }

        uiShowWaterAndCanvas = DrawFoldout("Water / Liquid / Canvas", uiShowWaterAndCanvas);
        if (uiShowWaterAndCanvas)
        {
            initialWaterAmount = DrawSlider("Initial Water Amount", initialWaterAmount, 0f, 1000f, "0.0");
            currentWaterAmount = DrawSlider("Current Water Amount", currentWaterAmount, 0f, Mathf.Max(0.0001f, initialWaterAmount), "0.0");
            fillBucketOnStart = GUILayout.Toggle(fillBucketOnStart, "Fill Bucket On Start");
            stopWhenBucketEmpty = GUILayout.Toggle(stopWhenBucketEmpty, "Stop When Bucket Empty");
            hideLiquidVisualWhenEmpty = GUILayout.Toggle(hideLiquidVisualWhenEmpty, "Hide Liquid Visual When Empty");
            emptyLiquidLocalYOffset = DrawSlider("Empty Liquid Local Y Offset", emptyLiquidLocalYOffset, -2f, 2f, "0.000");
            emptyLiquidScaleYMultiplier = DrawSlider("Empty Liquid Scale Y", emptyLiquidScaleYMultiplier, 0.001f, 1f, "0.000");
            liquidVisualSmoothing = DrawSlider("Liquid Visual Smoothing", liquidVisualSmoothing, 0f, 0.98f, "0.00");

            paintViscosity = DrawSlider("Paint Viscosity", paintViscosity, 0f, 1f, "0.00");
            paintBridgeInterval = DrawSlider("Paint Bridge Interval", paintBridgeInterval, 0.01f, 0.3f, "0.000");
            paintBridgeMarksPerTick = DrawIntSlider("Paint Bridge Marks Per Tick", paintBridgeMarksPerTick, 1, 12);
            paintBridgeMarkSizeMultiplier = DrawSlider("Paint Mark Size Multiplier", paintBridgeMarkSizeMultiplier, 0.001f, 6f, "0.00");
            paintBridgeJitterRadius = DrawSlider("Paint Bridge Jitter Radius", paintBridgeJitterRadius, 0f, 0.6f, "0.000");
            minimumImpactSpeedToDraw = DrawSlider("Minimum Impact Speed To Draw", minimumImpactSpeedToDraw, 0f, 10f, "0.00");
        }

        uiShowPerformanceSettings = DrawFoldout("Performance / Safety", uiShowPerformanceSettings);
        if (uiShowPerformanceSettings)
        {
            startPausedForEditing = GUILayout.Toggle(startPausedForEditing, "Start Paused For Editing");
            resetOnPlay = GUILayout.Toggle(resetOnPlay, "Reset On Play");
            recycleWhenTooFar = GUILayout.Toggle(recycleWhenTooFar, "Recycle When Too Far");
            autoTurboMode = GUILayout.Toggle(autoTurboMode, "Auto Turbo Mode");
            disableSPHInTurboMode = GUILayout.Toggle(disableSPHInTurboMode, "Disable SPH In Turbo Mode");
            useSimulationBudget = GUILayout.Toggle(useSimulationBudget, "Use Simulation Budget");
            maxAllowedParticles = DrawIntSlider("Max Allowed Particles", maxAllowedParticles, 1000, 500000);
            maxCalculatedEmissionRate = DrawSlider("Max Calculated Emission Rate", maxCalculatedEmissionRate, 1f, 500000f, "0");
            maxDistanceFromEmitter = DrawSlider("Max Distance From Emitter", maxDistanceFromEmitter, 1f, 100f, "0.0");
            maxDistanceBelowCanvas = DrawSlider("Max Distance Below Canvas", maxDistanceBelowCanvas, 0.1f, 100f, "0.0");
        }

        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Default Smooth Stream"))
        {
            ApplySmoothStreamPreset();
        }
        if (GUILayout.Button("Fast Mode"))
        {
            ApplyFastModePreset();
        }
        if (GUILayout.Button("Pink Paint"))
        {
            startColor = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);
            liquidColor = startColor;
        }
        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 28f));
        GUILayout.EndVertical();

        ClampRuntimeSettingsFromGUI();

        bool bufferShapeChanged =
            maxParticles != maxParticlesBeforeGUI ||
            maxSPHParticles != maxSPHParticlesBeforeGUI ||
            maxAllowedParticles != maxAllowedParticlesBeforeGUI ||
            gridResolution != gridResolutionBeforeGUI ||
            maxParticlesPerCell != maxParticlesPerCellBeforeGUI;

        if (bufferShapeChanged)
        {
            pauseSimulation = true;
            applyParticleCountNow = true;
        }
    }

    private bool DrawFoldout(string title, bool value)
    {
        GUILayout.Space(6f);
        Color oldBg = GUI.backgroundColor;
        GUI.backgroundColor = runtimeSettingsAccentColor;
        if (GUILayout.Button((value ? "▼ " : "▶ ") + title, GUILayout.Height(25f)))
        {
            value = !value;
        }
        GUI.backgroundColor = oldBg;
        return value;
    }

    private float DrawSlider(string label, float value, float min, float max, string format)
    {
        GUILayout.Label(label + ": " + value.ToString(format));
        return GUILayout.HorizontalSlider(value, min, max);
    }

    private int DrawIntSlider(string label, int value, int min, int max)
    {
        GUILayout.Label(label + ": " + value.ToString("N0"));
        return Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max));
    }

    private Color DrawColorSliders(string label, Color color, bool includeAlpha)
    {
        GUILayout.Label(label);
        color.r = DrawSlider("R", color.r, 0f, 1f, "0.00");
        color.g = DrawSlider("G", color.g, 0f, 1f, "0.00");
        color.b = DrawSlider("B", color.b, 0f, 1f, "0.00");
        if (includeAlpha)
        {
            color.a = DrawSlider("A", color.a, 0f, 1f, "0.00");
        }
        return color;
    }

    private void DrawCompactRuntimeStats()
    {
        float mbParticles = GetRuntimeParticleCount() * PARTICLE_STRIDE / (1024f * 1024f);
        float mbGridCounts = cellsCount * sizeof(uint) / (1024f * 1024f);
        float mbGridParticles = cellsCount * maxParticlesPerCell * sizeof(uint) / (1024f * 1024f);
        float total = mbParticles + mbGridCounts + mbGridParticles;

        GUI.Label(new Rect(12, 12, 800, 118),
            "BucketParticleSystemCustom | FULL GPU SPH | Particles: " + GetRuntimeParticleCount().ToString("N0") +
            " | Tilt: " + CurrentTiltFactor.ToString("0.00") +
            " | Flow: " + CurrentFlowRate.ToString("0") +
            " | Water: " + CurrentWaterAmount.ToString("0.0") + "/" + InitialWaterAmount.ToString("0.0") +
            "\nGrid: " + gridResolution + "^3" +
            " | Cell cap: " + maxParticlesPerCell +
            " | VRAM approx: " + total.ToString("0.0") + " MB" +
            " | Shape: " + outletShape +
            " | " + (pauseSimulation ? "Paused" : "Running"));
    }

    private void DrawCompactRuntimeStatsInsideWindow()
    {
        float mbParticles = GetRuntimeParticleCount() * PARTICLE_STRIDE / (1024f * 1024f);
        float mbGridCounts = cellsCount * sizeof(uint) / (1024f * 1024f);
        float mbGridParticles = cellsCount * maxParticlesPerCell * sizeof(uint) / (1024f * 1024f);
        float total = mbParticles + mbGridCounts + mbGridParticles;

        GUILayout.Label(
            "Particles: " + GetRuntimeParticleCount().ToString("N0") +
            " | Flow: " + CurrentFlowRate.ToString("0") +
            " | Water: " + CurrentWaterAmount.ToString("0.0") + "/" + InitialWaterAmount.ToString("0.0") +
            "\nTilt: " + CurrentTiltFactor.ToString("0.00") +
            " | Grid: " + gridResolution + "^3" +
            " | VRAM: " + total.ToString("0.0") + " MB" +
            " | " + (pauseSimulation ? "Paused" : "Running")
        );
    }

    private void ApplySmoothStreamPreset()
    {
        useRealisticFallingStream = true;
        streamCoherence = 0.86f;
        streamBreakup = 0.14f;
        streamTurbulence = 0.10f;
        flowPulseStrength = 0.07f;
        streamWobbleAngle = 0.85f;
        forceNarrowOutletSpread = 0.82f;
        automaticJitterReduction = 0.88f;
        spreadAngle = 1.5f;
        randomOutletJitter = 0.18f;
        sphViscosityStrength = 0.09f;
        sphSurfaceTensionStrength = 0.01f;
        drag = 0.01f;
    }

    private void ApplyFastModePreset()
    {
        pauseSimulation = true;
        simulationStepsPerFrame = 1;
        gridResolution = Mathf.Clamp(gridResolution, 8, 40);
        maxParticlesPerCell = Mathf.Clamp(maxParticlesPerCell, 8, 64);
        maxEmittedPerFixedStep = Mathf.Clamp(maxEmittedPerFixedStep, 1, 1024);
        useSPHSimulation = true;
        drawParticles = true;
        cullOffscreen = false;
        castShadows = false;
        receiveShadows = false;
    }

    private void ClampRuntimeSettingsFromGUI()
    {
        SyncAndClampParticleCounts();
        runtimeSettingsFontSize = Mathf.Clamp(runtimeSettingsFontSize, 10, 22);
        runtimeSettingsWindowOpacity = Mathf.Clamp(runtimeSettingsWindowOpacity, 0.55f, 1f);

        initialWaterAmount = Mathf.Max(0f, initialWaterAmount);
        currentWaterAmount = Mathf.Clamp(currentWaterAmount, 0f, initialWaterAmount);
        waterPerParticle = Mathf.Max(0.000001f, waterPerParticle);

        initialSpeedMin = Mathf.Max(0f, initialSpeedMin);
        initialSpeedMax = Mathf.Max(initialSpeedMin, initialSpeedMax);

        sizeMin = Mathf.Max(0.001f, sizeMin);
        sizeMax = Mathf.Max(sizeMin, sizeMax);
        renderParticleSize = Mathf.Max(0.001f, renderParticleSize);

        outletShapeRadius = Mathf.Max(0f, outletShapeRadius);
        lineOutletLength = Mathf.Max(0.001f, lineOutletLength);
        multipleHoleCount = Mathf.Clamp(multipleHoleCount, 2, 12);
        multipleHoleSpacing = Mathf.Max(0.001f, multipleHoleSpacing);
        randomOutletJitter = Mathf.Clamp01(randomOutletJitter);

        streamCoherence = Mathf.Clamp01(streamCoherence);
        streamBreakup = Mathf.Clamp01(streamBreakup);
        streamTurbulence = Mathf.Clamp01(streamTurbulence);
        flowPulseStrength = Mathf.Clamp(flowPulseStrength, 0f, 0.6f);
        gravityDirectionBlend = Mathf.Clamp01(gravityDirectionBlend);
        forceNarrowOutletSpread = Mathf.Clamp01(forceNarrowOutletSpread);
        automaticJitterReduction = Mathf.Clamp01(automaticJitterReduction);

        sphInfluence = Mathf.Clamp(sphInfluence, 0f, 2f);
        sphSmoothingRadius = Mathf.Max(0.01f, sphSmoothingRadius);
        sphParticleMass = Mathf.Max(0.0001f, sphParticleMass);
        sphRestDensity = Mathf.Max(0.0001f, sphRestDensity);
        sphMaxAcceleration = Mathf.Max(0.01f, sphMaxAcceleration);

        boundsSize = Mathf.Max(0.1f, boundsSize);
        gridResolution = Mathf.Clamp(gridResolution, 8, 96);
        maxParticlesPerCell = Mathf.Clamp(maxParticlesPerCell, 8, 256);
        simulationStepsPerFrame = Mathf.Clamp(simulationStepsPerFrame, 1, 8);
        maxEmittedPerFixedStep = Mathf.Max(1, maxEmittedPerFixedStep);

        UpdateLiquidMaterialColor();
    }

    private void OnDrawGizmosSelected()
    {
        Transform t = emitter != null ? emitter : transform;
        Vector3 emitterPosition = t.TransformPoint(emissionLocalOffset);
        Vector3 emissionDirection = GetEmissionDirection().normalized;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(boundsCenter, Vector3.one * boundsSize);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(emitterPosition, 0.025f);
        if (showOutletGizmo)
        {
            Gizmos.DrawLine(emitterPosition, emitterPosition + emissionDirection * outletGizmoLength);
            DrawOutletShapeGizmo(t, emitterPosition);
        }

        if (useManualCanvasCollision && paintCanvas != null)
        {
            float halfWidth = useUnityPlaneLocalSize ? 5f : canvasWidth * 0.5f;
            float halfLength = useUnityPlaneLocalSize ? 5f : canvasLength * 0.5f;
            Vector3 p1 = paintCanvas.TransformPoint(new Vector3(-halfWidth, 0f, -halfLength));
            Vector3 p2 = paintCanvas.TransformPoint(new Vector3(halfWidth, 0f, -halfLength));
            Vector3 p3 = paintCanvas.TransformPoint(new Vector3(halfWidth, 0f, halfLength));
            Vector3 p4 = paintCanvas.TransformPoint(new Vector3(-halfWidth, 0f, halfLength));
            Gizmos.color = startColor;
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p4);
            Gizmos.DrawLine(p4, p1);
        }
    }

    private void DrawOutletShapeGizmo(Transform t, Vector3 emitterPosition)
    {
        Vector3 localLineDirection = GetOutletLineDirectionLocal();
        Vector3 worldLineDirection = t.TransformDirection(localLineDirection).normalized;
        Vector3 localPerpendicular = new Vector3(-localLineDirection.z, 0f, localLineDirection.x);
        Vector3 worldPerpendicular = t.TransformDirection(localPerpendicular).normalized;

        switch (outletShape)
        {
            case OutletShape.Circle:
                Gizmos.DrawWireSphere(emitterPosition, outletShapeRadius);
                break;
            case OutletShape.Line:
                Gizmos.DrawLine(emitterPosition - worldLineDirection * lineOutletLength * 0.5f, emitterPosition + worldLineDirection * lineOutletLength * 0.5f);
                break;
            case OutletShape.MultipleHoles:
                for (int i = 0; i < multipleHoleCount; i++)
                {
                    float centeredIndex = i - (multipleHoleCount - 1) * 0.5f;
                    Vector3 point = emitterPosition + worldLineDirection * centeredIndex * multipleHoleSpacing;
                    Gizmos.DrawWireSphere(point, Mathf.Max(0.01f, outletShapeRadius * 0.35f));
                }
                break;
            case OutletShape.Random:
                Gizmos.DrawWireSphere(emitterPosition, outletShapeRadius * Mathf.Lerp(1f, 2f, randomOutletJitter));
                Gizmos.DrawLine(emitterPosition - worldLineDirection * outletShapeRadius, emitterPosition + worldLineDirection * outletShapeRadius);
                Gizmos.DrawLine(emitterPosition - worldPerpendicular * outletShapeRadius, emitterPosition + worldPerpendicular * outletShapeRadius);
                break;
        }
    }
}

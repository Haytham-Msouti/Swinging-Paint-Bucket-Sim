using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

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

    private struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 acceleration;
        public float size;
        public Color color;
        public float density;
        public float pressure;
        public bool active;
        public bool stuckToCanvas;
    }

    [Header("References")]
    [SerializeField] private Transform emitter;
    [SerializeField] private Transform tiltReference;
    [SerializeField] private Camera renderCamera;
    [SerializeField] private Mesh particleMesh;
    [SerializeField] private Material particleMaterial;

    [Header("Outlet Direction - No Collider")]
    [SerializeField] private EmissionAxis emissionAxis = EmissionAxis.LocalDown;
    [SerializeField] private Vector3 emissionLocalOffset = Vector3.zero;
    [SerializeField] private bool showOutletGizmo = true;
    [SerializeField, Min(0.01f)] private float outletGizmoLength = 0.35f;

    [Header("Outlet Shape Algorithm")]
    [SerializeField] private OutletShape outletShape = OutletShape.Circle;

    [Tooltip("Small radius around the outlet center. It controls the width of Circle and Random outlet shapes.")]
    [SerializeField, Min(0f)] private float outletShapeRadius = 0.035f;

    [Tooltip("Length of the slit when Outlet Shape is Line.")]
    [SerializeField, Min(0.001f)] private float lineOutletLength = 0.18f;

    [Tooltip("Local direction of the slit or multiple holes on the bucket mouth plane.")]
    [SerializeField] private Vector3 outletLineLocalDirection = Vector3.right;

    [Tooltip("Number of holes used when Outlet Shape is Multiple Holes.")]
    [SerializeField, Range(2, 12)] private int multipleHoleCount = 3;

    [Tooltip("Distance between holes when Outlet Shape is Multiple Holes.")]
    [SerializeField, Min(0.001f)] private float multipleHoleSpacing = 0.06f;

    [Tooltip("How irregular the Random outlet shape is.")]
    [SerializeField, Range(0f, 1f)] private float randomOutletJitter = 0.75f;

    [Tooltip("If ON, the selected outlet shape changes the calculated flow rate.")]
    [SerializeField] private bool outletShapeAffectsFlowRate = true;

    [Tooltip("If ON, Line and Random outlet shapes slightly change the particle spread.")]
    [SerializeField] private bool outletShapeAffectsSpread = true;

    [Header("Manual Paint Canvas - No Collider")]
    [SerializeField] private Transform paintCanvas;
    [SerializeField] private PaintCanvasDrawer paintCanvasDrawer;
    [SerializeField] private bool useManualCanvasCollision = true;

    [Tooltip("Keep this ON if PaintCanvas is Unity's default Plane.")]
    [SerializeField] private bool useUnityPlaneLocalSize = true;

    [Tooltip("Used only when Use Unity Plane Local Size is OFF.")]
    [SerializeField, Min(0.01f)] private float canvasWidth = 10f;

    [Tooltip("Used only when Use Unity Plane Local Size is OFF.")]
    [SerializeField, Min(0.01f)] private float canvasLength = 10f;

    [SerializeField] private bool stickParticlesOnCanvas = false;
    [SerializeField, Min(0f)] private float paintMarkLift = 0.003f;

    [Header("Impact Timing Fix")]
    [Tooltip("If ON, paint is drawn only when the particle center reaches the canvas plane. This prevents the texture mark from appearing before the visible particle reaches the surface.")]
    [SerializeField] private bool drawOnlyWhenParticleCenterReachesCanvas = true;


    [Header("Paint Drawing Algorithm")]
    [SerializeField] private bool drawOnCanvasTexture = true;
    [SerializeField] private bool removeParticleAfterDrawing = true;
    [SerializeField, Min(0f)] private float minimumImpactSpeedToDraw = 0f;

    [Header("Canvas Hit Behavior - No Reflection")]
    [Tooltip("If ON, a particle never bounces from the canvas after impact. It is removed after drawing instead.")]
    [SerializeField] private bool forceRemoveParticleOnCanvasHit = true;

    [Tooltip("If Force Remove is OFF, this makes the particle stop on the canvas instead of bouncing back.")]
    [SerializeField] private bool absorbParticleOnCanvasHit = true;

    [Header("Bucket Water Amount")]
    [SerializeField] private bool fillBucketOnStart = true;
    [SerializeField, Min(0f)] private float initialWaterAmount = 100f;
    [SerializeField, Min(0f)] private float currentWaterAmount = 100f;

    [Tooltip("How much water one emitted particle consumes.")]
    [SerializeField, Min(0.000001f)] private float waterPerParticle = 0.02f;

    [Tooltip("If ON, emission stops automatically when Current Water Amount reaches zero.")]
    [SerializeField] private bool stopWhenBucketEmpty = true;

    [Header("Visible Liquid Inside Bucket")]
    [Tooltip("If ON, a visible liquid surface inside the bucket follows Current Water Amount.")]
    [SerializeField] private bool updateBucketLiquidVisual = true;

    [Tooltip("Assign a thin cylinder/mesh placed inside the bucket. If empty and Auto Create is ON, the script creates one.")]
    [SerializeField] private Transform bucketLiquidVisual;

    [Tooltip("Creates a simple flattened cylinder inside the emitter if no liquid visual is assigned.")]
    [SerializeField] private bool autoCreateLiquidVisualIfMissing = true;

    [SerializeField] private Vector3 autoLiquidLocalPosition = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 autoLiquidLocalScale = new Vector3(0.65f, 0.035f, 0.65f);

    [Tooltip("How far the liquid surface moves downward locally when the bucket is empty.")]
    [SerializeField] private float emptyLiquidLocalYOffset = -0.35f;

    [Tooltip("Y scale multiplier when the bucket is empty. Keep very small but not zero.")]
    [SerializeField, Range(0.001f, 1f)] private float emptyLiquidScaleYMultiplier = 0.03f;

    [SerializeField] private bool hideLiquidVisualWhenEmpty = true;
    [SerializeField] private bool liquidUsesPaintColor = true;
    [SerializeField] private Color liquidColor = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);

    [Tooltip("0 = instant level update, 1 = very smooth/slow update.")]
    [SerializeField, Range(0f, 0.98f)] private float liquidVisualSmoothing = 0.15f;

    [Header("Emission")]
    [SerializeField] private bool emissionEnabled = true;
    [SerializeField, Min(1)] private int maxParticles = 5000;
    [SerializeField, Min(0f)] private float emissionRate = 200f;
    [SerializeField] private bool emitOnlyWhenTilted = true;
    [SerializeField, Range(0f, 180f)] private float tiltStartAngle = 25f;
    [SerializeField, Range(0f, 180f)] private float tiltFullAngle = 80f;
    [SerializeField, Min(0f)] private float initialSpeedMin = 0.2f;
    [SerializeField, Min(0f)] private float initialSpeedMax = 0.4f;
    [SerializeField, Range(0f, 89f)] private float spreadAngle = 2f;
    [SerializeField, Range(0f, 2f)] private float inheritEmitterVelocity = 1f;
    [SerializeField, Min(0.001f)] private float sizeMin = 0.05f;
    [SerializeField, Min(0.001f)] private float sizeMax = 0.12f;
    [SerializeField] private Color startColor = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);

    [Header("Paint Impact Algorithm")]
    [Tooltip("0 = watery paint, 1 = thick paint")]
    [SerializeField, Range(0f, 1f)] private float paintViscosity = 0.45f;

    [Header("SPH Fluid Simulation")]
    [Tooltip("If ON, particles interact with nearby particles using a light SPH pressure/viscosity model.")]
    [SerializeField] private bool useSPHSimulation = false;

    [Tooltip("Overall strength of the SPH acceleration added to each particle.")]
    [SerializeField, Range(0f, 2f)] private float sphInfluence = 0.65f;

    [Tooltip("Neighbor distance. Bigger value = particles influence each other from farther away.")]
    [SerializeField, Min(0.01f)] private float sphSmoothingRadius = 0.22f;

    [Tooltip("Pseudo mass used only by the SPH solver.")]
    [SerializeField, Min(0.0001f)] private float sphParticleMass = 1f;

    [Tooltip("Target density. Lower values make the stream spread sooner; higher values keep it tighter.")]
    [SerializeField, Min(0.0001f)] private float sphRestDensity = 120f;

    [Tooltip("Pressure stiffness. Bigger value = stronger separation between crowded particles.")]
    [SerializeField, Min(0f)] private float sphPressureStiffness = 0.045f;

    [Tooltip("Makes nearby particles share velocity and look more like liquid.")]
    [SerializeField, Min(0f)] private float sphViscosityStrength = 0.08f;

    [Tooltip("Small cohesion force that keeps the stream from looking like dust.")]
    [SerializeField, Min(0f)] private float sphSurfaceTensionStrength = 0.018f;

    [Tooltip("Safety clamp for SPH acceleration.")]
    [SerializeField, Min(0.01f)] private float sphMaxAcceleration = 35f;

    [Tooltip("Performance safety: only this many active particles are included in SPH each physics step.")]
    [SerializeField, Min(1)] private int maxSPHParticles = 600;

    [Header("Flow Rate Algorithm")]
    [Tooltip("If ON, the emission rate is calculated from tilt, remaining water, outlet size, and viscosity.")]
    [SerializeField] private bool useDynamicFlowRate = true;

    [Tooltip("Base multiplier for the calculated flow rate. Keep it near 1 at first.")]
    [SerializeField, Min(0f)] private float flowRateMultiplier = 1f;

    [Tooltip("Represents the outlet/hole size. 1 = normal hole, bigger values = more flow.")]
    [SerializeField, Min(0.01f)] private float outletSizeMultiplier = 1f;

    [Tooltip("Controls how strongly the remaining bucket amount reduces the flow. 1 = linear, bigger values = faster weakening near the end.")]
    [SerializeField, Min(0.1f)] private float waterLevelPower = 1f;

    [Tooltip("Flow resistance when the paint is watery. Lower value means faster flow.")]
    [SerializeField, Min(0.05f)] private float wateryViscosityResistance = 0.45f;

    [Tooltip("Flow resistance when the paint is thick. Higher value means slower flow.")]
    [SerializeField, Min(0.05f)] private float thickViscosityResistance = 2.25f;

    [Tooltip("Safety limit to avoid creating too many particles per second.")]
    [SerializeField, Min(1f)] private float maxCalculatedEmissionRate = 2000f;

    [Header("Manual Physics")]
    [SerializeField, Min(0.0001f)] private float particleMass = 1f;
    [SerializeField, Min(0f)] private float gravityAcceleration = 9.81f;
    [SerializeField] private float gravityScale = 1f;
    [SerializeField, Min(0f)] private float drag = 0.05f;
    [SerializeField, Min(0f)] private float fixedDtOverride = 0f;

    [Header("Manual Ground Fallback")]
    [SerializeField] private bool collideWithGround = false;
    [SerializeField] private float groundY = 0f;
    [SerializeField, Range(0f, 1f)] private float collisionBounce = 0.35f;
    [SerializeField, Range(0f, 1f)] private float tangentDamping = 0.15f;
    [SerializeField, Min(0f)] private float collisionScatter = 0.10f;
    [SerializeField, Min(0.001f)] private float particleRadiusMultiplier = 0.5f;

    [Header("Particle Cleanup - No Lifetime")]
    [Tooltip("This is not a lifetime. It only recycles particles that go too far away and can never be seen again.")]
    [SerializeField] private bool recycleWhenTooFar = true;
    [SerializeField, Min(1f)] private float maxDistanceFromEmitter = 35f;
    [SerializeField, Min(0.1f)] private float maxDistanceBelowCanvas = 15f;

    [Header("Rendering")]
    [SerializeField] private bool billboardToCamera = true;
    [SerializeField] private bool cullOffscreen = true;
    [SerializeField, Range(0f, 0.5f)] private float viewportCullMargin = 0.05f;
    [SerializeField] private bool castShadows = false;
    [SerializeField] private bool receiveShadows = false;

    [Header("High Count Optimization")]
    [Tooltip("Draws particles in GPU-instanced batches instead of one DrawMesh call per particle. Keep ON for high counts.")]
    [SerializeField] private bool useInstancedRendering = true;

    [Tooltip("Render only every Nth active particle. 1 = render all. Use 2, 4, or 8 when testing 500k+ particles.")]
    [SerializeField, Min(1)] private int renderEveryNthParticle = 1;

    [Tooltip("Hard cap for how many active particles are sent to the renderer each frame.")]
    [SerializeField, Min(1)] private int maxRenderedParticles = 50000;

    [Tooltip("Viewport culling costs CPU per particle. This turns it off automatically for very high active counts.")]
    [SerializeField] private bool skipViewportCullingWhenManyParticles = true;

    [SerializeField, Min(1)] private int viewportCullingDisableThreshold = 50000;

    [Tooltip("Optional CPU safety mode. It updates only a budget of particles per FixedUpdate and uses a larger dt for those particles. OFF = more accurate, ON = smoother editor performance at 500k.")]
    [SerializeField] private bool useSimulationBudget = true;

    [SerializeField, Min(1)] private int maxParticlesSimulatedPerFixedStep = 20000;

    [Header("Turbo / Anti-Freeze Mode")]
    [Tooltip("Automatically switches to cheaper simulation/rendering when many particles are active. Keep ON for 500k tests.")]
    [SerializeField] private bool autoTurboMode = true;

    [Tooltip("Turbo mode starts when Active Particle Count reaches this number.")]
    [SerializeField, Min(1)] private int turboParticleThreshold = 50000;

    [Tooltip("SPH is very expensive on CPU. This disables SPH automatically in turbo mode.")]
    [SerializeField] private bool disableSPHInTurboMode = true;

    [Tooltip("Only this many particles are drawn in turbo mode. The system still keeps/simulates more particles, but renders a sampled subset.")]
    [SerializeField, Min(1)] private int turboRenderedParticleTarget = 35000;

    [Tooltip("Only every Nth particle checks canvas collision in turbo mode. This prevents hundreds of thousands of collision tests.")]
    [SerializeField, Min(1)] private int turboCanvasCollisionStride = 20;

    [Tooltip("Maximum new particles created in one FixedUpdate. This prevents one lag spike from creating a huge emission burst next frame.")]
    [SerializeField, Min(1)] private int maxEmittedPerFixedStep = 1000;


    private const int MaxInstancesPerBatch = 1023;

    private Particle[] particles;
    private int[] freeStack;
    private int freeTop;
    private int[] activeIndices;
    private int[] activeSlots;
    private int activeCount;
    private int simulationCursor;
    private Matrix4x4[] instancedMatrices;
    private float emissionAccumulator;
    private float lastCalculatedFlowRate;
    private float lastTiltFactor;
    private float lastWaterLevelFactor;
    private float lastViscosityFlowFactor;

    private bool liquidFullTransformCaptured;
    private Vector3 liquidFullLocalPosition;
    private Vector3 liquidFullLocalScale;
    private Renderer cachedLiquidRenderer;
    private Material liquidMaterialInstance;

    private float[] sphDensities;
    private float[] sphPressures;
    private Vector3[] sphAccelerations;
    private List<int> sphActiveIndices;
    private Dictionary<Vector3Int, List<int>> sphGrid;

    private bool initialized;
    private bool waterWasInitialized;

    private Vector3 lastEmitterPosition;
    private Vector3 emitterVelocity;

    private MaterialPropertyBlock mpb;
    private int colorPropertyId = -1;

    public float CurrentWaterAmount
    {
        get { return currentWaterAmount; }
    }

    public float InitialWaterAmount
    {
        get { return initialWaterAmount; }
    }

    public bool IsBucketEmpty
    {
        get { return currentWaterAmount <= 0f; }
    }

    public int ActiveParticleCount
    {
        get { return activeCount; }
    }

    public float CurrentFlowRate
    {
        get { return lastCalculatedFlowRate; }
    }

    public float CurrentTiltFactor
    {
        get { return lastTiltFactor; }
    }

    public float CurrentWaterLevelFactor
    {
        get { return lastWaterLevelFactor; }
    }

    public float CurrentWater01
    {
        get
        {
            if (initialWaterAmount <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(currentWaterAmount / initialWaterAmount);
        }
    }

    public void SetEmitter(Transform newEmitter)
    {
        emitter = newEmitter;
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
        Emit(count);
    }

    public void ClearAll()
    {
        EnsureInitialized();

        for (int a = 0; a < activeCount; a++)
        {
            int idx = activeIndices[a];
            particles[idx].active = false;
            particles[idx].stuckToCanvas = false;
            activeSlots[idx] = -1;
        }

        activeCount = 0;
        simulationCursor = 0;

        freeTop = maxParticles;
        for (int i = 0; i < maxParticles; i++)
        {
            freeStack[i] = maxParticles - 1 - i;
        }

        emissionAccumulator = 0f;
        RefillBucket();

        if (paintCanvasDrawer != null)
        {
            paintCanvasDrawer.ClearCanvas();
        }
    }

    private void Reset()
    {
        emitter = transform;
        tiltReference = transform;
    }

    private void OnValidate()
    {
        maxParticles = Mathf.Max(1, maxParticles);
        initialWaterAmount = Mathf.Max(0f, initialWaterAmount);
        currentWaterAmount = Mathf.Clamp(currentWaterAmount, 0f, initialWaterAmount);
        waterPerParticle = Mathf.Max(0.000001f, waterPerParticle);
        initialSpeedMax = Mathf.Max(initialSpeedMin, initialSpeedMax);
        sizeMax = Mathf.Max(sizeMin, sizeMax);
        tiltFullAngle = Mathf.Max(tiltStartAngle + 0.001f, tiltFullAngle);
        particleMass = Mathf.Max(0.0001f, particleMass);
        particleRadiusMultiplier = Mathf.Max(0.001f, particleRadiusMultiplier);
        canvasWidth = Mathf.Max(0.01f, canvasWidth);
        canvasLength = Mathf.Max(0.01f, canvasLength);
        outletGizmoLength = Mathf.Max(0.01f, outletGizmoLength);
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
        maxDistanceFromEmitter = Mathf.Max(1f, maxDistanceFromEmitter);
        maxDistanceBelowCanvas = Mathf.Max(0.1f, maxDistanceBelowCanvas);
        autoLiquidLocalScale.x = Mathf.Max(0.001f, autoLiquidLocalScale.x);
        autoLiquidLocalScale.y = Mathf.Max(0.001f, autoLiquidLocalScale.y);
        autoLiquidLocalScale.z = Mathf.Max(0.001f, autoLiquidLocalScale.z);
        sphSmoothingRadius = Mathf.Max(0.01f, sphSmoothingRadius);
        sphParticleMass = Mathf.Max(0.0001f, sphParticleMass);
        sphRestDensity = Mathf.Max(0.0001f, sphRestDensity);
        sphMaxAcceleration = Mathf.Max(0.01f, sphMaxAcceleration);
        maxSPHParticles = Mathf.Max(1, maxSPHParticles);
        renderEveryNthParticle = Mathf.Max(1, renderEveryNthParticle);
        maxRenderedParticles = Mathf.Max(1, maxRenderedParticles);
        viewportCullingDisableThreshold = Mathf.Max(1, viewportCullingDisableThreshold);
        maxParticlesSimulatedPerFixedStep = Mathf.Max(1, maxParticlesSimulatedPerFixedStep);
        turboParticleThreshold = Mathf.Max(1, turboParticleThreshold);
        turboRenderedParticleTarget = Mathf.Max(1, turboRenderedParticleTarget);
        turboCanvasCollisionStride = Mathf.Max(1, turboCanvasCollisionStride);
        maxEmittedPerFixedStep = Mathf.Max(1, maxEmittedPerFixedStep);
    }

    private void Start()
    {
        EnsureInitialized();

        if (fillBucketOnStart || !waterWasInitialized)
        {
            RefillBucket();
        }

        UpdateBucketLiquidVisual(true);
    }

    private void OnEnable()
    {
        EnsureInitialized();
        lastEmitterPosition = GetEmitterPosition();
        UpdateBucketLiquidVisual(true);
    }

    private void FixedUpdate()
    {
        EnsureInitialized();

        float dt = fixedDtOverride > 0f ? fixedDtOverride : Time.fixedDeltaTime;
        if (dt <= 0f)
        {
            return;
        }

        UpdateEmitterVelocity(dt);

        if (emissionEnabled && (!stopWhenBucketEmpty || currentWaterAmount > 0f))
        {
            float tiltFactor = emitOnlyWhenTilted ? GetTiltFactor() : 1f;
            float currentFlowRate = useDynamicFlowRate
                ? CalculateDynamicFlowRate(tiltFactor)
                : emissionRate * tiltFactor;

            lastCalculatedFlowRate = currentFlowRate;
            emissionAccumulator += currentFlowRate * dt;

            int requestedCount = Mathf.FloorToInt(emissionAccumulator);
            if (requestedCount > 0)
            {
                // Anti-freeze safety: never create a massive burst after a frame hitch.
                requestedCount = Mathf.Min(requestedCount, maxEmittedPerFixedStep);
                int emittedCount = Emit(requestedCount);

                if (emittedCount > 0)
                {
                    emissionAccumulator -= emittedCount;
                }

                if (stopWhenBucketEmpty && currentWaterAmount <= 0f)
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

        SimulateParticles(dt);
        UpdateBucketLiquidVisual(false);
    }

    private void Update()
    {
        EnsureInitialized();

        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }

        UpdateBucketLiquidVisual(false);
        RenderParticles();
    }

    private void EnsureInitialized()
    {
        if (initialized && particles != null && particles.Length == maxParticles)
        {
            return;
        }

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

        particles = new Particle[maxParticles];
        freeStack = new int[maxParticles];
        activeIndices = new int[maxParticles];
        activeSlots = new int[maxParticles];
        freeTop = maxParticles;
        activeCount = 0;
        simulationCursor = 0;

        for (int i = 0; i < maxParticles; i++)
        {
            freeStack[i] = maxParticles - 1 - i;
            activeSlots[i] = -1;
        }

        mpb = new MaterialPropertyBlock();
        instancedMatrices = new Matrix4x4[MaxInstancesPerBatch];

        if (particleMesh == null)
        {
            particleMesh = CreateQuadMesh();
        }

        if (particleMaterial == null)
        {
            particleMaterial = CreateFallbackMaterial();
        }

        if (particleMaterial != null)
        {
            particleMaterial.enableInstancing = true;
        }

        colorPropertyId = DetectColorProperty(particleMaterial);
        EnsureSPHStorage();
        InitializeBucketLiquidVisualIfNeeded();
        lastEmitterPosition = GetEmitterPosition();
        emitterVelocity = Vector3.zero;
        emissionAccumulator = 0f;
        initialized = true;
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
            if (sourceMaterial != null)
            {
                liquidMaterialInstance = sourceMaterial;
            }
            else
            {
                liquidMaterialInstance = CreateFallbackMaterial();
                cachedLiquidRenderer.material = liquidMaterialInstance;
            }
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

    private void EnsureSPHStorage()
    {
        if (sphDensities == null || sphDensities.Length != maxParticles)
        {
            sphDensities = new float[maxParticles];
            sphPressures = new float[maxParticles];
            sphAccelerations = new Vector3[maxParticles];
        }

        if (sphActiveIndices == null)
        {
            sphActiveIndices = new List<int>(Mathf.Min(maxParticles, Mathf.Max(1, maxSPHParticles)));
        }

        if (sphGrid == null)
        {
            sphGrid = new Dictionary<Vector3Int, List<int>>();
        }
    }

    private void ComputeSPHAccelerations()
    {
        EnsureSPHStorage();

        // Important for high counts: do not clear arrays of 500k every physics tick.
        // Only reset the particles that were part of the previous SPH solve.
        if (sphActiveIndices != null)
        {
            for (int a = 0; a < sphActiveIndices.Count; a++)
            {
                int idx = sphActiveIndices[a];
                if (idx >= 0 && idx < sphAccelerations.Length)
                {
                    sphAccelerations[idx] = Vector3.zero;
                    sphDensities[idx] = 0f;
                    sphPressures[idx] = 0f;
                }
            }
        }

        if (!useSPHSimulation || particles == null || activeCount == 0)
        {
            return;
        }

        sphActiveIndices.Clear();
        sphGrid.Clear();

        int limit = Mathf.Min(maxSPHParticles, activeCount);
        float h = Mathf.Max(0.01f, sphSmoothingRadius);
        float h2 = h * h;

        // SPH is intentionally limited. The full 500k stream should be rendered/simulated cheaply,
        // while only a small front sample receives neighbor interaction.
        for (int a = 0; a < activeCount && sphActiveIndices.Count < limit; a++)
        {
            int i = activeIndices[a];
            if (!particles[i].active || particles[i].stuckToCanvas)
            {
                continue;
            }

            sphAccelerations[i] = Vector3.zero;
            sphDensities[i] = 0f;
            sphPressures[i] = 0f;
            sphActiveIndices.Add(i);
            Vector3Int cell = PositionToSPHCell(particles[i].position, h);

            List<int> cellList;
            if (!sphGrid.TryGetValue(cell, out cellList))
            {
                cellList = new List<int>(8);
                sphGrid.Add(cell, cellList);
            }

            cellList.Add(i);
        }

        if (sphActiveIndices.Count <= 1)
        {
            return;
        }

        float h6 = h2 * h2 * h2;
        float h9 = h6 * h2 * h;
        float poly6 = 315f / (64f * Mathf.PI * Mathf.Max(0.000001f, h9));
        float spikyGradient = 45f / (Mathf.PI * Mathf.Max(0.000001f, h6));
        float viscosityLaplacian = 45f / (Mathf.PI * Mathf.Max(0.000001f, h6));

        for (int a = 0; a < sphActiveIndices.Count; a++)
        {
            int i = sphActiveIndices[a];
            Vector3 pi = particles[i].position;
            float density = 0f;

            ForEachSPHNeighbor(pi, h, (j) =>
            {
                Vector3 delta = particles[j].position - pi;
                float r2 = delta.sqrMagnitude;
                if (r2 > h2)
                {
                    return;
                }

                float kernelValue = h2 - r2;
                density += sphParticleMass * poly6 * kernelValue * kernelValue * kernelValue;
            });

            sphDensities[i] = Mathf.Max(0.0001f, density);
            sphPressures[i] = Mathf.Max(0f, (sphDensities[i] - sphRestDensity) * sphPressureStiffness);
        }

        for (int a = 0; a < sphActiveIndices.Count; a++)
        {
            int i = sphActiveIndices[a];
            Vector3 pi = particles[i].position;
            Vector3 vi = particles[i].velocity;
            Vector3 force = Vector3.zero;

            ForEachSPHNeighbor(pi, h, (j) =>
            {
                if (j == i)
                {
                    return;
                }

                Vector3 delta = particles[j].position - pi;
                float r2 = delta.sqrMagnitude;
                if (r2 <= 0.0000001f || r2 > h2)
                {
                    return;
                }

                float r = Mathf.Sqrt(r2);
                Vector3 dirToNeighbor = delta / r;
                float q = Mathf.Clamp01(1f - r / h);
                float neighborDensity = Mathf.Max(0.0001f, sphDensities[j]);

                float pressureMagnitude =
                    sphParticleMass *
                    (sphPressures[i] + sphPressures[j]) /
                    (2f * neighborDensity) *
                    spikyGradient *
                    (h - r) *
                    (h - r);

                Vector3 pressureForce = -dirToNeighbor * pressureMagnitude;

                Vector3 viscosityForce =
                    sphViscosityStrength *
                    sphParticleMass *
                    (particles[j].velocity - vi) /
                    neighborDensity *
                    viscosityLaplacian *
                    (h - r);

                Vector3 cohesionForce = dirToNeighbor * (sphSurfaceTensionStrength * q * q);

                force += pressureForce + viscosityForce + cohesionForce;
            });

            Vector3 acceleration = force / Mathf.Max(0.0001f, sphDensities[i]);
            sphAccelerations[i] = Vector3.ClampMagnitude(acceleration, sphMaxAcceleration);
            particles[i].density = sphDensities[i];
            particles[i].pressure = sphPressures[i];
        }
    }

    private delegate void SPHNeighborAction(int particleIndex);

    private void ForEachSPHNeighbor(Vector3 position, float h, SPHNeighborAction action)
    {
        Vector3Int centerCell = PositionToSPHCell(position, h);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int cell = new Vector3Int(centerCell.x + x, centerCell.y + y, centerCell.z + z);
                    List<int> cellList;
                    if (!sphGrid.TryGetValue(cell, out cellList))
                    {
                        continue;
                    }

                    for (int i = 0; i < cellList.Count; i++)
                    {
                        action(cellList[i]);
                    }
                }
            }
        }
    }

    private Vector3Int PositionToSPHCell(Vector3 position, float h)
    {
        float invH = 1f / Mathf.Max(0.0001f, h);
        return new Vector3Int(
            Mathf.FloorToInt(position.x * invH),
            Mathf.FloorToInt(position.y * invH),
            Mathf.FloorToInt(position.z * invH)
        );
    }

    private void UpdateEmitterVelocity(float dt)
    {
        Vector3 currentPosition = GetEmitterPosition();
        emitterVelocity = (currentPosition - lastEmitterPosition) / dt;
        lastEmitterPosition = currentPosition;
    }

    private float GetTiltFactor()
    {
        Transform t = tiltReference != null ? tiltReference : (emitter != null ? emitter : transform);
        float tilt = Vector3.Angle(t.up, Vector3.up);
        return Mathf.InverseLerp(tiltStartAngle, tiltFullAngle, tilt);
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

        float viscosityResistance = Mathf.Lerp(
            wateryViscosityResistance,
            thickViscosityResistance,
            Mathf.Clamp01(paintViscosity)
        );

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

    private float GetOutletShapeFlowFactor()
    {
        if (!outletShapeAffectsFlowRate)
        {
            return 1f;
        }

        switch (outletShape)
        {
            case OutletShape.Circle:
                return 1f;

            case OutletShape.Line:
                // A longer slit behaves like a wider outlet, so it lets more paint out.
                return Mathf.Clamp(lineOutletLength / 0.18f, 0.45f, 2.5f);

            case OutletShape.MultipleHoles:
                // More holes create several streams and increase the overall flow.
                return Mathf.Clamp(multipleHoleCount * 0.55f, 0.8f, 4f);

            case OutletShape.Random:
                // Irregular holes are slightly unstable, so the flow is less predictable.
                return Random.Range(0.75f, 1.25f);

            default:
                return 1f;
        }
    }

    private int Emit(int requestedCount)
    {
        int emitted = 0;

        for (int n = 0; n < requestedCount; n++)
        {
            if (freeTop <= 0)
            {
                return emitted;
            }

            if (stopWhenBucketEmpty)
            {
                if (currentWaterAmount <= 0f)
                {
                    currentWaterAmount = 0f;
                    return emitted;
                }

                currentWaterAmount -= waterPerParticle;
                if (currentWaterAmount < 0f)
                {
                    currentWaterAmount = 0f;
                }
            }

            int idx = freeStack[--freeTop];
            InitializeParticle(ref particles[idx]);
            AddActiveIndex(idx);
            emitted++;
        }

        return emitted;
    }

    private void InitializeParticle(ref Particle p)
    {
        p.active = true;
        p.stuckToCanvas = false;
        p.position = GetOutletEmissionPosition();
        p.size = Random.Range(sizeMin, sizeMax);
        p.color = startColor;
        p.density = 0f;
        p.pressure = 0f;

        Vector3 dir = SampleDirectionInCone(GetOutletShapeSpreadAngle());
        float speed = Random.Range(initialSpeedMin, initialSpeedMax);

        p.velocity = dir * speed + emitterVelocity * inheritEmitterVelocity;
        p.acceleration = Vector3.zero;
    }

    private Vector3 GetOutletEmissionPosition()
    {
        Transform t = emitter != null ? emitter : transform;
        Vector3 localOffset = emissionLocalOffset + GetOutletShapeLocalOffset();
        return t.TransformPoint(localOffset);
    }

    private Vector3 GetOutletShapeLocalOffset()
    {
        Vector3 lineDirection = GetOutletLineDirectionLocal();
        Vector3 perpendicular = new Vector3(-lineDirection.z, 0f, lineDirection.x);

        switch (outletShape)
        {
            case OutletShape.Circle:
                {
                    Vector2 disk = Random.insideUnitCircle * outletShapeRadius;
                    return new Vector3(disk.x, 0f, disk.y);
                }

            case OutletShape.Line:
                {
                    float alongLine = Random.Range(-0.5f, 0.5f) * lineOutletLength;
                    float smallWidth = Random.Range(-0.5f, 0.5f) * outletShapeRadius * 0.35f;
                    return lineDirection * alongLine + perpendicular * smallWidth;
                }

            case OutletShape.MultipleHoles:
                {
                    int holeIndex = Random.Range(0, multipleHoleCount);
                    float centeredIndex = holeIndex - (multipleHoleCount - 1) * 0.5f;
                    Vector2 disk = Random.insideUnitCircle * outletShapeRadius * 0.30f;
                    return lineDirection * centeredIndex * multipleHoleSpacing + new Vector3(disk.x, 0f, disk.y);
                }

            case OutletShape.Random:
                {
                    Vector2 disk = Random.insideUnitCircle * outletShapeRadius;
                    Vector2 jitter = Random.insideUnitCircle * outletShapeRadius * randomOutletJitter;
                    Vector2 finalOffset = disk + jitter * Random.Range(0.25f, 1f);
                    return new Vector3(finalOffset.x, 0f, finalOffset.y);
                }

            default:
                return Vector3.zero;
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

    private Vector3 SampleDirectionInCone(float coneAngleDeg)
    {
        Vector3 baseDirection = GetEmissionDirection().normalized;
        if (baseDirection.sqrMagnitude < 0.000001f)
        {
            baseDirection = Vector3.down;
        }

        float radius = Mathf.Tan(coneAngleDeg * Mathf.Deg2Rad);
        Vector2 disk = Random.insideUnitCircle * radius;
        Vector3 local = new Vector3(disk.x, disk.y, 1f).normalized;
        Quaternion rotationToDirection = Quaternion.FromToRotation(Vector3.forward, baseDirection);
        return (rotationToDirection * local).normalized;
    }

    private bool IsTurboModeActive()
    {
        return autoTurboMode && activeCount >= turboParticleThreshold;
    }

    private bool IsSPHEnabledThisStep()
    {
        if (!useSPHSimulation)
        {
            return false;
        }

        if (IsTurboModeActive() && disableSPHInTurboMode)
        {
            return false;
        }

        return true;
    }

    private void ClearPreviousSPHAccelerations()
    {
        if (sphActiveIndices == null || sphAccelerations == null)
        {
            return;
        }

        for (int a = 0; a < sphActiveIndices.Count; a++)
        {
            int idx = sphActiveIndices[a];
            if (idx >= 0 && idx < sphAccelerations.Length)
            {
                sphAccelerations[idx] = Vector3.zero;
                if (sphDensities != null && idx < sphDensities.Length) sphDensities[idx] = 0f;
                if (sphPressures != null && idx < sphPressures.Length) sphPressures[idx] = 0f;
            }
        }

        sphActiveIndices.Clear();
        if (sphGrid != null)
        {
            sphGrid.Clear();
        }
    }

    private int GetAdaptiveRenderStride()
    {
        int stride = Mathf.Max(1, renderEveryNthParticle);
        int target = maxRenderedParticles;

        if (IsTurboModeActive())
        {
            target = Mathf.Min(target, turboRenderedParticleTarget);
        }

        if (activeCount > target)
        {
            stride = Mathf.Max(stride, Mathf.CeilToInt(activeCount / (float)Mathf.Max(1, target)));
        }

        return stride;
    }

    private void SimulateParticles(float dt)
    {
        Vector3 gravity = Vector3.down * gravityAcceleration * gravityScale;

        if (activeCount == 0)
        {
            ClearPreviousSPHAccelerations();
            return;
        }

        bool turboMode = IsTurboModeActive();
        bool sphEnabledThisStep = IsSPHEnabledThisStep();
        if (sphEnabledThisStep)
        {
            ComputeSPHAccelerations();
        }
        else
        {
            ClearPreviousSPHAccelerations();
        }

        bool useBudgetNow = (useSimulationBudget || turboMode) && activeCount > maxParticlesSimulatedPerFixedStep;
        int processLimit = useBudgetNow ? Mathf.Min(maxParticlesSimulatedPerFixedStep, activeCount) : activeCount;
        int cursor = useBudgetNow ? Mathf.Clamp(simulationCursor, 0, activeCount - 1) : 0;
        float stepDt = useBudgetNow ? dt * Mathf.Ceil(activeCount / (float)processLimit) : dt;
        int processed = 0;

        while (activeCount > 0 && processed < processLimit)
        {
            if (useBudgetNow)
            {
                if (cursor >= activeCount)
                {
                    cursor = 0;
                }
            }
            else if (cursor >= activeCount)
            {
                break;
            }

            int i = activeIndices[cursor];
            if (!particles[i].active)
            {
                RemoveActiveIndex(i);
                processed++;
                continue;
            }

            Particle p = particles[i];

            if (p.stuckToCanvas)
            {
                particles[i] = p;
                cursor++;
                processed++;
                continue;
            }

            Vector3 previousPosition = p.position;

            Vector3 dragAcc = (-drag / particleMass) * p.velocity;
            Vector3 sphAcc = (sphEnabledThisStep && sphAccelerations != null && i < sphAccelerations.Length)
                ? sphAccelerations[i] * sphInfluence
                : Vector3.zero;
            p.acceleration = gravity + dragAcc + sphAcc;

            p.velocity += p.acceleration * stepDt;
            Vector3 nextPosition = p.position + p.velocity * stepDt;

            float radius = p.size * particleRadiusMultiplier;

            bool hitCanvas = false;
            bool removeAfterHit = false;
            bool allowCanvasCollision = useManualCanvasCollision && paintCanvas != null;
            if (turboMode && turboCanvasCollisionStride > 1 && (processed % turboCanvasCollisionStride) != 0)
            {
                allowCanvasCollision = false;
            }

            if (allowCanvasCollision)
            {
                hitCanvas = ResolveManualCanvasCollision(previousPosition, ref nextPosition, ref p.velocity, radius, ref p, out removeAfterHit);
            }

            if (removeAfterHit)
            {
                Recycle(i);
                processed++;
                continue;
            }

            if (!hitCanvas && collideWithGround)
            {
                ResolveGroundCollision(ref nextPosition, ref p.velocity, radius);
            }

            if (!hitCanvas)
            {
                p.position = nextPosition;
                p.color = startColor;
            }

            particles[i] = p;

            if (recycleWhenTooFar && ShouldRecycleParticle(p.position))
            {
                Recycle(i);
                processed++;
                continue;
            }

            cursor++;
            processed++;
        }

        simulationCursor = useBudgetNow && activeCount > 0 ? cursor % activeCount : 0;
    }

    private bool ResolveManualCanvasCollision(
        Vector3 previousPosition,
        ref Vector3 nextPosition,
        ref Vector3 velocity,
        float radius,
        ref Particle p,
        out bool removeAfterHit)
    {
        removeAfterHit = false;

        Vector3 canvasPoint = paintCanvas.position;
        Vector3 canvasNormal = GetCanvasNormal();

        float previousDistance = Vector3.Dot(previousPosition - canvasPoint, canvasNormal);
        float nextDistance = Vector3.Dot(nextPosition - canvasPoint, canvasNormal);

        // Old behavior used radius, so the drawing happened as soon as the front edge
        // of the particle touched the canvas. Visually this can look early because
        // the rendered particle center is still above the canvas.
        // New behavior waits until the particle center reaches the canvas plane.
        float impactDistance = drawOnlyWhenParticleCenterReachesCanvas ? 0f : radius;

        if (previousDistance <= impactDistance || nextDistance > impactDistance)
        {
            return false;
        }

        float denominator = previousDistance - nextDistance;
        if (Mathf.Abs(denominator) < 0.000001f)
        {
            return false;
        }

        float t = (previousDistance - impactDistance) / denominator;
        t = Mathf.Clamp01(t);

        Vector3 centerAtHit = Vector3.Lerp(previousPosition, nextPosition, t);
        Vector3 surfacePoint = centerAtHit - canvasNormal * impactDistance;

        if (!IsPointInsideCanvas(surfacePoint))
        {
            return false;
        }

        float normalImpactSpeed = Mathf.Abs(Vector3.Dot(velocity, canvasNormal));
        float impactSpeed = velocity.magnitude;

        Vector3 incomingDirection = velocity.sqrMagnitude > 0.000001f
            ? velocity.normalized
            : -canvasNormal;

        // 0 degrees = direct hit
        // 90 degrees = glancing / tangential hit
        float impactAngle = Vector3.Angle(-incomingDirection, canvasNormal);

        // Use only the velocity component that lies on the canvas plane for the splatter direction.
        // This prevents the directional splatter from looking reflected by the canvas normal.
        Vector3 splatterDirectionOnCanvas = Vector3.ProjectOnPlane(velocity, canvasNormal);
        if (splatterDirectionOnCanvas.sqrMagnitude < 0.000001f)
        {
            splatterDirectionOnCanvas = incomingDirection;
        }
        else
        {
            splatterDirectionOnCanvas.Normalize();
        }

        if (drawOnCanvasTexture && paintCanvasDrawer != null && normalImpactSpeed >= minimumImpactSpeedToDraw)
        {
            paintCanvasDrawer.DrawPaint(
                surfacePoint,
                startColor,
                p.size,
                impactSpeed,
                impactAngle,
                paintViscosity,
                splatterDirectionOnCanvas
            );
        }

        if (forceRemoveParticleOnCanvasHit || (drawOnCanvasTexture && removeParticleAfterDrawing))
        {
            removeAfterHit = true;
            return true;
        }

        if (absorbParticleOnCanvasHit || stickParticlesOnCanvas)
        {
            p.position = surfacePoint + canvasNormal * paintMarkLift;
            p.velocity = Vector3.zero;
            p.acceleration = Vector3.zero;
            p.stuckToCanvas = true;
            p.color = startColor;
            return true;
        }

        nextPosition = centerAtHit;
        ApplyBounce(ref velocity, canvasNormal);

        return true;
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

        return Mathf.Abs(localPoint.x) <= halfWidth &&
               Mathf.Abs(localPoint.z) <= halfLength;
    }

    private Vector3 GetCanvasNormal()
    {
        if (paintCanvas == null)
        {
            return Vector3.up;
        }

        Vector3 normal = paintCanvas.up;
        if (normal.sqrMagnitude < 0.000001f)
        {
            return Vector3.up;
        }

        return normal.normalized;
    }

    private bool ShouldRecycleParticle(Vector3 position)
    {
        Vector3 emitterPosition = GetEmitterPosition();
        if ((position - emitterPosition).sqrMagnitude > maxDistanceFromEmitter * maxDistanceFromEmitter)
        {
            return true;
        }

        if (paintCanvas != null)
        {
            Vector3 normal = GetCanvasNormal();
            float distance = Vector3.Dot(position - paintCanvas.position, normal);
            if (distance < -maxDistanceBelowCanvas)
            {
                return true;
            }
        }
        else if (position.y < groundY - maxDistanceBelowCanvas)
        {
            return true;
        }

        return false;
    }

    private void ResolveGroundCollision(ref Vector3 position, ref Vector3 velocity, float radius)
    {
        if (position.y - radius >= groundY)
        {
            return;
        }

        position.y = groundY + radius;
        ApplyBounce(ref velocity, Vector3.up);
    }

    private void ApplyBounce(ref Vector3 velocity, Vector3 normal)
    {
        float normalSpeed = Vector3.Dot(velocity, normal);
        if (normalSpeed >= 0f)
        {
            return;
        }

        Vector3 normalVelocity = normal * normalSpeed;
        Vector3 tangentVelocity = velocity - normalVelocity;

        velocity = (-collisionBounce * normalVelocity) + ((1f - tangentDamping) * tangentVelocity);

        if (collisionScatter > 0f)
        {
            velocity += Random.insideUnitSphere * collisionScatter;
        }
    }

    private void AddActiveIndex(int particleIndex)
    {
        if (activeSlots == null || activeIndices == null)
        {
            return;
        }

        if (particleIndex < 0 || particleIndex >= activeSlots.Length)
        {
            return;
        }

        if (activeSlots[particleIndex] >= 0)
        {
            return;
        }

        activeSlots[particleIndex] = activeCount;
        activeIndices[activeCount] = particleIndex;
        activeCount++;
    }

    private void RemoveActiveIndex(int particleIndex)
    {
        if (activeSlots == null || activeIndices == null)
        {
            return;
        }

        if (particleIndex < 0 || particleIndex >= activeSlots.Length)
        {
            return;
        }

        int slot = activeSlots[particleIndex];
        if (slot < 0 || slot >= activeCount)
        {
            activeSlots[particleIndex] = -1;
            return;
        }

        int lastSlot = activeCount - 1;
        int lastParticle = activeIndices[lastSlot];
        activeIndices[slot] = lastParticle;
        activeSlots[lastParticle] = slot;
        activeSlots[particleIndex] = -1;
        activeCount--;
    }

    private void Recycle(int particleIndex)
    {
        if (particleIndex < 0 || particles == null || particleIndex >= particles.Length)
        {
            return;
        }

        if (!particles[particleIndex].active)
        {
            return;
        }

        particles[particleIndex].active = false;
        particles[particleIndex].stuckToCanvas = false;
        RemoveActiveIndex(particleIndex);

        if (freeTop < freeStack.Length)
        {
            freeStack[freeTop++] = particleIndex;
        }
    }

    private void RenderParticles()
    {
        if (particleMesh == null || particleMaterial == null || activeCount == 0)
        {
            return;
        }

        if (useInstancedRendering && SystemInfo.supportsInstancing)
        {
            RenderParticlesInstanced();
        }
        else
        {
            // Legacy rendering means one draw call per particle. In turbo mode that is a guaranteed freeze.
            if (IsTurboModeActive())
            {
                return;
            }
            RenderParticlesLegacy();
        }
    }

    private void RenderParticlesInstanced()
    {
        if (instancedMatrices == null || instancedMatrices.Length != MaxInstancesPerBatch)
        {
            instancedMatrices = new Matrix4x4[MaxInstancesPerBatch];
        }

        Quaternion billboardRotation = renderCamera != null ? renderCamera.transform.rotation : Quaternion.identity;
        Quaternion canvasRotation = paintCanvas != null ? Quaternion.LookRotation(GetCanvasNormal()) : Quaternion.identity;
        bool skipCull = !cullOffscreen ||
                        (skipViewportCullingWhenManyParticles && activeCount >= viewportCullingDisableThreshold) ||
                        renderCamera == null;

        mpb.Clear();
        if (colorPropertyId >= 0)
        {
            mpb.SetColor(colorPropertyId, startColor);
        }

        int batchCount = 0;
        int rendered = 0;
        int stride = GetAdaptiveRenderStride();

        for (int a = 0; a < activeCount && rendered < maxRenderedParticles; a += stride)
        {
            Particle p = particles[activeIndices[a]];
            if (!p.active)
            {
                continue;
            }

            if (!skipCull && IsParticleOutsideCamera(p.position))
            {
                continue;
            }

            Quaternion rotation;
            if (p.stuckToCanvas && paintCanvas != null)
            {
                rotation = canvasRotation;
            }
            else
            {
                rotation = billboardToCamera ? billboardRotation : Quaternion.identity;
            }

            instancedMatrices[batchCount] = Matrix4x4.TRS(p.position, rotation, Vector3.one * p.size);
            batchCount++;
            rendered++;

            if (batchCount == MaxInstancesPerBatch)
            {
                FlushInstancedBatch(batchCount);
                batchCount = 0;
            }
        }

        if (batchCount > 0)
        {
            FlushInstancedBatch(batchCount);
        }
    }

    private void FlushInstancedBatch(int batchCount)
    {
        Graphics.DrawMeshInstanced(
            particleMesh,
            0,
            particleMaterial,
            instancedMatrices,
            batchCount,
            mpb,
            castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows,
            gameObject.layer,
            null,
            LightProbeUsage.Off
        );
    }

    private void RenderParticlesLegacy()
    {
        Quaternion billboardRotation = renderCamera != null ? renderCamera.transform.rotation : Quaternion.identity;
        Quaternion canvasRotation = paintCanvas != null ? Quaternion.LookRotation(GetCanvasNormal()) : Quaternion.identity;
        bool skipCull = !cullOffscreen || renderCamera == null;
        int rendered = 0;
        int stride = GetAdaptiveRenderStride();

        for (int a = 0; a < activeCount && rendered < maxRenderedParticles; a += stride)
        {
            Particle p = particles[activeIndices[a]];
            if (!p.active)
            {
                continue;
            }

            if (!skipCull && IsParticleOutsideCamera(p.position))
            {
                continue;
            }

            Quaternion rotation;
            if (p.stuckToCanvas && paintCanvas != null)
            {
                rotation = canvasRotation;
            }
            else
            {
                rotation = billboardToCamera ? billboardRotation : Quaternion.identity;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(p.position, rotation, Vector3.one * p.size);

            mpb.Clear();
            if (colorPropertyId >= 0)
            {
                mpb.SetColor(colorPropertyId, p.color);
            }

            Graphics.DrawMesh(
                particleMesh,
                matrix,
                particleMaterial,
                gameObject.layer,
                null,
                0,
                mpb,
                castShadows,
                receiveShadows,
                false
            );

            rendered++;
        }
    }

    private bool IsParticleOutsideCamera(Vector3 position)
    {
        if (renderCamera == null)
        {
            return false;
        }

        Vector3 vp = renderCamera.WorldToViewportPoint(position);
        return vp.z < 0f ||
               vp.x < -viewportCullMargin || vp.x > 1f + viewportCullMargin ||
               vp.y < -viewportCullMargin || vp.y > 1f + viewportCullMargin;
    }

    private Vector3 GetEmitterPosition()
    {
        Transform t = emitter != null ? emitter : transform;
        return t.TransformPoint(emissionLocalOffset);
    }

    private Vector3 GetEmissionDirection()
    {
        Transform t = emitter != null ? emitter : transform;

        switch (emissionAxis)
        {
            case EmissionAxis.LocalForward:
                return t.forward;
            case EmissionAxis.LocalBack:
                return -t.forward;
            case EmissionAxis.LocalUp:
                return t.up;
            case EmissionAxis.LocalDown:
                return -t.up;
            case EmissionAxis.LocalRight:
                return t.right;
            case EmissionAxis.LocalLeft:
                return -t.right;
            case EmissionAxis.WorldDown:
                return Vector3.down;
            default:
                return -t.up;
        }
    }

    private int DetectColorProperty(Material mat)
    {
        if (mat == null)
        {
            return -1;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            return Shader.PropertyToID("_BaseColor");
        }

        if (mat.HasProperty("_Color"))
        {
            return Shader.PropertyToID("_Color");
        }

        if (mat.HasProperty("_TintColor"))
        {
            return Shader.PropertyToID("_TintColor");
        }

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

    private void DrawOutletShapeGizmo(Transform t, Vector3 emitterPosition)
    {
        if (t == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;

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
                {
                    Vector3 a = emitterPosition - worldLineDirection * lineOutletLength * 0.5f;
                    Vector3 b = emitterPosition + worldLineDirection * lineOutletLength * 0.5f;
                    Gizmos.DrawLine(a, b);
                    Gizmos.DrawSphere(a, 0.012f);
                    Gizmos.DrawSphere(b, 0.012f);
                    break;
                }

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

    private Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "BucketParticleQuad";

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

        mesh.triangles = new int[]
        {
            0, 1, 2,
            0, 2, 3
        };

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private void OnDrawGizmosSelected()
    {
        Transform t = emitter != null ? emitter : transform;

        Gizmos.color = Color.cyan;
        Vector3 emitterPosition = t.TransformPoint(emissionLocalOffset);
        Vector3 emissionDirection = GetEmissionDirection();
        Gizmos.DrawSphere(emitterPosition, 0.025f);

        if (showOutletGizmo)
        {
            Gizmos.DrawLine(emitterPosition, emitterPosition + emissionDirection.normalized * outletGizmoLength);
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

            Gizmos.color = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p4);
            Gizmos.DrawLine(p4, p1);

            Vector3 center = paintCanvas.position;
            Vector3 normal = GetCanvasNormal();
            Gizmos.DrawLine(center, center + normal * 0.5f);
        }

        if (collideWithGround)
        {
            Gizmos.color = new Color(1.0f, 0.4117647f, 0.7058824f, 1.0f);
            Gizmos.DrawLine(new Vector3(-2f, groundY, -2f), new Vector3(2f, groundY, -2f));
            Gizmos.DrawLine(new Vector3(2f, groundY, -2f), new Vector3(2f, groundY, 2f));
            Gizmos.DrawLine(new Vector3(2f, groundY, 2f), new Vector3(-2f, groundY, 2f));
            Gizmos.DrawLine(new Vector3(-2f, groundY, 2f), new Vector3(-2f, groundY, -2f));
        }
    }
}

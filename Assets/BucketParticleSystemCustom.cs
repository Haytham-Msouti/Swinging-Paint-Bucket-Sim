using UnityEngine;

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

    private Particle[] particles;
    private int[] freeStack;
    private int freeTop;
    private float emissionAccumulator;
    private float lastCalculatedFlowRate;
    private float lastTiltFactor;
    private float lastWaterLevelFactor;
    private float lastViscosityFlowFactor;
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
    }

    public void SetCurrentWaterAmount(float amount)
    {
        currentWaterAmount = Mathf.Clamp(amount, 0f, initialWaterAmount);
        if (currentWaterAmount <= 0f)
        {
            emissionAccumulator = 0f;
        }
        waterWasInitialized = true;
    }

    public void AddWater(float amount)
    {
        currentWaterAmount = Mathf.Clamp(currentWaterAmount + Mathf.Max(0f, amount), 0f, initialWaterAmount);
        waterWasInitialized = true;
    }

    public void EmitBurst(int count)
    {
        EnsureInitialized();
        Emit(count);
    }

    public void ClearAll()
    {
        EnsureInitialized();

        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].active = false;
            particles[i].stuckToCanvas = false;
        }

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
    }

    private void Start()
    {
        EnsureInitialized();

        if (fillBucketOnStart || !waterWasInitialized)
        {
            RefillBucket();
        }
    }

    private void OnEnable()
    {
        EnsureInitialized();
        lastEmitterPosition = GetEmitterPosition();
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
    }

    private void Update()
    {
        EnsureInitialized();

        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }

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
        freeTop = maxParticles;

        for (int i = 0; i < maxParticles; i++)
        {
            freeStack[i] = maxParticles - 1 - i;
        }

        mpb = new MaterialPropertyBlock();

        if (particleMesh == null)
        {
            particleMesh = CreateQuadMesh();
        }

        if (particleMaterial == null)
        {
            particleMaterial = CreateFallbackMaterial();
        }

        colorPropertyId = DetectColorProperty(particleMaterial);
        lastEmitterPosition = GetEmitterPosition();
        emitterVelocity = Vector3.zero;
        emissionAccumulator = 0f;
        initialized = true;
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

    private void SimulateParticles(float dt)
    {
        Vector3 gravity = Vector3.down * gravityAcceleration * gravityScale;

        for (int i = 0; i < particles.Length; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            Particle p = particles[i];

            if (p.stuckToCanvas)
            {
                particles[i] = p;
                continue;
            }

            Vector3 previousPosition = p.position;

            Vector3 dragAcc = (-drag / particleMass) * p.velocity;
            p.acceleration = gravity + dragAcc;

            p.velocity += p.acceleration * dt;
            Vector3 nextPosition = p.position + p.velocity * dt;

            float radius = p.size * particleRadiusMultiplier;

            bool hitCanvas = false;
            bool removeAfterHit = false;
            if (useManualCanvasCollision && paintCanvas != null)
            {
                hitCanvas = ResolveManualCanvasCollision(previousPosition, ref nextPosition, ref p.velocity, radius, ref p, out removeAfterHit);
            }

            if (removeAfterHit)
            {
                Recycle(i);
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
            }
        }
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

    private void Recycle(int particleIndex)
    {
        particles[particleIndex].active = false;
        particles[particleIndex].stuckToCanvas = false;

        if (freeTop < freeStack.Length)
        {
            freeStack[freeTop++] = particleIndex;
        }
    }

    private void RenderParticles()
    {
        if (particleMesh == null || particleMaterial == null)
        {
            return;
        }

        Quaternion billboardRotation = renderCamera != null ? renderCamera.transform.rotation : Quaternion.identity;
        Quaternion canvasRotation = paintCanvas != null ? Quaternion.LookRotation(GetCanvasNormal()) : Quaternion.identity;

        for (int i = 0; i < particles.Length; i++)
        {
            Particle p = particles[i];
            if (!p.active)
            {
                continue;
            }

            if (cullOffscreen && renderCamera != null)
            {
                Vector3 vp = renderCamera.WorldToViewportPoint(p.position);
                if (vp.z < 0f ||
                    vp.x < -viewportCullMargin || vp.x > 1f + viewportCullMargin ||
                    vp.y < -viewportCullMargin || vp.y > 1f + viewportCullMargin)
                {
                    continue;
                }
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

            if (colorPropertyId >= 0)
            {
                mpb.Clear();
                mpb.SetColor(colorPropertyId, p.color);
            }
            else
            {
                mpb.Clear();
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
        }
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

            Gizmos.color = new Color(0f, 0.5f, 1f, 0.85f);
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
            Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
            Gizmos.DrawLine(new Vector3(-2f, groundY, -2f), new Vector3(2f, groundY, -2f));
            Gizmos.DrawLine(new Vector3(2f, groundY, -2f), new Vector3(2f, groundY, 2f));
            Gizmos.DrawLine(new Vector3(2f, groundY, 2f), new Vector3(-2f, groundY, 2f));
            Gizmos.DrawLine(new Vector3(-2f, groundY, 2f), new Vector3(-2f, groundY, -2f));
        }
    }
}

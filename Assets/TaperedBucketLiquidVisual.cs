using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TaperedBucketLiquidVisual : MonoBehaviour
{

    private struct ReservoirParticle
    {
        public Vector3 localPosition;
        public Vector3 localVelocity;
        public float phase;
        public bool active;
    }

    [Header("References")]
    [Tooltip("Drag the object that has BucketParticleSystemCustom here. The visible amount follows CurrentWaterAmount / InitialWaterAmount.")]
    [SerializeField] private BucketParticleSystemCustom bucketSystem;

    [Tooltip("The local space used for the bucket. Usually leave empty so this object's transform is used.")]
    [SerializeField] private Transform bucketSpace;

    [SerializeField] private Camera renderCamera;
    [SerializeField] private Mesh particleMesh;
    [SerializeField] private Material particleMaterial;

    [Header("Frustum / Bucket Interior Shape")]
    [Tooltip("Local Y of the inside bottom of the bucket.")]
    [SerializeField] private float bottomLocalY = -0.35f;

    [Tooltip("Height of the liquid when the bucket is full.")]
    [SerializeField, Min(0.001f)] private float fullLiquidHeight = 0.70f;

    [Tooltip("Radius of the bucket interior at the bottom.")]
    [SerializeField, Min(0.001f)] private float bottomRadius = 0.28f;

    [Tooltip("Radius of the bucket interior at the full top level.")]
    [SerializeField, Min(0.001f)] private float fullTopRadius = 0.62f;

    [Tooltip("If ON, the top radius becomes smaller as the liquid level goes down, matching a tapered bucket.")]
    [SerializeField] private bool radiusFollowsFillHeight = true;

    [Header("Visible Reservoir Particles")]
    [Tooltip("How many particles are visible when the bucket is full.")]
    [SerializeField, Range(20, 3000)] private int particlesAtFull = 850;

    [SerializeField, Min(0.001f)] private float particleSize = 0.035f;
    [SerializeField] private Color particleColor = new Color(1.0f, 0.4117647f, 0.7058824f, 0.85f);

    [Tooltip("If ON, the reservoir particles use the material color property every frame.")]
    [SerializeField] private bool forceParticleColor = true;

    [Tooltip("Keeps the surface less flat by adding a small random height wobble.")]
    [SerializeField, Range(0f, 0.08f)] private float surfaceWobble = 0.018f;

    [Header("Interior SPH Practical Simulation")]
    [SerializeField] private bool simulateSPHInsideBucket = true;

    [Tooltip("Neighbor distance for the simple SPH-like pressure inside the bucket.")]
    [SerializeField, Min(0.01f)] private float sphRadius = 0.095f;

    [Tooltip("Pushes crowded particles away from each other.")]
    [SerializeField, Range(0f, 80f)] private float pressureStrength = 18f;

    [Tooltip("Makes nearby particles share velocity so they move as liquid, not dust.")]
    [SerializeField, Range(0f, 25f)] private float viscosityStrength = 4.5f;

    [Tooltip("Tiny random movement so the inside does not look frozen.")]
    [SerializeField, Range(0f, 5f)] private float agitation = 0.55f;

    [Tooltip("How strongly particles settle according to world gravity inside the tilted bucket.")]
    [SerializeField, Range(0f, 12f)] private float insideGravity = 4.5f;

    [Tooltip("Velocity loss when particles hit the inner bucket wall.")]
    [SerializeField, Range(0f, 1f)] private float wallDamping = 0.55f;

    [Tooltip("Safety clamp for particle velocity inside the bucket.")]
    [SerializeField, Min(0.01f)] private float maxInsideVelocity = 2.2f;

    [Header("Level Sync")]
    [Tooltip("If ON, particles above the current level are pushed down instead of staying above the liquid surface.")]
    [SerializeField] private bool clampParticlesToWaterLevel = true;

    [Tooltip("If ON, when water decreases, the highest particles are hidden first.")]
    [SerializeField] private bool hideHighestParticlesFirst = true;

    [Header("Rendering")]
    [SerializeField] private bool billboardToCamera = true;
    [SerializeField] private bool castShadows = false;
    [SerializeField] private bool receiveShadows = false;

    private ReservoirParticle[] particles;
    private int currentVisibleCount;
    private MaterialPropertyBlock mpb;
    private int colorPropertyId = -1;
    private bool initialized;

    private void Reset()
    {
        bucketSpace = transform;
        bucketSystem = GetComponentInParent<BucketParticleSystemCustom>();
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        RebuildReservoir(true);
    }

    private void OnValidate()
    {
        fullLiquidHeight = Mathf.Max(0.001f, fullLiquidHeight);
        bottomRadius = Mathf.Max(0.001f, bottomRadius);
        fullTopRadius = Mathf.Max(0.001f, fullTopRadius);
        particleSize = Mathf.Max(0.001f, particleSize);
        particlesAtFull = Mathf.Clamp(particlesAtFull, 20, 3000);
        sphRadius = Mathf.Max(0.01f, sphRadius);
        maxInsideVelocity = Mathf.Max(0.01f, maxInsideVelocity);

        if (Application.isPlaying && initialized)
        {
            EnsureArraySize();
            RebuildReservoir(false);
        }
    }

    private void FixedUpdate()
    {
        EnsureInitialized();
        SyncVisibleCountWithBucketAmount();

        if (simulateSPHInsideBucket && currentVisibleCount > 0)
        {
            SimulateInteriorSPH(Time.fixedDeltaTime);
        }
    }

    private void Update()
    {
        EnsureInitialized();

        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }

        RenderReservoirParticles();
    }

    public void RebuildReservoir(bool forceRandomize)
    {
        EnsureInitialized();
        EnsureArraySize();
        SyncVisibleCountWithBucketAmount();

        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].active = i < currentVisibleCount;

            if (forceRandomize || particles[i].localPosition == Vector3.zero)
            {
                particles[i].localPosition = SamplePointInsideCurrentLiquidVolume();
                particles[i].localVelocity = Vector3.zero;
                particles[i].phase = Random.Range(0f, 1000f);
            }
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        if (bucketSpace == null)
        {
            bucketSpace = transform;
        }

        if (bucketSystem == null)
        {
            bucketSystem = GetComponentInParent<BucketParticleSystemCustom>();
        }

        if (particleMesh == null)
        {
            particleMesh = CreateQuadMesh();
        }

        if (particleMaterial == null)
        {
            particleMaterial = CreateFallbackMaterial();
        }

        mpb = new MaterialPropertyBlock();
        colorPropertyId = DetectColorProperty(particleMaterial);
        EnsureArraySize();
        initialized = true;
    }

    private void EnsureArraySize()
    {
        if (particles != null && particles.Length == particlesAtFull)
        {
            return;
        }

        ReservoirParticle[] oldParticles = particles;
        particles = new ReservoirParticle[particlesAtFull];

        int copyCount = oldParticles != null ? Mathf.Min(oldParticles.Length, particles.Length) : 0;
        for (int i = 0; i < copyCount; i++)
        {
            particles[i] = oldParticles[i];
        }

        for (int i = copyCount; i < particles.Length; i++)
        {
            particles[i].localPosition = SamplePointInsideCurrentLiquidVolume();
            particles[i].localVelocity = Vector3.zero;
            particles[i].phase = Random.Range(0f, 1000f);
            particles[i].active = false;
        }
    }

    private void SyncVisibleCountWithBucketAmount()
    {
        float fill01 = GetFill01();
        int targetVisibleCount = Mathf.RoundToInt(particlesAtFull * fill01);
        targetVisibleCount = Mathf.Clamp(targetVisibleCount, 0, particlesAtFull);

        if (targetVisibleCount == currentVisibleCount)
        {
            return;
        }

        if (targetVisibleCount > currentVisibleCount)
        {
            for (int i = currentVisibleCount; i < targetVisibleCount; i++)
            {
                particles[i].active = true;
                particles[i].localPosition = SamplePointInsideCurrentLiquidVolume();
                particles[i].localVelocity = Vector3.zero;
            }
        }
        else
        {
            if (hideHighestParticlesFirst)
            {
                SortActiveParticlesByHeightDescending(currentVisibleCount);
            }

            for (int i = targetVisibleCount; i < currentVisibleCount; i++)
            {
                particles[i].active = false;
                particles[i].localVelocity = Vector3.zero;
            }
        }

        currentVisibleCount = targetVisibleCount;
    }

    private void SortActiveParticlesByHeightDescending(int count)
    {
        // Small simple sort. Runs only when the fill amount visibly decreases.
        for (int i = 0; i < count - 1; i++)
        {
            int highestIndex = i;
            float highestY = particles[i].localPosition.y;

            for (int j = i + 1; j < count; j++)
            {
                if (particles[j].localPosition.y > highestY)
                {
                    highestY = particles[j].localPosition.y;
                    highestIndex = j;
                }
            }

            if (highestIndex != i)
            {
                ReservoirParticle temp = particles[i];
                particles[i] = particles[highestIndex];
                particles[highestIndex] = temp;
            }
        }
    }

    private float GetFill01()
    {
        if (bucketSystem == null || bucketSystem.InitialWaterAmount <= 0.000001f)
        {
            return 1f;
        }

        return Mathf.Clamp01(bucketSystem.CurrentWaterAmount / bucketSystem.InitialWaterAmount);
    }

    private float GetCurrentTopY()
    {
        return bottomLocalY + fullLiquidHeight * GetFill01();
    }

    private float GetRadiusAtLocalY(float localY)
    {
        float y01 = Mathf.InverseLerp(bottomLocalY, bottomLocalY + fullLiquidHeight, localY);

        if (!radiusFollowsFillHeight)
        {
            y01 = Mathf.Clamp01(y01);
        }

        return Mathf.Lerp(bottomRadius, fullTopRadius, Mathf.Clamp01(y01));
    }

    private Vector3 SamplePointInsideCurrentLiquidVolume()
    {
        float fill01 = Mathf.Max(0.001f, GetFill01());
        float topY = bottomLocalY + fullLiquidHeight * fill01;
        float y = Random.Range(bottomLocalY + particleSize, topY - particleSize);

        if (topY <= bottomLocalY + particleSize * 2f)
        {
            y = bottomLocalY + particleSize;
        }

        float radius = Mathf.Max(0.001f, GetRadiusAtLocalY(y) - particleSize);
        Vector2 disk = Random.insideUnitCircle * radius;
        return new Vector3(disk.x, y, disk.y);
    }

    private void SimulateInteriorSPH(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        float topY = GetCurrentTopY();
        Vector3 localGravity = bucketSpace != null
            ? bucketSpace.InverseTransformDirection(Vector3.down) * insideGravity
            : Vector3.down * insideGravity;

        Vector3[] accelerations = new Vector3[currentVisibleCount];

        for (int i = 0; i < currentVisibleCount; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            accelerations[i] = localGravity;

            float wobble = Mathf.Sin(Time.time * 2.5f + particles[i].phase) * surfaceWobble;
            if (particles[i].localPosition.y > topY - sphRadius * 0.65f)
            {
                accelerations[i] += Vector3.up * wobble;
            }

            if (agitation > 0f)
            {
                Vector3 noise = new Vector3(
                    Mathf.PerlinNoise(particles[i].phase, Time.time) - 0.5f,
                    Mathf.PerlinNoise(Time.time, particles[i].phase + 13.7f) - 0.5f,
                    Mathf.PerlinNoise(particles[i].phase + 29.3f, Time.time) - 0.5f
                );

                accelerations[i] += noise * agitation;
            }
        }

        float h = Mathf.Max(0.0001f, sphRadius);
        float h2 = h * h;

        for (int i = 0; i < currentVisibleCount; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            for (int j = i + 1; j < currentVisibleCount; j++)
            {
                if (!particles[j].active)
                {
                    continue;
                }

                Vector3 delta = particles[i].localPosition - particles[j].localPosition;
                float dist2 = delta.sqrMagnitude;

                if (dist2 > h2 || dist2 < 0.0000001f)
                {
                    continue;
                }

                float dist = Mathf.Sqrt(dist2);
                Vector3 dir = delta / dist;
                float q = 1f - dist / h;

                Vector3 pressure = dir * (pressureStrength * q * q);
                Vector3 viscosity = (particles[j].localVelocity - particles[i].localVelocity) * (viscosityStrength * q);

                accelerations[i] += pressure + viscosity;
                accelerations[j] -= pressure + viscosity;
            }
        }

        for (int i = 0; i < currentVisibleCount; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            ReservoirParticle p = particles[i];
            p.localVelocity += accelerations[i] * dt;
            p.localVelocity = Vector3.ClampMagnitude(p.localVelocity, maxInsideVelocity);
            p.localPosition += p.localVelocity * dt;

            ApplyBucketInteriorConstraint(ref p, topY);
            particles[i] = p;
        }
    }

    private void ApplyBucketInteriorConstraint(ref ReservoirParticle p, float topY)
    {
        float minY = bottomLocalY + particleSize * 0.5f;
        float maxY = Mathf.Max(minY, topY - particleSize * 0.5f);

        if (p.localPosition.y < minY)
        {
            p.localPosition.y = minY;
            if (p.localVelocity.y < 0f) p.localVelocity.y *= -wallDamping;
        }

        if (clampParticlesToWaterLevel && p.localPosition.y > maxY)
        {
            p.localPosition.y = maxY;
            if (p.localVelocity.y > 0f) p.localVelocity.y *= -wallDamping;
        }

        float allowedRadius = Mathf.Max(0.001f, GetRadiusAtLocalY(p.localPosition.y) - particleSize * 0.5f);
        Vector2 radial = new Vector2(p.localPosition.x, p.localPosition.z);
        float radialLength = radial.magnitude;

        if (radialLength > allowedRadius)
        {
            Vector2 normal = radialLength > 0.000001f ? radial / radialLength : Vector2.right;
            radial = normal * allowedRadius;
            p.localPosition.x = radial.x;
            p.localPosition.z = radial.y;

            Vector3 localNormal = new Vector3(normal.x, 0f, normal.y);
            float wallSpeed = Vector3.Dot(p.localVelocity, localNormal);
            if (wallSpeed > 0f)
            {
                p.localVelocity -= localNormal * wallSpeed * (1f + wallDamping);
            }
        }
    }

    private void RenderReservoirParticles()
    {
        if (particleMesh == null || particleMaterial == null || currentVisibleCount <= 0)
        {
            return;
        }

        Transform t = bucketSpace != null ? bucketSpace : transform;
        Quaternion rotation = billboardToCamera && renderCamera != null
            ? renderCamera.transform.rotation
            : t.rotation;

        mpb.Clear();
        if (forceParticleColor && colorPropertyId >= 0)
        {
            mpb.SetColor(colorPropertyId, particleColor);
        }

        for (int i = 0; i < currentVisibleCount; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            Vector3 worldPosition = t.TransformPoint(particles[i].localPosition);
            Matrix4x4 matrix = Matrix4x4.TRS(worldPosition, rotation, Vector3.one * particleSize);

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

    private int DetectColorProperty(Material mat)
    {
        if (mat == null)
        {
            return -1;
        }

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

        int propertyId = DetectColorProperty(mat);
        if (propertyId >= 0)
        {
            mat.SetColor(propertyId, particleColor);
        }

        return mat;
    }

    private Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "SPHBucketInteriorParticleQuad";

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
        Transform t = bucketSpace != null ? bucketSpace : transform;
        float fill01 = GetFill01();
        float topY = bottomLocalY + fullLiquidHeight * fill01;
        float currentTopRadius = radiusFollowsFillHeight ? GetRadiusAtLocalY(topY) : fullTopRadius;

        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.85f);
        DrawCircleGizmo(t, bottomLocalY, bottomRadius);
        DrawCircleGizmo(t, topY, currentTopRadius);

        for (int i = 0; i < 8; i++)
        {
            float angle = (Mathf.PI * 2f / 8f) * i;
            Vector3 bottom = new Vector3(Mathf.Cos(angle) * bottomRadius, bottomLocalY, Mathf.Sin(angle) * bottomRadius);
            Vector3 top = new Vector3(Mathf.Cos(angle) * currentTopRadius, topY, Mathf.Sin(angle) * currentTopRadius);
            Gizmos.DrawLine(t.TransformPoint(bottom), t.TransformPoint(top));
        }
    }

    private void DrawCircleGizmo(Transform t, float y, float radius)
    {
        const int steps = 48;
        Vector3 previous = Vector3.zero;

        for (int i = 0; i <= steps; i++)
        {
            float angle = (Mathf.PI * 2f / steps) * i;
            Vector3 current = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

            if (i > 0)
            {
                Gizmos.DrawLine(t.TransformPoint(previous), t.TransformPoint(current));
            }

            previous = current;
        }
    }
}

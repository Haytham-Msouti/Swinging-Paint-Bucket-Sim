using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Lightweight 3D monitor for BucketParticleSystemCustom.
/// It does NOT change the GPU SPH simulation. It only reads water/flow values and draws:
/// 1) helical / vortex-like tracer dots inside the bucket,
/// 2) a water-level surface + level bar,
/// 3) outlet-aware flow paths for Circle / Line / MultipleHoles / Random openings,
/// 4) a small 3D text showing water %, flow rate, and decrease per second.
///
/// The monitor is intentionally visual-only. The real SPH particles remain GPU particles,
/// not GameObjects. These tracer dots are just a readable preview of motion inside the bucket.
/// </summary>
public class BucketFlowMonitor3D : MonoBehaviour
{
    private enum VisualOutletShape
    {
        AutoFromSource,
        Circle,
        Line,
        MultipleHoles,
        Random
    }

    private class TracerState
    {
        public Transform transform;
        public Renderer renderer;
        public float y01;
        public float angle;
        public float radius01;
        public float speedMul;
        public float phase;
        public float colorHue;
        public MaterialPropertyBlock propertyBlock;
        public float line01;
        public int holeIndex;
        public Vector2 randomBias;
    }

    private struct OutletRuntimeInfo
    {
        public VisualOutletShape shape;
        public string shapeName;
        public Vector3 localDirection;
        public Vector3 localOffset;
        public Vector3 localLineDirection;
        public float radius;
        public float lineLength;
        public int holeCount;
        public float holeSpacing;
        public float jitter;
    }

    [Header("Source")]
    [SerializeField] private BucketParticleSystemCustom source;
    [Tooltip("Usually the bucket/emitter transform. If empty, the source transform is used.")]
    [SerializeField] private Transform bucketRoot;
    [SerializeField] private Camera renderCamera;

    [Header("Auto Match Source Outlet")]
    [Tooltip("Reads the private serialized outlet fields from BucketParticleSystemCustom so this monitor follows Circle / Line / MultipleHoles / Random automatically.")]
    [SerializeField] private bool autoReadOutletFromSource = true;
    [SerializeField] private VisualOutletShape visualOutletShape = VisualOutletShape.AutoFromSource;
    [Tooltip("Uses the source emissionLocalOffset/emitter as the visual drain point when possible.")]
    [SerializeField] private bool useSourceEmissionOffset = true;
    [Tooltip("Uses the source emissionAxis as the outlet direction when possible.")]
    [SerializeField] private bool useSourceEmissionDirection = true;
    [Tooltip("If the automatic direction looks wrong, turn Auto OFF and adjust Outlet Local Direction below.")]
    [SerializeField] private Vector3 outletLocalDirection = new Vector3(0f, 0f, -1f);

    [Header("Bucket Local Shape")]
    [Tooltip("Local position offset of the visualizer center inside the bucket.")]
    [SerializeField] private Vector3 localCenterOffset = Vector3.zero;
    [SerializeField] private float bucketBottomY = -0.35f;
    [SerializeField] private float bucketTopY = 0.55f;
    [SerializeField, Min(0.01f)] private float bottomRadius = 0.32f;
    [SerializeField, Min(0.01f)] private float topRadius = 0.62f;

    [Header("Inside Bucket Safety")]
    [Tooltip("Keeps tracer dots, vortex path, arrows, and outlet preview paths inside the bucket instead of letting them visually leave through the opening.")]
    [SerializeField] private bool keepFlowVisualsInsideBucket = true;
    [Tooltip("1 = touches the inner wall. Lower values keep everything farther from the wall.")]
    [SerializeField, Range(0.55f, 0.99f)] private float insideWallPaddingMultiplier = 0.88f;
    [SerializeField, Range(0f, 0.15f)] private float bottomClampPadding = 0.035f;
    [SerializeField, Range(0f, 0.15f)] private float surfaceClampPadding = 0.012f;
    [Tooltip("How close the internal drain target gets to the bucket wall. Keep below 1 so it never exits the bucket.")]
    [SerializeField, Range(0.15f, 0.95f)] private float drainTargetRadiusFactor = 0.72f;

    [Header("Inside Helical Particle Motion")]
    [SerializeField] private bool showInsideTracers = true;
    [SerializeField, Range(0, 70000)] private int tracerCount = 120;
    [Tooltip("Default size of normal tracer dots inside the bucket.")]
    [SerializeField, Min(0.001f)] private float tracerSize = 0.017f;
    [Tooltip("Size of the dots when they are visually falling/pulled down toward the drain/outlet.")]
    [SerializeField, Min(0.001f)] private float fallingTracerSize = 0.012f;
    [SerializeField, Min(0f)] private float idleVerticalSpeed = 0.025f;
    [SerializeField, Min(0f)] private float flowVerticalSpeed = 0.55f;
    [SerializeField, Min(0f)] private float swirlSpeed = 3.2f;
    [SerializeField, Range(0f, 4f)] private float vortexStrength = 1.65f;
    [SerializeField, Range(0f, 4f)] private float drainPullStrength = 1.25f;
    [SerializeField, Range(0f, 1f)] private float turbulenceAmount = 0.16f;
    [SerializeField, Range(0.05f, 1f)] private float vortexCoreRadius = 0.22f;
    [SerializeField, Min(1f)] private float flowRateForFullMotion = 18000f;

    [Header("Outlet Shape Preview")]
    [SerializeField] private bool showOutletAwarePaths = true;
    [SerializeField, Range(1, 12)] private int maxPreviewPaths = 12;
    [SerializeField, Min(0.001f)] private float previewPathWidth = 0.008f;
    [SerializeField, Range(0.1f, 3f)] private float outletPreviewScale = 1.0f;
    [SerializeField] private bool showVortexPath = true;
    [SerializeField, Range(0.001f, 0.04f)] private float vortexPathWidth = 0.012f;
    [SerializeField, Range(1f, 7f)] private float vortexTurns = 3.2f;

    [Header("Level / Flow Visuals")]
    [SerializeField] private bool showLevelSurface = true;
    [SerializeField] private bool showLevelBar = true;
    [SerializeField] private bool showFlowArrow = true;
    [SerializeField] private bool showText = true;
    [SerializeField] private Vector3 levelBarLocalPosition = new Vector3(0.72f, 0.10f, 0f);
    [SerializeField] private Vector3 textLocalPosition = new Vector3(0.88f, 0.64f, 0f);
    [SerializeField] private float levelBarHeight = 0.85f;
    [SerializeField] private float levelBarWidth = 0.035f;
    [SerializeField] private float surfaceThickness = 0.018f;
    [SerializeField, Range(0.01f, 0.8f)] private float valueSmoothing = 0.18f;

    [Header("Colors")]
    [SerializeField] private bool useRainbowVisualColors = true;
    [SerializeField, Range(0f, 1f)] private float rainbowSaturation = 1f;
    [SerializeField, Range(0f, 1f)] private float rainbowValue = 1f;
    [SerializeField, Range(0f, 1f)] private float rainbowAlpha = 0.92f;
    [SerializeField, Range(0f, 2f)] private float rainbowScrollSpeed = 0.08f;
    [Tooltip("Higher value means the tracer colors change more across height/radius instead of all shifting together.")]
    [SerializeField, Range(0f, 3f)] private float tracerRainbowSpread = 1.35f;
    [Tooltip("Keep OFF for readable text. Turn ON if you also want the text color to animate.")]
    [SerializeField] private bool rainbowTextColor = false;

    [Header("Fallback Colors - Used when Rainbow is OFF")]
    [SerializeField] private Color tracerColor = new Color(0.15f, 0.85f, 1f, 0.9f);
    [SerializeField] private Color surfaceColor = new Color(0.1f, 0.55f, 1f, 0.28f);
    [SerializeField] private Color barBackColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color barFillColor = new Color(0.1f, 0.65f, 1f, 0.78f);
    [SerializeField] private Color arrowColor = new Color(0.1f, 1f, 0.65f, 0.95f);
    [SerializeField] private Color vortexColor = new Color(0.6f, 0.95f, 1f, 0.62f);
    [SerializeField] private Color textColor = Color.white;


    [Header("Runtime Settings UI")]
    [SerializeField] private bool showRuntimeSettingsUI = true;
    [SerializeField] private bool runtimeSettingsPanelOpen = true;
    [SerializeField] private Rect runtimeSettingsWindowRect = new Rect(14f, 14f, 430f, 640f);
    [SerializeField, Range(0.75f, 1.6f)] private float runtimeUiScale = 1f;
    [SerializeField, Range(280f, 680f)] private float runtimeUiWidth = 430f;
    [SerializeField, Range(260f, 820f)] private float runtimeUiHeight = 640f;
    [SerializeField, Range(0.15f, 1f)] private float runtimeUiOpacity = 0.94f;
    [SerializeField] private bool runtimeUiShowSourceReadout = true;
    [SerializeField] private bool runtimeUiShowColorSettings = false;

    private Transform visualRoot;
    private Transform levelSurface;
    private Transform levelBarBack;
    private Transform levelBarFill;
    private LineRenderer arrowLine;
    private LineRenderer arrowHeadA;
    private LineRenderer arrowHeadB;
    private LineRenderer vortexLine;
    private TextMesh infoText;

    private Material tracerMaterial;
    private Material surfaceMaterial;
    private Material barBackMaterial;
    private Material barFillMaterial;
    private Material arrowMaterial;
    private Material vortexMaterial;
    private Material textMaterial;

    private readonly List<TracerState> tracers = new List<TracerState>();
    private readonly List<LineRenderer> outletPreviewLines = new List<LineRenderer>();

    private float displayedWater01 = 1f;
    private float displayedFlowRate;
    private float displayedDecreasePerSecond;
    private float previousWaterAmount;
    private bool previousWaterAmountReady;
    private Vector2 runtimeSettingsScroll;
    private bool runtimeUiRebuildRequested;
    private bool runtimeUiStylesReady;
    private GUIStyle runtimeUiHeaderStyle;
    private GUIStyle runtimeUiSectionStyle;
    private GUIStyle runtimeUiMiniLabelStyle;

    private int builtTracerCount = -1;
    private int builtMaxPreviewPaths = -1;

    private const int RuntimeSettingsWindowId = 342971;
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private readonly Dictionary<string, FieldInfo> sourceFieldCache = new Dictionary<string, FieldInfo>();

    private void Reset()
    {
        source = GetComponent<BucketParticleSystemCustom>();
        bucketRoot = transform;
    }

    private void Start()
    {
        ResolveReferences();
        BuildVisuals();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BuildVisuals();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (visualRoot == null || runtimeUiRebuildRequested || builtTracerCount != tracerCount || builtMaxPreviewPaths != maxPreviewPaths)
        {
            runtimeUiRebuildRequested = false;
            BuildVisuals();
        }

        float dt = Mathf.Max(Time.deltaTime, 0.00001f);
        float targetWater01 = source != null ? source.CurrentWater01 : 1f;
        float targetFlowRate = source != null ? source.CurrentFlowRate : 0f;
        float currentWaterAmount = source != null ? source.CurrentWaterAmount : targetWater01;
        float initialWaterAmount = source != null ? source.InitialWaterAmount : 1f;

        float targetDecreasePerSecond = 0f;
        if (previousWaterAmountReady)
        {
            targetDecreasePerSecond = Mathf.Max(0f, (previousWaterAmount - currentWaterAmount) / dt);
        }
        previousWaterAmount = currentWaterAmount;
        previousWaterAmountReady = true;

        float lerp = valueSmoothing <= 0f ? 1f : 1f - Mathf.Pow(valueSmoothing, dt * 60f);
        displayedWater01 = Mathf.Lerp(displayedWater01, targetWater01, lerp);
        displayedFlowRate = Mathf.Lerp(displayedFlowRate, targetFlowRate, lerp);
        displayedDecreasePerSecond = Mathf.Lerp(displayedDecreasePerSecond, targetDecreasePerSecond, lerp);

        OutletRuntimeInfo outlet = GetOutletRuntimeInfo();

        UpdateLevelSurface(displayedWater01);
        UpdateLevelBar(displayedWater01);
        UpdateFlowArrow(displayedWater01, displayedFlowRate, outlet);
        UpdateOutletPreviewPaths(displayedWater01, displayedFlowRate, outlet);
        UpdateVortexPath(displayedWater01, displayedFlowRate, outlet);
        UpdateTracers(dt, displayedWater01, displayedFlowRate, outlet);
        UpdateText(targetWater01, currentWaterAmount, initialWaterAmount, displayedFlowRate, displayedDecreasePerSecond, outlet);
    }

    private void ResolveReferences()
    {
        if (source == null)
        {
            source = GetComponent<BucketParticleSystemCustom>();
        }

        if (source == null)
        {
            source = FindFirstObjectByType<BucketParticleSystemCustom>();
        }

        if (bucketRoot == null)
        {
            bucketRoot = source != null ? source.transform : transform;
        }

        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }
    }

    private void BuildVisuals()
    {
        ClearVisuals();
        ResolveReferences();

        if (bucketRoot == null)
        {
            return;
        }

        visualRoot = new GameObject("Bucket Flow Monitor Visuals").transform;
        visualRoot.SetParent(bucketRoot, false);
        visualRoot.localPosition = localCenterOffset;
        visualRoot.localRotation = Quaternion.identity;
        visualRoot.localScale = Vector3.one;

        tracerMaterial = CreateMaterial("Bucket Flow Tracer Material", tracerColor, true);
        surfaceMaterial = CreateMaterial("Bucket Level Surface Material", surfaceColor, true);
        barBackMaterial = CreateMaterial("Bucket Level Bar Back Material", barBackColor, true);
        barFillMaterial = CreateMaterial("Bucket Level Bar Fill Material", barFillColor, true);
        arrowMaterial = CreateMaterial("Bucket Flow Arrow Material", arrowColor, true);
        vortexMaterial = CreateMaterial("Bucket Flow Vortex Material", vortexColor, true);
        textMaterial = CreateMaterial("Bucket Flow Text Material", textColor, false);

        if (showLevelSurface)
        {
            levelSurface = CreatePrimitiveChild("Water Level Surface", PrimitiveType.Cylinder, surfaceMaterial);
        }

        if (showLevelBar)
        {
            levelBarBack = CreatePrimitiveChild("Water Level Bar Back", PrimitiveType.Cube, barBackMaterial);
            levelBarFill = CreatePrimitiveChild("Water Level Bar Fill", PrimitiveType.Cube, barFillMaterial);
        }

        if (showFlowArrow)
        {
            arrowLine = CreateLineChild("Flow Arrow Line", 0.015f, arrowMaterial, 2);
            arrowHeadA = CreateLineChild("Flow Arrow Head A", 0.012f, arrowMaterial, 2);
            arrowHeadB = CreateLineChild("Flow Arrow Head B", 0.012f, arrowMaterial, 2);
        }

        if (showOutletAwarePaths)
        {
            int pathCount = Mathf.Clamp(maxPreviewPaths, 1, 12);
            for (int i = 0; i < pathCount; i++)
            {
                outletPreviewLines.Add(CreateLineChild("Outlet Shape Flow Path " + i, previewPathWidth, arrowMaterial, 14));
            }
        }

        if (showVortexPath)
        {
            vortexLine = CreateLineChild("Internal Helical Vortex Path", vortexPathWidth, vortexMaterial, 64);
        }

        if (showText)
        {
            GameObject textObject = new GameObject("Water Flow Info Text");
            textObject.transform.SetParent(visualRoot, false);
            textObject.transform.localPosition = textLocalPosition;
            infoText = textObject.AddComponent<TextMesh>();
            infoText.anchor = TextAnchor.MiddleLeft;
            infoText.alignment = TextAlignment.Left;
            infoText.characterSize = 0.045f;
            infoText.fontSize = 64;
            infoText.color = textColor;
            Renderer textRenderer = textObject.GetComponent<Renderer>();
            if (textRenderer != null && textMaterial != null)
            {
                textRenderer.material = textMaterial;
            }
        }

        BuildTracers();
        builtTracerCount = tracerCount;
        builtMaxPreviewPaths = maxPreviewPaths;
    }

    private void BuildTracers()
    {
        tracers.Clear();

        if (!showInsideTracers || tracerCount <= 0 || visualRoot == null)
        {
            return;
        }

        for (int i = 0; i < tracerCount; i++)
        {
            Transform t = CreatePrimitiveChild("Inside Helical Flow Tracer " + i, PrimitiveType.Sphere, tracerMaterial);
            t.localScale = Vector3.one * tracerSize;

            TracerState state = new TracerState
            {
                transform = t,
                renderer = t.GetComponent<Renderer>(),
                y01 = UnityEngine.Random.Range(0f, 1f),
                angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                radius01 = Mathf.Sqrt(UnityEngine.Random.Range(0.02f, 1f)) * 0.92f,
                speedMul = UnityEngine.Random.Range(0.75f, 1.45f),
                phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                colorHue = UnityEngine.Random.value,
                propertyBlock = new MaterialPropertyBlock(),
                line01 = UnityEngine.Random.value,
                holeIndex = UnityEngine.Random.Range(0, 12),
                randomBias = UnityEngine.Random.insideUnitCircle
            };

            tracers.Add(state);
        }
    }

    private Transform CreatePrimitiveChild(string objectName, PrimitiveType type, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = objectName;
        obj.transform.SetParent(visualRoot, false);

        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && material != null)
        {
            renderer.material = material;
        }

        return obj.transform;
    }

    private LineRenderer CreateLineChild(string objectName, float width, Material material, int positions)
    {
        GameObject obj = new GameObject(objectName);
        obj.transform.SetParent(visualRoot, false);
        LineRenderer line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = Mathf.Max(2, positions);
        line.startWidth = width;
        line.endWidth = width;
        line.numCornerVertices = 6;
        line.numCapVertices = 6;
        line.material = material != null ? material : arrowMaterial;
        return line;
    }

    private void UpdateLevelSurface(float water01)
    {
        if (levelSurface == null)
        {
            return;
        }

        bool visible = showLevelSurface && water01 > 0.001f;
        if (levelSurface.gameObject.activeSelf != visible)
        {
            levelSurface.gameObject.SetActive(visible);
        }
        if (!visible)
        {
            return;
        }

        float y = Mathf.Lerp(bucketBottomY, bucketTopY, water01);
        float radius = RadiusAtLocalY(y);
        levelSurface.localPosition = new Vector3(0f, y, 0f);
        levelSurface.localRotation = Quaternion.identity;
        levelSurface.localScale = new Vector3(radius * 2f, surfaceThickness * 0.5f, radius * 2f);
        ApplyRendererColor(levelSurface.GetComponent<Renderer>(), GetSurfaceVisualColor(water01));
    }

    private void UpdateLevelBar(float water01)
    {
        if (levelBarBack == null || levelBarFill == null)
        {
            return;
        }

        bool visible = showLevelBar;
        levelBarBack.gameObject.SetActive(visible);
        levelBarFill.gameObject.SetActive(visible && water01 > 0.001f);
        if (!visible)
        {
            return;
        }

        levelBarBack.localPosition = levelBarLocalPosition;
        levelBarBack.localRotation = Quaternion.identity;
        levelBarBack.localScale = new Vector3(levelBarWidth, levelBarHeight, levelBarWidth);

        float fillHeight = Mathf.Max(0.0001f, levelBarHeight * Mathf.Clamp01(water01));
        levelBarFill.localPosition = levelBarLocalPosition + Vector3.down * (levelBarHeight - fillHeight) * 0.5f;
        levelBarFill.localRotation = Quaternion.identity;
        levelBarFill.localScale = new Vector3(levelBarWidth * 1.25f, fillHeight, levelBarWidth * 1.25f);

        ApplyRendererColor(levelBarBack.GetComponent<Renderer>(), useRainbowVisualColors ? WithAlpha(Color.white, barBackColor.a) : barBackColor);
        ApplyRendererColor(levelBarFill.GetComponent<Renderer>(), GetBarFillVisualColor(water01));
    }

    private void UpdateFlowArrow(float water01, float flowRate, OutletRuntimeInfo outlet)
    {
        if (arrowLine == null || arrowHeadA == null || arrowHeadB == null)
        {
            return;
        }

        float flow01 = Mathf.Clamp01(flowRate / Mathf.Max(1f, flowRateForFullMotion));
        bool visible = showFlowArrow && water01 > 0.01f && flowRate > 1f;
        arrowLine.gameObject.SetActive(visible);
        arrowHeadA.gameObject.SetActive(visible);
        arrowHeadB.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        Vector3 outletDir = GetHorizontalSafe(outlet.localDirection, Vector3.forward);
        Vector3 sideDir = new Vector3(-outletDir.z, 0f, outletDir.x).normalized;
        if (sideDir.sqrMagnitude < 0.0001f)
        {
            sideDir = Vector3.right;
        }

        float surfaceY = Mathf.Lerp(bucketBottomY, bucketTopY, water01);
        float radius = RadiusAtLocalY(surfaceY);
        Vector3 start = new Vector3(0f, surfaceY - 0.03f, 0f);
        Vector3 end = GetOutletTargetLocal(outlet, 0, 1, 0.5f, surfaceY, flow01);
        end = Vector3.Lerp(outletDir * radius * Mathf.Lerp(0.28f, 0.58f, flow01), end, 0.75f);
        end.y = Mathf.Lerp(surfaceY, bucketBottomY, Mathf.Lerp(0.18f, 0.58f, flow01));

        if (keepFlowVisualsInsideBucket)
        {
            start = ClampInsideBucket(start, start.y, water01);
            end = ClampInsideBucket(end, end.y, water01);
        }

        arrowLine.SetPosition(0, start);
        arrowLine.SetPosition(1, end);
        arrowLine.startWidth = Mathf.Lerp(0.008f, 0.023f, flow01);
        arrowLine.endWidth = Mathf.Lerp(0.006f, 0.017f, flow01);
        Color flowColor = GetFlowVisualColor(flow01);
        ApplyLineColor(arrowLine, flowColor, flowColor);

        Vector3 back = (start - end).sqrMagnitude > 0.0001f ? (start - end).normalized : -outletDir;
        float headSize = Mathf.Lerp(0.045f, 0.10f, flow01);
        Vector3 headA = end + (back + sideDir * 0.55f).normalized * headSize;
        Vector3 headB = end + (back - sideDir * 0.55f).normalized * headSize;
        if (keepFlowVisualsInsideBucket)
        {
            headA = ClampInsideBucket(headA, headA.y, water01);
            headB = ClampInsideBucket(headB, headB.y, water01);
        }
        arrowHeadA.SetPosition(0, end);
        arrowHeadA.SetPosition(1, headA);
        arrowHeadB.SetPosition(0, end);
        arrowHeadB.SetPosition(1, headB);
        ApplyLineColor(arrowHeadA, flowColor, flowColor);
        ApplyLineColor(arrowHeadB, flowColor, flowColor);
    }

    private void UpdateOutletPreviewPaths(float water01, float flowRate, OutletRuntimeInfo outlet)
    {
        if (!showOutletAwarePaths || outletPreviewLines.Count == 0)
        {
            return;
        }

        float flow01 = Mathf.Clamp01(flowRate / Mathf.Max(1f, flowRateForFullMotion));
        bool anyVisible = water01 > 0.01f && flowRate > 1f;
        int activeCount = GetPreviewPathCount(outlet);
        float surfaceY = Mathf.Lerp(bucketBottomY, bucketTopY, water01);
        float radius = RadiusAtLocalY(surfaceY);
        Vector3 outletDir = GetHorizontalSafe(outlet.localDirection, Vector3.forward);
        Vector3 lineDir = GetHorizontalSafe(outlet.localLineDirection, new Vector3(-outletDir.z, 0f, outletDir.x));
        Vector3 sideDir = new Vector3(-outletDir.z, 0f, outletDir.x).normalized;

        for (int i = 0; i < outletPreviewLines.Count; i++)
        {
            LineRenderer line = outletPreviewLines[i];
            bool visible = anyVisible && i < activeCount;
            line.gameObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

            int samples = Mathf.Max(8, line.positionCount);
            for (int s = 0; s < samples; s++)
            {
                float t = samples <= 1 ? 0f : s / (float)(samples - 1);
                Vector3 startOffset = sideDir * Mathf.Sin((i + 1) * 1.37f) * radius * 0.18f + lineDir * NormalizedIndex(i, activeCount) * radius * 0.18f;
                Vector3 start = new Vector3(0f, surfaceY - 0.035f, 0f) + startOffset;
                Vector3 target = GetOutletTargetLocal(outlet, i, activeCount, activeCount <= 1 ? 0.5f : i / (float)(activeCount - 1), surfaceY, flow01);
                target.y = Mathf.Lerp(surfaceY, bucketBottomY + 0.04f, Mathf.Lerp(0.30f, 0.82f, flow01));

                Vector3 p = Vector3.Lerp(start, target, Smooth01(t));
                float swirl = Mathf.Sin(t * Mathf.PI * 2f * Mathf.Lerp(0.8f, 1.8f, flow01) + i * 0.9f + Time.time * flow01 * 5f);
                p += sideDir * swirl * radius * 0.045f * flow01 * (1f - t);
                p.y += Mathf.Sin(t * Mathf.PI) * radius * 0.07f * flow01;
                if (keepFlowVisualsInsideBucket)
                {
                    p = ClampInsideBucket(p, p.y, water01);
                }
                line.SetPosition(s, p);
            }

            float width = previewPathWidth * Mathf.Lerp(0.7f, 1.7f, flow01);
            line.startWidth = width;
            line.endWidth = width * 0.55f;
            Color pathStart = useRainbowVisualColors ? GetRainbowOrFallback(0.08f + i * 0.09f + flow01 * 0.18f, arrowColor.a) : arrowColor;
            Color pathEnd = useRainbowVisualColors ? GetRainbowOrFallback(0.48f + i * 0.09f + flow01 * 0.18f, arrowColor.a * 0.55f) : WithAlpha(arrowColor, arrowColor.a * 0.55f);
            ApplyLineColor(line, pathStart, pathEnd);
        }
    }

    private void UpdateVortexPath(float water01, float flowRate, OutletRuntimeInfo outlet)
    {
        if (vortexLine == null)
        {
            return;
        }

        float flow01 = Mathf.Clamp01(flowRate / Mathf.Max(1f, flowRateForFullMotion));
        bool visible = showVortexPath && water01 > 0.03f && (flow01 > 0.03f || source == null);
        vortexLine.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        int samples = Mathf.Max(20, vortexLine.positionCount);
        float surfaceY = Mathf.Lerp(bucketBottomY, bucketTopY, water01);
        float radiusAtSurface = RadiusAtLocalY(surfaceY);
        Vector3 outletDir = GetHorizontalSafe(outlet.localDirection, Vector3.forward);
        Vector3 sideDir = new Vector3(-outletDir.z, 0f, outletDir.x).normalized;
        Vector3 target = GetOutletTargetLocal(outlet, 0, 1, 0.5f, surfaceY, flow01);

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            float y = Mathf.Lerp(surfaceY - 0.02f, bucketBottomY + 0.04f, t);
            float spiralRadius = Mathf.Lerp(radiusAtSurface * 0.48f, RadiusAtLocalY(y) * vortexCoreRadius, t);
            float angle = Time.time * flow01 * 3.0f + t * Mathf.PI * 2f * vortexTurns;
            Vector3 circular = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spiralRadius;
            Vector3 centerDrift = Vector3.Lerp(Vector3.zero, target, Mathf.Pow(t, 1.6f) * flow01);
            Vector3 p = circular + centerDrift;
            p.y = y;
            p += sideDir * Mathf.Sin(angle * 0.5f + Time.time) * 0.018f * flow01;
            if (keepFlowVisualsInsideBucket)
            {
                p = ClampInsideBucket(p, p.y, water01);
            }
            vortexLine.SetPosition(i, p);
        }

        float width = vortexPathWidth * Mathf.Lerp(0.65f, 1.35f, flow01);
        vortexLine.startWidth = width;
        vortexLine.endWidth = width * 0.42f;
        Color vortexStart = useRainbowVisualColors ? GetRainbowOrFallback(0.68f + flow01 * 0.16f, vortexColor.a) : vortexColor;
        Color vortexEnd = useRainbowVisualColors ? GetRainbowOrFallback(0.92f + flow01 * 0.16f, vortexColor.a * 0.45f) : WithAlpha(vortexColor, vortexColor.a * 0.45f);
        ApplyLineColor(vortexLine, vortexStart, vortexEnd);
    }

    private void UpdateTracers(float dt, float water01, float flowRate, OutletRuntimeInfo outlet)
    {
        if (!showInsideTracers || tracers.Count == 0)
        {
            return;
        }

        float flow01 = Mathf.Clamp01(flowRate / Mathf.Max(1f, flowRateForFullMotion));
        float waterHeight = Mathf.Max(0.001f, bucketTopY - bucketBottomY);
        float surfaceY = Mathf.Lerp(bucketBottomY, bucketTopY, water01);
        float visibleRatio = Mathf.Clamp01(water01 + 0.10f);
        int visibleCount = Mathf.RoundToInt(tracers.Count * visibleRatio);

        for (int i = 0; i < tracers.Count; i++)
        {
            TracerState state = tracers[i];
            if (state == null || state.transform == null)
            {
                continue;
            }

            bool visible = i < visibleCount && water01 > 0.015f;
            state.transform.gameObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

            float verticalSpeed = Mathf.Lerp(idleVerticalSpeed, flowVerticalSpeed, flow01) * state.speedMul;
            state.y01 -= (verticalSpeed * dt) / waterHeight;
            if (state.y01 < 0f)
            {
                RespawnTracer(state);
            }

            float localFlowBoost = Mathf.Lerp(0.35f, 2.25f, flow01);
            state.angle += swirlSpeed * dt * localFlowBoost * state.speedMul * Mathf.Lerp(0.65f, 1.35f, vortexStrength);

            float y = Mathf.Lerp(bucketBottomY, surfaceY, Mathf.Clamp01(state.y01));
            float radiusAtY = RadiusAtLocalY(y) * 0.88f;
            float normalizedDepth = 1f - Mathf.Clamp01(state.y01);
            float funnel = Mathf.Clamp01(Mathf.Pow(normalizedDepth, 1.35f) * flow01 * drainPullStrength);
            float pulse = 0.88f + 0.12f * Mathf.Sin(Time.time * 4.0f + state.phase);
            float turbulence = turbulenceAmount * flow01 * Mathf.Sin(Time.time * 7.0f + state.phase) * 0.05f;

            Vector3 outletTarget = GetOutletTargetLocal(outlet, state.holeIndex, Mathf.Max(1, outlet.holeCount), state.line01, surfaceY, flow01);
            outletTarget.y = Mathf.Lerp(y, bucketBottomY + 0.03f, Mathf.Clamp01(normalizedDepth * flow01));

            Vector3 helicalCenter = Vector3.Lerp(Vector3.zero, outletTarget, funnel);
            float helicalRadius = Mathf.Lerp(radiusAtY * state.radius01, radiusAtY * vortexCoreRadius * state.radius01, funnel) * pulse;
            Vector3 helix = new Vector3(Mathf.Cos(state.angle), 0f, Mathf.Sin(state.angle)) * helicalRadius;

            Vector3 localPosition = helicalCenter + helix;
            localPosition.y = y;
            localPosition += GetShapeTangentialDrift(outlet, state, y, flow01) * funnel;
            localPosition += new Vector3(Mathf.Sin(state.phase + Time.time * 5.1f), 0f, Mathf.Cos(state.phase + Time.time * 4.7f)) * turbulence;

            localPosition = ClampInsideBucket(localPosition, y, water01);
            state.transform.localPosition = localPosition;
            float normalParticleSize = Mathf.Lerp(tracerSize * 0.78f, tracerSize * 1.42f, flow01);
            float fallingParticleSize = Mathf.Lerp(fallingTracerSize * 0.78f, fallingTracerSize * 1.42f, flow01);
            float falling01 = Mathf.Clamp01(funnel);
            state.transform.localScale = Vector3.one * Mathf.Lerp(normalParticleSize, fallingParticleSize, falling01);

            Color tracerVisualColor = GetTracerVisualColor(state, flow01, falling01);
            ApplyTracerColor(state, tracerVisualColor);
        }
    }

    private Vector3 GetShapeTangentialDrift(OutletRuntimeInfo outlet, TracerState state, float y, float flow01)
    {
        Vector3 outletDir = GetHorizontalSafe(outlet.localDirection, Vector3.forward);
        Vector3 lineDir = GetHorizontalSafe(outlet.localLineDirection, new Vector3(-outletDir.z, 0f, outletDir.x));
        float radius = RadiusAtLocalY(y);

        switch (outlet.shape)
        {
            case VisualOutletShape.Line:
                return lineDir * (state.line01 - 0.5f) * Mathf.Max(0.02f, outlet.lineLength) * outletPreviewScale;
            case VisualOutletShape.MultipleHoles:
                return lineDir * (state.holeIndex - (outlet.holeCount - 1) * 0.5f) * outlet.holeSpacing * outletPreviewScale;
            case VisualOutletShape.Random:
                return (lineDir * state.randomBias.x + outletDir * state.randomBias.y) * radius * Mathf.Lerp(0.12f, 0.36f, outlet.jitter) * flow01;
            case VisualOutletShape.Circle:
            default:
                return Vector3.zero;
        }
    }

    private void RespawnTracer(TracerState state)
    {
        state.y01 = UnityEngine.Random.Range(0.84f, 1f);
        state.angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        state.radius01 = Mathf.Sqrt(UnityEngine.Random.Range(0.02f, 1f)) * 0.92f;
        state.speedMul = UnityEngine.Random.Range(0.75f, 1.45f);
        state.phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        state.colorHue = Mathf.Repeat(state.colorHue + UnityEngine.Random.Range(0.11f, 0.37f), 1f);
        state.line01 = UnityEngine.Random.value;
        state.holeIndex = UnityEngine.Random.Range(0, 12);
        state.randomBias = UnityEngine.Random.insideUnitCircle;
    }

    private void UpdateText(float water01, float currentWater, float initialWater, float flowRate, float decreasePerSecond, OutletRuntimeInfo outlet)
    {
        if (infoText == null)
        {
            return;
        }

        infoText.gameObject.SetActive(showText);
        if (!showText)
        {
            return;
        }

        infoText.text =
            "Water: " + (water01 * 100f).ToString("0") + "%\n" +
            "Amount: " + currentWater.ToString("0.0") + " / " + Mathf.Max(0f, initialWater).ToString("0.0") + "\n" +
            "Flow: " + flowRate.ToString("0") + " particles/s\n" +
            "Drop: " + decreasePerSecond.ToString("0.00") + " water/s\n" +
            "Outlet: " + outlet.shapeName;

        infoText.color = GetTextVisualColor();
        infoText.transform.localPosition = textLocalPosition;

        if (renderCamera != null)
        {
            Vector3 cameraDirection = infoText.transform.position - renderCamera.transform.position;
            if (cameraDirection.sqrMagnitude > 0.0001f)
            {
                infoText.transform.rotation = Quaternion.LookRotation(cameraDirection.normalized, Vector3.up);
            }
        }
    }

    private OutletRuntimeInfo GetOutletRuntimeInfo()
    {
        OutletRuntimeInfo info = new OutletRuntimeInfo
        {
            shape = visualOutletShape == VisualOutletShape.AutoFromSource ? VisualOutletShape.Circle : visualOutletShape,
            shapeName = visualOutletShape == VisualOutletShape.AutoFromSource ? "Circle" : visualOutletShape.ToString(),
            localDirection = GetManualOutletDirection(),
            localOffset = Vector3.zero,
            localLineDirection = Vector3.right,
            radius = 0.035f,
            lineLength = 0.18f,
            holeCount = 3,
            holeSpacing = 0.06f,
            jitter = 0.75f
        };

        if (autoReadOutletFromSource && source != null)
        {
            string sourceShapeName = ReadSourceEnumName("outletShape", info.shapeName);
            if (visualOutletShape == VisualOutletShape.AutoFromSource)
            {
                info.shape = ParseOutletShape(sourceShapeName);
                info.shapeName = sourceShapeName;
            }

            info.radius = Mathf.Max(0.0001f, ReadSourceField("outletShapeRadius", info.radius));
            info.lineLength = Mathf.Max(0.001f, ReadSourceField("lineOutletLength", info.lineLength));
            info.holeCount = Mathf.Clamp(ReadSourceField("multipleHoleCount", info.holeCount), 1, 12);
            info.holeSpacing = Mathf.Max(0.001f, ReadSourceField("multipleHoleSpacing", info.holeSpacing));
            info.jitter = Mathf.Clamp01(ReadSourceField("randomOutletJitter", info.jitter));

            if (useSourceEmissionDirection)
            {
                info.localDirection = ReadSourceEmissionDirection(info.localDirection);
            }

            if (useSourceEmissionOffset)
            {
                info.localOffset = ReadSourceEmissionLocalOffset();
            }

            info.localLineDirection = ReadSourceOutletLineDirection(info.localLineDirection);
        }

        info.localDirection = GetHorizontalSafe(info.localDirection, outletLocalDirection);
        info.localLineDirection = GetHorizontalSafe(info.localLineDirection, new Vector3(-info.localDirection.z, 0f, info.localDirection.x));

        if (info.shape == VisualOutletShape.MultipleHoles)
        {
            info.shapeName = "MultipleHoles x" + info.holeCount;
        }
        else if (info.shape == VisualOutletShape.Line)
        {
            info.shapeName = "Line";
        }
        else if (info.shape == VisualOutletShape.Random)
        {
            info.shapeName = "Random";
        }
        else
        {
            info.shapeName = "Circle";
        }

        return info;
    }

    private Vector3 GetOutletTargetLocal(OutletRuntimeInfo outlet, int index, int count, float line01, float surfaceY, float flow01)
    {
        Vector3 dir = GetHorizontalSafe(outlet.localDirection, Vector3.forward);
        Vector3 lineDir = GetHorizontalSafe(outlet.localLineDirection, new Vector3(-dir.z, 0f, dir.x));
        float water01ForClamp = Mathf.Clamp01(Mathf.InverseLerp(bucketBottomY, bucketTopY, surfaceY));
        float radius = RadiusAtLocalY(surfaceY);

        // This is only an INTERNAL drain target. It should suggest that water is being pulled
        // toward the opening, but it must stay inside the bucket visual volume.
        float safeDrainFactor = keepFlowVisualsInsideBucket ? drainTargetRadiusFactor : Mathf.Lerp(0.58f, 0.92f, Mathf.Clamp01(outletPreviewScale));
        Vector3 center = outlet.localOffset + dir * radius * safeDrainFactor;
        center.y = Mathf.Lerp(surfaceY, bucketBottomY, Mathf.Lerp(0.16f, 0.74f, flow01));

        Vector3 result;
        switch (outlet.shape)
        {
            case VisualOutletShape.Line:
                {
                    float centered = Mathf.Clamp01(line01) - 0.5f;
                    result = center + lineDir * centered * Mathf.Max(0.001f, outlet.lineLength) * outletPreviewScale;
                    break;
                }
            case VisualOutletShape.MultipleHoles:
                {
                    int holes = Mathf.Clamp(outlet.holeCount, 1, 12);
                    int hole = holes <= 1 ? 0 : Mathf.Abs(index) % holes;
                    float centered = hole - (holes - 1) * 0.5f;
                    result = center + lineDir * centered * Mathf.Max(0.001f, outlet.holeSpacing) * outletPreviewScale;
                    break;
                }
            case VisualOutletShape.Random:
                {
                    float seedA = Mathf.Sin((index + 1) * 12.9898f + Time.time * 0.55f);
                    float seedB = Mathf.Cos((index + 1) * 78.233f + Time.time * 0.41f);
                    Vector3 side = new Vector3(-dir.z, 0f, dir.x).normalized;
                    result = center + (lineDir * seedA + side * seedB) * radius * Mathf.Lerp(0.08f, 0.36f, outlet.jitter) * outletPreviewScale;
                    break;
                }
            case VisualOutletShape.Circle:
            default:
                result = center;
                break;
        }

        return keepFlowVisualsInsideBucket ? ClampInsideBucket(result, result.y, water01ForClamp) : result;
    }

    private int GetPreviewPathCount(OutletRuntimeInfo outlet)
    {
        int cap = Mathf.Clamp(maxPreviewPaths, 1, 12);
        switch (outlet.shape)
        {
            case VisualOutletShape.Line:
                return Mathf.Min(cap, 5);
            case VisualOutletShape.MultipleHoles:
                return Mathf.Min(cap, Mathf.Max(1, outlet.holeCount));
            case VisualOutletShape.Random:
                return Mathf.Min(cap, 4);
            case VisualOutletShape.Circle:
            default:
                return 1;
        }
    }

    private Vector3 ClampInsideBucket(Vector3 localPosition, float y, float water01)
    {
        if (!keepFlowVisualsInsideBucket)
        {
            return localPosition;
        }

        float surfaceY = Mathf.Lerp(bucketBottomY, bucketTopY, Mathf.Clamp01(water01));
        float minY = bucketBottomY + bottomClampPadding;
        float maxY = Mathf.Max(minY, surfaceY - surfaceClampPadding);
        localPosition.y = Mathf.Clamp(localPosition.y, minY, maxY);

        float radius = RadiusAtLocalY(localPosition.y) * insideWallPaddingMultiplier;
        Vector2 xz = new Vector2(localPosition.x, localPosition.z);
        if (xz.magnitude > radius)
        {
            xz = xz.sqrMagnitude > 0.000001f ? xz.normalized * radius : Vector2.zero;
            localPosition.x = xz.x;
            localPosition.z = xz.y;
        }

        return localPosition;
    }

    private Vector3 GetManualOutletDirection()
    {
        Vector3 direction = outletLocalDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }
        return direction.normalized;
    }

    private Vector3 GetHorizontalSafe(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = fallback;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }
        return direction.normalized;
    }

    private float RadiusAtLocalY(float y)
    {
        float t = Mathf.InverseLerp(bucketBottomY, bucketTopY, y);
        return Mathf.Lerp(bottomRadius, topRadius, t);
    }

    private float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private float NormalizedIndex(int index, int count)
    {
        if (count <= 1)
        {
            return 0f;
        }
        return index / (float)(count - 1) - 0.5f;
    }

    private VisualOutletShape ParseOutletShape(string shapeName)
    {
        if (string.Equals(shapeName, "Line", StringComparison.OrdinalIgnoreCase)) return VisualOutletShape.Line;
        if (string.Equals(shapeName, "MultipleHoles", StringComparison.OrdinalIgnoreCase)) return VisualOutletShape.MultipleHoles;
        if (string.Equals(shapeName, "Random", StringComparison.OrdinalIgnoreCase)) return VisualOutletShape.Random;
        return VisualOutletShape.Circle;
    }

    private string ReadSourceEnumName(string fieldName, string fallback)
    {
        object value = ReadSourceFieldRaw(fieldName);
        return value != null ? value.ToString() : fallback;
    }

    private float ReadSourceField(string fieldName, float fallback)
    {
        object value = ReadSourceFieldRaw(fieldName);
        if (value is float) return (float)value;
        if (value is int) return (int)value;
        if (value is double) return (float)(double)value;
        return fallback;
    }

    private int ReadSourceField(string fieldName, int fallback)
    {
        object value = ReadSourceFieldRaw(fieldName);
        if (value is int) return (int)value;
        if (value is float) return Mathf.RoundToInt((float)value);
        if (value is double) return Mathf.RoundToInt((float)(double)value);
        return fallback;
    }

    private Vector3 ReadSourceField(string fieldName, Vector3 fallback)
    {
        object value = ReadSourceFieldRaw(fieldName);
        if (value is Vector3) return (Vector3)value;
        return fallback;
    }

    private object ReadSourceFieldRaw(string fieldName)
    {
        if (source == null || string.IsNullOrEmpty(fieldName))
        {
            return null;
        }

        FieldInfo field;
        if (!sourceFieldCache.TryGetValue(fieldName, out field))
        {
            field = source.GetType().GetField(fieldName, PrivateInstance);
            sourceFieldCache[fieldName] = field;
        }

        return field != null ? field.GetValue(source) : null;
    }

    private Transform ReadSourceTransform(string fieldName, Transform fallback)
    {
        object value = ReadSourceFieldRaw(fieldName);
        Transform transformValue = value as Transform;
        return transformValue != null ? transformValue : fallback;
    }

    private Vector3 ReadSourceEmissionDirection(Vector3 fallback)
    {
        string axis = ReadSourceEnumName("emissionAxis", "");
        Transform emitterTransform = ReadSourceTransform("emitter", source != null ? source.transform : bucketRoot);
        Vector3 worldDirection = Vector3.zero;

        if (emitterTransform == null)
        {
            return fallback;
        }

        switch (axis)
        {
            case "LocalForward": worldDirection = emitterTransform.forward; break;
            case "LocalBack": worldDirection = -emitterTransform.forward; break;
            case "LocalUp": worldDirection = emitterTransform.up; break;
            case "LocalDown": worldDirection = -emitterTransform.up; break;
            case "LocalRight": worldDirection = emitterTransform.right; break;
            case "LocalLeft": worldDirection = -emitterTransform.right; break;
            case "WorldDown": worldDirection = Vector3.down; break;
            default: return fallback;
        }

        Transform basis = visualRoot != null ? visualRoot : bucketRoot;
        if (basis == null)
        {
            return fallback;
        }

        Vector3 localDirection = basis.InverseTransformDirection(worldDirection);
        localDirection.y = 0f;
        return localDirection.sqrMagnitude > 0.0001f ? localDirection.normalized : fallback;
    }

    private Vector3 ReadSourceEmissionLocalOffset()
    {
        if (source == null)
        {
            return Vector3.zero;
        }

        Transform emitterTransform = ReadSourceTransform("emitter", source.transform);
        Vector3 emissionLocalOffset = ReadSourceField("emissionLocalOffset", Vector3.zero);
        if (emitterTransform == null)
        {
            return Vector3.zero;
        }

        Vector3 worldPosition = emitterTransform.TransformPoint(emissionLocalOffset);
        Transform basis = visualRoot != null ? visualRoot : bucketRoot;
        if (basis == null)
        {
            return Vector3.zero;
        }

        Vector3 local = basis.InverseTransformPoint(worldPosition);
        local.y = 0f;
        return local;
    }

    private Vector3 ReadSourceOutletLineDirection(Vector3 fallback)
    {
        if (source == null)
        {
            return fallback;
        }

        Transform emitterTransform = ReadSourceTransform("emitter", source.transform);
        Vector3 sourceLocalLine = ReadSourceField("outletLineLocalDirection", Vector3.right);
        if (emitterTransform == null)
        {
            return fallback;
        }

        Vector3 worldDirection = emitterTransform.TransformDirection(sourceLocalLine);
        Transform basis = visualRoot != null ? visualRoot : bucketRoot;
        if (basis == null)
        {
            return fallback;
        }

        Vector3 localDirection = basis.InverseTransformDirection(worldDirection);
        localDirection.y = 0f;
        return localDirection.sqrMagnitude > 0.0001f ? localDirection.normalized : fallback;
    }

    private Color GetRainbowOrFallback(float hueOffset, float alpha)
    {
        if (!useRainbowVisualColors)
        {
            Color fallback = Color.white;
            fallback.a = alpha;
            return fallback;
        }

        float hue = Mathf.Repeat(hueOffset + Time.time * rainbowScrollSpeed, 1f);
        Color color = Color.HSVToRGB(hue, Mathf.Clamp01(rainbowSaturation), Mathf.Clamp01(rainbowValue));
        color.a = Mathf.Clamp01(alpha * rainbowAlpha);
        return color;
    }

    private Color GetSurfaceVisualColor(float water01)
    {
        if (!useRainbowVisualColors)
        {
            return surfaceColor;
        }

        return GetRainbowOrFallback(0.52f + water01 * 0.18f, surfaceColor.a);
    }

    private Color GetBarFillVisualColor(float water01)
    {
        if (!useRainbowVisualColors)
        {
            return barFillColor;
        }

        return GetRainbowOrFallback(0.30f + water01 * 0.45f, barFillColor.a);
    }

    private Color GetFlowVisualColor(float flow01)
    {
        if (!useRainbowVisualColors)
        {
            return arrowColor;
        }

        return GetRainbowOrFallback(0.12f + flow01 * 0.35f, arrowColor.a);
    }

    private Color GetTracerVisualColor(TracerState state, float flow01, float falling01)
    {
        if (!useRainbowVisualColors || state == null)
        {
            return tracerColor;
        }

        float heightShift = state.y01 * 0.22f * tracerRainbowSpread;
        float motionShift = flow01 * 0.16f + falling01 * 0.08f;
        return GetRainbowOrFallback(state.colorHue + heightShift + motionShift, tracerColor.a);
    }

    private Color GetTextVisualColor()
    {
        if (!useRainbowVisualColors || !rainbowTextColor)
        {
            return textColor;
        }

        return GetRainbowOrFallback(0.0f, textColor.a);
    }

    private Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private void ApplyRendererColor(Renderer targetRenderer, Color color)
    {
        if (targetRenderer == null)
        {
            return;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorPropertyId, color);
        block.SetColor(ColorPropertyId, color);
        targetRenderer.SetPropertyBlock(block);
    }

    private void ApplyTracerColor(TracerState state, Color color)
    {
        if (state == null || state.renderer == null)
        {
            return;
        }

        if (state.propertyBlock == null)
        {
            state.propertyBlock = new MaterialPropertyBlock();
        }

        state.renderer.GetPropertyBlock(state.propertyBlock);
        state.propertyBlock.SetColor(BaseColorPropertyId, color);
        state.propertyBlock.SetColor(ColorPropertyId, color);
        state.renderer.SetPropertyBlock(state.propertyBlock);
    }

    private void ApplyLineColor(LineRenderer line, Color startColorValue, Color endColorValue)
    {
        if (line == null)
        {
            return;
        }

        line.startColor = startColorValue;
        line.endColor = endColorValue;

        if (line.material != null)
        {
            if (line.material.HasProperty(BaseColorPropertyId))
            {
                line.material.SetColor(BaseColorPropertyId, startColorValue);
            }
            if (line.material.HasProperty(ColorPropertyId))
            {
                line.material.SetColor(ColorPropertyId, startColorValue);
            }
        }
    }

    private Material CreateMaterial(string materialName, Color color, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");

        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader);
        mat.name = materialName;
        mat.hideFlags = HideFlags.DontSave;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (transparent)
        {
            mat.renderQueue = 3000;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
        }

        return mat;
    }


    private void OnGUI()
    {
        if (!showRuntimeSettingsUI)
        {
            return;
        }

        EnsureRuntimeUiStyles();

        float scaledWidth = Mathf.Clamp(runtimeUiWidth * runtimeUiScale, 250f, Mathf.Max(260f, Screen.width - 20f));
        float scaledHeight = Mathf.Clamp(runtimeUiHeight * runtimeUiScale, 220f, Mathf.Max(230f, Screen.height - 20f));

        if (runtimeSettingsWindowRect.width <= 10f || runtimeSettingsWindowRect.height <= 10f)
        {
            runtimeSettingsWindowRect = new Rect(14f, 14f, scaledWidth, scaledHeight);
        }

        runtimeSettingsWindowRect.width = scaledWidth;
        runtimeSettingsWindowRect.height = scaledHeight;
        runtimeSettingsWindowRect.x = Mathf.Clamp(runtimeSettingsWindowRect.x, 0f, Mathf.Max(0f, Screen.width - runtimeSettingsWindowRect.width));
        runtimeSettingsWindowRect.y = Mathf.Clamp(runtimeSettingsWindowRect.y, 0f, Mathf.Max(0f, Screen.height - runtimeSettingsWindowRect.height));

        Color oldColor = GUI.color;
        Color oldBackground = GUI.backgroundColor;
        GUI.color = new Color(1f, 1f, 1f, runtimeUiOpacity);
        GUI.backgroundColor = new Color(0.08f, 0.12f, 0.16f, runtimeUiOpacity);

        if (!runtimeSettingsPanelOpen)
        {
            Rect buttonRect = new Rect(runtimeSettingsWindowRect.x, runtimeSettingsWindowRect.y, 180f * runtimeUiScale, 34f * runtimeUiScale);
            if (GUI.Button(buttonRect, "Flow Monitor Settings"))
            {
                runtimeSettingsPanelOpen = true;
            }

            GUI.color = oldColor;
            GUI.backgroundColor = oldBackground;
            return;
        }

        runtimeSettingsWindowRect = GUI.Window(RuntimeSettingsWindowId, runtimeSettingsWindowRect, DrawRuntimeSettingsWindow, "Bucket Flow Monitor 3D");

        GUI.color = oldColor;
        GUI.backgroundColor = oldBackground;
    }

    private void EnsureRuntimeUiStyles()
    {
        if (runtimeUiStylesReady && runtimeUiHeaderStyle != null)
        {
            return;
        }

        runtimeUiHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = Mathf.RoundToInt(15 * runtimeUiScale),
            alignment = TextAnchor.MiddleLeft
        };

        runtimeUiSectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = Mathf.RoundToInt(13 * runtimeUiScale),
            normal = { textColor = new Color(0.65f, 0.92f, 1f, 1f) }
        };

        runtimeUiMiniLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(11 * runtimeUiScale),
            wordWrap = true,
            normal = { textColor = new Color(0.88f, 0.94f, 1f, 1f) }
        };

        runtimeUiStylesReady = true;
    }

    private void DrawRuntimeSettingsWindow(int windowId)
    {
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Runtime visual settings", runtimeUiHeaderStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Hide", GUILayout.Width(58f)))
        {
            runtimeSettingsPanelOpen = false;
        }
        GUILayout.EndHorizontal();

        runtimeSettingsScroll = GUILayout.BeginScrollView(runtimeSettingsScroll);

        if (runtimeUiShowSourceReadout)
        {
            DrawRuntimeReadoutSection();
        }

        DrawRuntimeMainVisualsSection();
        DrawRuntimeTracerSection();
        DrawRuntimeOutletSection();
        DrawRuntimeLevelTextSection();
        DrawRuntimeBucketSafetySection();
        DrawRuntimeColorSection();
        DrawRuntimeUiSection();
        DrawRuntimeActionsSection();

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private void DrawRuntimeReadoutSection()
    {
        GUILayout.Space(4f);
        GUILayout.Label("Live Readout", runtimeUiSectionStyle);

        float water01 = source != null ? source.CurrentWater01 : displayedWater01;
        float currentWater = source != null ? source.CurrentWaterAmount : displayedWater01;
        float initialWater = source != null ? source.InitialWaterAmount : 1f;
        float flowRate = source != null ? source.CurrentFlowRate : displayedFlowRate;
        OutletRuntimeInfo outlet = GetOutletRuntimeInfo();

        GUILayout.Label(
            "Water: " + (water01 * 100f).ToString("0") + "%  |  Amount: " + currentWater.ToString("0.0") + "/" + Mathf.Max(0f, initialWater).ToString("0.0") +
            "\nFlow: " + flowRate.ToString("0") + " particles/s  |  Drop: " + displayedDecreasePerSecond.ToString("0.00") + " water/s" +
            "\nOutlet: " + outlet.shapeName + "  |  Tracers: " + tracerCount,
            runtimeUiMiniLabelStyle
        );
    }

    private void DrawRuntimeMainVisualsSection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Visible Elements", runtimeUiSectionStyle);

        showInsideTracers = DrawToggleSetting("Inside helical tracer dots", showInsideTracers, true);
        showOutletAwarePaths = DrawToggleSetting("Outlet-aware flow paths", showOutletAwarePaths, true);
        showVortexPath = DrawToggleSetting("Internal vortex spiral path", showVortexPath, true);
        showLevelSurface = DrawToggleSetting("Water level surface", showLevelSurface, true);
        showLevelBar = DrawToggleSetting("Water level bar", showLevelBar, true);
        showFlowArrow = DrawToggleSetting("Flow arrow", showFlowArrow, true);
        showText = DrawToggleSetting("3D info text", showText, true);
        keepFlowVisualsInsideBucket = DrawToggleSetting("Keep all visuals inside bucket", keepFlowVisualsInsideBucket, false);
    }

    private void DrawRuntimeTracerSection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Inside Tracer Motion", runtimeUiSectionStyle);

        int newTracerCount = DrawIntSliderSetting("Tracer Count", tracerCount, 0, 70000);
        if (newTracerCount != tracerCount)
        {
            tracerCount = newTracerCount;
            RequestRuntimeVisualRebuild();
        }

        tracerSize = DrawFloatSliderSetting("Tracer Size", tracerSize, 0.001f, 0.08f, "0.000");
        fallingTracerSize = DrawFloatSliderSetting("Falling Tracer Size", fallingTracerSize, 0.001f, 0.08f, "0.000");
        idleVerticalSpeed = DrawFloatSliderSetting("Idle Vertical Speed", idleVerticalSpeed, 0f, 0.35f, "0.000");
        flowVerticalSpeed = DrawFloatSliderSetting("Flow Vertical Speed", flowVerticalSpeed, 0f, 3f, "0.00");
        swirlSpeed = DrawFloatSliderSetting("Swirl Speed", swirlSpeed, 0f, 12f, "0.00");
        vortexStrength = DrawFloatSliderSetting("Vortex Strength", vortexStrength, 0f, 4f, "0.00");
        drainPullStrength = DrawFloatSliderSetting("Drain Pull Strength", drainPullStrength, 0f, 4f, "0.00");
        turbulenceAmount = DrawFloatSliderSetting("Turbulence Amount", turbulenceAmount, 0f, 1f, "0.00");
        vortexCoreRadius = DrawFloatSliderSetting("Vortex Core Radius", vortexCoreRadius, 0.05f, 1f, "0.00");
        flowRateForFullMotion = DrawFloatSliderSetting("Flow Rate For Full Motion", flowRateForFullMotion, 1f, 120000f, "0");
    }

    private void DrawRuntimeOutletSection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Outlet / Path Preview", runtimeUiSectionStyle);

        autoReadOutletFromSource = DrawToggleSetting("Auto read outlet from source", autoReadOutletFromSource, false);
        useSourceEmissionOffset = DrawToggleSetting("Use source emission offset", useSourceEmissionOffset, false);
        useSourceEmissionDirection = DrawToggleSetting("Use source emission direction", useSourceEmissionDirection, false);

        visualOutletShape = DrawOutletShapePopup("Visual Outlet Shape", visualOutletShape);
        outletLocalDirection = DrawVector3Setting("Manual Outlet Direction", outletLocalDirection, -1f, 1f);

        int newPreviewPaths = DrawIntSliderSetting("Max Preview Paths", maxPreviewPaths, 1, 12);
        if (newPreviewPaths != maxPreviewPaths)
        {
            maxPreviewPaths = newPreviewPaths;
            RequestRuntimeVisualRebuild();
        }

        previewPathWidth = DrawFloatSliderSetting("Preview Path Width", previewPathWidth, 0.001f, 0.04f, "0.000");
        outletPreviewScale = DrawFloatSliderSetting("Outlet Preview Scale", outletPreviewScale, 0.1f, 3f, "0.00");
        vortexPathWidth = DrawFloatSliderSetting("Vortex Path Width", vortexPathWidth, 0.001f, 0.04f, "0.000");
        vortexTurns = DrawFloatSliderSetting("Vortex Turns", vortexTurns, 1f, 7f, "0.00");
    }

    private void DrawRuntimeLevelTextSection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Level Bar / Text", runtimeUiSectionStyle);

        levelBarLocalPosition = DrawVector3Setting("Level Bar Local Position", levelBarLocalPosition, -2f, 2f);
        textLocalPosition = DrawVector3Setting("Text Local Position", textLocalPosition, -2f, 2f);
        levelBarHeight = DrawFloatSliderSetting("Level Bar Height", levelBarHeight, 0.05f, 2f, "0.00");
        levelBarWidth = DrawFloatSliderSetting("Level Bar Width", levelBarWidth, 0.005f, 0.16f, "0.000");
        surfaceThickness = DrawFloatSliderSetting("Surface Thickness", surfaceThickness, 0.001f, 0.08f, "0.000");
        valueSmoothing = DrawFloatSliderSetting("Value Smoothing", valueSmoothing, 0.01f, 0.8f, "0.00");
    }

    private void DrawRuntimeBucketSafetySection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Bucket Shape / Clamp", runtimeUiSectionStyle);

        localCenterOffset = DrawVector3Setting("Local Center Offset", localCenterOffset, -1.5f, 1.5f);
        bucketBottomY = DrawFloatSliderSetting("Bucket Bottom Y", bucketBottomY, -2f, 1f, "0.00");
        bucketTopY = DrawFloatSliderSetting("Bucket Top Y", bucketTopY, bucketBottomY + 0.001f, 2.5f, "0.00");
        bottomRadius = DrawFloatSliderSetting("Bottom Radius", bottomRadius, 0.01f, 1.5f, "0.00");
        topRadius = DrawFloatSliderSetting("Top Radius", topRadius, 0.01f, 2.0f, "0.00");
        insideWallPaddingMultiplier = DrawFloatSliderSetting("Inside Wall Padding", insideWallPaddingMultiplier, 0.55f, 0.99f, "0.00");
        bottomClampPadding = DrawFloatSliderSetting("Bottom Clamp Padding", bottomClampPadding, 0f, 0.15f, "0.000");
        surfaceClampPadding = DrawFloatSliderSetting("Surface Clamp Padding", surfaceClampPadding, 0f, 0.15f, "0.000");
        drainTargetRadiusFactor = DrawFloatSliderSetting("Drain Target Radius Factor", drainTargetRadiusFactor, 0.15f, 0.95f, "0.00");
    }

    private void DrawRuntimeColorSection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Colors", runtimeUiSectionStyle);

        useRainbowVisualColors = DrawToggleSetting("Use Rainbow Visual Colors", useRainbowVisualColors, false);
        rainbowTextColor = DrawToggleSetting("Rainbow Text Color", rainbowTextColor, false);
        rainbowSaturation = DrawFloatSliderSetting("Rainbow Saturation", rainbowSaturation, 0f, 1f, "0.00");
        rainbowValue = DrawFloatSliderSetting("Rainbow Value", rainbowValue, 0f, 1f, "0.00");
        rainbowAlpha = DrawFloatSliderSetting("Rainbow Alpha", rainbowAlpha, 0f, 1f, "0.00");
        rainbowScrollSpeed = DrawFloatSliderSetting("Rainbow Scroll Speed", rainbowScrollSpeed, 0f, 2f, "0.00");
        tracerRainbowSpread = DrawFloatSliderSetting("Tracer Rainbow Spread", tracerRainbowSpread, 0f, 3f, "0.00");

        runtimeUiShowColorSettings = DrawToggleSetting("Show fallback RGBA color sliders", runtimeUiShowColorSettings, false);
        if (runtimeUiShowColorSettings)
        {
            tracerColor = DrawColorSetting("Tracer Color", tracerColor);
            surfaceColor = DrawColorSetting("Surface Color", surfaceColor);
            barBackColor = DrawColorSetting("Bar Back Color", barBackColor);
            barFillColor = DrawColorSetting("Bar Fill Color", barFillColor);
            arrowColor = DrawColorSetting("Arrow / Paths Color", arrowColor);
            vortexColor = DrawColorSetting("Vortex Color", vortexColor);
            textColor = DrawColorSetting("Text Color", textColor);
        }
    }

    private void DrawRuntimeUiSection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Panel UI", runtimeUiSectionStyle);

        runtimeUiShowSourceReadout = DrawToggleSetting("Show live readout", runtimeUiShowSourceReadout, false);

        float oldScale = runtimeUiScale;
        runtimeUiScale = DrawFloatSliderSetting("Panel Scale", runtimeUiScale, 0.75f, 1.6f, "0.00");
        if (!Mathf.Approximately(oldScale, runtimeUiScale))
        {
            runtimeUiStylesReady = false;
        }

        runtimeUiWidth = DrawFloatSliderSetting("Panel Width", runtimeUiWidth, 280f, 680f, "0");
        runtimeUiHeight = DrawFloatSliderSetting("Panel Height", runtimeUiHeight, 260f, 820f, "0");
        runtimeUiOpacity = DrawFloatSliderSetting("Panel Opacity", runtimeUiOpacity, 0.15f, 1f, "0.00");
    }

    private void DrawRuntimeActionsSection()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Actions", runtimeUiSectionStyle);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild Visuals"))
        {
            RequestRuntimeVisualRebuild();
        }

        if (GUILayout.Button("Reset Tracers"))
        {
            for (int i = 0; i < tracers.Count; i++)
            {
                RespawnTracer(tracers[i]);
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Hide All"))
        {
            showInsideTracers = false;
            showOutletAwarePaths = false;
            showVortexPath = false;
            showLevelSurface = false;
            showLevelBar = false;
            showFlowArrow = false;
            showText = false;
            RequestRuntimeVisualRebuild();
        }

        if (GUILayout.Button("Show All"))
        {
            showInsideTracers = true;
            showOutletAwarePaths = true;
            showVortexPath = true;
            showLevelSurface = true;
            showLevelBar = true;
            showFlowArrow = true;
            showText = true;
            RequestRuntimeVisualRebuild();
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("The panel changes only this visual monitor. It does not change the GPU SPH simulation itself.", runtimeUiMiniLabelStyle);
    }

    private bool DrawToggleSetting(string label, bool value, bool rebuildVisuals)
    {
        bool newValue = GUILayout.Toggle(value, label);
        if (newValue != value && rebuildVisuals)
        {
            RequestRuntimeVisualRebuild();
        }
        return newValue;
    }

    private float DrawFloatSliderSetting(string label, float value, float min, float max, string format)
    {
        min = Mathf.Min(min, max);
        max = Mathf.Max(min, max);
        GUILayout.Label(label + ": " + value.ToString(format), runtimeUiMiniLabelStyle);
        return GUILayout.HorizontalSlider(value, min, max);
    }

    private int DrawIntSliderSetting(string label, int value, int min, int max)
    {
        GUILayout.Label(label + ": " + value.ToString(), runtimeUiMiniLabelStyle);
        return Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max));
    }

    private Vector3 DrawVector3Setting(string label, Vector3 value, float min, float max)
    {
        GUILayout.Label(label + ": " + value.ToString("F2"), runtimeUiMiniLabelStyle);
        value.x = DrawFloatSliderSetting("  X", value.x, min, max, "0.00");
        value.y = DrawFloatSliderSetting("  Y", value.y, min, max, "0.00");
        value.z = DrawFloatSliderSetting("  Z", value.z, min, max, "0.00");
        return value;
    }

    private Color DrawColorSetting(string label, Color color)
    {
        GUILayout.Space(4f);
        GUILayout.Label(label + "  RGBA: " + color.ToString(), runtimeUiMiniLabelStyle);
        color.r = DrawFloatSliderSetting("  R", color.r, 0f, 1f, "0.00");
        color.g = DrawFloatSliderSetting("  G", color.g, 0f, 1f, "0.00");
        color.b = DrawFloatSliderSetting("  B", color.b, 0f, 1f, "0.00");
        color.a = DrawFloatSliderSetting("  A", color.a, 0f, 1f, "0.00");
        return color;
    }

    private VisualOutletShape DrawOutletShapePopup(string label, VisualOutletShape value)
    {
        GUILayout.Label(label + ": " + value, runtimeUiMiniLabelStyle);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Auto"))
        {
            value = VisualOutletShape.AutoFromSource;
        }
        if (GUILayout.Button("Circle"))
        {
            value = VisualOutletShape.Circle;
        }
        if (GUILayout.Button("Line"))
        {
            value = VisualOutletShape.Line;
        }
        if (GUILayout.Button("Holes"))
        {
            value = VisualOutletShape.MultipleHoles;
        }
        if (GUILayout.Button("Random"))
        {
            value = VisualOutletShape.Random;
        }

        GUILayout.EndHorizontal();
        return value;
    }

    private void RequestRuntimeVisualRebuild()
    {
        runtimeUiRebuildRequested = true;
        builtTracerCount = -1;
        builtMaxPreviewPaths = -1;
    }

    private void ClearVisuals()
    {
        if (visualRoot != null)
        {
            Destroy(visualRoot.gameObject);
            visualRoot = null;
        }

        tracers.Clear();
        outletPreviewLines.Clear();
        levelSurface = null;
        levelBarBack = null;
        levelBarFill = null;
        arrowLine = null;
        arrowHeadA = null;
        arrowHeadB = null;
        vortexLine = null;
        infoText = null;
    }

    private void OnDisable()
    {
        ClearVisuals();
    }

    private void OnDestroy()
    {
        ClearVisuals();
    }

    private void OnValidate()
    {
        bucketTopY = Mathf.Max(bucketBottomY + 0.001f, bucketTopY);
        bottomRadius = Mathf.Max(0.01f, bottomRadius);
        topRadius = Mathf.Max(0.01f, topRadius);
        tracerSize = Mathf.Max(0.001f, tracerSize);
        fallingTracerSize = Mathf.Max(0.001f, fallingTracerSize);
        levelBarHeight = Mathf.Max(0.001f, levelBarHeight);
        levelBarWidth = Mathf.Max(0.001f, levelBarWidth);
        surfaceThickness = Mathf.Max(0.001f, surfaceThickness);
        flowRateForFullMotion = Mathf.Max(1f, flowRateForFullMotion);
        insideWallPaddingMultiplier = Mathf.Clamp(insideWallPaddingMultiplier, 0.55f, 0.99f);
        bottomClampPadding = Mathf.Clamp(bottomClampPadding, 0f, 0.15f);
        surfaceClampPadding = Mathf.Clamp(surfaceClampPadding, 0f, 0.15f);
        drainTargetRadiusFactor = Mathf.Clamp(drainTargetRadiusFactor, 0.15f, 0.95f);
        maxPreviewPaths = Mathf.Clamp(maxPreviewPaths, 1, 12);
        rainbowSaturation = Mathf.Clamp01(rainbowSaturation);
        rainbowValue = Mathf.Clamp01(rainbowValue);
        rainbowAlpha = Mathf.Clamp01(rainbowAlpha);
        rainbowScrollSpeed = Mathf.Clamp(rainbowScrollSpeed, 0f, 2f);
        tracerRainbowSpread = Mathf.Clamp(tracerRainbowSpread, 0f, 3f);
        runtimeUiScale = Mathf.Clamp(runtimeUiScale, 0.75f, 1.6f);
        runtimeUiWidth = Mathf.Clamp(runtimeUiWidth, 280f, 680f);
        runtimeUiHeight = Mathf.Clamp(runtimeUiHeight, 260f, 820f);
        runtimeUiOpacity = Mathf.Clamp(runtimeUiOpacity, 0.15f, 1f);
    }
}

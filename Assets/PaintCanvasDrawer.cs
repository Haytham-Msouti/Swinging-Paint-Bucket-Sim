using UnityEngine;

public class PaintCanvasDrawer : MonoBehaviour
{
    private enum SurfaceMaterialType
    {
        Paper,
        Wood,
        Custom
    }


    [Header("Runtime Settings UI")]
    [Tooltip("Shows an in-game settings panel for everything visible/interactive on the paint canvas.")]
    [SerializeField] private bool showRuntimeSettingsUI = true;

    [SerializeField] private Rect runtimeSettingsWindow = new Rect(12f, 12f, 430f, 650f);
    [SerializeField] private bool runtimeShowCanvasSettings = true;
    [SerializeField] private bool runtimeShowBrushSettings = true;
    [SerializeField] private bool runtimeShowSurfaceSettings = false;
    [SerializeField] private bool runtimeShowImpactSettings = false;
    [SerializeField] private bool runtimeShowOptimizationSettings = false;
    [SerializeField] private bool runtimeShowCorrectionSettings = false;

    private static int runtimeSettingsWindowIdCounter = 902400;
    private int runtimeSettingsWindowId;
    private Vector2 runtimeSettingsScroll;

    [Header("Canvas Texture")]
    [SerializeField, Min(64)] private int textureWidth = 1024;
    [SerializeField, Min(64)] private int textureHeight = 1024;
    [SerializeField] private Color backgroundColor = Color.white;
    [SerializeField] private bool clearOnStart = true;
    [SerializeField] private bool createMaterialInstance = true;

    [Header("Canvas Size - No Collider")]
    [Tooltip("Keep this ON if this object is Unity's default Plane.")]
    [SerializeField] private bool useUnityPlaneLocalSize = true;

    [Tooltip("Used only when Use Unity Plane Local Size is OFF.")]
    [SerializeField, Min(0.01f)] private float canvasWidth = 10f;

    [Tooltip("Used only when Use Unity Plane Local Size is OFF.")]
    [SerializeField, Min(0.01f)] private float canvasLength = 10f;

    [Header("Brush")]
    [SerializeField, Range(0f, 1f)] private float brushOpacity = 0.9f;
    [SerializeField, Range(0f, 1f)] private float softEdge = 0.65f;
    [SerializeField, Min(0)] private int splatterCount = 1;
    [SerializeField, Min(0f)] private float splatterDistanceMultiplier = 2.2f;
    [SerializeField, Range(0.05f, 1f)] private float splatterSizeMultiplier = 0.28f;

    [Header("Surface Material / Absorption")]
    [Tooltip("Paper = high absorption and fiber spread. Wood = directional grain absorption. Custom = keep your own values.")]
    [SerializeField] private SurfaceMaterialType surfaceMaterial = SurfaceMaterialType.Paper;

    [Tooltip("When ON, choosing Paper or Wood fills the values below automatically.")]
    [SerializeField] private bool applySurfacePresetAutomatically = true;

    [SerializeField, Range(0f, 1f)] private float surfaceAbsorption = 0.88f;
    [SerializeField, Range(0.25f, 2.5f)] private float surfaceSpreadMultiplier = 1.35f;
    [SerializeField, Range(0.1f, 1.5f)] private float surfaceOpacityMultiplier = 0.72f;
    [SerializeField, Range(0f, 1f)] private float surfaceStainDarkening = 0.08f;
    [SerializeField, Range(0.1f, 2f)] private float surfaceEdgeSoftnessMultiplier = 1.35f;
    [SerializeField, Range(0f, 2f)] private float surfaceSplatterAmountMultiplier = 0.65f;
    [SerializeField, Range(0f, 2f)] private float surfaceSplatterDistanceMultiplier = 0.70f;
    [SerializeField, Range(0f, 1f)] private float surfaceFiberNoiseStrength = 0.22f;
    [SerializeField, Min(0.001f)] private float surfaceFiberNoiseScale = 0.025f;
    [SerializeField, Range(0f, 1f)] private float surfaceColorTintStrength = 0.06f;
    [SerializeField] private Color surfaceTintColor = new Color(0.98f, 0.96f, 0.90f, 1f);

    [Header("Wood Grain Response")]
    [SerializeField, Range(0f, 1f)] private float woodGrainStrength = 0.05f;
    [SerializeField, Range(1f, 4f)] private float woodGrainStretch = 1.15f;
    [SerializeField] private Vector2 woodGrainTextureDirection = Vector2.right;

    [Header("High Count Paint Optimization")]
    [Tooltip("Limits how many paint stamps can modify the texture each frame. This prevents hundreds of thousands of impacts from freezing the CPU.")]
    [SerializeField] private bool usePaintBudget = true;

    [SerializeField, Min(1)] private int maxPaintStampsPerFrame = 30;

    [Tooltip("Upload only the changed rectangle instead of the full texture when possible.")]
    [SerializeField] private bool useDirtyRectUpload = true;

    [Tooltip("Minimum time between texture uploads. 0 = upload every frame when dirty.")]
    [SerializeField, Range(0f, 0.2f)] private float textureApplyInterval = 0.033f;

    [Tooltip("Safety clamp for brush radius in pixels. Big radii create huge nested pixel loops.")]
    [SerializeField, Min(1)] private int maxBrushRadiusPixels = 18;

    private Texture2D canvasTexture;
    private Color[] pixels;
    private Color[] dirtyUploadBuffer;
    private Renderer cachedRenderer;
    private bool dirty;
    private bool dirtyRectValid;
    private int dirtyMinX;
    private int dirtyMinY;
    private int dirtyMaxX;
    private int dirtyMaxY;
    private float nextAllowedTextureApplyTime;
    private int paintBudgetFrame = -1;
    private int paintStampsThisFrame;
    private int texturePropertyId = -1;

    private void Awake()
    {
        InitializeCanvas();
    }

    private void Start()
    {
        if (clearOnStart)
        {
            ClearCanvas();
        }
    }

    private void Update()
    {
        ApplyTextureChangesIfNeeded();
    }

    public void InitializeCanvas()
    {
        ApplySurfacePresetIfNeeded();
        ClampSurfaceSettings();

        textureWidth = Mathf.Max(64, textureWidth);
        textureHeight = Mathf.Max(64, textureHeight);
        canvasWidth = Mathf.Max(0.01f, canvasWidth);
        canvasLength = Mathf.Max(0.01f, canvasLength);
        maxBrushRadiusPixels = Mathf.Max(1, maxBrushRadiusPixels);
        maxPaintStampsPerFrame = Mathf.Max(1, maxPaintStampsPerFrame);

        cachedRenderer = GetComponent<Renderer>();

        canvasTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        canvasTexture.name = "ManualPaintCanvasTexture";
        canvasTexture.wrapMode = TextureWrapMode.Clamp;
        canvasTexture.filterMode = FilterMode.Bilinear;

        pixels = new Color[textureWidth * textureHeight];

        AssignTextureToMaterial();
        FillTexture(backgroundColor);
        ApplyTextureChangesIfNeeded(true);
    }

    private void AssignTextureToMaterial()
    {
        if (cachedRenderer == null)
        {
            return;
        }

        Material mat = createMaterialInstance ? cachedRenderer.material : cachedRenderer.sharedMaterial;
        if (mat == null)
        {
            return;
        }

        if (mat.HasProperty("_BaseMap"))
        {
            texturePropertyId = Shader.PropertyToID("_BaseMap");
        }
        else if (mat.HasProperty("_MainTex"))
        {
            texturePropertyId = Shader.PropertyToID("_MainTex");
        }
        else
        {
            texturePropertyId = -1;
        }

        if (texturePropertyId >= 0)
        {
            mat.SetTexture(texturePropertyId, canvasTexture);
        }
        else
        {
            mat.mainTexture = canvasTexture;
        }

        ApplyRendererSurfaceLook(mat);
    }

    public void ClearCanvas()
    {
        EnsureCanvasReady();
        FillTexture(backgroundColor);
    }

    private void FillTexture(Color color)
    {
        if (pixels == null || pixels.Length != textureWidth * textureHeight)
        {
            pixels = new Color[textureWidth * textureHeight];
        }

        color.a = 1f;

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        MarkFullTextureDirty();
    }

    private void ApplySurfacePresetIfNeeded()
    {
        if (!applySurfacePresetAutomatically || surfaceMaterial == SurfaceMaterialType.Custom)
        {
            return;
        }

        switch (surfaceMaterial)
        {
            case SurfaceMaterialType.Wood:
                backgroundColor = new Color(0.55f, 0.36f, 0.19f, 1f);
                surfaceAbsorption = 0.55f;
                surfaceSpreadMultiplier = 0.85f;
                surfaceOpacityMultiplier = 0.68f;
                surfaceStainDarkening = 0.22f;
                surfaceEdgeSoftnessMultiplier = 0.85f;
                surfaceSplatterAmountMultiplier = 0.55f;
                surfaceSplatterDistanceMultiplier = 0.55f;
                surfaceFiberNoiseStrength = 0.32f;
                surfaceFiberNoiseScale = 0.060f;
                surfaceColorTintStrength = 0.32f;
                surfaceTintColor = new Color(0.62f, 0.39f, 0.20f, 1f);
                woodGrainStrength = 0.78f;
                woodGrainStretch = 2.45f;
                woodGrainTextureDirection = Vector2.right;
                break;

            case SurfaceMaterialType.Paper:
            default:
                backgroundColor = new Color(0.96f, 0.945f, 0.895f, 1f);
                surfaceAbsorption = 0.88f;
                surfaceSpreadMultiplier = 1.35f;
                surfaceOpacityMultiplier = 0.72f;
                surfaceStainDarkening = 0.08f;
                surfaceEdgeSoftnessMultiplier = 1.35f;
                surfaceSplatterAmountMultiplier = 0.65f;
                surfaceSplatterDistanceMultiplier = 0.70f;
                surfaceFiberNoiseStrength = 0.22f;
                surfaceFiberNoiseScale = 0.025f;
                surfaceColorTintStrength = 0.06f;
                surfaceTintColor = new Color(0.98f, 0.96f, 0.90f, 1f);
                woodGrainStrength = 0.05f;
                woodGrainStretch = 1.15f;
                woodGrainTextureDirection = Vector2.right;
                break;
        }
    }

    private void ClampSurfaceSettings()
    {
        surfaceAbsorption = Mathf.Clamp01(surfaceAbsorption);
        surfaceSpreadMultiplier = Mathf.Clamp(surfaceSpreadMultiplier, 0.25f, 2.5f);
        surfaceOpacityMultiplier = Mathf.Clamp(surfaceOpacityMultiplier, 0.1f, 1.5f);
        surfaceStainDarkening = Mathf.Clamp01(surfaceStainDarkening);
        surfaceEdgeSoftnessMultiplier = Mathf.Clamp(surfaceEdgeSoftnessMultiplier, 0.1f, 2f);
        surfaceSplatterAmountMultiplier = Mathf.Clamp(surfaceSplatterAmountMultiplier, 0f, 2f);
        surfaceSplatterDistanceMultiplier = Mathf.Clamp(surfaceSplatterDistanceMultiplier, 0f, 2f);
        surfaceFiberNoiseStrength = Mathf.Clamp01(surfaceFiberNoiseStrength);
        surfaceFiberNoiseScale = Mathf.Max(0.001f, surfaceFiberNoiseScale);
        surfaceColorTintStrength = Mathf.Clamp01(surfaceColorTintStrength);
        woodGrainStrength = Mathf.Clamp01(woodGrainStrength);
        woodGrainStretch = Mathf.Clamp(woodGrainStretch, 1f, 4f);

        if (woodGrainTextureDirection.sqrMagnitude < 0.000001f)
        {
            woodGrainTextureDirection = Vector2.right;
        }
    }

    private void ApplyRendererSurfaceLook(Material mat)
    {
        if (mat == null)
        {
            return;
        }

        // Keep the paint texture as the main look, but tune the material response.
        // Paper is very rough. Wood is still rough, but usually a bit smoother.
        float smoothness = surfaceMaterial == SurfaceMaterialType.Wood ? 0.20f : 0.04f;

        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", smoothness);
        }

        if (mat.HasProperty("_Glossiness"))
        {
            mat.SetFloat("_Glossiness", smoothness);
        }

        if (mat.HasProperty("_Metallic"))
        {
            mat.SetFloat("_Metallic", 0f);
        }

        int colorId = DetectMaterialColorProperty(mat);
        if (colorId >= 0)
        {
            mat.SetColor(colorId, Color.white);
        }
    }

    private int DetectMaterialColorProperty(Material mat)
    {
        if (mat == null) return -1;
        if (mat.HasProperty("_BaseColor")) return Shader.PropertyToID("_BaseColor");
        if (mat.HasProperty("_Color")) return Shader.PropertyToID("_Color");
        if (mat.HasProperty("_TintColor")) return Shader.PropertyToID("_TintColor");
        return -1;
    }

    public void SetSurfaceToPaper(bool clearCanvasAfterChange = true)
    {
        surfaceMaterial = SurfaceMaterialType.Paper;
        applySurfacePresetAutomatically = true;
        ApplySurfacePresetIfNeeded();
        ClampSurfaceSettings();

        if (clearCanvasAfterChange)
        {
            ClearCanvas();
        }

        if (cachedRenderer != null)
        {
            ApplyRendererSurfaceLook(createMaterialInstance ? cachedRenderer.material : cachedRenderer.sharedMaterial);
        }
    }

    public void SetSurfaceToWood(bool clearCanvasAfterChange = true)
    {
        surfaceMaterial = SurfaceMaterialType.Wood;
        applySurfacePresetAutomatically = true;
        ApplySurfacePresetIfNeeded();
        ClampSurfaceSettings();

        if (clearCanvasAfterChange)
        {
            ClearCanvas();
        }

        if (cachedRenderer != null)
        {
            ApplyRendererSurfaceLook(createMaterialInstance ? cachedRenderer.material : cachedRenderer.sharedMaterial);
        }
    }

    [Header("Impact And Splatter Algorithm")]
    [SerializeField, Min(0.1f)] private float maxImpactSpeed = 6f;
    [SerializeField, Range(1f, 89f)] private float maxImpactAngle = 75f;
    [SerializeField, Min(0.1f)] private float baseSpotRadiusMultiplier = 1f;
    [SerializeField, Min(0.1f)] private float highSpeedSpotMultiplier = 2.1f;
    [SerializeField, Range(1f, 4f)] private float angledSpotStretch = 2.4f;
    [SerializeField, Range(0f, 1f)] private float directionalSplatterStrength = 0.8f;

    [Header("Splatter Direction Correction")]
    [Tooltip("If the splatter looks mirrored left/right on the canvas, turn this ON.")]
    [SerializeField] private bool flipSplatterDirectionX = false;

    [Tooltip("If the splatter looks mirrored forward/back on the canvas, turn this ON.")]
    [SerializeField] private bool flipSplatterDirectionY = false;

    [Tooltip("If ON, splatter uses the motion direction on the canvas plane only. This avoids a reflected-looking direction caused by the canvas normal component.")]
    [SerializeField] private bool usePlaneProjectedSplatterDirection = true;

    [Header("Canvas Texture Mapping Correction")]
    [Tooltip("Flip the whole painted texture horizontally. Use this if the drawing position itself looks mirrored left/right.")]
    [SerializeField] private bool flipCanvasTextureX = false;

    [Tooltip("Flip the whole painted texture vertically. Use this if the drawing position itself looks mirrored forward/back.")]
    [SerializeField] private bool flipCanvasTextureY = false;

    [Tooltip("Swap X and Y texture axes. Use this only if the drawing looks rotated/transposed, not just mirrored.")]
    [SerializeField] private bool swapCanvasTextureAxes = false;

    public void DrawPaint(Vector3 worldPoint, Color paintColor, float particleSize, float impactSpeed)
    {
        DrawPaint(worldPoint, paintColor, particleSize, impactSpeed, 0f, 0.5f, Vector3.zero);
    }

    public void DrawPaint(
        Vector3 worldPoint,
        Color paintColor,
        float particleSize,
        float impactSpeed,
        float impactAngleDegrees,
        float viscosity01,
        Vector3 impactDirectionWorld)
    {
        float speed01 = Mathf.Clamp01(impactSpeed / Mathf.Max(0.0001f, maxImpactSpeed));
        float angle01 = Mathf.Clamp01(impactAngleDegrees / Mathf.Max(1f, maxImpactAngle));
        viscosity01 = Mathf.Clamp01(viscosity01);

        DrawPaintSpot(
            worldPoint,
            paintColor,
            particleSize,
            speed01,
            angle01,
            viscosity01,
            impactDirectionWorld
        );
    }

    public bool DrawPaintSpot(Vector3 worldPoint, Color paintColor, float worldRadius, float speed01)
    {
        return DrawPaintSpot(worldPoint, paintColor, worldRadius, speed01, 0f, 0.5f, Vector3.zero);
    }

    public bool DrawPaintSpot(
        Vector3 worldPoint,
        Color paintColor,
        float worldRadius,
        float speed01,
        float angle01,
        float viscosity01,
        Vector3 impactDirectionWorld)
    {
        if (!TryConsumePaintBudget())
        {
            return false;
        }

        EnsureCanvasReady();

        int centerX;
        int centerY;

        if (!WorldPointToPixel(worldPoint, out centerX, out centerY))
        {
            return false;
        }

        speed01 = Mathf.Clamp01(speed01);
        angle01 = Mathf.Clamp01(angle01);
        viscosity01 = Mathf.Clamp01(viscosity01);
        ClampSurfaceSettings();

        float lowViscosity01 = 1f - viscosity01;
        float absorb01 = Mathf.Clamp01(surfaceAbsorption);
        Color surfacePaintColor = ApplySurfaceTintToPaint(paintColor);
        float effectiveBrushOpacity = GetEffectivePaintOpacity();

        // High speed => larger spot.
        // High viscosity => heavier spot with less spread.
        // High impact angle => stretched / oval mark.
        // Surface response:
        // Paper absorbs more, feathers the edge, and spreads into fibers.
        // Wood absorbs along the grain, so it stretches directionally and produces fewer splashes.
        float speedRadiusFactor = Mathf.Lerp(0.75f, highSpeedSpotMultiplier, speed01);
        float viscosityRadiusFactor = Mathf.Lerp(1.35f, 0.75f, viscosity01);
        float absorptionRadiusFactor = Mathf.Lerp(0.95f, 1.20f, absorb01);
        float finalWorldRadius =
            worldRadius *
            baseSpotRadiusMultiplier *
            speedRadiusFactor *
            viscosityRadiusFactor *
            surfaceSpreadMultiplier *
            absorptionRadiusFactor;

        int baseRadiusPixels = Mathf.Clamp(WorldRadiusToPixels(finalWorldRadius), 1, maxBrushRadiusPixels);

        Vector2 pixelDirection = WorldDirectionToPixelDirection(impactDirectionWorld);
        Vector2 surfaceDirection = GetSurfaceDrivenDirection(pixelDirection);

        int majorRadius = Mathf.Max(
            1,
            Mathf.RoundToInt(
                baseRadiusPixels *
                Mathf.Lerp(1f, angledSpotStretch, angle01) *
                Mathf.Lerp(1f, woodGrainStretch, woodGrainStrength)
            )
        );

        int minorRadius = Mathf.Max(
            1,
            Mathf.RoundToInt(
                baseRadiusPixels *
                Mathf.Lerp(1f, 0.55f, angle01) *
                Mathf.Lerp(1f, 0.72f, woodGrainStrength)
            )
        );

        DrawEllipse(centerX, centerY, majorRadius, minorRadius, surfaceDirection, surfacePaintColor, effectiveBrushOpacity);

        // High speed + low viscosity => more and farther splatter.
        // High absorption => fewer droplets because the surface drinks the fluid.
        // Wood grain => shorter splatter distance and directional staining.
        int dynamicSplatterCount = Mathf.RoundToInt(
            splatterCount *
            Mathf.Lerp(0.4f, 2.0f, speed01) *
            Mathf.Lerp(1.6f, 0.35f, viscosity01) *
            Mathf.Lerp(0.8f, 1.4f, angle01) *
            surfaceSplatterAmountMultiplier *
            Mathf.Lerp(1f, 0.55f, absorb01)
        );

        dynamicSplatterCount = Mathf.Max(0, dynamicSplatterCount);

        int maxSplatterDistance = Mathf.Max(
            baseRadiusPixels + 1,
            Mathf.RoundToInt(
                baseRadiusPixels *
                splatterDistanceMultiplier *
                surfaceSplatterDistanceMultiplier *
                Mathf.Lerp(0.7f, 1.9f, speed01) *
                Mathf.Lerp(1.5f, 0.55f, viscosity01) *
                Mathf.Lerp(0.8f, 1.5f, angle01) *
                Mathf.Lerp(1f, 0.65f, absorb01)
            )
        );

        int minSplatterRadius = Mathf.Max(
            1,
            Mathf.RoundToInt(baseRadiusPixels * splatterSizeMultiplier * 0.35f)
        );

        int maxSplatterRadius = Mathf.Max(
            minSplatterRadius,
            Mathf.RoundToInt(baseRadiusPixels * splatterSizeMultiplier * Mathf.Lerp(1.15f, 0.65f, viscosity01))
        );

        Vector2 perpendicular = new Vector2(-surfaceDirection.y, surfaceDirection.x);
        float directionality = directionalSplatterStrength * Mathf.Clamp01(speed01 + angle01 * 0.5f);
        directionality = Mathf.Clamp01(directionality + woodGrainStrength * 0.35f);

        for (int i = 0; i < dynamicSplatterCount; i++)
        {
            float distance = Random.Range(baseRadiusPixels, maxSplatterDistance + 1f);

            Vector2 randomOffset = Random.insideUnitCircle * distance;

            float sideSpread = Random.Range(-0.45f, 0.45f) * maxSplatterDistance * Mathf.Lerp(1f, 0.55f, woodGrainStrength);
            Vector2 directedOffset = surfaceDirection * distance + perpendicular * sideSpread;

            Vector2 finalOffset = Vector2.Lerp(randomOffset, directedOffset, directionality);

            int splatterX = centerX + Mathf.RoundToInt(finalOffset.x);
            int splatterY = centerY + Mathf.RoundToInt(finalOffset.y);

            int splatterRadius = Random.Range(minSplatterRadius, maxSplatterRadius + 1);

            float splatterOpacity =
                effectiveBrushOpacity *
                Random.Range(0.22f, 0.68f) *
                Mathf.Lerp(1.12f, 0.65f, viscosity01) *
                Mathf.Lerp(0.85f, 1.1f, lowViscosity01) *
                Mathf.Lerp(1f, 0.70f, absorb01);

            DrawCircle(splatterX, splatterY, splatterRadius, surfacePaintColor, splatterOpacity);
        }

        dirty = true;
        return true;
    }

    private void DrawEllipse(
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        Vector2 direction,
        Color paintColor,
        float opacity)
    {
        radiusX = Mathf.Max(1, radiusX);
        radiusY = Mathf.Max(1, radiusY);

        if (direction.sqrMagnitude < 0.000001f)
        {
            direction = Vector2.right;
        }

        direction.Normalize();
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        int maxRadius = Mathf.Max(radiusX, radiusY);

        int minX = Mathf.Max(0, centerX - maxRadius);
        int maxX = Mathf.Min(textureWidth - 1, centerX + maxRadius);
        int minY = Mathf.Max(0, centerY - maxRadius);
        int maxY = Mathf.Min(textureHeight - 1, centerY + maxRadius);

        paintColor.a = 1f;

        float effectiveSoftEdge = GetEffectiveSoftEdge();
        float innerNorm = Mathf.Clamp01(1f - effectiveSoftEdge);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 delta = new Vector2(x - centerX, y - centerY);

                float localX = Vector2.Dot(delta, direction);
                float localY = Vector2.Dot(delta, perpendicular);

                float ellipseValue =
                    (localX * localX) / (radiusX * radiusX) +
                    (localY * localY) / (radiusY * radiusY);

                if (ellipseValue > 1f)
                {
                    continue;
                }

                float normalizedDistance = Mathf.Sqrt(ellipseValue);
                float edgeFactor = 1f;

                if (effectiveSoftEdge > 0f && normalizedDistance > innerNorm)
                {
                    edgeFactor = Mathf.InverseLerp(1f, innerNorm, normalizedDistance);
                }

                float finalOpacity = Mathf.Clamp01(opacity * edgeFactor);
                BlendPixel(x, y, paintColor, finalOpacity);
            }
        }

        MarkDirtyRect(minX, minY, maxX, maxY);
    }

    private Vector2 WorldDirectionToPixelDirection(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 0.000001f)
        {
            return Vector2.right;
        }

        Vector3 directionForCanvas = worldDirection.normalized;

        if (usePlaneProjectedSplatterDirection)
        {
            directionForCanvas = Vector3.ProjectOnPlane(directionForCanvas, transform.up);

            if (directionForCanvas.sqrMagnitude < 0.000001f)
            {
                return Vector2.right;
            }

            directionForCanvas.Normalize();
        }

        Vector3 localDirection = transform.InverseTransformDirection(directionForCanvas);
        Vector2 pixelDirection = new Vector2(localDirection.x, localDirection.z);

        pixelDirection = ApplyTextureMappingToDirection(pixelDirection);

        if (flipSplatterDirectionX)
        {
            pixelDirection.x *= -1f;
        }

        if (flipSplatterDirectionY)
        {
            pixelDirection.y *= -1f;
        }

        if (pixelDirection.sqrMagnitude < 0.000001f)
        {
            return Vector2.right;
        }

        return pixelDirection.normalized;
    }

    private Vector2 ApplyTextureMappingToDirection(Vector2 direction)
    {
        if (swapCanvasTextureAxes)
        {
            direction = new Vector2(direction.y, direction.x);
        }

        if (flipCanvasTextureX)
        {
            direction.x *= -1f;
        }

        if (flipCanvasTextureY)
        {
            direction.y *= -1f;
        }

        return direction;
    }

    private void ApplyTextureMappingToUV(ref float u, ref float v)
    {
        if (swapCanvasTextureAxes)
        {
            float temp = u;
            u = v;
            v = temp;
        }

        if (flipCanvasTextureX)
        {
            u = 1f - u;
        }

        if (flipCanvasTextureY)
        {
            v = 1f - v;
        }
    }

    private void DrawCircle(int centerX, int centerY, int radiusPixels, Color paintColor, float opacity)
    {
        if (radiusPixels <= 0)
        {
            return;
        }

        int minX = Mathf.Max(0, centerX - radiusPixels);
        int maxX = Mathf.Min(textureWidth - 1, centerX + radiusPixels);
        int minY = Mathf.Max(0, centerY - radiusPixels);
        int maxY = Mathf.Min(textureHeight - 1, centerY + radiusPixels);

        float radiusSquared = radiusPixels * radiusPixels;
        float effectiveSoftEdge = GetEffectiveSoftEdge();
        float innerRadius = radiusPixels * (1f - effectiveSoftEdge);
        float innerSquared = innerRadius * innerRadius;

        paintColor.a = 1f;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - centerX;
                float dy = y - centerY;
                float distanceSquared = dx * dx + dy * dy;

                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float edgeFactor = 1f;
                if (distanceSquared > innerSquared)
                {
                    float distance = Mathf.Sqrt(distanceSquared);
                    edgeFactor = Mathf.InverseLerp(radiusPixels, innerRadius, distance);
                }

                float finalOpacity = Mathf.Clamp01(opacity * edgeFactor);
                BlendPixel(x, y, paintColor, finalOpacity);
            }
        }

        MarkDirtyRect(minX, minY, maxX, maxY);
    }

    private void BlendPixel(int x, int y, Color paintColor, float opacity)
    {
        int index = y * textureWidth + x;
        Color oldColor = pixels[index];

        float fiberFactor = GetSurfaceFiberFactor(x, y);
        float effectiveOpacity = Mathf.Clamp01(opacity * Mathf.Lerp(1f, fiberFactor, surfaceAbsorption));

        Color targetColor = paintColor;
        Color blended = Color.Lerp(oldColor, targetColor, effectiveOpacity);

        if (surfaceStainDarkening > 0f && effectiveOpacity > 0f)
        {
            float darkenAmount = surfaceStainDarkening * surfaceAbsorption * effectiveOpacity;
            blended.r *= 1f - darkenAmount;
            blended.g *= 1f - darkenAmount;
            blended.b *= 1f - darkenAmount;
        }

        blended.a = 1f;
        pixels[index] = blended;
    }

    private Color ApplySurfaceTintToPaint(Color paintColor)
    {
        paintColor.a = 1f;
        if (surfaceColorTintStrength <= 0f)
        {
            return paintColor;
        }

        Color tinted = Color.Lerp(paintColor, paintColor * surfaceTintColor, surfaceColorTintStrength * Mathf.Clamp01(surfaceAbsorption));
        tinted.a = 1f;
        return tinted;
    }

    private float GetEffectivePaintOpacity()
    {
        return Mathf.Clamp01(brushOpacity * surfaceOpacityMultiplier * Mathf.Lerp(1.05f, 0.72f, surfaceAbsorption));
    }

    private float GetEffectiveSoftEdge()
    {
        return Mathf.Clamp01(softEdge * surfaceEdgeSoftnessMultiplier);
    }

    private Vector2 GetSurfaceDrivenDirection(Vector2 fallbackDirection)
    {
        Vector2 direction = fallbackDirection;
        if (direction.sqrMagnitude < 0.000001f)
        {
            direction = Vector2.right;
        }

        if (woodGrainStrength <= 0f)
        {
            return direction.normalized;
        }

        Vector2 grain = woodGrainTextureDirection;
        if (grain.sqrMagnitude < 0.000001f)
        {
            grain = Vector2.right;
        }

        grain.Normalize();
        direction.Normalize();

        if (Vector2.Dot(direction, grain) < 0f)
        {
            grain *= -1f;
        }

        return Vector2.Lerp(direction, grain, woodGrainStrength).normalized;
    }

    private float GetSurfaceFiberFactor(int x, int y)
    {
        float strength = Mathf.Clamp01(surfaceFiberNoiseStrength);
        if (strength <= 0f)
        {
            return 1f;
        }

        float scale = Mathf.Max(0.001f, surfaceFiberNoiseScale);
        float baseNoise = Mathf.PerlinNoise(x * scale, y * scale);
        float factor = Mathf.Lerp(1f - strength, 1f + strength, baseNoise);

        if (woodGrainStrength > 0f)
        {
            Vector2 grain = woodGrainTextureDirection;
            if (grain.sqrMagnitude < 0.000001f)
            {
                grain = Vector2.right;
            }

            grain.Normalize();
            Vector2 perpendicular = new Vector2(-grain.y, grain.x);

            float along = x * grain.x + y * grain.y;
            float across = x * perpendicular.x + y * perpendicular.y;

            float grainNoise = Mathf.PerlinNoise(along * scale * 0.18f, across * scale * 3.8f);
            float grainFactor = Mathf.Lerp(0.72f, 1.28f, grainNoise);
            factor = Mathf.Lerp(factor, grainFactor, woodGrainStrength);
        }

        return Mathf.Clamp(factor, 0.25f, 1.45f);
    }

    private bool WorldPointToPixel(Vector3 worldPoint, out int pixelX, out int pixelY)
    {
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);

        float halfWidth = useUnityPlaneLocalSize ? 5f : canvasWidth * 0.5f;
        float halfLength = useUnityPlaneLocalSize ? 5f : canvasLength * 0.5f;

        pixelX = 0;
        pixelY = 0;

        if (Mathf.Abs(localPoint.x) > halfWidth || Mathf.Abs(localPoint.z) > halfLength)
        {
            return false;
        }

        float u = Mathf.InverseLerp(-halfWidth, halfWidth, localPoint.x);
        float v = Mathf.InverseLerp(-halfLength, halfLength, localPoint.z);

        ApplyTextureMappingToUV(ref u, ref v);

        pixelX = Mathf.Clamp(Mathf.RoundToInt(u * (textureWidth - 1)), 0, textureWidth - 1);
        pixelY = Mathf.Clamp(Mathf.RoundToInt(v * (textureHeight - 1)), 0, textureHeight - 1);

        return true;
    }

    private int WorldRadiusToPixels(float worldRadius)
    {
        float halfWidth = useUnityPlaneLocalSize ? 5f : canvasWidth * 0.5f;
        float halfLength = useUnityPlaneLocalSize ? 5f : canvasLength * 0.5f;

        Vector3 worldLeft = transform.TransformPoint(new Vector3(-halfWidth, 0f, 0f));
        Vector3 worldRight = transform.TransformPoint(new Vector3(halfWidth, 0f, 0f));
        Vector3 worldBottom = transform.TransformPoint(new Vector3(0f, 0f, -halfLength));
        Vector3 worldTop = transform.TransformPoint(new Vector3(0f, 0f, halfLength));

        float worldCanvasWidth = Mathf.Max(0.0001f, Vector3.Distance(worldLeft, worldRight));
        float worldCanvasLength = Mathf.Max(0.0001f, Vector3.Distance(worldBottom, worldTop));

        float pixelsPerWorldX = textureWidth / worldCanvasWidth;
        float pixelsPerWorldY = textureHeight / worldCanvasLength;
        float pixelsPerWorld = (pixelsPerWorldX + pixelsPerWorldY) * 0.5f;

        return Mathf.Max(1, Mathf.RoundToInt(worldRadius * pixelsPerWorld));
    }

    private bool TryConsumePaintBudget()
    {
        if (!usePaintBudget)
        {
            return true;
        }

        if (paintBudgetFrame != Time.frameCount)
        {
            paintBudgetFrame = Time.frameCount;
            paintStampsThisFrame = 0;
        }

        if (paintStampsThisFrame >= maxPaintStampsPerFrame)
        {
            return false;
        }

        paintStampsThisFrame++;
        return true;
    }

    private void MarkFullTextureDirty()
    {
        dirty = true;
        dirtyRectValid = true;
        dirtyMinX = 0;
        dirtyMinY = 0;
        dirtyMaxX = textureWidth - 1;
        dirtyMaxY = textureHeight - 1;
    }

    private void MarkDirtyRect(int minX, int minY, int maxX, int maxY)
    {
        dirty = true;

        minX = Mathf.Clamp(minX, 0, textureWidth - 1);
        minY = Mathf.Clamp(minY, 0, textureHeight - 1);
        maxX = Mathf.Clamp(maxX, 0, textureWidth - 1);
        maxY = Mathf.Clamp(maxY, 0, textureHeight - 1);

        if (!dirtyRectValid)
        {
            dirtyMinX = minX;
            dirtyMinY = minY;
            dirtyMaxX = maxX;
            dirtyMaxY = maxY;
            dirtyRectValid = true;
            return;
        }

        dirtyMinX = Mathf.Min(dirtyMinX, minX);
        dirtyMinY = Mathf.Min(dirtyMinY, minY);
        dirtyMaxX = Mathf.Max(dirtyMaxX, maxX);
        dirtyMaxY = Mathf.Max(dirtyMaxY, maxY);
    }

    private void ApplyTextureChangesIfNeeded(bool force = false)
    {
        if (canvasTexture == null || pixels == null)
        {
            return;
        }

        if (!dirty && !force)
        {
            return;
        }

        if (!force && textureApplyInterval > 0f && Time.time < nextAllowedTextureApplyTime)
        {
            return;
        }

        if (useDirtyRectUpload && dirtyRectValid)
        {
            ApplyDirtyRectToTexture();
        }
        else
        {
            canvasTexture.SetPixels(pixels);
        }

        canvasTexture.Apply(false);
        dirty = false;
        dirtyRectValid = false;
        nextAllowedTextureApplyTime = Time.time + textureApplyInterval;
    }

    private void ApplyDirtyRectToTexture()
    {
        int width = Mathf.Max(1, dirtyMaxX - dirtyMinX + 1);
        int height = Mathf.Max(1, dirtyMaxY - dirtyMinY + 1);
        int requiredLength = width * height;

        if (dirtyUploadBuffer == null || dirtyUploadBuffer.Length != requiredLength)
        {
            dirtyUploadBuffer = new Color[requiredLength];
        }

        int write = 0;
        for (int y = dirtyMinY; y <= dirtyMaxY; y++)
        {
            int rowStart = y * textureWidth + dirtyMinX;
            for (int x = 0; x < width; x++)
            {
                dirtyUploadBuffer[write++] = pixels[rowStart + x];
            }
        }

        canvasTexture.SetPixels(dirtyMinX, dirtyMinY, width, height, dirtyUploadBuffer);
    }

    private void EnsureCanvasReady()
    {
        if (canvasTexture == null || pixels == null || pixels.Length != textureWidth * textureHeight)
        {
            InitializeCanvas();
        }
    }


    private void OnGUI()
    {
        if (!showRuntimeSettingsUI)
        {
            return;
        }

        if (runtimeSettingsWindowId == 0)
        {
            runtimeSettingsWindowId = ++runtimeSettingsWindowIdCounter;
        }

        runtimeSettingsWindow.width = Mathf.Clamp(runtimeSettingsWindow.width, 340f, Screen.width);
        runtimeSettingsWindow.height = Mathf.Clamp(runtimeSettingsWindow.height, 260f, Screen.height);
        runtimeSettingsWindow.x = Mathf.Clamp(runtimeSettingsWindow.x, 0f, Mathf.Max(0f, Screen.width - runtimeSettingsWindow.width));
        runtimeSettingsWindow.y = Mathf.Clamp(runtimeSettingsWindow.y, 0f, Mathf.Max(0f, Screen.height - runtimeSettingsWindow.height));

        runtimeSettingsWindow = GUI.Window(
            runtimeSettingsWindowId,
            runtimeSettingsWindow,
            DrawRuntimeSettingsWindow,
            "Paint Canvas Runtime Settings"
        );
    }

    private void DrawRuntimeSettingsWindow(int windowId)
    {
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Canvas", GUILayout.Height(24f)))
        {
            ClearCanvas();
        }

        if (GUILayout.Button("Reinitialize", GUILayout.Height(24f)))
        {
            InitializeCanvas();
        }

        if (GUILayout.Button("Hide UI", GUILayout.Height(24f)))
        {
            showRuntimeSettingsUI = false;
        }
        GUILayout.EndHorizontal();

        runtimeSettingsScroll = GUILayout.BeginScrollView(
            runtimeSettingsScroll,
            GUILayout.Width(Mathf.Max(300f, runtimeSettingsWindow.width - 14f)),
            GUILayout.Height(Mathf.Max(160f, runtimeSettingsWindow.height - 72f))
        );

        DrawRuntimeQuickActions();

        runtimeShowCanvasSettings = RuntimeSection("Canvas / Texture", runtimeShowCanvasSettings);
        if (runtimeShowCanvasSettings)
        {
            DrawRuntimeCanvasSettings();
        }

        runtimeShowBrushSettings = RuntimeSection("Brush / Splatter", runtimeShowBrushSettings);
        if (runtimeShowBrushSettings)
        {
            DrawRuntimeBrushSettings();
        }

        runtimeShowSurfaceSettings = RuntimeSection("Surface / Absorption", runtimeShowSurfaceSettings);
        if (runtimeShowSurfaceSettings)
        {
            DrawRuntimeSurfaceSettings();
        }

        runtimeShowImpactSettings = RuntimeSection("Impact Algorithm", runtimeShowImpactSettings);
        if (runtimeShowImpactSettings)
        {
            DrawRuntimeImpactSettings();
        }

        runtimeShowOptimizationSettings = RuntimeSection("Performance / Budget", runtimeShowOptimizationSettings);
        if (runtimeShowOptimizationSettings)
        {
            DrawRuntimeOptimizationSettings();
        }

        runtimeShowCorrectionSettings = RuntimeSection("Direction / Mapping Correction", runtimeShowCorrectionSettings);
        if (runtimeShowCorrectionSettings)
        {
            DrawRuntimeCorrectionSettings();
        }

        GUILayout.Space(8f);
        GUILayout.Label(
            "Texture: " + textureWidth + " x " + textureHeight +
            " | Stamps this frame: " + paintStampsThisFrame + "/" + maxPaintStampsPerFrame +
            " | Dirty: " + (dirty ? "Yes" : "No")
        );

        GUILayout.EndScrollView();

        GUILayout.Label("Drag this window from the title area.");
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        GUILayout.EndVertical();
    }

    private void DrawRuntimeQuickActions()
    {
        GUILayout.Space(4f);
        GUILayout.Label("Quick Presets");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Paper + Clear", GUILayout.Height(24f)))
        {
            SetSurfaceToPaper(true);
        }

        if (GUILayout.Button("Wood + Clear", GUILayout.Height(24f)))
        {
            SetSurfaceToWood(true);
        }

        if (GUILayout.Button("Custom", GUILayout.Height(24f)))
        {
            surfaceMaterial = SurfaceMaterialType.Custom;
            applySurfacePresetAutomatically = false;
            ClampSurfaceSettings();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Soft Paper", GUILayout.Height(24f)))
        {
            surfaceMaterial = SurfaceMaterialType.Custom;
            applySurfacePresetAutomatically = false;
            brushOpacity = 0.78f;
            softEdge = 0.82f;
            splatterCount = 2;
            surfaceAbsorption = 0.92f;
            surfaceSpreadMultiplier = 1.45f;
            surfaceOpacityMultiplier = 0.68f;
            surfaceEdgeSoftnessMultiplier = 1.55f;
            surfaceFiberNoiseStrength = 0.26f;
            ClampSurfaceSettings();
        }

        if (GUILayout.Button("Clean Brush", GUILayout.Height(24f)))
        {
            brushOpacity = 0.95f;
            softEdge = 0.42f;
            splatterCount = 0;
            surfaceSpreadMultiplier = 1.0f;
            surfaceSplatterAmountMultiplier = 0f;
            surfaceFiberNoiseStrength = 0.05f;
            ClampSurfaceSettings();
        }

        if (GUILayout.Button("Fast Mode", GUILayout.Height(24f)))
        {
            usePaintBudget = true;
            maxPaintStampsPerFrame = 12;
            useDirtyRectUpload = true;
            textureApplyInterval = 0.05f;
            maxBrushRadiusPixels = 10;
        }
        GUILayout.EndHorizontal();
    }

    private void DrawRuntimeCanvasSettings()
    {
        clearOnStart = GUILayout.Toggle(clearOnStart, "Clear On Start");
        createMaterialInstance = GUILayout.Toggle(createMaterialInstance, "Create Material Instance");
        useUnityPlaneLocalSize = GUILayout.Toggle(useUnityPlaneLocalSize, "Use Unity Plane Local Size");

        textureWidth = RuntimeIntSlider("Texture Width", textureWidth, 64, 2048);
        textureHeight = RuntimeIntSlider("Texture Height", textureHeight, 64, 2048);

        if (!useUnityPlaneLocalSize)
        {
            canvasWidth = RuntimeFloatSlider("Canvas Width", canvasWidth, 0.01f, 30f, "0.00");
            canvasLength = RuntimeFloatSlider("Canvas Length", canvasLength, 0.01f, 30f, "0.00");
        }

        backgroundColor = RuntimeColorSliders("Background Color", backgroundColor, false);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply Background + Clear", GUILayout.Height(24f)))
        {
            ClearCanvas();
        }

        if (GUILayout.Button("Reassign Texture", GUILayout.Height(24f)))
        {
            AssignTextureToMaterial();
            ApplyTextureChangesIfNeeded(true);
        }
        GUILayout.EndHorizontal();
    }

    private void DrawRuntimeBrushSettings()
    {
        brushOpacity = RuntimeFloatSlider("Brush Opacity", brushOpacity, 0f, 1f, "0.00");
        softEdge = RuntimeFloatSlider("Soft Edge", softEdge, 0f, 1f, "0.00");
        splatterCount = RuntimeIntSlider("Splatter Count", splatterCount, 0, 30);
        splatterDistanceMultiplier = RuntimeFloatSlider("Splatter Distance", splatterDistanceMultiplier, 0f, 8f, "0.00");
        splatterSizeMultiplier = RuntimeFloatSlider("Splatter Size", splatterSizeMultiplier, 0.05f, 1f, "0.00");
    }

    private void DrawRuntimeSurfaceSettings()
    {
        GUILayout.Label("Surface Type: " + surfaceMaterial.ToString());
        applySurfacePresetAutomatically = GUILayout.Toggle(applySurfacePresetAutomatically, "Apply Surface Preset Automatically");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Paper", GUILayout.Height(24f)))
        {
            SetSurfaceToPaper(false);
        }

        if (GUILayout.Button("Wood", GUILayout.Height(24f)))
        {
            SetSurfaceToWood(false);
        }

        if (GUILayout.Button("Custom", GUILayout.Height(24f)))
        {
            surfaceMaterial = SurfaceMaterialType.Custom;
            applySurfacePresetAutomatically = false;
        }
        GUILayout.EndHorizontal();

        surfaceAbsorption = RuntimeFloatSlider("Surface Absorption", surfaceAbsorption, 0f, 1f, "0.00");
        surfaceSpreadMultiplier = RuntimeFloatSlider("Spread Multiplier", surfaceSpreadMultiplier, 0.25f, 2.5f, "0.00");
        surfaceOpacityMultiplier = RuntimeFloatSlider("Opacity Multiplier", surfaceOpacityMultiplier, 0.1f, 1.5f, "0.00");
        surfaceStainDarkening = RuntimeFloatSlider("Stain Darkening", surfaceStainDarkening, 0f, 1f, "0.00");
        surfaceEdgeSoftnessMultiplier = RuntimeFloatSlider("Edge Softness", surfaceEdgeSoftnessMultiplier, 0.1f, 2f, "0.00");
        surfaceSplatterAmountMultiplier = RuntimeFloatSlider("Splatter Amount", surfaceSplatterAmountMultiplier, 0f, 2f, "0.00");
        surfaceSplatterDistanceMultiplier = RuntimeFloatSlider("Splatter Distance Surface", surfaceSplatterDistanceMultiplier, 0f, 2f, "0.00");
        surfaceFiberNoiseStrength = RuntimeFloatSlider("Fiber Noise Strength", surfaceFiberNoiseStrength, 0f, 1f, "0.00");
        surfaceFiberNoiseScale = RuntimeFloatSlider("Fiber Noise Scale", surfaceFiberNoiseScale, 0.001f, 0.15f, "0.000");
        surfaceColorTintStrength = RuntimeFloatSlider("Surface Tint Strength", surfaceColorTintStrength, 0f, 1f, "0.00");
        surfaceTintColor = RuntimeColorSliders("Surface Tint Color", surfaceTintColor, false);

        GUILayout.Label("Wood Grain");
        woodGrainStrength = RuntimeFloatSlider("Wood Grain Strength", woodGrainStrength, 0f, 1f, "0.00");
        woodGrainStretch = RuntimeFloatSlider("Wood Grain Stretch", woodGrainStretch, 1f, 4f, "0.00");
        woodGrainTextureDirection.x = RuntimeFloatSlider("Grain Direction X", woodGrainTextureDirection.x, -1f, 1f, "0.00");
        woodGrainTextureDirection.y = RuntimeFloatSlider("Grain Direction Y", woodGrainTextureDirection.y, -1f, 1f, "0.00");

        ClampSurfaceSettings();

        if (GUILayout.Button("Apply Surface Look To Material", GUILayout.Height(24f)) && cachedRenderer != null)
        {
            ApplyRendererSurfaceLook(createMaterialInstance ? cachedRenderer.material : cachedRenderer.sharedMaterial);
        }
    }

    private void DrawRuntimeImpactSettings()
    {
        maxImpactSpeed = RuntimeFloatSlider("Max Impact Speed", maxImpactSpeed, 0.1f, 20f, "0.00");
        maxImpactAngle = RuntimeFloatSlider("Max Impact Angle", maxImpactAngle, 1f, 89f, "0.0");
        baseSpotRadiusMultiplier = RuntimeFloatSlider("Base Spot Radius", baseSpotRadiusMultiplier, 0.1f, 5f, "0.00");
        highSpeedSpotMultiplier = RuntimeFloatSlider("High Speed Spot", highSpeedSpotMultiplier, 0.1f, 6f, "0.00");
        angledSpotStretch = RuntimeFloatSlider("Angled Spot Stretch", angledSpotStretch, 1f, 4f, "0.00");
        directionalSplatterStrength = RuntimeFloatSlider("Directional Splatter", directionalSplatterStrength, 0f, 1f, "0.00");
    }

    private void DrawRuntimeOptimizationSettings()
    {
        usePaintBudget = GUILayout.Toggle(usePaintBudget, "Use Paint Budget");
        maxPaintStampsPerFrame = RuntimeIntSlider("Max Paint Stamps / Frame", maxPaintStampsPerFrame, 1, 200);
        useDirtyRectUpload = GUILayout.Toggle(useDirtyRectUpload, "Use Dirty Rect Upload");
        textureApplyInterval = RuntimeFloatSlider("Texture Apply Interval", textureApplyInterval, 0f, 0.2f, "0.000");
        maxBrushRadiusPixels = RuntimeIntSlider("Max Brush Radius Pixels", maxBrushRadiusPixels, 1, 128);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Safe CPU", GUILayout.Height(24f)))
        {
            usePaintBudget = true;
            maxPaintStampsPerFrame = 20;
            useDirtyRectUpload = true;
            textureApplyInterval = 0.033f;
            maxBrushRadiusPixels = 14;
        }

        if (GUILayout.Button("Quality", GUILayout.Height(24f)))
        {
            usePaintBudget = true;
            maxPaintStampsPerFrame = 60;
            useDirtyRectUpload = true;
            textureApplyInterval = 0.016f;
            maxBrushRadiusPixels = 24;
        }
        GUILayout.EndHorizontal();
    }

    private void DrawRuntimeCorrectionSettings()
    {
        flipSplatterDirectionX = GUILayout.Toggle(flipSplatterDirectionX, "Flip Splatter Direction X");
        flipSplatterDirectionY = GUILayout.Toggle(flipSplatterDirectionY, "Flip Splatter Direction Y");
        usePlaneProjectedSplatterDirection = GUILayout.Toggle(usePlaneProjectedSplatterDirection, "Use Plane Projected Splatter Direction");

        GUILayout.Space(4f);
        flipCanvasTextureX = GUILayout.Toggle(flipCanvasTextureX, "Flip Canvas Texture X");
        flipCanvasTextureY = GUILayout.Toggle(flipCanvasTextureY, "Flip Canvas Texture Y");
        swapCanvasTextureAxes = GUILayout.Toggle(swapCanvasTextureAxes, "Swap Canvas Texture Axes");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Normal Mapping", GUILayout.Height(24f)))
        {
            flipCanvasTextureX = false;
            flipCanvasTextureY = false;
            swapCanvasTextureAxes = false;
            flipSplatterDirectionX = false;
            flipSplatterDirectionY = false;
        }

        if (GUILayout.Button("Apply + Clear", GUILayout.Height(24f)))
        {
            ClearCanvas();
        }
        GUILayout.EndHorizontal();
    }

    private bool RuntimeSection(string title, bool expanded)
    {
        GUILayout.Space(7f);
        return GUILayout.Toggle(expanded, (expanded ? "▼ " : "▶ ") + title, GUI.skin.button);
    }

    private float RuntimeFloatSlider(string label, float value, float min, float max, string format)
    {
        GUILayout.Label(label + ": " + value.ToString(format));
        return GUILayout.HorizontalSlider(Mathf.Clamp(value, min, max), min, max);
    }

    private int RuntimeIntSlider(string label, int value, int min, int max)
    {
        GUILayout.Label(label + ": " + value);
        return Mathf.RoundToInt(GUILayout.HorizontalSlider(Mathf.Clamp(value, min, max), min, max));
    }

    private Color RuntimeColorSliders(string title, Color color, bool includeAlpha)
    {
        GUILayout.Space(4f);
        GUILayout.Label(title);
        color.r = RuntimeFloatSlider("R", color.r, 0f, 1f, "0.00");
        color.g = RuntimeFloatSlider("G", color.g, 0f, 1f, "0.00");
        color.b = RuntimeFloatSlider("B", color.b, 0f, 1f, "0.00");
        if (includeAlpha)
        {
            color.a = RuntimeFloatSlider("A", color.a, 0f, 1f, "0.00");
        }
        return color;
    }


    private void OnValidate()
    {
        ApplySurfacePresetIfNeeded();
        ClampSurfaceSettings();
        textureWidth = Mathf.Max(64, textureWidth);
        textureHeight = Mathf.Max(64, textureHeight);
        canvasWidth = Mathf.Max(0.01f, canvasWidth);
        canvasLength = Mathf.Max(0.01f, canvasLength);
        maxBrushRadiusPixels = Mathf.Max(1, maxBrushRadiusPixels);
        maxPaintStampsPerFrame = Mathf.Max(1, maxPaintStampsPerFrame);
    }
}

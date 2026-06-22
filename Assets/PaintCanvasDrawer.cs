using UnityEngine;

public class PaintCanvasDrawer : MonoBehaviour
{
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

        float lowViscosity01 = 1f - viscosity01;

        // High speed => larger spot.
        // High viscosity => heavier spot with less spread.
        // High impact angle => stretched / oval mark.
        float speedRadiusFactor = Mathf.Lerp(0.75f, highSpeedSpotMultiplier, speed01);
        float viscosityRadiusFactor = Mathf.Lerp(1.35f, 0.75f, viscosity01);
        float finalWorldRadius = worldRadius * baseSpotRadiusMultiplier * speedRadiusFactor * viscosityRadiusFactor;

        int baseRadiusPixels = Mathf.Clamp(WorldRadiusToPixels(finalWorldRadius), 1, maxBrushRadiusPixels);

        int majorRadius = Mathf.Max(
            1,
            Mathf.RoundToInt(baseRadiusPixels * Mathf.Lerp(1f, angledSpotStretch, angle01))
        );

        int minorRadius = Mathf.Max(
            1,
            Mathf.RoundToInt(baseRadiusPixels * Mathf.Lerp(1f, 0.55f, angle01))
        );

        Vector2 pixelDirection = WorldDirectionToPixelDirection(impactDirectionWorld);
        DrawEllipse(centerX, centerY, majorRadius, minorRadius, pixelDirection, paintColor, brushOpacity);

        // High speed + low viscosity => more and farther splatter.
        // High viscosity => fewer, closer, heavier droplets.
        int dynamicSplatterCount = Mathf.RoundToInt(
            splatterCount *
            Mathf.Lerp(0.4f, 2.0f, speed01) *
            Mathf.Lerp(1.6f, 0.35f, viscosity01) *
            Mathf.Lerp(0.8f, 1.4f, angle01)
        );

        dynamicSplatterCount = Mathf.Max(0, dynamicSplatterCount);

        int maxSplatterDistance = Mathf.Max(
            1,
            Mathf.RoundToInt(
                baseRadiusPixels *
                splatterDistanceMultiplier *
                Mathf.Lerp(0.7f, 1.9f, speed01) *
                Mathf.Lerp(1.5f, 0.55f, viscosity01) *
                Mathf.Lerp(0.8f, 1.5f, angle01)
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

        Vector2 perpendicular = new Vector2(-pixelDirection.y, pixelDirection.x);
        float directionality = directionalSplatterStrength * Mathf.Clamp01(speed01 + angle01 * 0.5f);

        for (int i = 0; i < dynamicSplatterCount; i++)
        {
            float distance = Random.Range(baseRadiusPixels, maxSplatterDistance + 1f);

            Vector2 randomOffset = Random.insideUnitCircle * distance;

            float sideSpread = Random.Range(-0.45f, 0.45f) * maxSplatterDistance;
            Vector2 directedOffset = pixelDirection * distance + perpendicular * sideSpread;

            Vector2 finalOffset = Vector2.Lerp(randomOffset, directedOffset, directionality);

            int splatterX = centerX + Mathf.RoundToInt(finalOffset.x);
            int splatterY = centerY + Mathf.RoundToInt(finalOffset.y);

            int splatterRadius = Random.Range(minSplatterRadius, maxSplatterRadius + 1);

            float splatterOpacity =
                brushOpacity *
                Random.Range(0.22f, 0.68f) *
                Mathf.Lerp(1.12f, 0.65f, viscosity01) *
                Mathf.Lerp(0.85f, 1.1f, lowViscosity01);

            DrawCircle(splatterX, splatterY, splatterRadius, paintColor, splatterOpacity);
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

        float innerNorm = Mathf.Clamp01(1f - softEdge);

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

                if (softEdge > 0f && normalizedDistance > innerNorm)
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
        float innerRadius = radiusPixels * (1f - softEdge);
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
        Color blended = Color.Lerp(oldColor, paintColor, opacity);
        blended.a = 1f;
        pixels[index] = blended;
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
}

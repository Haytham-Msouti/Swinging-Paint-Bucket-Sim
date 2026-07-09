using UnityEngine;

public class scriptForb : MonoBehaviour
{
    [SerializeField] Transform center;
    [SerializeField] Transform upCenter;
    [SerializeField] Transform side;
    [SerializeField] Transform ball;
    [SerializeField] Transform ropeAxis1;
    [SerializeField] Transform ropeAxis2;

    [Header("Optional Rope Visuals")]
    [SerializeField] Transform ropeVisual1;
    [SerializeField] Transform ropeVisual2;

    [Header("Physics")]
    [SerializeField] float length = 0.3f;
    [SerializeField] float dragCoeff = 0.1f;
    [SerializeField] float dt = 0.02f;
    [SerializeField] float mass = 10f;
    [SerializeField] float g = 9.81f;

    [Header("Very Cheap Rope Stretch")]
    [SerializeField] float stretchPower = 0.015f;
    [SerializeField] float maxStretch = 1.18f;
    [SerializeField] float stretchReturnSpeed = 0.08f;

    [Header("Very Cheap Rope Twist")]
    [SerializeField] float twistPower = 15f;

    [Header("Runtime Interface")]
    [SerializeField] bool showRuntimeInterface = true;
    [SerializeField] bool showMiniToggleButton = true;
    [SerializeField] bool showRuntimeStats = true;
    [SerializeField] bool showPhysicsSettings = true;
    [SerializeField] bool showRopeSettings = true;
    [SerializeField] bool showVisualSettings = true;
    [SerializeField] bool pausePhysicsFromUI = false;

    [Tooltip("Only pauses the visual rope stretch/twist. It does not pause the pendulum physics.")]
    [SerializeField] bool pauseRopeVisualsFromUI = false;

    [Tooltip("If ON, the optional rope visual objects rotate around themselves for a cheap twist effect.")]
    [SerializeField] bool enableVisualTwist = true;

    [Tooltip("If ON, the rope visual stretches on its local Y axis.")]
    [SerializeField] bool enableVisualStretch = true;

    [SerializeField] bool showRopeVisual1 = true;
    [SerializeField] bool showRopeVisual2 = true;

    [Header("Runtime Interface Layout")]
    [SerializeField] Rect runtimeWindowRect = new Rect(12f, 12f, 390f, 620f);
    [SerializeField] Rect miniButtonRect = new Rect(12f, 12f, 120f, 32f);
    [SerializeField, Range(0.8f, 1.35f)] float runtimeUIScale = 1f;

    [Header("Runtime Interface Colors")]
    [SerializeField] Color uiBackgroundColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] Color uiTextColor = Color.white;
    [SerializeField] Color uiAccentColor = new Color(0.3f, 0.8f, 1f, 1f);

    Vector3 axis;
    Vector3 referenceDir;

    Vector3 angularVel;
    Vector3 angularAcc;
    Vector3 angularPos;
    Vector3 forces;

    Vector3 rope1BaseScale = Vector3.one;
    Vector3 rope2BaseScale = Vector3.one;

    float currentStretch = 1f;

    Transform rope1Target;
    Transform rope2Target;

    Vector2 uiScroll;
    bool referencesReady;

    GUIStyle headerStyle;
    GUIStyle smallLabelStyle;
    GUIStyle valueStyle;
    Texture2D uiBackgroundTexture;

    void Start()
    {
        InitializeReferences();
    }

    void Update()
    {
        if (!referencesReady)
        {
            InitializeReferences();
            if (!referencesReady)
            {
                return;
            }
        }

        // نفس خوارزمية الحركة تمامًا، فقط صار في خيار Pause من الواجهة
        if (!pausePhysicsFromUI)
        {
            UpdatePhysics();
        }

        // نفس الحركة القديمة
        if (ropeAxis1 != null)
        {
            ropeAxis1.rotation = Quaternion.Euler(new Vector3(angularPos.x, 0f, 0f));
        }

        if (ropeAxis2 != null)
        {
            ropeAxis2.localRotation = Quaternion.Euler(new Vector3(0f, 0f, angularPos.z));
        }

        // تمدد ودوران بصري خفيف جدًا
        if (!pauseRopeVisualsFromUI)
        {
            UpdateRopeVisualFast();
        }

        UpdateRuntimeVisualVisibility();
    }

    void InitializeReferences()
    {
        referencesReady =
            center != null &&
            upCenter != null &&
            side != null &&
            ball != null &&
            ropeAxis1 != null &&
            ropeAxis2 != null;

        if (!referencesReady)
        {
            return;
        }

        axis = upCenter.position - center.position;
        referenceDir = side.position - center.position;

        if (axis.sqrMagnitude < 0.000001f)
        {
            axis = Vector3.up;
        }

        if (referenceDir.sqrMagnitude < 0.000001f)
        {
            referenceDir = Vector3.forward;
        }

        // إذا عندك مجسم حبل منفصل حطيه هون
        // إذا ما حطيتي شي، رح يستخدم ropeAxis نفسه
        rope1Target = ropeVisual1 != null ? ropeVisual1 : ropeAxis1;
        rope2Target = ropeVisual2 != null ? ropeVisual2 : ropeAxis2;

        if (rope1Target != null)
        {
            rope1BaseScale = rope1Target.localScale;
        }

        if (rope2Target != null)
        {
            rope2BaseScale = rope2Target.localScale;
        }
    }

    void UpdatePhysics()
    {
        float epsilon = GetEpsilon() * Mathf.Deg2Rad;
        float phi = GetPhi() * Mathf.Deg2Rad;

        float safeLength = Mathf.Max(0.0001f, length);
        float safeMass = Mathf.Max(0.0001f, mass);
        float safeDt = Mathf.Max(0.000001f, dt);

        float gravity = -safeMass * g * Mathf.Sin(phi);

        Vector3 movingForce = new Vector3(
            gravity * Mathf.Cos(epsilon),
            0f,
            gravity * -Mathf.Sin(epsilon)
        );

        Vector3 dragForce = -dragCoeff * angularVel;

        forces = movingForce + dragForce;

        angularAcc = forces / (safeMass * safeLength);
        angularVel += angularAcc * safeDt;
        angularPos += angularVel * safeDt;
    }

    void UpdateRopeVisualFast()
    {
        if (rope1Target == null || rope2Target == null)
        {
            return;
        }

        // حساب رخيص جدًا بدون Distance وبدون LookAt
        float movementAmount = Mathf.Abs(angularVel.x) + Mathf.Abs(angularVel.z);

        if (enableVisualStretch)
        {
            float targetStretch = 1f + movementAmount * stretchPower;

            if (targetStretch > maxStretch)
            {
                targetStretch = maxStretch;
            }

            // رجوع بسيط وناعم بدون Mathf.Exp
            currentStretch += (targetStretch - currentStretch) * stretchReturnSpeed;

            // ملاحظة: إذا الحبل عندك Cylinder عادي، غالبًا طوله على Y
            Vector3 s1 = rope1BaseScale;
            s1.y *= currentStretch;
            rope1Target.localScale = s1;

            Vector3 s2 = rope2BaseScale;
            s2.y *= currentStretch;
            rope2Target.localScale = s2;
        }
        else
        {
            currentStretch += (1f - currentStretch) * stretchReturnSpeed;
            rope1Target.localScale = rope1BaseScale;
            rope2Target.localScale = rope2BaseScale;
        }

        // دوران بصري خفيف جدًا فقط إذا في Rope Visual منفصل
        // ما مندوّر ropeAxis نفسه حتى ما نخرب الحركة
        if (enableVisualTwist)
        {
            float twist = movementAmount * twistPower * Time.deltaTime;

            if (ropeVisual1 != null)
            {
                ropeVisual1.Rotate(0f, twist, 0f, Space.Self);
            }

            if (ropeVisual2 != null)
            {
                ropeVisual2.Rotate(0f, twist, 0f, Space.Self);
            }
        }
    }

    void UpdateRuntimeVisualVisibility()
    {
        if (ropeVisual1 != null && ropeVisual1.gameObject.activeSelf != showRopeVisual1)
        {
            ropeVisual1.gameObject.SetActive(showRopeVisual1);
        }

        if (ropeVisual2 != null && ropeVisual2.gameObject.activeSelf != showRopeVisual2)
        {
            ropeVisual2.gameObject.SetActive(showRopeVisual2);
        }
    }

    float GetPhi()
    {
        if (ball == null || upCenter == null)
        {
            return 0f;
        }

        Vector3 ropeDir = ball.position - upCenter.position;
        if (ropeDir.sqrMagnitude < 0.000001f)
        {
            return 0f;
        }

        return Vector3.Angle(axis, ropeDir);
    }

    float GetEpsilon()
    {
        if (ball == null || upCenter == null)
        {
            return 0f;
        }

        Vector3 currentDir = ball.position - upCenter.position;

        Vector3 projectedReference = Vector3.ProjectOnPlane(referenceDir, axis);
        Vector3 projectedCurrent = Vector3.ProjectOnPlane(currentDir, axis);

        if (projectedReference.sqrMagnitude < 0.000001f || projectedCurrent.sqrMagnitude < 0.000001f)
        {
            return 0f;
        }

        float angle = Vector3.SignedAngle(
            projectedReference.normalized,
            projectedCurrent.normalized,
            axis
        );

        return angle < 0f ? angle + 360f : angle;
    }

    void ResetMotionRuntime()
    {
        angularVel = Vector3.zero;
        angularAcc = Vector3.zero;
        angularPos = Vector3.zero;
        forces = Vector3.zero;
        currentStretch = 1f;

        if (rope1Target != null)
        {
            rope1Target.localScale = rope1BaseScale;
        }

        if (rope2Target != null)
        {
            rope2Target.localScale = rope2BaseScale;
        }

        if (ropeAxis1 != null)
        {
            ropeAxis1.rotation = Quaternion.identity;
        }

        if (ropeAxis2 != null)
        {
            ropeAxis2.localRotation = Quaternion.identity;
        }
    }

    void CaptureCurrentRopeScaleAsBase()
    {
        if (rope1Target != null)
        {
            rope1BaseScale = rope1Target.localScale;
        }

        if (rope2Target != null)
        {
            rope2BaseScale = rope2Target.localScale;
        }

        currentStretch = 1f;
    }

    void OnGUI()
    {
        BuildRuntimeGUIStyles();

        if (showMiniToggleButton)
        {
            GUI.color = Color.white;
            if (GUI.Button(miniButtonRect, showRuntimeInterface ? "Hide Rope UI" : "Show Rope UI"))
            {
                showRuntimeInterface = !showRuntimeInterface;
            }
        }

        if (!showRuntimeInterface)
        {
            return;
        }

        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;

        if (Mathf.Abs(runtimeUIScale - 1f) > 0.001f)
        {
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(runtimeUIScale, runtimeUIScale, 1f));
        }

        runtimeWindowRect = GUI.Window(GetInstanceID(), runtimeWindowRect, DrawRuntimeSettingsWindow, "Pendulum Rope Runtime Settings");

        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    void DrawRuntimeSettingsWindow(int windowId)
    {
        GUI.color = Color.white;

        Rect backgroundRect = new Rect(0f, 0f, runtimeWindowRect.width, runtimeWindowRect.height);
        GUI.DrawTexture(backgroundRect, uiBackgroundTexture);

        GUILayout.BeginVertical();
        try
        {
            uiScroll = GUILayout.BeginScrollView(uiScroll, GUILayout.Width(runtimeWindowRect.width - 12f), GUILayout.Height(runtimeWindowRect.height - 42f));
            try
            {
                DrawMainControls();

                showRuntimeStats = DrawToggle("Show Stats", showRuntimeStats);
                if (showRuntimeStats)
                {
                    DrawStatsBlock();
                }

                showPhysicsSettings = DrawToggle("Physics Settings", showPhysicsSettings);
                if (showPhysicsSettings)
                {
                    DrawPhysicsBlock();
                }

                showRopeSettings = DrawToggle("Rope Stretch / Twist Settings", showRopeSettings);
                if (showRopeSettings)
                {
                    DrawRopeBlock();
                }

                showVisualSettings = DrawToggle("Visual Settings", showVisualSettings);
                if (showVisualSettings)
                {
                    DrawVisualBlock();
                }

                DrawInterfaceBlock();
            }
            finally
            {
                GUILayout.EndScrollView();
            }

            GUILayout.Space(4f);
            GUILayout.Label("اسحبي من هون لتحريك نافذة الإعدادات", smallLabelStyle);
            GUI.DragWindow(new Rect(0f, 0f, runtimeWindowRect.width, 24f));
        }
        finally
        {
            GUILayout.EndVertical();
        }
    }

    void DrawMainControls()
    {
        GUILayout.Label("Main Controls", headerStyle);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(pausePhysicsFromUI ? "Play Physics" : "Pause Physics"))
        {
            pausePhysicsFromUI = !pausePhysicsFromUI;
        }

        if (GUILayout.Button(pauseRopeVisualsFromUI ? "Play Rope Visual" : "Pause Rope Visual"))
        {
            pauseRopeVisualsFromUI = !pauseRopeVisualsFromUI;
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Motion"))
        {
            ResetMotionRuntime();
        }

        if (GUILayout.Button("Capture Rope Scale"))
        {
            CaptureCurrentRopeScaleAsBase();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
    }

    void DrawStatsBlock()
    {
        GUILayout.Label("Live Values", headerStyle);

        DrawReadOnly("Phi Angle", GetPhi().ToString("0.00") + " deg");
        DrawReadOnly("Epsilon Angle", GetEpsilon().ToString("0.00") + " deg");
        DrawReadOnly("Angular Vel", angularVel.ToString("F3"));
        DrawReadOnly("Angular Acc", angularAcc.ToString("F3"));
        DrawReadOnly("Angular Pos", angularPos.ToString("F3"));
        DrawReadOnly("Forces", forces.ToString("F3"));
        DrawReadOnly("Current Stretch", currentStretch.ToString("0.000"));

        GUILayout.Space(8f);
    }

    void DrawPhysicsBlock()
    {
        GUILayout.Label("Physics", headerStyle);

        length = DrawSlider("Length", length, 0.01f, 5f);
        dragCoeff = DrawSlider("Drag", dragCoeff, 0f, 5f);
        dt = DrawSlider("Delta Time", dt, 0.0005f, 0.08f);
        mass = DrawSlider("Mass", mass, 0.01f, 100f);
        g = DrawSlider("Gravity", g, 0f, 25f);

        if (GUILayout.Button("Soft Physics Preset"))
        {
            length = 0.3f;
            dragCoeff = 0.1f;
            dt = 0.02f;
            mass = 10f;
            g = 9.81f;
        }

        GUILayout.Space(8f);
    }

    void DrawRopeBlock()
    {
        GUILayout.Label("Cheap Rope Visuals", headerStyle);

        enableVisualStretch = DrawToggle("Enable Visual Stretch", enableVisualStretch);
        enableVisualTwist = DrawToggle("Enable Visual Twist", enableVisualTwist);

        stretchPower = DrawSlider("Stretch Power", stretchPower, 0f, 0.15f);
        maxStretch = DrawSlider("Max Stretch", maxStretch, 1f, 2.5f);
        stretchReturnSpeed = DrawSlider("Stretch Smooth/Return", stretchReturnSpeed, 0.001f, 1f);
        twistPower = DrawSlider("Twist Power", twistPower, 0f, 180f);

        fallingSafetyClamp();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Gentle Rope"))
        {
            stretchPower = 0.012f;
            maxStretch = 1.12f;
            stretchReturnSpeed = 0.07f;
            twistPower = 8f;
        }

        if (GUILayout.Button("Strong Rope"))
        {
            stretchPower = 0.025f;
            maxStretch = 1.28f;
            stretchReturnSpeed = 0.11f;
            twistPower = 22f;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
    }

    void DrawVisualBlock()
    {
        GUILayout.Label("Visibility", headerStyle);

        if (ropeVisual1 != null)
        {
            showRopeVisual1 = DrawToggle("Show Rope Visual 1", showRopeVisual1);
        }
        else
        {
            DrawReadOnly("Rope Visual 1", "Not Assigned - using RopeAxis1");
        }

        if (ropeVisual2 != null)
        {
            showRopeVisual2 = DrawToggle("Show Rope Visual 2", showRopeVisual2);
        }
        else
        {
            DrawReadOnly("Rope Visual 2", "Not Assigned - using RopeAxis2");
        }

        GUILayout.Space(8f);
    }

    void DrawInterfaceBlock()
    {
        GUILayout.Label("Interface", headerStyle);

        showMiniToggleButton = DrawToggle("Show Mini Toggle Button", showMiniToggleButton);
        runtimeUIScale = DrawSlider("UI Scale", runtimeUIScale, 0.8f, 1.35f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Small UI"))
        {
            runtimeWindowRect.width = 360f;
            runtimeWindowRect.height = 500f;
        }

        if (GUILayout.Button("Big UI"))
        {
            runtimeWindowRect.width = 430f;
            runtimeWindowRect.height = 680f;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
    }

    void DrawReadOnly(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, smallLabelStyle, GUILayout.Width(145f));
        GUILayout.Label(value, valueStyle);
        GUILayout.EndHorizontal();
    }

    float DrawSlider(string label, float value, float min, float max)
    {
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, smallLabelStyle, GUILayout.Width(160f));
        GUILayout.Label(value.ToString("0.###"), valueStyle, GUILayout.Width(75f));
        GUILayout.EndHorizontal();

        float newValue = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.EndVertical();

        return newValue;
    }

    bool DrawToggle(string label, bool value)
    {
        GUI.color = value ? Color.white : new Color(1f, 1f, 1f, 0.68f);
        bool newValue = GUILayout.Toggle(value, label);
        GUI.color = Color.white;
        return newValue;
    }

    void BuildRuntimeGUIStyles()
    {
        if (uiBackgroundTexture == null)
        {
            uiBackgroundTexture = new Texture2D(1, 1);
            uiBackgroundTexture.hideFlags = HideFlags.DontSave;
        }

        uiBackgroundTexture.SetPixel(0, 0, uiBackgroundColor);
        uiBackgroundTexture.Apply(false);

        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = 14;
            headerStyle.normal.textColor = uiAccentColor;
        }

        if (smallLabelStyle == null)
        {
            smallLabelStyle = new GUIStyle(GUI.skin.label);
            smallLabelStyle.fontSize = 12;
            smallLabelStyle.normal.textColor = uiTextColor;
        }

        if (valueStyle == null)
        {
            valueStyle = new GUIStyle(GUI.skin.label);
            valueStyle.fontSize = 12;
            valueStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 1f);
        }

        headerStyle.normal.textColor = uiAccentColor;
        smallLabelStyle.normal.textColor = uiTextColor;
        valueStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 1f);
    }

    void fallingSafetyClamp()
    {
        maxStretch = Mathf.Max(1f, maxStretch);
        stretchReturnSpeed = Mathf.Clamp(stretchReturnSpeed, 0.001f, 1f);
        stretchPower = Mathf.Max(0f, stretchPower);
        twistPower = Mathf.Max(0f, twistPower);
    }

    void OnValidate()
    {
        length = Mathf.Max(0.0001f, length);
        mass = Mathf.Max(0.0001f, mass);
        dt = Mathf.Max(0.000001f, dt);
        dragCoeff = Mathf.Max(0f, dragCoeff);
        g = Mathf.Max(0f, g);

        stretchPower = Mathf.Max(0f, stretchPower);
        maxStretch = Mathf.Max(1f, maxStretch);
        stretchReturnSpeed = Mathf.Clamp(stretchReturnSpeed, 0.001f, 1f);
        twistPower = Mathf.Max(0f, twistPower);

        runtimeUIScale = Mathf.Clamp(runtimeUIScale, 0.8f, 1.35f);
        runtimeWindowRect.width = Mathf.Max(280f, runtimeWindowRect.width);
        runtimeWindowRect.height = Mathf.Max(260f, runtimeWindowRect.height);
        miniButtonRect.width = Mathf.Max(80f, miniButtonRect.width);
        miniButtonRect.height = Mathf.Max(24f, miniButtonRect.height);
    }
}

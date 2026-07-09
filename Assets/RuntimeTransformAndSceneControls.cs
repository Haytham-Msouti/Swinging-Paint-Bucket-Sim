using UnityEngine;
using UnityEngine.EventSystems;

public class RuntimeTransformAndSceneControls : MonoBehaviour
{
    enum ToolMode
    {
        Move,
        Rotate,
        Scale
    }

    [Header("Camera")]
    [SerializeField] Camera sceneCamera;
    [SerializeField] float orbitSpeed = 4f;
    [SerializeField] float panSpeed = 0.01f;
    [SerializeField] float zoomSpeed = 5f;
    [SerializeField] float minZoom = 1f;
    [SerializeField] float maxZoom = 80f;

    [Header("Selection")]
    [SerializeField] LayerMask selectableLayers = ~0;
    [SerializeField] Material selectedMaterial;

    [Header("Object Control")]
    [SerializeField] float rotateSpeed = 0.4f;
    [SerializeField] float scaleSpeed = 0.01f;
    [SerializeField] float keyboardMoveSpeed = 2f;
    [SerializeField] float keyboardRotateSpeed = 90f;
    [SerializeField] float keyboardScaleSpeed = 1f;
    [SerializeField] float minScale = 0.05f;

    ToolMode currentMode = ToolMode.Move;

    Transform selectedObject;
    Renderer selectedRenderer;
    Material oldMaterial;

    Plane movePlane;
    Vector3 dragOffset;
    Vector3 lastMousePos;
    bool isDraggingObject;

    Vector3 cameraPivot = Vector3.zero;

    void Start()
    {
        if (sceneCamera == null)
            sceneCamera = Camera.main;
    }

    void Update()
    {
        HandleModeKeys();
        HandleSelectionAndObjectMouseControl();
        HandleKeyboardTransformControl();
        HandleCameraControl();
    }

    void HandleModeKeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            currentMode = ToolMode.Move;

        if (Input.GetKeyDown(KeyCode.Alpha2))
            currentMode = ToolMode.Rotate;

        if (Input.GetKeyDown(KeyCode.Alpha3))
            currentMode = ToolMode.Scale;

        if (Input.GetKeyDown(KeyCode.Escape))
            ClearSelection();
    }

    void HandleSelectionAndObjectMouseControl()
    {
        if (IsPointerOverUI())
            return;

        if (Input.GetMouseButtonDown(0))
        {
            TrySelectObject();

            if (selectedObject != null)
                BeginObjectDrag();
        }

        if (Input.GetMouseButton(0) && isDraggingObject && selectedObject != null)
        {
            DragSelectedObject();
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDraggingObject = false;
        }
    }

    void TrySelectObject()
    {
        Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, selectableLayers))
        {
            SetSelectedObject(hit.transform);
        }
    }

    void SetSelectedObject(Transform obj)
    {
        ClearSelection();

        selectedObject = obj;
        cameraPivot = selectedObject.position;

        selectedRenderer = selectedObject.GetComponent<Renderer>();

        if (selectedRenderer != null && selectedMaterial != null)
        {
            oldMaterial = selectedRenderer.material;
            selectedRenderer.material = selectedMaterial;
        }
    }

    void ClearSelection()
    {
        if (selectedRenderer != null && oldMaterial != null)
            selectedRenderer.material = oldMaterial;

        selectedObject = null;
        selectedRenderer = null;
        oldMaterial = null;
    }

    void BeginObjectDrag()
    {
        isDraggingObject = true;
        lastMousePos = Input.mousePosition;

        if (currentMode == ToolMode.Move)
        {
            movePlane = new Plane(Vector3.up, selectedObject.position);

            Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);

            if (movePlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                dragOffset = selectedObject.position - hitPoint;
            }
        }
    }

    void DragSelectedObject()
    {
        Vector3 mouseDelta = Input.mousePosition - lastMousePos;

        if (currentMode == ToolMode.Move)
        {
            MoveObjectWithMouse();
        }
        else if (currentMode == ToolMode.Rotate)
        {
            RotateObjectWithMouse(mouseDelta);
        }
        else if (currentMode == ToolMode.Scale)
        {
            ScaleObjectWithMouse(mouseDelta);
        }

        lastMousePos = Input.mousePosition;
        cameraPivot = selectedObject.position;
    }

    void MoveObjectWithMouse()
    {
        Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);

        if (movePlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector3 newPos = hitPoint + dragOffset;

            if (Input.GetKey(KeyCode.LeftShift))
            {
                float verticalMove = (Input.mousePosition.y - lastMousePos.y) * 0.01f;
                newPos = selectedObject.position + Vector3.up * verticalMove;
            }

            selectedObject.position = newPos;
        }
    }

    void RotateObjectWithMouse(Vector3 mouseDelta)
    {
        Vector3 axis = Vector3.up;

        if (Input.GetKey(KeyCode.X))
            axis = Vector3.right;
        else if (Input.GetKey(KeyCode.Y))
            axis = Vector3.up;
        else if (Input.GetKey(KeyCode.Z))
            axis = Vector3.forward;

        selectedObject.Rotate(axis, -mouseDelta.x * rotateSpeed, Space.World);
    }

    void ScaleObjectWithMouse(Vector3 mouseDelta)
    {
        float amount = mouseDelta.y * scaleSpeed;
        Vector3 newScale = selectedObject.localScale + Vector3.one * amount;

        newScale.x = Mathf.Max(newScale.x, minScale);
        newScale.y = Mathf.Max(newScale.y, minScale);
        newScale.z = Mathf.Max(newScale.z, minScale);

        selectedObject.localScale = newScale;
    }

    void HandleKeyboardTransformControl()
    {
        if (selectedObject == null)
            return;

        float dt = Time.deltaTime;

        if (currentMode == ToolMode.Move)
        {
            Vector3 move = Vector3.zero;

            if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move += Vector3.back;
            if (Input.GetKey(KeyCode.A)) move += Vector3.left;
            if (Input.GetKey(KeyCode.D)) move += Vector3.right;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move += Vector3.down;

            selectedObject.position += move * keyboardMoveSpeed * dt;
        }

        if (currentMode == ToolMode.Rotate)
        {
            if (Input.GetKey(KeyCode.X))
                selectedObject.Rotate(Vector3.right, keyboardRotateSpeed * dt, Space.World);

            if (Input.GetKey(KeyCode.Y))
                selectedObject.Rotate(Vector3.up, keyboardRotateSpeed * dt, Space.World);

            if (Input.GetKey(KeyCode.Z))
                selectedObject.Rotate(Vector3.forward, keyboardRotateSpeed * dt, Space.World);
        }

        if (currentMode == ToolMode.Scale)
        {
            if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus))
                selectedObject.localScale += Vector3.one * keyboardScaleSpeed * dt;

            if (Input.GetKey(KeyCode.Minus))
            {
                Vector3 newScale = selectedObject.localScale - Vector3.one * keyboardScaleSpeed * dt;

                newScale.x = Mathf.Max(newScale.x, minScale);
                newScale.y = Mathf.Max(newScale.y, minScale);
                newScale.z = Mathf.Max(newScale.z, minScale);

                selectedObject.localScale = newScale;
            }
        }

        cameraPivot = selectedObject.position;
    }

    void HandleCameraControl()
    {
        if (sceneCamera == null)
            return;

        Transform cam = sceneCamera.transform;

        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X") * orbitSpeed;
            float mouseY = -Input.GetAxis("Mouse Y") * orbitSpeed;

            cam.RotateAround(cameraPivot, Vector3.up, mouseX);
            cam.RotateAround(cameraPivot, cam.right, mouseY);
        }

        if (Input.GetMouseButton(2))
        {
            float mouseX = -Input.GetAxis("Mouse X") * panSpeed;
            float mouseY = -Input.GetAxis("Mouse Y") * panSpeed;

            Vector3 pan = cam.right * mouseX + cam.up * mouseY;
            cam.position += pan;
            cameraPivot += pan;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0.001f)
        {
            Vector3 direction = cam.forward;
            float distanceToPivot = Vector3.Distance(cam.position, cameraPivot);

            if ((scroll > 0 && distanceToPivot > minZoom) ||
                (scroll < 0 && distanceToPivot < maxZoom))
            {
                cam.position += direction * scroll * zoomSpeed;
            }
        }
    }

    bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
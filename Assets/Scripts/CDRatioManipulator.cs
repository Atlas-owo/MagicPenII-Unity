using UnityEngine;

/// <summary>
/// Helper class for applying Control/Display (C/D) ratios to NURBS surfaces and objects.
/// C/D ratio manipulates the relationship between reference and target stimuli.
/// </summary>
public class CDRatioManipulator : MonoBehaviour
{
    [Header("NURBS Surface References")]
    [Tooltip("Reference NURBS surface (source)")]
    [SerializeField] private NURBSSurface referenceSurface;

    [Tooltip("Target NURBS surface (will be modified based on ratio)")]
    [SerializeField] private NURBSSurface targetSurface;

    [Header("Object References")]
    [Tooltip("Reference object O1 (source)")]
    [SerializeField] private GameObject referenceObject;

    [Tooltip("Target object O2 (will be positioned based on ratio)")]
    [SerializeField] private GameObject targetObject;

    [Tooltip("Reference plane for measuring distances")]
    [SerializeField] private GameObject referencePlane;
    [Range(0f, 0.01f)] [SerializeField] private float distanceOffset = 0.001f;


    [Header("Current Ratio")]
    [Tooltip("Current C/D ratio being applied")]
    [SerializeField] private float currentRatio = 1.0f;

    [Header("Continuous Update Settings")]
    [Tooltip("Automatically apply ratio every frame")]
    [SerializeField] private bool enableContinuousUpdate = true;

    [Tooltip("Enable NURBS manipulation")]
    [SerializeField] private bool manipulateNURBS = true;

    [Tooltip("Enable object manipulation")]
    [SerializeField] private bool manipulateObjects = true;

    [Header("Visibility Settings")]
    [Tooltip("Show reference surface mesh renderer")]
    [SerializeField] private bool showReferenceSurface = true;

    [Tooltip("Show reference object mesh renderer")]
    [SerializeField] private bool showReferenceObject = true;

    [Header("Debug Settings")]
    [Tooltip("Log manipulation details to console")]
    [SerializeField] private bool verboseLogging = false;

    private bool previousShowReferenceSurface = true;
    private bool previousShowReferenceObject = true;

    private float previousReferenceAmplitude = 0f;
    private Vector3 previousReferenceObjectPosition = Vector3.zero;
    private Quaternion previousReferenceObjectRotation = Quaternion.identity;
    private float previousRatio = 1.0f;

    void Start()
    {
        // Initialize previous values
        if (referenceSurface != null)
        {
            previousReferenceAmplitude = referenceSurface.GetCurrentHeight();
        }

        if (referenceObject != null)
        {
            previousReferenceObjectPosition = referenceObject.transform.position;
            previousReferenceObjectRotation = referenceObject.transform.rotation;
        }

        previousRatio = currentRatio;

        // Initialize visibility states
        previousShowReferenceSurface = showReferenceSurface;
        previousShowReferenceObject = showReferenceObject;

        // Apply initial visibility
        UpdateVisibility();

        // Apply initial ratio
        if (enableContinuousUpdate)
        {
            if (manipulateNURBS && referenceSurface != null && targetSurface != null)
            {
                ApplyRatioToNURBS(currentRatio);
            }

            if (manipulateObjects && referenceObject != null && targetObject != null)
            {
                ApplyRatioToObjects(currentRatio);
            }
        }
    }

    void LateUpdate()
    {
        // Continuously apply ratio if enabled
        if (enableContinuousUpdate)
        {
            ApplyRatioContinuous();
        }

        // Check for visibility changes
        if (showReferenceSurface != previousShowReferenceSurface ||
            showReferenceObject != previousShowReferenceObject)
        {
            UpdateVisibility();
            previousShowReferenceSurface = showReferenceSurface;
            previousShowReferenceObject = showReferenceObject;
        }
    }

    /// <summary>
    /// Continuously apply C/D ratio, called every frame in LateUpdate.
    /// Only updates when reference values or ratio changes to avoid unnecessary mesh regeneration.
    /// </summary>
    private void ApplyRatioContinuous()
    {
        bool ratioChanged = !Mathf.Approximately(currentRatio, previousRatio);

        // Check if NURBS reference amplitude changed OR ratio changed
        if (manipulateNURBS && referenceSurface != null && targetSurface != null)
        {
            float currentAmplitude = referenceSurface.GetCurrentHeight();
            bool amplitudeChanged = !Mathf.Approximately(currentAmplitude, previousReferenceAmplitude);

            if (amplitudeChanged || ratioChanged)
            {
                ApplyRatioToNURBS(currentRatio);
                previousReferenceAmplitude = currentAmplitude;
            }
        }

        // Check if reference object position/rotation changed OR ratio changed
        if (manipulateObjects && referenceObject != null && targetObject != null)
        {
            Vector3 currentPosition = referenceObject.transform.position;
            Quaternion currentRotation = referenceObject.transform.rotation;

            bool positionChanged = Vector3.Distance(currentPosition, previousReferenceObjectPosition) > 0.0001f;
            bool rotationChanged = Quaternion.Angle(currentRotation, previousReferenceObjectRotation) > 0.01f;

            if (positionChanged || rotationChanged || ratioChanged)
            {
                ApplyRatioToObjects(currentRatio);
                previousReferenceObjectPosition = currentPosition;
                previousReferenceObjectRotation = currentRotation;
            }
        }

        // Update previous ratio
        if (ratioChanged)
        {
            previousRatio = currentRatio;
        }
    }

    /// <summary>
    /// Set C/D ratio (will be applied continuously if enableContinuousUpdate is true).
    /// </summary>
    /// <param name="ratio">C/D ratio to apply</param>
    public void SetRatio(float ratio)
    {
        currentRatio = ratio;

        // Force immediate update when ratio changes
        if (manipulateNURBS)
        {
            ApplyRatioToNURBS(ratio);
        }

        if (manipulateObjects)
        {
            ApplyRatioToObjects(ratio);
        }
    }

    /// <summary>
    /// Apply C/D ratio to both NURBS surfaces and objects (legacy method, still available).
    /// </summary>
    /// <param name="ratio">C/D ratio to apply</param>
    public void ApplyRatio(float ratio)
    {
        SetRatio(ratio);
    }

    /// <summary>
    /// Apply C/D ratio to NURBS surfaces only.
    /// Reads amplitude a1 from reference surface, sets target surface to a2 = a1 * ratio.
    /// </summary>
    /// <param name="ratio">C/D ratio to apply</param>
    public void ApplyRatioToNURBS(float ratio)
    {
        if (referenceSurface == null || targetSurface == null)
        {
            if (verboseLogging)
            {
                Debug.LogWarning("CDRatioManipulator: Reference or target NURBS surface not assigned.");
            }
            return;
        }

        // Read amplitude from reference surface
        float a1 = referenceSurface.GetCurrentHeight();

        // Calculate new amplitude with ratio
        float a2 = a1 * ratio;

        // Set target surface amplitude
        targetSurface.SetHeight(a2);

        if (verboseLogging)
        {
            Debug.Log($"CDRatioManipulator: Applied NURBS ratio {ratio:F3} - Reference: {a1:F4}m, Target: {a2:F4}m");
        }
    }

    /// <summary>
    /// Apply C/D ratio to objects only.
    /// Calculates distance d1 from O1 to plane, positions O2 such that d2 = d1 * ratio.
    /// O2 maintains same X and Z coordinates as O1, only Y changes.
    /// O2 rotation matches O1 rotation exactly.
    /// </summary>
    /// <param name="ratio">C/D ratio to apply</param>
    public void ApplyRatioToObjects(float ratio)
    {
        if (referenceObject == null || targetObject == null || referencePlane == null)
        {
            if (verboseLogging)
            {
                Debug.LogWarning("CDRatioManipulator: Reference object, target object, or plane not assigned.");
            }
            return;
        }

        // Get plane's Y position (assuming plane is horizontal)
        float planeY = referencePlane.transform.position.y;

        // Calculate vertical distance d1 from O1 to plane
        float d1 = referenceObject.transform.position.y-distanceOffset - planeY;

        // Calculate new distance d2 = d1 * ratio
        float d2 = d1 * ratio;

        // Position O2: same X and Z as O1, Y based on ratio
        Vector3 newPosition = new Vector3(
            referenceObject.transform.position.x,  // Same X
            planeY + d2,                           // Y based on ratio
            referenceObject.transform.position.z   // Same Z
        );

        targetObject.transform.position = newPosition;

        // Copy rotation from reference object to target object
        targetObject.transform.rotation = referenceObject.transform.rotation;

        if (verboseLogging)
        {
            Debug.Log($"CDRatioManipulator: Applied object ratio {ratio:F3} - O1 distance: {d1:F4}m, O2 distance: {d2:F4}m, rotation: {referenceObject.transform.rotation.eulerAngles}");
        }
    }

    /// <summary>
    /// Set the reference NURBS surface.
    /// </summary>
    public void SetReferenceSurface(NURBSSurface surface)
    {
        referenceSurface = surface;
    }

    /// <summary>
    /// Set the target NURBS surface.
    /// </summary>
    public void SetTargetSurface(NURBSSurface surface)
    {
        targetSurface = surface;
    }

    /// <summary>
    /// Set the reference object O1.
    /// </summary>
    public void SetReferenceObject(GameObject obj)
    {
        referenceObject = obj;
    }

    /// <summary>
    /// Set the target object O2.
    /// </summary>
    public void SetTargetObject(GameObject obj)
    {
        targetObject = obj;
    }

    /// <summary>
    /// Set the reference plane.
    /// </summary>
    public void SetReferencePlane(GameObject plane)
    {
        referencePlane = plane;
    }

    /// <summary>
    /// Get the current C/D ratio.
    /// </summary>
    public float GetCurrentRatio()
    {
        return currentRatio;
    }

    /// <summary>
    /// Update visibility of reference surface and object mesh renderers.
    /// </summary>
    private void UpdateVisibility()
    {
        // Update reference surface visibility
        if (referenceSurface != null)
        {
            MeshRenderer surfaceRenderer = referenceSurface.GetComponent<MeshRenderer>();
            if (surfaceRenderer != null)
            {
                surfaceRenderer.enabled = showReferenceSurface;
            }
        }

        // Update reference object visibility
        if (referenceObject != null)
        {
            MeshRenderer[] objectRenderers = referenceObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in objectRenderers)
            {
                renderer.enabled = showReferenceObject;
            }
        }
    }

    /// <summary>
    /// Set visibility of reference surface.
    /// </summary>
    public void SetReferenceSurfaceVisibility(bool visible)
    {
        showReferenceSurface = visible;
        UpdateVisibility();
    }

    /// <summary>
    /// Set visibility of reference object.
    /// </summary>
    public void SetReferenceObjectVisibility(bool visible)
    {
        showReferenceObject = visible;
        UpdateVisibility();
    }

    /// <summary>
    /// Set visibility of target surface.
    /// </summary>
    public void SetTargetSurfaceVisibility(bool visible)
    {
        if (targetSurface != null)
        {
            MeshRenderer surfaceRenderer = targetSurface.GetComponent<MeshRenderer>();
            if (surfaceRenderer != null)
            {
                surfaceRenderer.enabled = visible;
            }
        }
    }

    /// <summary>
    /// Set enabled state of reference surface mesh collider.
    /// </summary>
    public void SetReferenceSurfaceCollider(bool enabled)
    {
        if (referenceSurface != null)
        {
            MeshCollider meshCollider = referenceSurface.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.enabled = enabled;
            }
        }
    }

    // Debug visualization in Scene view
    void OnDrawGizmos()
    {
        if (referenceObject != null && referencePlane != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 planePoint = new Vector3(
                referenceObject.transform.position.x,
                referencePlane.transform.position.y,
                referenceObject.transform.position.z
            );
            Gizmos.DrawLine(referenceObject.transform.position, planePoint);
        }

        if (targetObject != null && referencePlane != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 planePoint = new Vector3(
                targetObject.transform.position.x,
                referencePlane.transform.position.y,
                targetObject.transform.position.z
            );
            Gizmos.DrawLine(targetObject.transform.position, planePoint);
        }
    }
}

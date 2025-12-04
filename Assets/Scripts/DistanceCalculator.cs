using UnityEngine;

/// <summary>
/// Calculates distance from pen tip to surfaces and objects using raycasting.
/// Handles smoothing and excludes grasped objects from calculations.
/// </summary>
public class DistanceCalculator : MonoBehaviour
{
    [Header("Pen Reference")]
    public Transform penTip;

    [Header("Surface Reference")]
    public Transform surface; // The reference surface to measure distance to

    [Header("Distance Measurement")]
    public LayerMask surfaceLayerMask = 1;
    public float maxDistance = 1f;
    [Range(-1.0f, 1.0f)]
    public float distanceOffset = 0f;
    [Range(0.01f, 1.0f)]
    public float rayOriginOffset = 0.05f;

    [Header("Distance Smoothing")]
    public bool enableSmoothing = true;
    [Range(0.01f, 1.0f)]
    public float smoothingTime = 0.1f;

    [Header("Debug")]
    public bool showDebugRays = true;
    public bool logDistanceData = false;

    // Grasping system reference (optional)
    private HapticPenGraspingSystem graspingSystem;

    // Distance values
    private float distanceToSurface = 0f;
    private float distanceToObject = 0f;
    private float calculatedDistance = 0f;
    private float currentDistance = 0f;
    private float smoothedDistance = 0f;
    private float smoothVelocity = 0f;

    // Public properties
    public float DistanceToSurface => distanceToSurface;
    public float DistanceToObject => distanceToObject;
    public float CalculatedDistance => calculatedDistance;
    public float CurrentDistance => currentDistance;
    public float SmoothedDistance => smoothedDistance;

    /// <summary>
    /// Set the grasping system reference for excluding grasped objects.
    /// </summary>
    public void SetGraspingSystem(HapticPenGraspingSystem system)
    {
        graspingSystem = system;
    }

    /// <summary>
    /// Calculate distance from pen tip to surfaces/objects.
    /// Returns the smoothed distance value.
    /// </summary>
    /// <param name="pressureDistanceOffset">Offset to apply based on pressure state</param>
    /// <returns>Smoothed distance value</returns>
    public float Calculate(float pressureDistanceOffset = 0f)
    {
        if (penTip == null) return maxDistance;

        // Cast a ray from pen tip towards the surface
        Vector3 rayOrigin = penTip.position - (penTip.forward * rayOriginOffset);
        Vector3 rayDirection = penTip.forward;

        // Reset distance values
        distanceToSurface = maxDistance;
        distanceToObject = maxDistance;

        // Use RaycastAll to get all hits
        RaycastHit[] allHits = Physics.RaycastAll(rayOrigin, rayDirection, maxDistance, surfaceLayerMask);

        RaycastHit surfaceHit = new RaycastHit();
        RaycastHit objectHit = new RaycastHit();
        bool foundSurface = false;
        bool foundObject = false;
        float closestSurfaceDistance = maxDistance;
        float closestObjectDistance = maxDistance;

        foreach (RaycastHit hit in allHits)
        {
            // Check if this hit is from a grasped object - skip grasped objects
            bool isGraspedObject = false;
            if (graspingSystem != null && graspingSystem.IsGrasping)
            {
                if (graspingSystem.IsGraspedObjectCollider(hit.collider))
                {
                    isGraspedObject = true;
                }
            }

            if (isGraspedObject) continue;

            // Check if this hit is from the reference surface
            bool isSurface = false;
            if (surface != null && hit.transform == surface)
            {
                isSurface = true;
            }

            // If it's the surface and closer than previous surface hits
            if (isSurface && hit.distance < closestSurfaceDistance)
            {
                surfaceHit = hit;
                foundSurface = true;
                closestSurfaceDistance = hit.distance;
            }
            // If it's an object (not surface) and closer than previous object hits
            else if (!isSurface && hit.distance < closestObjectDistance)
            {
                objectHit = hit;
                foundObject = true;
                closestObjectDistance = hit.distance;
            }
        }

        // Update distance values
        if (foundSurface)
        {
            distanceToSurface = surfaceHit.distance + distanceOffset;
        }

        if (foundObject)
        {
            distanceToObject = objectHit.distance + distanceOffset;
        }

        // Calculate the final distance based on the logic
        if (foundObject && foundSurface)
        {
            // Both object and surface found
            float d_s = distanceToSurface;
            float d_o = distanceToObject;

            if (d_o < d_s) // Object is closer than surface
            {
                float difference = d_s - d_o;
                if (d_s <= difference)
                {
                    calculatedDistance = difference;
                }
                else
                {
                    calculatedDistance = d_s;
                }
            }
            else
            {
                // Object is farther than surface, use surface distance
                calculatedDistance = d_s;
            }
        }
        else if (foundSurface)
        {
            // Only surface found
            calculatedDistance = distanceToSurface;
        }
        else
        {
            // No valid hits found
            calculatedDistance = maxDistance + distanceOffset;
        }

        // Apply pressure offset to the calculated distance
        currentDistance = Mathf.Max(0, calculatedDistance - pressureDistanceOffset);

        // Apply distance smoothing
        if (enableSmoothing)
        {
            smoothedDistance = Mathf.SmoothDamp(smoothedDistance, currentDistance, ref smoothVelocity, smoothingTime);
        }
        else
        {
            smoothedDistance = currentDistance;
        }

        // Debug visualization
        if (showDebugRays)
        {
            if (foundSurface)
            {
                Debug.DrawRay(rayOrigin, rayDirection * surfaceHit.distance, Color.blue);
            }
            if (foundObject)
            {
                Debug.DrawRay(rayOrigin, rayDirection * objectHit.distance, Color.yellow);
            }
            if (!foundSurface && !foundObject)
            {
                Debug.DrawRay(rayOrigin, rayDirection * maxDistance, Color.red);
            }
        }

        if (logDistanceData)
        {
            string graspedInfo = (graspingSystem != null && graspingSystem.IsGrasping) ?
                $" (Ignoring grasped: {graspingSystem.GraspedObject.name})" : "";
            Debug.Log($"[DistanceCalculator] Distance: {currentDistance:F3} | d_s: {distanceToSurface:F3} | d_o: {distanceToObject:F3} | Calculated: {calculatedDistance:F3} | Offset: {pressureDistanceOffset:F3}{graspedInfo}");
        }

        return smoothedDistance;
    }
}

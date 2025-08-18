using UnityEngine;

public class SimpleXZFollower : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform targetToFollow;
    public float followSpeed = 2f;

    void Update()
    {
        if (targetToFollow != null)
        {
            // Create target position with target's X and Z, but keep our Y
            Vector3 targetPos = new Vector3(
                targetToFollow.position.x,
                transform.position.y, // Keep current Y position
                targetToFollow.position.z
            );

            // Move smoothly towards target position
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                followSpeed * Time.deltaTime
            );
        }
    }
}
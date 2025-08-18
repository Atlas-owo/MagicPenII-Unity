using UnityEngine;

public class FollowBelow : MonoBehaviour
{
    [Tooltip("要跟随的目标物体")]
    public Transform target;

    [Tooltip("距离目标物体底部的垂直偏移量")]
    public float verticalOffset = 1.0f;

    void Update()
    {
        if (target != null)
        {
            Vector3 newPosition = target.position;
            newPosition.y -= verticalOffset;
            transform.position = newPosition;
        }
    }
}

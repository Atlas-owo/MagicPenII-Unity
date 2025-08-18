using UnityEngine;

public class SingleAxisMovement : MonoBehaviour
{
    void Start()
    {
        Rigidbody rb = GetComponent<Rigidbody>();

        // To move only on X-axis
        rb.constraints = RigidbodyConstraints.FreezePositionY |
                        RigidbodyConstraints.FreezePositionZ;

        // To move only on Y-axis
        // rb.constraints = RigidbodyConstraints.FreezePositionX | 
        //                 RigidbodyConstraints.FreezePositionZ;
    }
}
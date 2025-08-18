using TMPro;
using UnityEngine;

public class ObjectMover : MonoBehaviour
{
    [Header("Movement Settings")]
    public Transform startPosition;
    public Transform endPosition;
    public KeyCode moveKey = KeyCode.Space;

    [Header("Speed Settings")]
    [Range(0.01f, 2f)]
    public float moveSpeed = 1f;

    [Header("Movement Options")]
    public bool canToggle = true; // If true, pressing key again moves back

    private bool isAtEndPosition = false;

    void Start()
    {
        // Set initial position to start position if assigned
        if (startPosition != null)
        {
            transform.position = startPosition.position;
        }
    }

    void Update()
    {
        // Check for key input - can press anytime
        if (Input.GetKeyDown(moveKey))
        {
            transform.position = Vector3.MoveTowards(transform.position, endPosition.position, moveSpeed * Time.deltaTime);
        }

    }


}
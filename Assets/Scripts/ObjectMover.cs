using TMPro;
using UnityEngine;
using System.Collections;
using System;

public class ObjectMover : MonoBehaviour
{
    [Header("Movement Settings")]
    public Transform startPosition;
    public Transform endPosition;
    public KeyCode moveKey = KeyCode.M;

    [Header("Speed Settings")]
    [Range(0.01f, 2f)]
    public float moveSpeed = 1f;

    [Header("Movement Options")]
    public bool canToggle = true; // If true, pressing key again moves back

    [Header("Blinking Settings")]
    [Range(0.1f, 1f)]
    public float blinkInterval = 0.3f;

    private bool isAtEndPosition = false;
    private bool isMoving = false;
    private Renderer objectRenderer;
    private Material originalMaterial;
    private Color originalColor;

    void Start()
    {
        // Set initial position to start position if assigned
        if (startPosition != null)
        {
            transform.position = startPosition.position;
        }

        // Get renderer component for blinking
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            originalMaterial = objectRenderer.material;
            originalColor = originalMaterial.color;
        }
    }

    void Update()
    {
        // Check for key input to start/stop movement
        if (Input.GetKeyDown(moveKey))
        {
            if (!isMoving)
            {
                isMoving = true;
            }
        }

        // Continuous movement when moving is active
        if (isMoving)
        {
            Vector3 targetPosition = isAtEndPosition ? startPosition.position : endPosition.position;
            
            // Move towards target
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            
            // Check if we've reached the target
            if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
            {
                transform.position = targetPosition; // Snap to exact position
                isMoving = false;
                
                if (canToggle)
                {
                    isAtEndPosition = !isAtEndPosition;
                }
            }
        }
    }

    // Public method to trigger blinking
    public void BlinkTimes(int blinkCount, Action onComplete = null)
    {
        StartCoroutine(BlinkCoroutine(blinkCount, onComplete));
    }

    // Public method to move to destination
    public void MoveToDestination(Action onComplete = null)
    {
        StartCoroutine(MoveToDestinationCoroutine(onComplete));
    }

    // Public method to reset to start position
    public void ResetToStart(Action onComplete = null)
    {
        StartCoroutine(ResetToStartCoroutine(onComplete));
    }

    // Public method to instantly teleport to start position
    public void TeleportToStart(Action onComplete = null)
    {
        if (startPosition != null)
        {
            transform.position = startPosition.position;
            isAtEndPosition = false;
            isMoving = false;
        }
        onComplete?.Invoke();
    }

    // Coroutine for blinking
    private IEnumerator BlinkCoroutine(int blinkCount, Action onComplete)
    {
        if (objectRenderer == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        for (int i = 0; i < blinkCount; i++)
        {
            // Hide (set alpha to 0)
            Color transparentColor = originalColor;
            transparentColor.a = 0f;
            objectRenderer.material.color = transparentColor;
            
            yield return new WaitForSeconds(blinkInterval);
            
            // Show (restore original color)
            objectRenderer.material.color = originalColor;
            
            yield return new WaitForSeconds(blinkInterval);
        }
        
        onComplete?.Invoke();
    }

    // Coroutine for moving to destination
    private IEnumerator MoveToDestinationCoroutine(Action onComplete)
    {
        if (endPosition == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        isMoving = true;
        Vector3 targetPosition = endPosition.position;

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = targetPosition;
        isAtEndPosition = true;
        isMoving = false;
        onComplete?.Invoke();
    }

    // Coroutine for resetting to start position
    private IEnumerator ResetToStartCoroutine(Action onComplete)
    {
        if (startPosition == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        isMoving = true;
        Vector3 targetPosition = startPosition.position;

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = targetPosition;
        isAtEndPosition = false;
        isMoving = false;
        onComplete?.Invoke();
    }

    // Check if currently at start position
    public bool IsAtStartPosition()
    {
        return !isAtEndPosition && !isMoving;
    }

    // Check if currently moving
    public bool IsMoving()
    {
        return isMoving;
    }
}
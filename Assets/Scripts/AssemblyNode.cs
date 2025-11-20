using UnityEngine;
using System.Collections.Generic;

public class AssemblyNode : MonoBehaviour
{
    [Header("Hierarchy")]
    [SerializeField]
    [Tooltip("List of child objects that should move with this assembly")]
    private List<AssemblyNode> childAssemblies = new List<AssemblyNode>();
    
    [Header("Group Settings")]
    [SerializeField]
    [Tooltip("Unique identifier for this assembly group (e.g., 'A1', 'A2', etc.)")]
    private string _groupId = "";

    public string groupId
    {
        get { return _groupId; }
        set
        {
            if (_groupId != value)
            {
                _groupId = value;
                PropagateGroupId(value);
            }
        }
    }

    [Header("Parent Reference")]
    [SerializeField]
    [Tooltip("Will be auto-assigned on Start")]
    private AssemblyNode parentAssembly;

    // Public access to children
    public List<AssemblyNode> Children => childAssemblies;

    [Header("Explosion Settings")]
    [Header("Explosion Distance per Axis")]
    [Tooltip("Distance to move along X axis when exploded (use negative for opposite direction)")]
    public float explodeDistanceX = 0.3f;
    [Tooltip("Distance to move along Y axis when exploded (use negative for opposite direction)")]
    public float explodeDistanceY = 0.0f;
    [Tooltip("Distance to move along Z axis when exploded (use negative for opposite direction)")]
    public float explodeDistanceZ = 0.0f;

    [Header("Movement Settings")]
    public float moveSpeed = 2f;
    public bool isExploded = false;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 targetPosition;
    private bool hasStoredInitialTransform = false;

    // Prevent re-entrant movement calls
    private bool isMoving = false;

    // Public read-only accessor for movement state (used by manager to wait for completion)
    public bool IsMoving => isMoving;

    private void Awake()
    {
        // Auto-find parent assembly if not set
        if (parentAssembly == null && transform.parent != null)
        {
            parentAssembly = transform.parent.GetComponent<AssemblyNode>();
        }
    }

    private void Start()
    {
        // Initialize transform state
        StoreInitialTransform();
        
        // Auto-populate child assemblies (but don't force groupId propagation)
        RefreshChildAssemblies();
        
        // Only propagate groupId to children that don't have their own groupId
        // This allows parts to inherit but sub-assemblies to keep their own IDs
        if (!string.IsNullOrEmpty(_groupId))
        {
            PropagateGroupIdToEmptyChildren(_groupId);
        }
    }

    public void RefreshChildAssemblies()
    {
        // Remove any null entries
        childAssemblies.RemoveAll(child => child == null);
        
        // Add any missing children from hierarchy
        foreach (Transform child in transform)
        {
            AssemblyNode childNode = child.GetComponent<AssemblyNode>();
            if (childNode != null && !childAssemblies.Contains(childNode))
            {
                childAssemblies.Add(childNode);
            }
        }
    }

    private void PropagateGroupId(string newGroupId)
    {
        if (string.IsNullOrEmpty(newGroupId))
        {
            return;
        }
        
        foreach (var child in childAssemblies)
        {
            if (child != null)
            {
                // Only propagate to children that don't have their own group ID
                // This allows sub-assemblies to maintain their own group IDs
                if (string.IsNullOrEmpty(child._groupId))
                {
                    child._groupId = newGroupId;
                    // Continue propagation down the hierarchy
                    child.PropagateGroupId(newGroupId);
                }
            }
        }
    }
    
    // Helper to propagate only to children without their own groupId
    private void PropagateGroupIdToEmptyChildren(string newGroupId)
    {
        PropagateGroupId(newGroupId);
    }

    private void OnValidate()
    {
        // Auto-update in editor
        if (Application.isEditor && !Application.isPlaying)
        {
            if (parentAssembly == null && transform.parent != null)
            {
                parentAssembly = transform.parent.GetComponent<AssemblyNode>();
            }
            
            // Ensure group ID propagation in editor
            if (!string.IsNullOrEmpty(_groupId))
            {
                PropagateGroupId(_groupId);
            }
        }
    }

    public void StoreInitialTransform()
    {
        if (hasStoredInitialTransform)
        {
            return;
        }
        initialPosition = transform.localPosition;
        initialRotation = transform.localRotation;
        hasStoredInitialTransform = true;
    }

    public void Explode()
    {
        // Prevent double-triggering
        if (isExploded || isMoving)
        {
            return;
        }

        if (!hasStoredInitialTransform)
        {
            StoreInitialTransform();
        }

        // Calculate directional explosion offset in the child's own local space
        // Use the distance values directly (0 means no movement on that axis)
        Vector3 localOffset = new Vector3(
            explodeDistanceX,
            explodeDistanceY,
            explodeDistanceZ
        );

        // Convert the child's local direction to parent's local space
        // This makes the explosion follow the child's own orientation
        Vector3 explosionOffset = transform.localRotation * localOffset;

        targetPosition = initialPosition + explosionOffset;
        
        // Validate the move
        if (float.IsNaN(targetPosition.x) || float.IsNaN(targetPosition.y) || float.IsNaN(targetPosition.z))
        {
            return;
        }
        
        // Mark state and start movement
        isExploded = true;
        isMoving = true;
        StopAllCoroutines();
        StartCoroutine(MoveTo(targetPosition, initialRotation));
    }

    public void Implode()
    {
        if (!hasStoredInitialTransform)
        {
            StoreInitialTransform();
        }

        // Check if we're actually displaced from initial position
        float distance = Vector3.Distance(transform.localPosition, initialPosition);
        bool isDisplaced = distance > 0.001f;
        
        // Skip if already at initial position and not moving AND not marked as exploded
        if (!isDisplaced && !isMoving && !isExploded)
        {
            return;
        }

        // First implode all children
        foreach (var child in childAssemblies)
        {
            if (child != null)
            {
                child.Implode();
            }
        }

        // Then move this node back (even if distance is small, if we're marked as exploded)
        if (isDisplaced || isExploded)
        {
            isExploded = false;
            isMoving = true;
            StopAllCoroutines();
            StartCoroutine(MoveTo(initialPosition, initialRotation));
        }
    }

    private System.Collections.IEnumerator MoveTo(Vector3 targetPos, Quaternion targetRot)
    {
        float startTime = Time.time;
        Vector3 startPos = transform.localPosition;
        Quaternion startRot = transform.localRotation;
        float journeyLength = Vector3.Distance(startPos, targetPos);
        
        while (Vector3.Distance(transform.localPosition, targetPos) > 0.001f)
        {
            if (!gameObject.activeInHierarchy) yield break;

            float distCovered = (Time.time - startTime) * moveSpeed;
            float fractionOfJourney = distCovered / journeyLength;
            fractionOfJourney = Mathf.Clamp01(fractionOfJourney);
            
            transform.localPosition = Vector3.Lerp(startPos, targetPos, fractionOfJourney);
            transform.localRotation = Quaternion.Lerp(startRot, targetRot, fractionOfJourney);
            
            yield return null;
        }
        
        // Ensure final position is exact
        transform.localPosition = targetPos;
        transform.localRotation = targetRot;
        
        // Movement finished
        isMoving = false;
    }
}
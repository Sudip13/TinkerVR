using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Controls the interpolation of an object between its initial position and its last grabbed position.
/// Integrates with Meta's Grabbable system for VR interaction.
/// </summary>
public class PathInterpolationController : MonoBehaviour
{
    #region Variables
    [Header("Debug")]
    [SerializeField, Tooltip("Shows if object is currently grabbed")]
    public bool isGrabbed = false;

    [Header("Interpolation Settings")]
    [Tooltip("Speed of interpolation movement")]
    public float moveSpeed = 2.0f;

    [Header("Grabbable Settings")]
    [Tooltip("Reference to Meta's Grabbable component (auto-assigned)")]
    [SerializeField] private Grabbable grabbable;
    [Tooltip("Reference to Hand Grab Interactable component")]
    [SerializeField] private MonoBehaviour handGrabInteractable;
    [Tooltip("Reference to Grab Interactable component")]
    [SerializeField] private MonoBehaviour grabInteractable;

    [Header("Audio Settings")]
    [Tooltip("Audio clip to play when returning to pick start position")]
    public AudioClip returnToPickStartClip;
    [Tooltip("Audio clip to play when returning to original position")]
    public AudioClip returnToOriginClip;
    [Tooltip("Volume for return audio clips")]
    [Range(0f, 1f)]
    public float audioVolume = 0.5f;
    
    // Audio source component
    private AudioSource audioSource;

    // Stored initial state
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 initialScale;

    // Last grabbed transform (acts as dynamic destination)
    private Vector3 lastGrabbedPosition;
    private Quaternion lastGrabbedRotation;
    private Vector3 lastGrabbedScale;
    private bool hasBeenGrabbed = false;

    // Pick start transform (where the grab began)
    private Vector3 pickStartPosition;
    private Quaternion pickStartRotation;
    private Vector3 pickStartScale;

    // Return control
    private bool returningToPickStart = false;
    private bool userInitiatedReturn = false; // Track if return was triggered by user release

    // State tracking
    private bool isExploded = false;
    private bool isInterpolating = false;

    // Public read-only accessor for interpolation state
    public bool IsInterpolating => isInterpolating;

    [SerializeField] private float scaleInterpolationSpeed = 2.0f;
    #endregion

    #region Unity Lifecycle
    private void Start()
    {
        // Store initial transform state
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialScale = transform.localScale;

        // Initialize last grabbed as initial
        lastGrabbedPosition = initialPosition;
        lastGrabbedRotation = initialRotation;
        lastGrabbedScale = initialScale;

        // Get all grab-related components if not assigned
        if (grabbable == null)
            grabbable = GetComponent<Grabbable>();
        if (handGrabInteractable == null)
            handGrabInteractable = GetComponent("HandGrabInteractable") as MonoBehaviour;
        if (grabInteractable == null)
            grabInteractable = GetComponent("GrabInteractable") as MonoBehaviour;

        // Get or create AudioSource component
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1.0f; // 3D sound
        }

        // Log warning if primary component is missing
        if (grabbable == null)
        {
            Debug.LogWarning($"[{gameObject.name}] PathInterpolationController: No Grabbable component found!");
        }

        // Initially disable all grab components
        SetGrabComponentsState(false);
    }

    private void Update()
    {
        // If object is being grabbed, skip interpolation
        if (IsGrabbed())
        {
            return;
        }

        // Drive interpolation when requested
        if (isInterpolating)
        {
            // Implode to origin takes priority over return-to-pick-start
            if (!isExploded)
            {
                // Cancel any return animation if we're imploding
                returningToPickStart = false;
                InterpolateToOrigin();
            }
            else if (returningToPickStart)
            {
                InterpolateToPickStart();
            }
        }
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Public API to enable grabbing externally.
    /// </summary>
    public void EnableGrabbing()
    {
        SetGrabComponentsState(true);
        isExploded = true;
    }

    /// <summary>
    /// Public API to disable grabbing externally.
    /// </summary>
    public void DisableGrabbing()
    {
        SetGrabComponentsState(false);
        // Don't change exploded flag here; manager controls implode state
    }

    /// <summary>
    /// Cancels any ongoing return-to-pick-start animation.
    /// </summary>
    public void CancelPickStartReturn()
    {
        returningToPickStart = false;
        userInitiatedReturn = false;
        isInterpolating = false;
    }

    /// <summary>
    /// Resets the object back to its pick-start transform immediately (before implosion).
    /// This is called by the manager to ensure all objects are in their pre-grab state before imploding.
    /// </summary>
    public void ResetToPickStart()
    {
        // If the object has been grabbed, reset it to pick-start position
        if (hasBeenGrabbed)
        {
            returningToPickStart = true;
            userInitiatedReturn = false; // Mark as manager-initiated return (no audio)
            isInterpolating = true;
            // Disable grabbing during reset to avoid interference
            SetGrabComponentsState(false);
        }
        else
        {
            // If never grabbed, already at correct position
            isInterpolating = false;
        }
    }

    /// <summary>
    /// Enables grabbing at current pose; no auto-move. Grabbing is allowed for inspection.
    /// </summary>
    public void ExplodeToDestination()
    {
        isExploded = true;
        isInterpolating = false; // no auto move on explode

        // Enable all grab components for interaction
        EnableGrabbing();

        OnExplodeStart();
    }

    /// <summary>
    /// Initiates implosion animation back to original position
    /// </summary>
    public void ImplodeToOrigin()
    {
        // Cancel any ongoing return-to-pick-start animation
        CancelPickStartReturn();
        
        isExploded = false;
        isInterpolating = true;
        hasBeenGrabbed = false; // Reset grab state so it can be grabbed again after next explode

        // Disable all grab components when imploding
        DisableGrabbing();
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Sets the enabled state of all grab-related components
    /// </summary>
    private void SetGrabComponentsState(bool enabled)
    {
        if (grabbable != null)
            grabbable.enabled = enabled;
        if (handGrabInteractable != null)
            handGrabInteractable.enabled = enabled;
        if (grabInteractable != null)
            grabInteractable.enabled = enabled;
    }

    /// <summary>
    /// Checks if the object is currently being grabbed
    /// </summary>
    private bool IsGrabbed()
    {
        if (grabbable == null) return false;
        
        // Check if there are any active grab points
        bool currentlyGrabbed = grabbable.GrabPoints.Count > 0;
        
        // Debug log if grab state changed
        if (currentlyGrabbed != isGrabbed)
        {
            isGrabbed = currentlyGrabbed;
            if (!isGrabbed)
            {
                hasBeenGrabbed = true;
                // Start returning to where pick began
                returningToPickStart = true;
                userInitiatedReturn = true; // Mark as user-initiated return
                isInterpolating = true;
                // Disable grabbing during return to avoid fighting the animation
                SetGrabComponentsState(false);
                
                // Play audio immediately when object is released by user
                if (userInitiatedReturn)
                {
                    PlayReturnToPickStartAudio();
                }
            }
            else // grabbed just started
            {
                // Mark the pick start transform
                pickStartPosition = transform.position;
                pickStartRotation = transform.rotation;
                pickStartScale = transform.localScale;
                // Cancel any pending return
                isInterpolating = false;
                returningToPickStart = false;
            }
        }
        
        return isGrabbed;
    }

    /// <summary>
    /// Handles interpolation back to the pick start transform
    /// </summary>
    private void InterpolateToPickStart()
    {
        if (!isInterpolating) return;

        // Interpolate position
        transform.position = Vector3.MoveTowards(
            transform.position,
            pickStartPosition,
            moveSpeed * Time.deltaTime
        );

        // Interpolate rotation
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            pickStartRotation,
            moveSpeed * 50f * Time.deltaTime
        );

        // Add scale interpolation
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            pickStartScale,
            scaleInterpolationSpeed * Time.deltaTime
        );

        // Check if we've reached the pick start (include scale check)
        if (Vector3.Distance(transform.position, pickStartPosition) < 0.001f &&
            Quaternion.Angle(transform.rotation, pickStartRotation) < 0.1f &&
            Vector3.Distance(transform.localScale, pickStartScale) < 0.001f)
        {
            // Snap to exact values
            transform.position = pickStartPosition;
            transform.rotation = pickStartRotation;
            transform.localScale = pickStartScale;
            
            isInterpolating = false;
            returningToPickStart = false;
            // Re-enable grabbing after return completes
            if (isExploded)
            {
                SetGrabComponentsState(true);
            }
            
            // Reset the flag after use (audio already played on release)
            userInitiatedReturn = false;
        }
    }

    /// <summary>
    /// Handles interpolation back to the original position
    /// </summary>
    private void InterpolateToOrigin()
    {
        if (!isInterpolating) return;

        // Interpolate position
        transform.position = Vector3.MoveTowards(
            transform.position,
            initialPosition,
            moveSpeed * Time.deltaTime
        );

        // Interpolate rotation
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            initialRotation,
            moveSpeed * 50f * Time.deltaTime
        );

        // Add scale interpolation
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            initialScale,
            scaleInterpolationSpeed * Time.deltaTime
        );

        // Check if we've reached the origin (include scale check)
        if (Vector3.Distance(transform.position, initialPosition) < 0.001f &&
            Quaternion.Angle(transform.rotation, initialRotation) < 0.1f &&
            Vector3.Distance(transform.localScale, initialScale) < 0.001f)
        {
            // Snap to exact values
            transform.position = initialPosition;
            transform.rotation = initialRotation;
            transform.localScale = initialScale;
            
            isInterpolating = false;
            
            // Ensure all grab components are disabled when fully returned to origin
            SetGrabComponentsState(false);
            
            // Note: No audio for ImplodeToOrigin as it's manager-initiated (back button)
            
            OnImplodeComplete();
        }
    }
    #endregion

    #region Audio Methods
    /// <summary>
    /// Plays audio when object returns to pick start position
    /// </summary>
    private void PlayReturnToPickStartAudio()
    {
        if (audioSource != null && returnToPickStartClip != null)
        {
            audioSource.clip = returnToPickStartClip;
            audioSource.volume = audioVolume;
            audioSource.Play();
        }
    }

    /// <summary>
    /// Plays audio when object returns to original position
    /// </summary>
    private void PlayReturnToOriginAudio()
    {
        if (audioSource != null && returnToOriginClip != null)
        {
            audioSource.clip = returnToOriginClip;
            audioSource.volume = audioVolume;
            audioSource.Play();
        }
    }
    #endregion

    #region Event Hooks
    /// <summary>
    /// Called when explosion animation begins (placeholder for future events)
    /// </summary>
    protected virtual void OnExplodeStart()
    {
        // Placeholder for future event handling
    }

    /// <summary>
    /// Called when implosion animation completes (placeholder for future events)
    /// </summary>
    protected virtual void OnImplodeComplete()
    {
        // Placeholder for future event handling
    }
    #endregion
    
}

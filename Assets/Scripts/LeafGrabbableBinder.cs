using UnityEngine;

// Binds a PathInterpolationController on leaf parts to HierarchyExplodeManager layers.
// Enables grabbing only when the owning group reaches a required layer.
[DefaultExecutionOrder(100)]
public class LeafGrabbableBinder : MonoBehaviour
{
    [Tooltip("Reference to the explode manager. If null, will auto-find in scene.")]
    public HierarchyExplodeManager manager;

    [Tooltip("Override groupId. If empty, will use nearest ancestor AssemblyNode.groupId.")]
    public string groupIdOverride = "";

    [Tooltip("Layer at which this object becomes grabbable (relative to its group). Typically 1 for parts.")]
    public int requiredLayer = 1;

    private string resolvedGroupId;
    private PathInterpolationController controller;
    private AssemblyNode nearestAssembly;

    private void Awake()
    {
        controller = GetComponent<PathInterpolationController>();
        if (controller == null)
        {
            Debug.LogError($"[LeafGrabbableBinder] Missing PathInterpolationController on {name}");
            enabled = false;
            return;
        }

        if (manager == null)
        {
            manager = FindObjectOfType<HierarchyExplodeManager>();
        }

        // Resolve group id
        if (!string.IsNullOrEmpty(groupIdOverride))
        {
            resolvedGroupId = groupIdOverride;
        }
        else
        {
            nearestAssembly = GetComponentInParent<AssemblyNode>();
            if (nearestAssembly != null)
            {
                resolvedGroupId = nearestAssembly.groupId;
            }
        }
    }

    private void OnEnable()
    {
        if (manager != null)
        {
            manager.OnGroupExploded.AddListener(OnGroupStateChanged);
            manager.OnGroupImploded.AddListener(OnGroupStateChanged);
        }
        UpdateGrabStateImmediate();
    }

    private void OnDisable()
    {
        if (manager != null)
        {
            manager.OnGroupExploded.RemoveListener(OnGroupStateChanged);
            manager.OnGroupImploded.RemoveListener(OnGroupStateChanged);
        }
    }

    private void OnGroupStateChanged(string groupId)
    {
        if (!IsMyGroup(groupId)) return;
        UpdateGrabStateImmediate();
    }

    private bool IsMyGroup(string groupId)
    {
        if (!string.IsNullOrEmpty(groupIdOverride))
            return groupId == groupIdOverride;
        if (nearestAssembly != null)
            return groupId == nearestAssembly.groupId;
        return false;
    }

    private void UpdateGrabStateImmediate()
    {
        if (manager == null || string.IsNullOrEmpty(GetMyGroupId()))
        {
            controller.DisableGrabbing();
            return;
        }

        int layer = manager.GetGroupLayer(GetMyGroupId());
        if (layer >= requiredLayer)
        {
            controller.EnableGrabbing();
        }
        else
        {
            controller.DisableGrabbing();
        }
    }

    private string GetMyGroupId()
    {
        if (!string.IsNullOrEmpty(groupIdOverride)) return groupIdOverride;
        return nearestAssembly != null ? nearestAssembly.groupId : null;
    }
}

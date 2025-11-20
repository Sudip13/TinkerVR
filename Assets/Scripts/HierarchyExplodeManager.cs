using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

public class HierarchyExplodeManager : MonoBehaviour
{
    [Header("Root Assembly")]
    public AssemblyNode rootAssembly;

    [Header("Group Settings")]
    [Tooltip("List of active groups that can be controlled independently")]
    public List<string> activeGroups = new List<string>();

    private Dictionary<string, Stack<AssemblyNode>> groupHierarchies = new Dictionary<string, Stack<AssemblyNode>>();
    // Root node per group (topmost AssemblyNode for that group)
    private Dictionary<string, AssemblyNode> groupRootNodes = new Dictionary<string, AssemblyNode>();
    private Stack<AssemblyNode> activeHierarchy = new Stack<AssemblyNode>();
    
    // Track hierarchical explode path for universal back (main -> sub1 -> sub1a, etc.)
    private List<string> explodedGroupPath = new List<string>();
    private string lastExplodedGroup = null;

    // Track current layer index per group (0 = base)
    private Dictionary<string, int> groupLayerIndex = new Dictionary<string, int>();


    [Header("Events")]
    [Tooltip("Invoked when a group is exploded. Passes groupId.")]
    public UnityEvent<string> OnGroupExploded;

    [Tooltip("Invoked when a group is imploded. Passes groupId.")]
    public UnityEvent<string> OnGroupImploded;
    
    [Tooltip("Invoked before a group's explode animation starts. Use to prepare visuals.")]
    public UnityEvent<string> OnGroupPreExplode;

    [Tooltip("Invoked before a group's implode animation starts. Use to prepare visuals.")]
    public UnityEvent<string> OnGroupPreImplode;

    private void Start()
    {
        if (rootAssembly != null)
        {
            activeHierarchy.Push(rootAssembly);
            InitializeGroups();
        }
    }

    private void InitializeGroups()
    {
        // Clear existing hierarchies
        groupHierarchies.Clear();
        groupRootNodes.Clear();
        
        // Initialize stacks for each group
        foreach (string groupId in activeGroups)
        {
            groupHierarchies[groupId] = new Stack<AssemblyNode>();
            groupLayerIndex[groupId] = 0;
        }

        // Find all nodes in the hierarchy that belong to groups
        FindAndInitializeGroupNodes(rootAssembly);
    }

    private void FindAndInitializeGroupNodes(AssemblyNode node)
    {
        if (node == null) return;

        // If this node belongs to a group and it's in our active groups
        if (!string.IsNullOrEmpty(node.groupId) && activeGroups.Contains(node.groupId))
        {
            if (!groupHierarchies.ContainsKey(node.groupId))
            {
                groupHierarchies[node.groupId] = new Stack<AssemblyNode>();
            }

            // Register this node for its group
            groupHierarchies[node.groupId].Push(node);

            // Set root node for this group if not already set (first encountered is topmost)
            if (!groupRootNodes.ContainsKey(node.groupId))
            {
                groupRootNodes[node.groupId] = node;
            }
        }

        // Process all children using the node's Children property
        var children = node.Children;
        if (children != null)
        {
            foreach (var child in children)
            {
                if (child != null)
                {
                    FindAndInitializeGroupNodes(child);
                }
            }
        }

        // Also check transform hierarchy for any missed children
        foreach (Transform child in node.transform)
        {
            AssemblyNode childNode = child.GetComponent<AssemblyNode>();
            if (childNode != null)
            {
                FindAndInitializeGroupNodes(childNode);
            }
        }
    }

    public void ExplodeGroup(string groupId)
    {
        // Prevent triggering explode if an animation is already running
        if (isAnimationRunning)
        {
            return;
        }
        StartCoroutine(ExplodeGroupCoroutine(groupId));
    }

    private System.Collections.IEnumerator ExplodeGroupCoroutine(string groupId)
    {
        if (!groupHierarchies.ContainsKey(groupId))
        {
            Debug.LogError($"[HierarchyExplodeManager] Group {groupId} not found!");
            yield break;
        }

        if (!groupRootNodes.ContainsKey(groupId) || groupRootNodes[groupId] == null)
        {
            Debug.LogError($"[HierarchyExplodeManager] No root node found for group {groupId}!");
            yield break;
        }

        // Mark animation as running
        isAnimationRunning = true;

        AssemblyNode current = groupRootNodes[groupId];

        // Notify pre-explode
        OnGroupPreExplode?.Invoke(groupId);

        // Collect all children that need to be exploded
        HashSet<AssemblyNode> nodesToWaitFor = new HashSet<AssemblyNode>();

        // Add children from the node's Children property
        foreach (var child in current.Children)
        {
            if (child != null)
            {
                nodesToWaitFor.Add(child);
            }
        }

        // Add any children from Transform hierarchy that might have been missed
        foreach (Transform child in current.transform)
        {
            AssemblyNode childNode = child.GetComponent<AssemblyNode>();
            if (childNode != null && !nodesToWaitFor.Contains(childNode))
            {
                nodesToWaitFor.Add(childNode);
            }
        }

        // Explode all children
        foreach (var child in nodesToWaitFor)
        {
            child.StoreInitialTransform();
            child.Explode();
        }

        // Then explode the parent
        current.StoreInitialTransform();
        current.Explode();
        nodesToWaitFor.Add(current);

        // Wait for all movement to complete
        bool anyMoving = true;
        while (anyMoving)
        {
            anyMoving = false;
            foreach (var node in nodesToWaitFor)
            {
                if (node != null && node.IsMoving)
                {
                    anyMoving = true;
                    break;
                }
            }
            yield return null;
        }

        // Advance the group's layer
        if (!groupLayerIndex.ContainsKey(groupId)) groupLayerIndex[groupId] = 0;
        groupLayerIndex[groupId] = groupLayerIndex[groupId] + 1;

        // Mark the last exploded group and push to hierarchical path
        lastExplodedGroup = groupId;
        if (explodedGroupPath.Count == 0 || explodedGroupPath[explodedGroupPath.Count - 1] != groupId)
            explodedGroupPath.Add(groupId);

        // Mark animation as complete
        isAnimationRunning = false;

        // Notify post-explode
        OnGroupExploded?.Invoke(groupId);
    }

    public void ImplodeGroup(string groupId)
    {
        // Prevent triggering implode if an animation is already running
        if (isAnimationRunning)
        {
            return;
        }
        StartCoroutine(ImplodeGroupCoroutine(groupId));
    }

    private System.Collections.IEnumerator ImplodeGroupCoroutine(string groupId)
    {
        if (!groupHierarchies.ContainsKey(groupId))
        {
            Debug.LogError($"[HierarchyExplodeManager] Group {groupId} not found!");
            yield break;
        }

        if (!groupRootNodes.ContainsKey(groupId) || groupRootNodes[groupId] == null)
        {
            Debug.LogError($"[HierarchyExplodeManager] No root node found for group {groupId}!");
            yield break;
        }

        // Mark animation as running
        isAnimationRunning = true;

        // Use the group's root node for implode of this group
        AssemblyNode current = groupRootNodes[groupId];

        // Notify pre-implode
        OnGroupPreImplode?.Invoke(groupId);

        // STEP 1: First, reset all PathInterpolationController objects to their pick-start transform
        // Only reset controllers that belong to THIS group (direct children only, not nested groups)
        List<PathInterpolationController> controllersToReset = new List<PathInterpolationController>();
        
        // Only check direct children, not recursive
        foreach (Transform t in current.transform)
        {
            // Skip objects that belong to a different group
            var childAssembly = t.GetComponent<AssemblyNode>();
            if (childAssembly != null && !string.IsNullOrEmpty(childAssembly.groupId) && childAssembly.groupId != groupId)
            {
                // This child belongs to a different group, skip it
                continue;
            }

            var controller = t.GetComponent<PathInterpolationController>();
            if (controller != null)
            {
                controllersToReset.Add(controller);
                controller.ResetToPickStart(); // Reset transform before implosion
            }
        }

        // Wait for all controllers to finish resetting to pick-start
        if (controllersToReset.Count > 0)
        {
            bool anyResetting = true;
            while (anyResetting)
            {
                anyResetting = false;
                foreach (var controller in controllersToReset)
                {
                    if (controller != null && controller.IsInterpolating)
                    {
                        anyResetting = true;
                        break;
                    }
                }
                yield return null;
            }
        }

        // STEP 2: Now implode all direct children of the current node
        HashSet<AssemblyNode> nodesToWaitFor = new HashSet<AssemblyNode>();
        
        // Children from the node's logical Children list
        foreach (var child in current.Children)
        {
            if (child == null) continue;
            child.Implode();
            nodesToWaitFor.Add(child);
        }
        
        // Also include any Transform children with AssemblyNode
        foreach (Transform t in current.transform)
        {
            var childNode = t.GetComponent<AssemblyNode>();
            if (childNode != null && !nodesToWaitFor.Contains(childNode))
            {
                childNode.Implode();
                nodesToWaitFor.Add(childNode);
            }
        }

        // Wait for all AssemblyNode movements to complete
        bool anyMoving = true;
        while (anyMoving)
        {
            anyMoving = false;
            foreach (var node in nodesToWaitFor)
            {
                if (node != null && node.IsMoving)
                {
                    anyMoving = true;
                    break;
                }
            }
            yield return null;
        }

        // STEP 3: After AssemblyNodes have imploded, now implode the PathInterpolationController parts
        // Only implode controllers that belong to THIS group AND are NOT part of an AssemblyNode
        // (AssemblyNodes handle their own implosion, so we skip those to avoid double-implosion)
        List<PathInterpolationController> controllersToImplode = new List<PathInterpolationController>();
        foreach (Transform t in current.transform)
        {
            // Skip objects that belong to a different group
            var childAssembly = t.GetComponent<AssemblyNode>();
            if (childAssembly != null && !string.IsNullOrEmpty(childAssembly.groupId) && childAssembly.groupId != groupId)
            {
                // This child belongs to a different group, skip it
                continue;
            }

            // Skip objects that ARE an AssemblyNode (they implode themselves in STEP 2)
            if (childAssembly != null)
            {
                continue;
            }

            // Only implode PathInterpolationControllers that are NOT part of an AssemblyNode
            var controller = t.GetComponent<PathInterpolationController>();
            if (controller != null)
            {
                controller.ImplodeToOrigin();
                controllersToImplode.Add(controller);
            }
        }

        // Wait for all PathInterpolationController implode movements to complete
        if (controllersToImplode.Count > 0)
        {
            bool anyImploding = true;
            while (anyImploding)
            {
                anyImploding = false;
                foreach (var controller in controllersToImplode)
                {
                    if (controller != null && controller.IsInterpolating)
                    {
                        anyImploding = true;
                        break;
                    }
                }
                yield return null;
            }
        }

        // DON'T implode the root node itself - it should only move when its parent group is imploded
        // The root node stays at its exploded position while its children implode back to it

        // Move the group's layer index back
        if (!groupLayerIndex.ContainsKey(groupId)) groupLayerIndex[groupId] = 0;
        groupLayerIndex[groupId] = Mathf.Max(0, groupLayerIndex[groupId] - 1);

        // If we just imploded the last exploded group and reached layer 0, remove from path
        if (lastExplodedGroup == groupId && groupLayerIndex[groupId] == 0)
        {
            if (explodedGroupPath.Count > 0 && explodedGroupPath[explodedGroupPath.Count - 1] == groupId)
                explodedGroupPath.RemoveAt(explodedGroupPath.Count - 1);
            lastExplodedGroup = explodedGroupPath.Count > 0 ? explodedGroupPath[explodedGroupPath.Count - 1] : null;
        }

        // Mark animation as complete
        isAnimationRunning = false;

        // Notify post-implode
        OnGroupImploded?.Invoke(groupId);
    }

    // Generic button handlers for any group
    public void OnGroupExplodeButton(string groupId)
    {
        // Prevent triggering if an animation is already running
        if (isAnimationRunning)
        {
            return;
        }

        if (activeGroups.Contains(groupId))
        {
            ExplodeGroup(groupId);
        }
        else
        {
            Debug.LogWarning($"Group {groupId} is not in the active groups list. Add it to activeGroups first.");
        }
    }

    public void OnGroupBackButton(string groupId)
    {
        // Prevent triggering if an animation is already running
        if (isAnimationRunning)
        {
            return;
        }

        if (activeGroups.Contains(groupId))
        {
            ImplodeGroup(groupId);
        }
        else
        {
            Debug.LogWarning($"Group {groupId} is not in the active groups list. Add it to activeGroups first.");
        }
    }

    // Universal Back button handler - single UI button can call this
    // Behavior: if a group was exploded most recently, implode that group;
    // otherwise fall back to the normal hierarchical back behavior (OnBackButton)
    public void OnUniversalBackButton()
    {
        // Prevent triggering if an animation is already running
        if (isAnimationRunning)
        {
            return;
        }

        // If a group was exploded most recently, run a sequence that implodes nested exploded nodes
        if (!string.IsNullOrEmpty(lastExplodedGroup) && groupHierarchies.ContainsKey(lastExplodedGroup))
        {
            if (!isUniversalBackRunning)
            {
                StartCoroutine(UniversalBackSequence());
            }
            return;
        }

        // If no group to implode, fallback to normal back behavior
        OnBackButton();
    }

    // Guard to prevent re-entrancy of the universal back process
    private bool isUniversalBackRunning = false;
    
    // Guard to track if any animation is currently running
    private bool isAnimationRunning = false;

    // Coroutine that implodes the last exploded group one step at a time
    private System.Collections.IEnumerator UniversalBackSequence()
    {
        if (isUniversalBackRunning) yield break;
        isUniversalBackRunning = true;

        if (!string.IsNullOrEmpty(lastExplodedGroup))
        {
            // Only implode if the group's layer index is above base
            if (groupLayerIndex.TryGetValue(lastExplodedGroup, out var idx) && idx > 0)
            {
                yield return StartCoroutine(ImplodeGroupCoroutine(lastExplodedGroup));
            }
        }
        // If lastExplodedGroup is null and path is not empty, remove and try previous group
        else if (explodedGroupPath.Count > 0)
        {
            explodedGroupPath.RemoveAt(explodedGroupPath.Count - 1);
            lastExplodedGroup = explodedGroupPath.Count > 0 ? explodedGroupPath[explodedGroupPath.Count - 1] : null;
        }

        isUniversalBackRunning = false;
    }

    // Public accessor for current layer index per group
    public int GetGroupLayer(string groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return 0;
        if (groupLayerIndex != null && groupLayerIndex.TryGetValue(groupId, out var idx)) return idx;
        return 0;
    }

    // Unified explode/implode methods for all cases
    public void OnExplodeButton(string groupId = "")
    {
        // Prevent triggering if an animation is already running
        if (isAnimationRunning)
        {
            return;
        }

        if (string.IsNullOrEmpty(groupId))
        {
            // Handle the case where no group is specified
            if (activeHierarchy.Count == 0) return;

            AssemblyNode current = activeHierarchy.Peek();

            if (!current.isExploded)
            {
                foreach (var child in current.Children)
                {
                    if (child != null)
                    {
                        child.Explode();
                    }
                }
                current.isExploded = true;
            }
            else
            {
                // Find the first valid child to push onto the hierarchy
                foreach (var child in current.Children)
                {
                    if (child != null)
                    {
                        activeHierarchy.Push(child);
                        break;
                    }
                }
            }
        }
        else
        {
            ExplodeGroup(groupId);
        }
    }

    public void OnBackButton(string groupId = "")
    {
        // Prevent triggering if an animation is already running
        if (isAnimationRunning)
        {
            return;
        }

        if (string.IsNullOrEmpty(groupId))
        {
            // Handle the case where no group is specified
            if (activeHierarchy.Count == 0) return;

            AssemblyNode current = activeHierarchy.Pop();

            if (activeHierarchy.Count > 0)
            {
                AssemblyNode parent = activeHierarchy.Peek();

                foreach (var child in parent.Children)
                {
                    if (child != null)
                    {
                        child.Implode();
                    }
                }
                parent.isExploded = false;
            }
            else
            {
                foreach (var child in current.Children)
                {
                    if (child != null)
                    {
                        child.Implode();
                    }
                }
                current.isExploded = false;
            }
        }
        else
        {
            ImplodeGroup(groupId);
        }
    }
}
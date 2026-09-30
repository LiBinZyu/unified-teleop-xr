using UnityEngine;

namespace BodyTracking
{
    /// <summary>
    /// Script to visualize pose axes of all joints under this root in the Editor.
    /// Attach this to the root of the skeleton.
    /// </summary>
    public class PoseDebugAxis : MonoBehaviour
    {
        [Tooltip("Length of the axis lines.")]
        public float axisLength = 0.05f;
        
        [Tooltip("Toggle to show/hide the axis.")]
        public bool showAxis = true;

        [Tooltip("Toggle to show/hide the lines connecting the bones.")]
        public bool drawBones = true;

        private void OnDrawGizmos()
        {
            if (!showAxis) return;
            
            // Get all child transforms, including the root itself
            Transform[] allTransforms = GetComponentsInChildren<Transform>();
            
            foreach (Transform t in allTransforms)
            {
                // Optionally skip drawing axis for the root container itself to keep things clean, 
                // but usually fine to draw it too. Let's just draw for all.
                
                // Draw Axes
                Gizmos.color = Color.red;
                Gizmos.DrawLine(t.position, t.position + t.right * axisLength);
                
                Gizmos.color = Color.green;
                Gizmos.DrawLine(t.position, t.position + t.up * axisLength);
                
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(t.position, t.position + t.forward * axisLength);
                
                // Draw connecting bone lines to form a stick figure
                if (drawBones && t.parent != null)
                {
                    // Skip drawing a bone line from Pelvis to the empty Root container on the floor
                    if (t.parent == this.transform) continue;

                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(t.position, t.parent.position);
                }
            }
        }
    }
}

using UnityEngine;

namespace CycloneDDS
{
    /// <summary>
    /// Synchronizes the SetActive state of this GameObject to target(s).
    /// Pure event-driven (OnEnable/OnDisable), zero per-frame cost.
    /// </summary>
    public class SetActiveSync : MonoBehaviour
    {
        [Tooltip("Primary target GameObject to synchronize with")]
        public GameObject target;

        [Tooltip("Optional additional target GameObjects")]
        public GameObject[] targets;

        [Tooltip("Invert active state (Active -> Inactive, Inactive -> Active)")]
        public bool invert;

        private void OnEnable() => Sync(true);
        private void OnDisable() => Sync(false);

        private void Sync(bool active)
        {
            bool state = invert ? !active : active;

            if (target != null && target != gameObject && target.activeSelf != state)
            {
                target.SetActive(state);
            }

            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var t = targets[i];
                    if (t != null && t != gameObject && t.activeSelf != state)
                    {
                        t.SetActive(state);
                    }
                }
            }
        }
    }
}

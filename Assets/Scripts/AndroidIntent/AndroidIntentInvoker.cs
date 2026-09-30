using UnityEngine;

namespace AndroidIntent
{
    /// <summary>
    /// Generic invoker MonoBehaviour.
    /// Bind this to a UI Button's OnClick() event, then pass any AndroidIntentData asset as the argument.
    /// </summary>
    public class AndroidIntentInvoker : MonoBehaviour
    {
        /// <summary>
        /// Launch the provided AndroidIntentData asset.
        /// Drag any .asset into the Button OnClick argument slot in the Inspector.
        /// </summary>
        public void Invoke(AndroidIntentData data)
        {
            AndroidIntentUtility.SendIntent(data);
        }
    }
}

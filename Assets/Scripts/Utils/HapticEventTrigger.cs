using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

public class HapticEventTrigger : MonoBehaviour
{
    private const string TAG = "[HapticEventTrigger] ";
    public enum HapticHand
    {
        Left,
        Right,
        Both
    }

    [System.Serializable]
    public class HapticProfile
    {
        public string profileName = "New Haptic";
        [Tooltip("Select target controller (Left/Right/Both)")]
        public HapticHand targetHand = HapticHand.Both;

        [Range(0f, 1f), Tooltip("Base haptic amplitude (0-1)")]
        public float amplitude = 0.5f;
        [Tooltip("Haptic duration in seconds")]
        public float duration = 0.1f;
        [Tooltip("Use animation curve to modulate haptic amplitude")]
        public bool useCurve = false;
        [Tooltip("Amplitude curve (X: Normalized Time 0-1, Y: Amplitude Multiplier 0-1)")]
        public AnimationCurve amplitudeCurve = AnimationCurve.Linear(0, 1, 1, 1);
    }

    public HapticImpulsePlayer hapticPlayerLeft;
    public HapticImpulsePlayer hapticPlayerRight;
    
    [Tooltip("Add haptic profiles here. Call TriggerHapticByName with profileName to trigger.")]
    public List<HapticProfile> hapticProfiles = new List<HapticProfile>();

    [Header("Default Haptic Settings (Obsolete)")]
    [Range(0f, 1f)]
    public float amplitude = 0.5f; // Default haptic amplitude
    public float duration = 0.1f;  // Default duration in seconds

    private void Reset()
    {
        AutoFindHapticPlayers();
    }

    private void Awake()
    {
        if (hapticPlayerLeft == null || hapticPlayerRight == null)
        {
            AutoFindHapticPlayers();
        }
    }

    [ContextMenu("Auto Find Haptic Players")]
    public void AutoFindHapticPlayers()
    {
        if (hapticPlayerLeft != null && hapticPlayerRight != null) return;

        // Find all haptic players in the scene, including inactive ones
        HapticImpulsePlayer[] players = Resources.FindObjectsOfTypeAll<HapticImpulsePlayer>();
        
        foreach (var player in players)
        {
            // Skip prefabs, only care about scene objects
            if (player.gameObject.scene.name == null) continue;

            string pathName = GetTransformPath(player.transform).ToLower();
            
            if (hapticPlayerLeft == null && (pathName.Contains("left") || pathName.Contains("_l")))
            {
                hapticPlayerLeft = player;
            }
            else if (hapticPlayerRight == null && (pathName.Contains("right") || pathName.Contains("_r")))
            {
                hapticPlayerRight = player;
            }
        }
    }

    private string GetTransformPath(Transform t)
    {
        string path = t.name;
        Transform current = t;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }
        return path;
    }

    /// <summary>
    /// Trigger haptic profile by name. Can be called from UnityEvents with a string parameter.
    /// </summary>
    public void TriggerHapticByName(string profileName)
    {
        HapticProfile profile = hapticProfiles.Find(p => p.profileName == profileName);
        if (profile != null)
        {
            PlayHaptic(profile);
        }
        else
        {
            Logger.LogApp($"{TAG}Haptic profile '{profileName}' not found!", LogType.Warning);
        }
    }

    /// <summary>
    /// Trigger haptic profile by index. Can be called from UnityEvents with an int parameter.
    /// </summary>
    public void TriggerHapticByIndex(int index)
    {
        if (index >= 0 && index < hapticProfiles.Count)
        {
            PlayHaptic(hapticProfiles[index]);
        }
        else
        {
            Logger.LogApp($"{TAG}Haptic profile index {index} out of range!", LogType.Warning);
        }
    }

    private void PlayHaptic(HapticProfile profile)
    {
        if (profile.useCurve)
        {
            StartCoroutine(PlayHapticCurveRoutine(profile));
        }
        else
        {
            if ((profile.targetHand == HapticHand.Left || profile.targetHand == HapticHand.Both) && hapticPlayerLeft != null)
            {
                hapticPlayerLeft.SendHapticImpulse(profile.amplitude, profile.duration);
            }

            if ((profile.targetHand == HapticHand.Right || profile.targetHand == HapticHand.Both) && hapticPlayerRight != null)
            {
                hapticPlayerRight.SendHapticImpulse(profile.amplitude, profile.duration);
            }
        }
    }

    private IEnumerator PlayHapticCurveRoutine(HapticProfile profile)
    {
        float elapsed = 0f;
        while (elapsed < profile.duration)
        {
            float normalizedTime = elapsed / profile.duration;
            // Get curve multiplier and apply to base amplitude
            float currentAmplitude = profile.amplitudeCurve.Evaluate(normalizedTime) * profile.amplitude;
            
            // Use slightly larger duration than deltaTime to prevent haptic dropouts on frame drops
            float impulseDuration = Mathf.Max(Time.deltaTime, 0.05f);

            if ((profile.targetHand == HapticHand.Left || profile.targetHand == HapticHand.Both) && hapticPlayerLeft != null)
            {
                hapticPlayerLeft.SendHapticImpulse(currentAmplitude, impulseDuration);
            }

            if ((profile.targetHand == HapticHand.Right || profile.targetHand == HapticHand.Both) && hapticPlayerRight != null)
            {
                hapticPlayerRight.SendHapticImpulse(currentAmplitude, impulseDuration);
            }

            yield return null;
            elapsed += Time.deltaTime;
        }

        // Send 0 amplitude impulse at the end to ensure haptics stop completely
        if ((profile.targetHand == HapticHand.Left || profile.targetHand == HapticHand.Both) && hapticPlayerLeft != null)
        {
            hapticPlayerLeft.SendHapticImpulse(0f, 0.01f);
        }
        if ((profile.targetHand == HapticHand.Right || profile.targetHand == HapticHand.Both) && hapticPlayerRight != null)
        {
            hapticPlayerRight.SendHapticImpulse(0f, 0.01f);
        }
    }

    public void SendHapticImpulseLeft(float amplitude, float duration)
    {
        if (hapticPlayerLeft != null)
        {
            hapticPlayerLeft.SendHapticImpulse(amplitude, duration);
        }
    }

    public void SendHapticImpulseRight(float amplitude, float duration)
    {
        if (hapticPlayerRight != null)
        {
            hapticPlayerRight.SendHapticImpulse(amplitude, duration);
        }
    }

    public void SendHapticImpulseBoth(float amplitude, float duration)
    {
        SendHapticImpulseLeft(amplitude, duration);
        SendHapticImpulseRight(amplitude, duration);
    }

    // Legacy methods preserved for backward compatibility
    public void TriggerHapticLeft()
    {
        if (hapticPlayerLeft != null)
        {
            hapticPlayerLeft.SendHapticImpulse(amplitude, duration);
        }
    }

    public void TriggerHapticRight()
    {
        if (hapticPlayerRight != null)
        {
            hapticPlayerRight.SendHapticImpulse(amplitude, duration);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.NativeTypes;

#if META_XR_SDK

/// <summary>
/// Controller for managing Meta Quest boundary visibility suppression (Boundaryless / Contextual Boundaryless mode).
/// Strictly guards all Boundary API calls until Passthrough is fully initialized and active.
/// </summary>
public class MetaXRBoundaryVisibilityController : MonoBehaviour
{
    private const string TAG = "[MetaXRBoundary] ";

    [Tooltip("If enabled, boundary will be automatically suppressed once Passthrough is fully active.")]
    [SerializeField] private bool suppressOnPassthroughStart = false;

    [Tooltip("Automatically activate OVRPassthroughLayer when suppressing boundary.")]
    [SerializeField] private bool autoEnablePassthrough = true;

    [SerializeField] private OVRPassthroughLayer passthroughLayer;

    [Tooltip("Invoked whenever the boundary suppression state changes. Safe to bind to Toggle.SetIsOnWithoutNotify.")]
    public UnityEvent<bool> onBoundarySuppressionChanged;

    private BoundaryVisibilityFeature _openXrFeature;
    private bool _desiredSuppressedState = false;
    private bool _lastReportedSuppressedState = false;
    private bool _lastPassthroughActive = false;

    public BoundaryVisibilityFeature OpenXRFeature
    {
        get
        {
            if (_openXrFeature == null && OpenXRSettings.Instance != null)
            {
                _openXrFeature = OpenXRSettings.Instance.GetFeature<BoundaryVisibilityFeature>();
            }
            return _openXrFeature;
        }
    }

    /// <summary>
    /// Checks if Passthrough is fully initialized and active in the runtime.
    /// </summary>
    public bool IsPassthroughActive
    {
        get
        {
            if (passthroughLayer != null && !passthroughLayer.enabled)
            {
                return false;
            }

            if (OVRManager.instance != null)
            {
                return OVRManager.instance.isInsightPassthroughEnabled && OVRManager.IsInsightPassthroughInitialized();
            }
            return OVRPlugin.IsInsightPassthroughInitialized();
        }
    }

    /// <summary>
    /// Returns true if boundary visibility is currently suppressed by the system.
    /// </summary>
    public bool IsSuppressed
    {
        get
        {
            if (OpenXRFeature != null && OpenXRFeature.enabled)
            {
                return OpenXRFeature.currentVisibility == XrBoundaryVisibility.VisibilitySuppressed;
            }

            if (OVRManager.instance != null)
            {
                return OVRManager.instance.isBoundaryVisibilitySuppressed;
            }

            var res = OVRPlugin.GetBoundaryVisibility(out var visibility);
            return res == OVRPlugin.Result.Success && visibility == OVRPlugin.BoundaryVisibility.Suppressed;
        }
    }

    private void Start()
    {
        if (passthroughLayer == null)
        {
            Debug.LogError($"{TAG}passthroughLayer is not assigned! Disabling MetaXRBoundaryVisibilityController.");
            enabled = false;
            return;
        }

        // Subscribe only to OpenXR feature if enabled, otherwise fallback to OVRManager event
        var feature = OpenXRFeature;
        if (feature != null && feature.enabled)
        {
            feature.boundaryVisibilityChanged += OnOpenXRBoundaryVisibilityChanged;
        }
        else
        {
            OVRManager.BoundaryVisibilityChanged += OnOVRBoundaryVisibilityChanged;
        }

        _lastPassthroughActive = IsPassthroughActive;

        if (suppressOnPassthroughStart)
        {
            SetBoundarySuppressed(true);
        }
        else
        {
            _lastReportedSuppressedState = IsSuppressed;
            onBoundarySuppressionChanged?.Invoke(_lastReportedSuppressedState);
        }
    }

    private void Update()
    {
        bool currentPtActive = IsPassthroughActive;

        // Strictly wait until Passthrough state flips to active before calling any boundary methods
        if (currentPtActive != _lastPassthroughActive)
        {
            _lastPassthroughActive = currentPtActive;

            if (currentPtActive && _desiredSuppressedState && !IsSuppressed)
            {
                ExecuteRequest(true);
            }
        }
    }

    private void OnDestroy()
    {
        var feature = OpenXRFeature;
        if (feature != null)
        {
            feature.boundaryVisibilityChanged -= OnOpenXRBoundaryVisibilityChanged;
        }
        OVRManager.BoundaryVisibilityChanged -= OnOVRBoundaryVisibilityChanged;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && _desiredSuppressedState && IsPassthroughActive && !IsSuppressed)
        {
            ExecuteRequest(true);
        }
    }

    private void OnOpenXRBoundaryVisibilityChanged(object sender, XrBoundaryVisibility visibility)
    {
        bool isSuppressed = visibility == XrBoundaryVisibility.VisibilitySuppressed;
        if (isSuppressed != _lastReportedSuppressedState)
        {
            _lastReportedSuppressedState = isSuppressed;
            Logger.LogApp($"{TAG}System boundary visibility changed (OpenXR): {visibility}");
            onBoundarySuppressionChanged?.Invoke(isSuppressed);
        }
    }

    private void OnOVRBoundaryVisibilityChanged(OVRPlugin.BoundaryVisibility visibility)
    {
        bool isSuppressed = visibility == OVRPlugin.BoundaryVisibility.Suppressed;
        if (isSuppressed != _lastReportedSuppressedState)
        {
            _lastReportedSuppressedState = isSuppressed;
            Logger.LogApp($"{TAG}System boundary visibility changed (OVR): {visibility}");
            onBoundarySuppressionChanged?.Invoke(isSuppressed);
        }
    }

    /// <summary>
    /// Suppresses boundary visibility so the user can move freely without boundary popups while in Passthrough.
    /// </summary>
    public void SuppressBoundary()
    {
        SetBoundarySuppressed(true);
    }

    /// <summary>
    /// Restores default boundary visibility.
    /// </summary>
    public void RestoreBoundary()
    {
        SetBoundarySuppressed(false);
    }

    /// <summary>
    /// Sets boundary suppression state.
    /// Guarantees no boundary methods are invoked if Passthrough is not yet active.
    /// </summary>
    /// <param name="suppress">True to suppress boundary; False to restore normal boundary visibility.</param>
    public void SetBoundarySuppressed(bool suppress)
    {
        _desiredSuppressedState = suppress;

        if (suppress && autoEnablePassthrough)
        {
            if (passthroughLayer != null)
            {
                passthroughLayer.enabled = true;
            }
            if (OVRManager.instance != null)
            {
                OVRManager.instance.isInsightPassthroughEnabled = true;
            }
        }

        // Only invoke boundary methods if Passthrough is actually active
        if (IsPassthroughActive)
        {
            ExecuteRequest(suppress);
        }
        else
        {
            Logger.LogApp($"{TAG}Passthrough is not active yet. Boundary methods will not be called until Passthrough is fully initialized.");
        }
    }

    private bool ExecuteRequest(bool suppress)
    {
        if (!IsPassthroughActive)
        {
            return false;
        }

        // Sync desired state to OVRManager only when Passthrough is verified active
        if (OVRManager.instance != null)
        {
            OVRManager.instance.shouldBoundaryVisibilityBeSuppressed = suppress;
        }

        var feature = OpenXRFeature;
        if (feature != null && feature.enabled)
        {
            var targetVisibility = suppress ? XrBoundaryVisibility.VisibilitySuppressed : XrBoundaryVisibility.VisibilityNotSuppressed;
            XrResult result = RequestBoundaryVisibility(targetVisibility);
            return result == XrResult.Success;
        }

        var ovrpTarget = suppress ? OVRPlugin.BoundaryVisibility.Suppressed : OVRPlugin.BoundaryVisibility.NotSuppressed;
        var pluginRes = OVRPlugin.RequestBoundaryVisibility(ovrpTarget);
        if (pluginRes == OVRPlugin.Result.Success)
        {
            Logger.LogApp($"{TAG}OVRPlugin.RequestBoundaryVisibility({ovrpTarget}) succeeded.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Direct low-level call to TryRequestBoundaryVisibility on BoundaryVisibilityFeature.
    /// </summary>
    public XrResult RequestBoundaryVisibility(XrBoundaryVisibility visibility)
    {
        var feature = OpenXRFeature;
        if (feature == null || !feature.enabled)
        {
            Logger.LogApp($"{TAG}BoundaryVisibilityFeature is not available or not enabled.", LogType.Warning);
            return XrResult.FeatureUnsupported;
        }

        XrResult result = feature.TryRequestBoundaryVisibility(visibility);

        if (result == XrResult.Success)
        {
            Logger.LogApp($"{TAG}Successfully requested boundary visibility: {visibility}");
        }
        else if ((int)result == BoundaryVisibilityFeature.XR_BOUNDARY_VISIBILITY_SUPPRESSION_NOT_ALLOWED_META)
        {
            Logger.LogApp($"{TAG}Boundary suppression not allowed by system.", LogType.Warning);
        }
        else
        {
            Logger.LogApp($"{TAG}Request boundary visibility failed with result: {result}", LogType.Error);
        }

        return result;
    }
}

#else

public class MetaXRBoundaryVisibilityController : MonoBehaviour
{
    private const string TAG = "[MetaXRBoundary] ";

    public UnityEvent<bool> onBoundarySuppressionChanged;

    public void SuppressBoundary()
    {
        Logger.LogApp($"{TAG}META_XR_SDK is not defined on the current build target.", LogType.Warning);
    }

    public void RestoreBoundary()
    {
        Logger.LogApp($"{TAG}META_XR_SDK is not defined on the current build target.", LogType.Warning);
    }

    public void SetBoundarySuppressed(bool suppress)
    {
        Logger.LogApp($"{TAG}META_XR_SDK is not defined on the current build target.", LogType.Warning);
    }
}

#endif

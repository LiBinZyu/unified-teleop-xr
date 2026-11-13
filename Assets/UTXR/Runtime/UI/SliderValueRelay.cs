
using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Slider))]
public sealed class SliderValueRelay : MonoBehaviour
{
    public enum ValueMode
    {
        RawValue,
        Normalized,
        Percentage
    }

    public enum PrecisionMode
    {
        Auto,
        Integer,
        FixedDecimals,
        MaxDecimals
    }

    [Serializable]
    public sealed class StringEvent : UnityEvent<string> { }

    [Min(1)]
    private int maxUpdatesPerSecond = 5;
    [SerializeField]
    private ValueMode valueMode = ValueMode.RawValue;
    [SerializeField]
    private PrecisionMode precisionMode = PrecisionMode.Auto;
    [Range(0, 8)]
    private int decimalPlaces = 4;
    [SerializeField]
    private bool useThousandsSeparator = false;
    [SerializeField]
    private bool useCurrentCulture = false;
    [SerializeField] private string prefix = "";
    [SerializeField] private string suffix = "";
    [SerializeField]
    private StringEvent onTextChanged = new StringEvent();

    private Slider _slider;
    private Coroutine _pendingCoroutine;

    private bool _hasPending;
    private float _pendingValue;

    private bool _hasPublished;
    private float _lastPublishedTime;
    private string _lastText;

    private void OnEnable()
    {
        _slider = GetComponent<Slider>();

        _hasPending = false;
        _hasPublished = false;
        _lastText = null;

        _slider.onValueChanged.AddListener(OnSliderValueChanged);

        // Immediately display the initial value.
        OnSliderValueChanged(_slider.value);
    }

    private void OnDisable()
    {
        if (_slider != null)
            _slider.onValueChanged.RemoveListener(OnSliderValueChanged);

        if (_pendingCoroutine != null)
        {
            StopCoroutine(_pendingCoroutine);
            _pendingCoroutine = null;
        }

        _hasPending = false;
    }

    private void OnSliderValueChanged(float value)
    {
        // Only retain the newest value.
        _pendingValue = value;
        _hasPending = true;

        TryPublish();

        if (_hasPending && _pendingCoroutine == null)
        {
            _pendingCoroutine =
                StartCoroutine(PublishPendingWhenAllowed());
        }
    }

    private IEnumerator PublishPendingWhenAllowed()
    {
        while (_hasPending)
        {
            float interval =
                1f / Mathf.Max(1, maxUpdatesPerSecond);

            float remaining =
                interval - (Time.unscaledTime - _lastPublishedTime);

            if (remaining > 0f)
                yield return new WaitForSecondsRealtime(remaining);

            TryPublish();
        }

        _pendingCoroutine = null;
    }

    private void TryPublish()
    {
        if (!_hasPending)
            return;

        float now = Time.unscaledTime;

        float interval =
            1f / Mathf.Max(1, maxUpdatesPerSecond);

        if (_hasPublished &&
            now - _lastPublishedTime < interval)
        {
            return;
        }

        float value = _pendingValue;
        _hasPending = false;

        string text = FormatValue(value);

        // Avoid redundant UI updates.
        if (text == _lastText)
            return;

        _lastText = text;
        _lastPublishedTime = now;
        _hasPublished = true;

        onTextChanged.Invoke(text);
    }

    private string FormatValue(float rawValue)
    {
        float displayValue = rawValue;

        if (valueMode != ValueMode.RawValue)
        {
            float normalized = Mathf.InverseLerp(
                _slider.minValue,
                _slider.maxValue,
                rawValue
            );

            displayValue = valueMode == ValueMode.Percentage
                ? normalized * 100f
                : normalized;
        }

        bool integerMode =
            precisionMode == PrecisionMode.Integer ||
            (precisionMode == PrecisionMode.Auto &&
             valueMode == ValueMode.RawValue &&
             _slider.wholeNumbers);

        int digits = Mathf.Clamp(decimalPlaces, 0, 8);

        string format =
            useThousandsSeparator ? "#,##0" : "0";

        if (!integerMode && digits > 0)
        {
            bool fixedDecimals =
                precisionMode == PrecisionMode.FixedDecimals;

            format += "." + new string(
                fixedDecimals ? '0' : '#',
                digits
            );
        }

        CultureInfo culture = useCurrentCulture
            ? CultureInfo.CurrentCulture
            : CultureInfo.InvariantCulture;

        return (prefix ?? "") +
               displayValue.ToString(format, culture) +
               (valueMode == ValueMode.Percentage ? "%" : "") +
               (suffix ?? "");
    }

    public void RefreshCurrentValue()
    {
        if (!isActiveAndEnabled || _slider == null)
            return;

        OnSliderValueChanged(_slider.value);
    }

    private void OnValidate()
    {
        maxUpdatesPerSecond =
            Mathf.Max(1, maxUpdatesPerSecond);

        decimalPlaces =
            Mathf.Clamp(decimalPlaces, 0, 8);
    }
}

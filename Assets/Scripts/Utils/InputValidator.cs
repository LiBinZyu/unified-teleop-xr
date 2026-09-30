using UnityEngine;
using TMPro;
using UnityEngine.Events;

[RequireComponent(typeof(TMP_InputField))]
public class InputValidator : MonoBehaviour
{
    public enum ValidationType
    {
        IntegerRange,       // 限制整数范围（如：身高 50-250）
        FloatRange,         // 限制浮点数范围（如：体重 30.5-200.0）
        StringLength,       // 限制字符串长度（如：密码至少6位）
        NotEmpty,           // 不能为空
        IPAddress,          // IPv4地址（如：192.168.1.1）
        IPAddressWithPort   // IPv4地址+端口（如：192.168.1.1:8080）
    }

    public ValidationType validationType = ValidationType.IntegerRange;

    [Header("Integer/Float Mode Setting")]
    public float minValue = 50f;
    public float maxValue = 250f;

    [Header("String Mode Setting")]
    public int minLength = 1;
    public int maxLength = 20;

    public UnityEvent OnInputValid;
    public UnityEvent OnInputInvalid;

    private TMP_InputField inputField;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        
        if (inputField != null)
        {
            inputField.onValueChanged.AddListener(ValidateInput);
        }
    }

    private void Start()
    {
        if (inputField != null)
        {
            ValidateInput(inputField.text);
        }
    }

    private void ValidateInput(string text)
    {
        bool isValid = false;

        // 默认空字符串按规则处理
        if (string.IsNullOrEmpty(text) && validationType != ValidationType.StringLength)
        {
            OnInputInvalid.Invoke();
            return;
        }

        switch (validationType)
        {
            case ValidationType.IntegerRange:
                if (int.TryParse(text, out int intVal))
                    isValid = (intVal >= minValue && intVal <= maxValue);
                break;

            case ValidationType.FloatRange:
                if (float.TryParse(text, out float floatVal))
                    isValid = (floatVal >= minValue && floatVal <= maxValue);
                break;

            case ValidationType.StringLength:
                isValid = (text.Length >= minLength && text.Length <= maxLength);
                break;
                
            case ValidationType.NotEmpty:
                isValid = !string.IsNullOrWhiteSpace(text);
                break;

            case ValidationType.IPAddress:
                isValid = IsValidIPv4(text);
                break;

            case ValidationType.IPAddressWithPort:
                string[] parts = text.Split(':');
                if (parts.Length == 2)
                {
                    if (IsValidIPv4(parts[0]) && int.TryParse(parts[1], out int port))
                        isValid = (port >= 0 && port <= 65535);
                }
                break;
        }

        if (isValid) OnInputValid.Invoke();
        else OnInputInvalid.Invoke();
    }

    private bool IsValidIPv4(string ipString)
    {
        if (string.IsNullOrWhiteSpace(ipString)) return false;

        string[] splitValues = ipString.Split('.');
        if (splitValues.Length != 4) return false;

        foreach (string val in splitValues)
        {
            if (!byte.TryParse(val, out _)) return false;
        }

        return true;
    }

    private void OnDestroy()
    {
        if (inputField != null)
        {
            inputField.onValueChanged.RemoveListener(ValidateInput);
        }
    }
}
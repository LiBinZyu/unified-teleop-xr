using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class CustomKeyboardNumber : MonoBehaviour
{
    [Header("Input Fields List")]
    public List<TMP_InputField> inputFields = new List<TMP_InputField>();

    [Header("Buttons (Auto-Filled)")]
    [SerializeField, ReadOnly] protected Button button0;
    [SerializeField, ReadOnly] protected Button button1;
    [SerializeField, ReadOnly] protected Button button2;
    [SerializeField, ReadOnly] protected Button button3;
    [SerializeField, ReadOnly] protected Button button4;
    [SerializeField, ReadOnly] protected Button button5;
    [SerializeField, ReadOnly] protected Button button6;
    [SerializeField, ReadOnly] protected Button button7;
    [SerializeField, ReadOnly] protected Button button8;
    [SerializeField, ReadOnly] protected Button button9;

    [SerializeField, ReadOnly] protected Button dotButton;    // 小数点按钮
    [SerializeField, ReadOnly] protected Button deleteButton; // 删除按钮
    
    [Header("Enter Button")]
    [SerializeField] protected Button enterButton; 

    private TMP_InputField activeInputField; // 当前唯一激活的输入框

    private void Reset()
    {
        AutoFillButtons();
    }

    [ContextMenu("Auto Fill Buttons")]
    public void AutoFillButtons()
    {
        if (button0 == null) button0 = FindButtonInChildren("0");
        if (button1 == null) button1 = FindButtonInChildren("1");
        if (button2 == null) button2 = FindButtonInChildren("2");
        if (button3 == null) button3 = FindButtonInChildren("3");
        if (button4 == null) button4 = FindButtonInChildren("4");
        if (button5 == null) button5 = FindButtonInChildren("5");
        if (button6 == null) button6 = FindButtonInChildren("6");
        if (button7 == null) button7 = FindButtonInChildren("7");
        if (button8 == null) button8 = FindButtonInChildren("8");
        if (button9 == null) button9 = FindButtonInChildren("9");
        if (dotButton == null) dotButton = FindButtonInChildren("period", ".");
        if (deleteButton == null) deleteButton = FindButtonInChildren("delete", "del");
        if (enterButton == null) enterButton = FindButtonInChildren("enter", "confirm");
    }

    private Button FindButtonInChildren(params string[] searchNames)
    {
        foreach (Transform child in transform)
        {
            foreach (string searchName in searchNames)
            {
                if (child.name.Equals(searchName, System.StringComparison.OrdinalIgnoreCase))
                {
                    Button btn = child.GetComponent<Button>();
                    if (btn != null) return btn;
                }
            }
        }
        return null;
    }

    void Start()
    {
        foreach (var field in inputFields)
        {
            if (field != null)
            {
                field.onSelect.AddListener((text) => SetActiveInputField(field));
                field.shouldHideSoftKeyboard = true;
            }
        }

        BindNumberButton(button0, "0");
        BindNumberButton(button1, "1");
        BindNumberButton(button2, "2");
        BindNumberButton(button3, "3");
        BindNumberButton(button4, "4");
        BindNumberButton(button5, "5");
        BindNumberButton(button6, "6");
        BindNumberButton(button7, "7");
        BindNumberButton(button8, "8");
        BindNumberButton(button9, "9");

        if (dotButton != null) dotButton.onClick.AddListener(() => OnInputChar("."));
        if (deleteButton != null) deleteButton.onClick.AddListener(OnDelete);
        
        if (enterButton != null)
        {
            enterButton.onClick.AddListener(OnEnterPressed);
        }
    }

    private void BindNumberButton(Button btn, string numberStr)
    {
        if (btn != null) btn.onClick.AddListener(() => OnInputChar(numberStr));
    }

    public void SetActiveInputField(TMP_InputField field)
    {
        if (field == null) return;
        if (activeInputField != null && activeInputField != field)
        {
            activeInputField.onEndEdit?.Invoke(activeInputField.text);
            activeInputField.DeactivateInputField();
        }
        activeInputField = field;
        activeInputField.ActivateInputField();
    }

    public void OnInputChar(string c)
    {
        if (activeInputField != null)
        {
            if (c == "." && (activeInputField.contentType == TMP_InputField.ContentType.IntegerNumber || 
                activeInputField.characterValidation == TMP_InputField.CharacterValidation.Integer))
            {
                return;
            }

            activeInputField.text += c;
            activeInputField.caretPosition = activeInputField.text.Length;
            activeInputField.ActivateInputField();
        }
    }
    public void OnDelete()
    {
        if (activeInputField != null && activeInputField.text.Length > 0)
        {
            activeInputField.text = activeInputField.text.Substring(0, activeInputField.text.Length - 1);
            activeInputField.caretPosition = activeInputField.text.Length;

            activeInputField.ActivateInputField();
        }
    }
    public void OnEnterPressed()
    {
        if (activeInputField != null)
        {
            activeInputField.onEndEdit?.Invoke(activeInputField.text);
            activeInputField.onEndEdit?.Invoke(activeInputField.text);
            activeInputField.DeactivateInputField();
            activeInputField = null;
        }
    }
}
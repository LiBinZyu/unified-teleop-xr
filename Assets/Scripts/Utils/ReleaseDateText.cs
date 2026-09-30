using UnityEngine;
using TMPro;
using System;

[RequireComponent(typeof(TMP_Text))]
public class ReleaseDateText : MonoBehaviour
{
    private TMP_Text _textComponent;

    private void Awake()
    {
        _textComponent = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        if (_textComponent != null)
        {
            string tagStr;
#if UNITY_EDITOR
            tagStr = DateTime.Now.ToString("yyMMdd");
#else
            TextAsset buildDateAsset = Resources.Load<TextAsset>("BuildVersion");
            tagStr = buildDateAsset != null ? buildDateAsset.text.Trim() : DateTime.Now.ToString("yyMMdd");
#endif
            _textComponent.text =  tagStr;
        }
    }
}

using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Apex.Audio;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using Newtonsoft.Json;
#endif

[RequireComponent(typeof(UILanguageManager))]
public class UiLocalization : MonoBehaviour
{
    public LanguageType defaultLanguage = LanguageType.en_Au; 

    private Dictionary<TextMeshProUGUI, string> _bindings = new Dictionary<TextMeshProUGUI, string>();
    private Dictionary<TMP_Dropdown, List<string>> _dropdownBindings = new Dictionary<TMP_Dropdown, List<string>>();
    private static readonly Regex ExtractRegex = new Regex(@"\{([^\{\}]+(?:\{#[^\{\}]+\})?)\}");

    private System.Collections.IEnumerator Start()
    {
        yield return null;

        ScanAndBind();
        RefreshAllUI();

        if (UILanguageManager.Instance != null)
        {
            UILanguageManager.Instance.OnLanguageChanged += RefreshAllUI;
        }
    }

    private void OnDestroy()
    {
        if (UILanguageManager.Instance != null)
        {
            UILanguageManager.Instance.OnLanguageChanged -= RefreshAllUI;
        }
    }

    public void ScanAndBind()
    {
        ScanAndBindTexts();
        ScanAndBindDropdowns();
        Logger.LogApp($"[UiLocalization] ScanAndBind: Registered {_bindings.Count} localizable text bindings and {_dropdownBindings.Count} dropdown bindings.");
    }

    public void ScanAndBindTexts()
    {
        _bindings.Clear();
        var allTexts = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var textComp in allTexts)
        {
            if (string.IsNullOrWhiteSpace(textComp.text)) continue;
            
            if (textComp.text.Contains("{") && textComp.text.Contains("}"))
            {
                _bindings[textComp] = textComp.text;
            }
        }
    }

    public void ScanAndBindDropdowns()
    {
        _dropdownBindings.Clear();
        var allDropdowns = Object.FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var dropdown in allDropdowns)
        {
            if (dropdown == null) continue;

            var originalOptions = new List<string>();
            foreach (var option in dropdown.options)
            {
                originalOptions.Add(option.text);
            }
            _dropdownBindings[dropdown] = originalOptions;
        }
    }

    public void RefreshAllUI()
    {
        if (UILanguageManager.Instance == null) return;

        foreach (var kvp in _bindings)
        {
            var targetUI = kvp.Key;
            var originalTemplate = kvp.Value;
            if (targetUI == null) continue;

            targetUI.text = ExtractRegex.Replace(originalTemplate, match =>
            {
                string rawKey = match.Groups[1].Value;
                return UILanguageManager.Instance.GetText(rawKey);
            });
        }

        foreach (var kvp in _dropdownBindings)
        {
            var dropdown = kvp.Key;
            var originalOptions = kvp.Value;
            if (dropdown == null) continue;

            for (int i = 0; i < dropdown.options.Count && i < originalOptions.Count; i++)
            {
                dropdown.options[i].text = UILanguageManager.Instance.GetText(originalOptions[i]);
            }
            dropdown.RefreshShownValue();
        }

        Logger.LogApp($"[UiLocalization] RefreshAllUI: Refreshed {_bindings.Count} UI text elements and {_dropdownBindings.Count} dropdowns to {UILanguageManager.Instance.CurrentLanguage}.");
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(UiLocalization))]
public class UiLocalizationEditor : Editor
{
    private static readonly Regex ExtractRegex = new Regex(@"\{([^\{\}]+(?:\{#[^\{\}]+\})?)\}");
    private static readonly string JsonRelativePath = "Assets/Resources/ui_localization.json";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        UiLocalization hub = (UiLocalization)target;

        GUILayout.Space(20);
        
        if (GUILayout.Button("Export JSON", GUILayout.Height(30)))
        {
            ExecuteExport(hub.defaultLanguage);
        }

        GUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "GUIDE.\n" +
            "1. Enter text directly into the Text field, e.g., {Start Game} or {Confirm{#context}}.\n" +
            "2. Supports parameter formatting: {Please enter: [IP address]}\n" +
            "3. Click the button above to export json. Then you translate it by your own.",
            MessageType.Info);
    }

    private void ExecuteExport(Apex.Audio.LanguageType defaultLang)
    {
        string fullPath = Path.Combine(Application.dataPath, JsonRelativePath.Replace("Assets/", ""));
        
        var localizationDb = new Dictionary<string, Dictionary<string, string>>();
        if (File.Exists(fullPath))
        {
            try
            {
                string existingJson = File.ReadAllText(fullPath);
                localizationDb = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(existingJson) 
                                 ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch (System.Exception e)
            {
                Logger.LogApp($"[Auto Exporter] Failed to parse existing JSON: {e.Message}", LogType.Error);
                return;
            }
        }

        TextMeshProUGUI[] allTexts = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        
        int newKeysCount = 0;
        int updatedKeysCount = 0;

        System.Action<string, string> registerKey = (key, defaultVal) =>
        {
            if (!localizationDb.ContainsKey(key))
            {
                var dict = new Dictionary<string, string>();
                foreach (Apex.Audio.LanguageType lang in System.Enum.GetValues(typeof(Apex.Audio.LanguageType)))
                {
                    dict[lang.ToString()] = (lang == defaultLang) ? defaultVal : "";
                }
                localizationDb[key] = dict;
                newKeysCount++;
            }
            else
            {
                string defaultLangStr = defaultLang.ToString();
                if (!localizationDb[key].ContainsKey(defaultLangStr) || string.IsNullOrEmpty(localizationDb[key][defaultLangStr]))
                {
                    localizationDb[key][defaultLangStr] = defaultVal;
                    updatedKeysCount++;
                }
            }
        };

        foreach (var textComp in allTexts)
        {
            if (string.IsNullOrWhiteSpace(textComp.text)) continue;

            MatchCollection matches = ExtractRegex.Matches(textComp.text);
            foreach (Match match in matches)
            {
                string rawKey = match.Groups[1].Value; 
                
                var paramMatches = System.Text.RegularExpressions.Regex.Matches(rawKey, @"\[([^\[\]]+)\]");
                if (paramMatches.Count > 0)
                {
                    string templateKey = rawKey;
                    for (int i = 0; i < paramMatches.Count; i++)
                    {
                        string paramPlaceholder = paramMatches[i].Value; 
                        string paramKey = paramMatches[i].Groups[1].Value; 

                        registerKey(paramKey, paramKey);

                        int index = templateKey.IndexOf(paramPlaceholder);
                        if (index >= 0)
                        {
                            templateKey = templateKey.Remove(index, paramPlaceholder.Length).Insert(index, "{" + i + "}");
                        }
                    }

                    registerKey(templateKey, templateKey);
                }
                else
                {
                    string cleanDisplayText = rawKey;
                    int contextIndex = rawKey.IndexOf("{#");
                    if (contextIndex >= 0)
                    {
                        cleanDisplayText = rawKey.Substring(0, contextIndex);
                    }

                    registerKey(rawKey, cleanDisplayText);
                }
            }
        }

        string directoryPath = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

        File.WriteAllText(fullPath, JsonConvert.SerializeObject(localizationDb, Formatting.Indented));
        AssetDatabase.Refresh();

        Logger.LogApp($"[Export Complete] Found {allTexts.Length} text components. Added {newKeysCount} keys, updated {updatedKeysCount} keys. Path: {JsonRelativePath}");
    }
}
#endif
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Apex.Audio;

[RequireComponent(typeof(UiLocalization))]
public class UILanguageManager : MonoBehaviour
{
    private const string TAG = "[UILanguageManager]";
    public static UILanguageManager Instance { get; private set; }
    
    public static string Get(string rawKey)
    {
        return Instance != null ? Instance.GetText(rawKey) : rawKey;
    }

    public event Action OnLanguageChanged;
    
    public LanguageType CurrentLanguage { get; private set; }

    private Dictionary<string, Dictionary<string, string>> _localizationDb;
    private static readonly Regex ParamRegex = new Regex(@"\[([^\[\]]+)\]");

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadDatabase();
            SyncLanguageFromPref();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SetLanguage(LanguageType newLang)
    {
        if (CurrentLanguage != newLang)
        {
            CurrentLanguage = newLang;
            PlayerPrefs.SetInt("Apex_Language", (int)newLang);
            PlayerPrefs.Save();
            Logger.LogApp($"{TAG} Language updated to: {CurrentLanguage}");
            OnLanguageChanged?.Invoke();
        }
    }

    private void LoadDatabase()
    {
        TextAsset jsonText = Resources.Load<TextAsset>("ui_localization");
        if (jsonText != null)
        {
            _localizationDb = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(jsonText.text);
            Logger.LogApp($"{TAG} Database loaded with {_localizationDb?.Count} entries.");
        }
        else
        {
            Logger.LogApp($"{TAG} ui_localization.json not found in Resources.", LogType.Error);
            _localizationDb = new Dictionary<string, Dictionary<string, string>>();
        }
    }

    public void SyncLanguageFromPref()
    {
        int langInt = PlayerPrefs.GetInt("Apex_Language", (int)LanguageType.en_Au); 
        LanguageType prefLanguage = (LanguageType)langInt;

        if (CurrentLanguage != prefLanguage)
        {
            CurrentLanguage = prefLanguage;
            Logger.LogApp($"{TAG} Language synced from PlayerPrefs to: {CurrentLanguage}");
            OnLanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// Get translation text in local language
    /// </summary>
    public string GetText(string rawKey)
    {
        if (_localizationDb == null) return rawKey;

        var paramMatches = ParamRegex.Matches(rawKey);
        if (paramMatches.Count > 0)
        {
            string templateKey = rawKey;
            List<string> translatedParams = new List<string>();

            for (int i = 0; i < paramMatches.Count; i++)
            {
                string paramPlaceholder = paramMatches[i].Value; 
                string paramKey = paramMatches[i].Groups[1].Value; 

                int index = templateKey.IndexOf(paramPlaceholder);
                if (index >= 0)
                {
                    templateKey = templateKey.Remove(index, paramPlaceholder.Length).Insert(index, "{" + i + "}");
                }

                translatedParams.Add(GetText(paramKey));
            }

            string translatedTemplate = GetTranslation(templateKey);

            try
            {
                return string.Format(translatedTemplate, translatedParams.ToArray());
            }
            catch (Exception ex)
            {
                Logger.LogApp($"{TAG} Error formatting template '{translatedTemplate}': {ex.Message}", LogType.Warning);
                return rawKey; 
            }
        }

        return GetTranslation(rawKey);
    }

    private string GetTranslation(string key)
    {
        string cleanKey = key;
        int contextIndex = key.IndexOf("{#");
        if (contextIndex >= 0)
        {
            cleanKey = key.Substring(0, contextIndex);
        }

        string langColumn = CurrentLanguage.ToString(); 

        if (_localizationDb.TryGetValue(key, out var translations))
        {
            if (translations.TryGetValue(langColumn, out var translatedText) && !string.IsNullOrEmpty(translatedText))
            {
                return translatedText;
            }
        }

        return cleanKey; 
    }
}
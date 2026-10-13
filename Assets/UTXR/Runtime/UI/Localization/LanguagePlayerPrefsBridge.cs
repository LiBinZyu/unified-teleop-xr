using UnityEngine;
using TMPro;
using UTXR.Prefs;
using UTXR.Audio;

namespace UTXR.UI.Localization
{
    /// <summary>
    /// Bridges the Language TMP_Dropdown to PlayerPrefs and synchronizes with UILanguageManager.
    /// </summary>
    public class LanguagePlayerPrefsBridge : MonoBehaviour
    {
        [Header("UI Dropdown Reference")]
        [SerializeField] private TMP_Dropdown languageDropdown;

        private void Start()
        {
            if (languageDropdown == null)
            {
                languageDropdown = GetComponent<TMP_Dropdown>();
            }

            LanguageType defaultLang = UILanguageManager.Instance != null 
                ? UILanguageManager.Instance.CurrentLanguage 
                : LanguageType.en_Au;

            PlayerPrefsManager.Bind(languageDropdown, UILanguageManager.PREF_KEY_LANGUAGE, defaultLang, lang =>
            {
                UILanguageManager.Instance?.SetLanguage(lang);
            });
        }
    }
}

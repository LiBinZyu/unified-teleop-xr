using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UTXR.Prefs
{
    /// <summary>
    /// Pure functional PlayerPrefs Utility & UI Data Binding Tool.
    /// Provides simple, high-performance two-way data binding for standard UI controls
    /// (Toggle, Slider, InputField, Dropdown) with debounced disk saving to prevent main-thread/render-frame stutters in XR.
    /// Completely decoupled from specific business features.
    /// </summary>
    public class PlayerPrefsManager : MonoBehaviour
    {
        private static PlayerPrefsManager _instance;
        private Coroutine _saveCoroutine;
        private bool _hasPendingSave;
        private const float SAVE_DEBOUNCE_DELAY = 0.5f;

        public static PlayerPrefsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PlayerPrefsManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[PlayerPrefsManager]");
                        _instance = go.AddComponent<PlayerPrefsManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        #region Functional UI Binding API

        /// <summary>
        /// Binds a Toggle to a boolean PlayerPref key.
        /// </summary>
        public static void Bind(Toggle toggle, string key, bool defaultValue, Action<bool> onChanged)
        {
            bool initial = UnityEngine.PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) == 1;
            onChanged?.Invoke(initial);

            if (toggle != null)
            {
                toggle.SetIsOnWithoutNotify(initial);
                toggle.onValueChanged.AddListener(val =>
                {
                    SetBool(key, val);
                    onChanged?.Invoke(val);
                });
            }
        }

        /// <summary>
        /// Binds a Slider to a float PlayerPref key.
        /// </summary>
        public static void Bind(Slider slider, string key, float defaultValue, Action<float> onChanged)
        {
            float initial = UnityEngine.PlayerPrefs.GetFloat(key, defaultValue);
            onChanged?.Invoke(initial);

            if (slider != null)
            {
                slider.SetValueWithoutNotify(initial);
                slider.onValueChanged.AddListener(val =>
                {
                    SetFloat(key, val);
                    onChanged?.Invoke(val);
                });
            }
        }

        /// <summary>
        /// Binds an integer Slider to an int PlayerPref key.
        /// </summary>
        public static void Bind(Slider slider, string key, int defaultValue, Action<int> onChanged)
        {
            int initial = UnityEngine.PlayerPrefs.GetInt(key, defaultValue);
            onChanged?.Invoke(initial);

            if (slider != null)
            {
                slider.wholeNumbers = true;
                slider.SetValueWithoutNotify(initial);
                slider.onValueChanged.AddListener(val =>
                {
                    int intVal = Mathf.RoundToInt(val);
                    SetInt(key, intVal);
                    onChanged?.Invoke(intVal);
                });
            }
        }

        /// <summary>
        /// Binds a TMP_InputField to a string PlayerPref key.
        /// </summary>
        public static void Bind(TMP_InputField inputField, string key, string defaultValue, Action<string> onChanged)
        {
            string initial = UnityEngine.PlayerPrefs.GetString(key, defaultValue ?? "");
            onChanged?.Invoke(initial);

            if (inputField != null)
            {
                inputField.SetTextWithoutNotify(initial);
                inputField.onEndEdit.AddListener(val =>
                {
                    string trimmed = val?.Trim() ?? "";
                    SetString(key, trimmed);
                    onChanged?.Invoke(trimmed);
                });
            }
        }

        /// <summary>
        /// Binds a TMP_Dropdown to an Enum PlayerPref key.
        /// </summary>
        public static void Bind<T>(TMP_Dropdown dropdown, string key, T defaultValue, Action<T> onChanged) where T : Enum
        {
            int defaultInt = Convert.ToInt32(defaultValue);
            int savedInt = UnityEngine.PlayerPrefs.GetInt(key, defaultInt);
            T initial = Enum.IsDefined(typeof(T), savedInt) ? (T)Enum.ToObject(typeof(T), savedInt) : defaultValue;
            onChanged?.Invoke(initial);

            if (dropdown != null)
            {
                dropdown.ClearOptions();
                var options = new List<string>(Enum.GetNames(typeof(T)));
                dropdown.AddOptions(options);
                dropdown.SetValueWithoutNotify(Convert.ToInt32(initial));
                dropdown.RefreshShownValue();

                dropdown.onValueChanged.AddListener(idx =>
                {
                    T selected = (T)Enum.ToObject(typeof(T), idx);
                    SetInt(key, idx);
                    onChanged?.Invoke(selected);
                });
            }
        }

        #endregion

        #region Getters & Setters

        public static bool GetBool(string key, bool defaultValue = false) =>
            UnityEngine.PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) == 1;

        public static void SetBool(string key, bool value)
        {
            UnityEngine.PlayerPrefs.SetInt(key, value ? 1 : 0);
            if (Application.isPlaying) Instance.ScheduleSave();
            else UnityEngine.PlayerPrefs.Save();
        }

        public static int GetInt(string key, int defaultValue = 0) =>
            UnityEngine.PlayerPrefs.GetInt(key, defaultValue);

        public static void SetInt(string key, int value)
        {
            UnityEngine.PlayerPrefs.SetInt(key, value);
            if (Application.isPlaying) Instance.ScheduleSave();
            else UnityEngine.PlayerPrefs.Save();
        }

        public static float GetFloat(string key, float defaultValue = 0f) =>
            UnityEngine.PlayerPrefs.GetFloat(key, defaultValue);

        public static void SetFloat(string key, float value)
        {
            UnityEngine.PlayerPrefs.SetFloat(key, value);
            if (Application.isPlaying) Instance.ScheduleSave();
            else UnityEngine.PlayerPrefs.Save();
        }

        public static string GetString(string key, string defaultValue = "") =>
            UnityEngine.PlayerPrefs.GetString(key, defaultValue);

        public static void SetString(string key, string value)
        {
            UnityEngine.PlayerPrefs.SetString(key, value ?? "");
            if (Application.isPlaying) Instance.ScheduleSave();
            else UnityEngine.PlayerPrefs.Save();
        }

        public static T GetEnum<T>(string key, T defaultValue) where T : Enum
        {
            int intVal = UnityEngine.PlayerPrefs.GetInt(key, Convert.ToInt32(defaultValue));
            return Enum.IsDefined(typeof(T), intVal) ? (T)Enum.ToObject(typeof(T), intVal) : defaultValue;
        }

        public static void SetEnum<T>(string key, T value) where T : Enum
        {
            UnityEngine.PlayerPrefs.SetInt(key, Convert.ToInt32(value));
            if (Application.isPlaying) Instance.ScheduleSave();
            else UnityEngine.PlayerPrefs.Save();
        }

        #endregion

        #region Debounced Save (Non-blocking disk write)

        public void ScheduleSave()
        {
            _hasPendingSave = true;
            if (_saveCoroutine != null)
                StopCoroutine(_saveCoroutine);

            if (gameObject.activeInHierarchy)
                _saveCoroutine = StartCoroutine(DebouncedSaveRoutine());
        }

        private IEnumerator DebouncedSaveRoutine()
        {
            yield return new WaitForSecondsRealtime(SAVE_DEBOUNCE_DELAY);
            SaveNow();
        }

        public void SaveNow()
        {
            if (_hasPendingSave)
            {
                UnityEngine.PlayerPrefs.Save();
                _hasPendingSave = false;
            }
            if (_saveCoroutine != null)
            {
                StopCoroutine(_saveCoroutine);
                _saveCoroutine = null;
            }
        }

        private void OnApplicationPause(bool pause) { if (pause) SaveNow(); }
        private void OnApplicationQuit() => SaveNow();
        private void OnDisable() => SaveNow();

        #endregion
    }
}

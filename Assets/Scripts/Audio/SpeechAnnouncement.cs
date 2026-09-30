using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apex.Audio
{
    public class SpeechAnnouncement : MonoBehaviour
    {
        public static SpeechAnnouncement Instance { get; private set; }

        private const string TAG = "[SpeechAnnouncement]";

        [HideInInspector]
        public LanguageType currentLanguage = LanguageType.zh_CN;
        [Range(0f, 1f)] public float voiceVolume = 1.0f;
        public int maxQueueSize = 2;
        
        [SerializeField] private AudioSource _voiceSource;

        private Queue<SpeechAsset> _speechQueue = new Queue<SpeechAsset>();

        public event Action<bool> OnVoicePlayStateChanged;

        private bool _isVoicePlaying = false;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeChannel();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Reset()
        {
            InitializeChannel();
        }

        private const string PREF_LANGUAGE = "Apex_Language";

        private void OnEnable()
        {
            if (UILanguageManager.Instance != null)
            {
                UILanguageManager.Instance.OnLanguageChanged += OnLanguageChangedFromManager;
            }
        }

        private void OnDisable()
        {
            if (UILanguageManager.Instance != null)
            {
                UILanguageManager.Instance.OnLanguageChanged -= OnLanguageChangedFromManager;
            }
        }

        private void OnLanguageChangedFromManager()
        {
            if (UILanguageManager.Instance != null && currentLanguage != UILanguageManager.Instance.CurrentLanguage)
            {
                currentLanguage = UILanguageManager.Instance.CurrentLanguage;
                Logger.LogApp($"{TAG} Language synced via UILanguageManager to: {currentLanguage}");
            }
        }

        private void Start()
        {
            SyncLanguageFromPref();
        }

        public void SyncLanguageFromPref()
        {
            int langInt = PlayerPrefs.GetInt(PREF_LANGUAGE, (int)LanguageType.en_Au);
            LanguageType prefLanguage = (LanguageType)langInt;
            
            if (currentLanguage != prefLanguage)
            {
                currentLanguage = prefLanguage;
                Logger.LogApp($"{TAG} Language synced from pref to: {currentLanguage}");
            }
        }

        private void InitializeChannel()
        {
            string channelName = "Channel_Voice";
            Transform childT = transform.Find(channelName);
            GameObject channelObj;

            if (childT != null)
            {
                channelObj = childT.gameObject;
            }
            else
            {
                channelObj = new GameObject(channelName);
                channelObj.transform.SetParent(transform);
                channelObj.transform.localPosition = Vector3.zero;
            }

            _voiceSource = channelObj.GetComponent<AudioSource>();
            if (_voiceSource == null)
            {
                _voiceSource = channelObj.AddComponent<AudioSource>();
            }

            // 覆盖基础设置
            _voiceSource.playOnAwake = false;
            _voiceSource.loop = false;
            if (!_voiceSource.isPlaying)
            {
                _voiceSource.volume = voiceVolume;
            }
        }

        private void Update()
        {
            // 队列处理逻辑
            if (!_voiceSource.isPlaying && _speechQueue.Count > 0)
            {
                SpeechAsset nextLine = _speechQueue.Dequeue();
                PlayVoiceInternal(nextLine);
            }

            // 状态变更广播 (通知 AudioManager 压低 BGM 等)
            if (_isVoicePlaying != _voiceSource.isPlaying)
            {
                _isVoicePlaying = _voiceSource.isPlaying;
                OnVoicePlayStateChanged?.Invoke(_isVoicePlaying);
            }
        }

        public void ChangeLanguage(LanguageType newLanguage)
        {
            if (currentLanguage == newLanguage) return;

            currentLanguage = newLanguage;
            Logger.LogApp($"{TAG} Language changed to: {newLanguage}");
            
            PlayerPrefs.SetInt(PREF_LANGUAGE, (int)newLanguage);
            PlayerPrefs.Save();
            
            if (UILanguageManager.Instance != null)
            {
                UILanguageManager.Instance.SetLanguage(newLanguage);
            }
        }

        /// <summary>
        /// 独占播放（打断当前正在播放的语音，清空队列）
        /// </summary>
        public void PlayVoiceExclusive(SpeechAsset speechasset)
        {
            if (speechasset == null) return;
            
            ClearAndStopVoice();
            PlayVoiceInternal(speechasset);
        }

        /// <summary>
        /// 加入队列播放
        /// </summary>
        public void PlayVoiceQueued(SpeechAsset speechasset)
        {
            if (speechasset == null) return;

            if (_speechQueue.Count >= maxQueueSize)
            {
                Logger.LogApp($"{TAG} Voice queue is full. Discarding line.");
                return;
            }

            _speechQueue.Enqueue(speechasset);
        }

        private void PlayVoiceInternal(SpeechAsset speechasset)
        {
            AudioClip clip = speechasset.GetClip(currentLanguage);
            if (clip != null)
            {
                _voiceSource.clip = clip;
                _voiceSource.Play();
            }
        }

        public void ClearAndStopVoice()
        {
            _speechQueue.Clear();
            if (_voiceSource != null) _voiceSource.Stop();
        }
    }
}
using UnityEngine;
using Apex.Audio;

namespace Apex.Audio
{
    public class AudioManager : MonoBehaviour
    {
        private const string TAG = "[AudioManager]";

        [Header("System Volume Settings")]
        public bool modifySystemVolume = true;
        [Range(0f, 1f)] public float initSystemVolumePercent = 0.5f;

        [Header("Auto Ducking Settings")]
        public bool enableAutoDucking = true;
        [Range(0f, 1f)] public float normalBgmVolume = 1.0f;
        [Range(0f, 1f)] public float duckedBgmVolume = 0.2f;
        public float duckingSpeed = 3.0f;

        [Header("Audio Channels")]
        [SerializeField] private AudioSource _bgmSource;
        [SerializeField] private AudioSource _sfxSource;

        private bool _isVoicePlaying = false;

        private void Awake()
        {
            InitializeChannels();
        }

        private void Reset()
        {
            InitializeChannels();
        }

        private void Start()
        {
            // 系统音量
            if (modifySystemVolume) SetSystemVolume(initSystemVolumePercent);

            // 订阅 SpeechAnnouncement 语音播报功能
            if (SpeechAnnouncement.Instance)
            {
                SpeechAnnouncement.Instance.OnVoicePlayStateChanged += HandleVoicePlayState;
            }
            else
            {
                Debug.LogWarning($"{TAG} SpeechAnnouncement.Instance is null. Auto ducking won't work.");
            }
        }

        private void OnDestroy()
        {
            if (SpeechAnnouncement.Instance)
            {
                SpeechAnnouncement.Instance.OnVoicePlayStateChanged -= HandleVoicePlayState;
            }
        }

        private void HandleVoicePlayState(bool isPlaying)
        {
            _isVoicePlaying = isPlaying;
        }

        private void Update()
        {
            // Auto Ducking
            if (enableAutoDucking && _bgmSource)
            {
                bool isImportantAudioPlaying = _isVoicePlaying || (_sfxSource && _sfxSource.isPlaying);
                float targetVolume = isImportantAudioPlaying ? duckedBgmVolume : normalBgmVolume;

                if (!Mathf.Approximately(_bgmSource.volume, targetVolume))
                {
                    _bgmSource.volume = Mathf.Lerp(_bgmSource.volume, targetVolume, Time.deltaTime * duckingSpeed);
                }
            }
        }

        private void InitializeChannels()
        {
            if (!_bgmSource) _bgmSource = GetOrCreateAudioChannel("Channel_BGM", true, normalBgmVolume);
            if (!_sfxSource) _sfxSource = GetOrCreateAudioChannel("Channel_SFX", false, 1.0f);
        }

        private AudioSource GetOrCreateAudioChannel(string channelName, bool loop, float defaultVolume)
        {
            Transform childT = transform.Find(channelName);
            GameObject channelObj = childT ? childT.gameObject : new GameObject(channelName);
            
            if (!childT)
            {
                channelObj.transform.SetParent(transform);
                channelObj.transform.localPosition = Vector3.zero;
            }

            if (!channelObj.TryGetComponent(out AudioSource source))
            {
                source = channelObj.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.loop = loop;
            if (!source.isPlaying) source.volume = defaultVolume;

            return source;
        }

        public void PlayBGM(AudioClip clip)
        {
            if (!clip || !_bgmSource) return;
            if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;

            _bgmSource.clip = clip;
            _bgmSource.Play();
        }

        public void StopBGM()
        {
            if (_bgmSource) _bgmSource.Stop();
        }

        public void PlaySFX(AudioClip clip)
        {
            if (!clip || !_sfxSource) return;
            _sfxSource.PlayOneShot(clip); // 推荐使用 PlayOneShot 支持多音效叠加
        }

        private void SetSystemVolume(float volumePercent)
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject audioManager = currentActivity.Call<AndroidJavaObject>("getSystemService", "audio"))
                {
                    int streamType = 3; 
                    int maxVolume = audioManager.Call<int>("getStreamMaxVolume", streamType);
                    int targetVolume = Mathf.RoundToInt(maxVolume * Mathf.Clamp01(volumePercent));
                    
                    audioManager.Call("setStreamVolume", streamType, targetVolume, 1);
                    Debug.Log($"{TAG} System volume successfully set to {targetVolume}/{maxVolume}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{TAG} Failed to set Android system volume: {e.Message}");
            }
    #endif
        }

        private void OnApplicationQuit()
        {
            StopBGM();
            if (_sfxSource != null) _sfxSource.Stop();
        }
    }
}
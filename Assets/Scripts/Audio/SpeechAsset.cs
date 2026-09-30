using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apex.Audio
{   
    public enum LanguageType
    {
        zh_CN,
        en_Au,
        jp
    }

    [CreateAssetMenu(fileName = "NewSpeechAsset", menuName = "Audio/Speech Asset", order = 1)]
    public class SpeechAsset : ScriptableObject
    {
        [Serializable]
        public class SpeechClipEntry 
        {
            [ReadOnly] 
            public LanguageType language;
            
            public AudioClip clip;
        }

        public List<SpeechClipEntry> speechClips = new List<SpeechClipEntry>();

        private void OnValidate()
        {
            SyncWithEnum();
        }

        private void Reset()
        {
            SyncWithEnum();
        }

        private void SyncWithEnum()
        {
            LanguageType[] allLanguages = (LanguageType[])Enum.GetValues(typeof(LanguageType));
            
            Dictionary<LanguageType, AudioClip> existingClips = new Dictionary<LanguageType, AudioClip>();
            if (speechClips != null)
            {
                foreach (var entry in speechClips)
                {
                    if (!existingClips.ContainsKey(entry.language))
                    {
                        existingClips[entry.language] = entry.clip;
                    }
                }
            }

            speechClips = new List<SpeechClipEntry>();
            foreach (LanguageType lang in allLanguages)
            {
                AudioClip savedClip = existingClips.ContainsKey(lang) ? existingClips[lang] : null;
                
                speechClips.Add(new SpeechClipEntry
                {
                    language = lang,
                    clip = savedClip
                });
            }
        }

        public AudioClip GetClip(LanguageType targetLanguage)
        {
            foreach (var entry in speechClips)
            {
                if (entry.language == targetLanguage)
                {
                    return entry.clip;
                }
            }
            
            if (speechClips.Count > 0 && speechClips[0].clip != null)
            {
                Debug.LogWarning($"[SpeechAsset] {name}: Language missing or empty clip for {targetLanguage}, fallback to {speechClips[0].language}.");
                Logger.LogApp($"[SpeechAsset] {name}: Language missing or empty clip for {targetLanguage}, fallback to {speechClips[0].language}.", LogType.Warning);
                return speechClips[0].clip;
            }
            
            return null;
        }
    }
}
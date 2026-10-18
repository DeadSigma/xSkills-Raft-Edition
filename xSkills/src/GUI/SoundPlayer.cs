using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace xSkills.GUI
{
    internal sealed class SoundPlayer : MonoBehaviour
    {
        private static SoundPlayer instance;
        private const string VolumeKey = "xskills_sound_volume";
        private static float volume = -1f;

        public static float Volume
        {
            get
            {
                if (volume < 0f) volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, .8f));
                return volume;
            }
            set
            {
                float next = Mathf.Clamp01(value);
                if (Mathf.Abs(Volume - next) < .0001f) return;
                volume = next;
                PlayerPrefs.SetFloat(VolumeKey, volume);
                PlayerPrefs.Save();
                if (instance != null && instance.source != null) instance.source.volume = volume;
            }
        }
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private AudioSource source;
        private string cacheDirectory;

        private static readonly string[] Sounds =
        {
            "open", "close", "page", "pagesub", "upgraded", "upgradedmax",
            "downgraded", "levelup", "sparringon", "sparringoff"
        };

        public static void Initialize()
        {
            if (instance != null) return;
            GameObject holder = new GameObject("xSkills Sound Player");
            DontDestroyOnLoad(holder);
            instance = holder.AddComponent<SoundPlayer>();
            instance.source = holder.AddComponent<AudioSource>();
            instance.source.playOnAwake = false;
            instance.source.spatialBlend = 0f;
            instance.source.volume = Volume;
            instance.cacheDirectory = Path.Combine(Application.temporaryCachePath, "xSkills", "sounds");
            foreach (string name in Sounds)
                instance.StartCoroutine(instance.Load(name));
        }

        private IEnumerator Load(string name)
        {
            byte[] bytes = ResourceLoader.FileBytes("assets/sounds/" + name + ".ogg", false);
            if (bytes == null)
            {
                Debug.LogWarning("[xSkills] Sound not found: assets/sounds/" + name + ".ogg");
                yield break;
            }

            string filename;
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                filename = Path.Combine(cacheDirectory, name + ".ogg");
                File.WriteAllBytes(filename, bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Unable to extract sound " + name + ": " + ex.Message);
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(filename).AbsoluteUri, AudioType.OGGVORBIS))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[xSkills] Unable to decode sound " + name + ": " + request.error);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    Debug.LogWarning("[xSkills] AudioClip is null: " + name);
                    yield break;
                }
                clip.name = "xSkills/" + name;
                clips[name] = clip;
                Debug.Log("[xSkills] Sound loaded: " + name);
            }
        }

        public static void Play(string name)
        {
            if (instance == null || instance.source == null) return;
            AudioClip clip;
            if (instance.clips.TryGetValue(name, out clip) && clip != null)
                instance.source.PlayOneShot(clip);
        }

        public static void Shutdown()
        {
            if (instance == null) return;
            instance.StopAllCoroutines();
            if (instance.source != null) instance.source.Stop();
            foreach (AudioClip clip in instance.clips.Values)
                if (clip != null) Destroy(clip);
            instance.clips.Clear();
            Destroy(instance.gameObject);
            instance = null;
        }
    }
}

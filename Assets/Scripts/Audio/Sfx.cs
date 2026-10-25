using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Every sound the game plays goes through here, by slot in the SoundLibrary (<see cref="Lib"/>): Sfx.Play(Sfx.Lib.WaveStart).
    /// Sounds are FMOD Studio events (the project in Assets/FMOD/FmodProject). When the FMOD for Unity plugin is
    /// installed the editor defines FMOD_PRESENT (Editor/FmodDefine) and sounds play through FMODUnity; without it every
    /// call is silent, so the game runs either way. On first use the Master banks are loaded explicitly (so sound works
    /// even if FMOD's settings list no banks) and an FMOD listener is put on the main camera.
    /// Each slot has its own spam limits (a minimum gap and a cap on overlapping copies). Volumes (master, effects,
    /// music, ambience, interface; Sfx.Settings, set in the SettingsMenu) scale every event as it starts, and running
    /// loops follow changes at once. One music track and one ambience loop play at a time (PlayMusic / PlayAmbience).
    /// </summary>
    public static class Sfx
    {
        public static SoundLibrary Lib => SoundLibrary.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lastPlayed.Clear();
            voices.Clear();
            loops.Clear();
            music = ambience = null;
            musicHandle = ambienceHandle = null;
        }

        /// <summary>Master switch for scripts (e.g. playtests); the player's mute is Settings.Mute.</summary>
        public static bool Muted;

        /// <summary>The player's audio settings, kept in PlayerPrefs.</summary>
        public static class Settings
        {
            public static float Master { get => Get("Master", 0.8f); set => Set("Master", value); }
            public static float Effects { get => Get("Effects", 1f); set => Set("Effects", value); }
            public static float Music { get => Get("Music", 0.6f); set => Set("Music", value); }
            public static float Ambience { get => Get("Ambience", 0.6f); set => Set("Ambience", value); }
            public static float Interface { get => Get("Interface", 0.9f); set => Set("Interface", value); }
            public static bool Mute { get => PlayerPrefs.GetInt("Audio.Mute", 0) == 1; set { PlayerPrefs.SetInt("Audio.Mute", value ? 1 : 0); UpdateVolumes(); } }

            static float Get(string key, float fallback) => PlayerPrefs.GetFloat("Audio." + key, fallback);
            static void Set(string key, float value) { PlayerPrefs.SetFloat("Audio." + key, Mathf.Clamp01(value)); UpdateVolumes(); }

            public static float Of(SoundChannel c) =>
                c == SoundChannel.Music ? Music : c == SoundChannel.Ambience ? Ambience : c == SoundChannel.Interface ? Interface : Effects;

            public static void Save() => PlayerPrefs.Save();
        }

        /// <summary>Final volume for a slot: its own volume x master x its channel (0 when muted).</summary>
        public static float VolumeOf(Sound s) => Settings.Mute || s == null ? 0f : s.Volume * Settings.Master * Settings.Of(s.Channel);

        // ---------------------------------------------------------------- one-shots

        static readonly Dictionary<Sound, float> lastPlayed = new Dictionary<Sound, float>();
        static readonly Dictionary<Sound, List<float>> voices = new Dictionary<Sound, List<float>>();

        /// <summary>Plays a slot once, at a world position if given and the slot is positional; silent if it has no event.</summary>
        public static void Play(Sound s, Vector3? at = null)
        {
            if (Muted || s == null || !s.IsSet || !Application.isPlaying) return;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(s, out float last) && now - last < s.MinGap) return;
            if (!voices.TryGetValue(s, out var list)) voices[s] = list = new List<float>();
            list.RemoveAll(t => now - t > 0.5f);
            if (list.Count >= Mathf.Max(1, s.MaxVoices)) return;
            float volume = VolumeOf(s);
            if (volume <= 0.001f) return;
            list.Add(now);
            lastPlayed[s] = now;
            Backend.PlayOneShot(s.Event, s.Positional ? at : null, volume);
        }

        /// <summary>A button click.</summary>
        public static void Click() => Play(Lib.Click);

        // ---------------------------------------------------------------- loops

        static readonly Dictionary<object, (object handle, Sound sound)> loops = new Dictionary<object, (object, Sound)>();

        /// <summary>Starts (or keeps) a looping slot tied to an owner; follows `at` if given.</summary>
        public static void Loop(object owner, Sound s, Vector3? at = null)
        {
            if (Muted || owner == null || s == null || !s.IsSet || !Application.isPlaying) return;
            if (loops.TryGetValue(owner, out var loop))
            {
                if (loop.sound == s)
                {
                    if (at.HasValue && s.Positional) Backend.Move(loop.handle, at.Value);
                    return;
                }
                StopLoop(owner); // switched to another sound
            }
            var handle = Backend.Start(s.Event, s.Positional ? at : null, VolumeOf(s));
            if (handle != null) loops[owner] = (handle, s);
        }

        /// <summary>Stops the owner's loop (fading out) if it has one.</summary>
        public static void StopLoop(object owner)
        {
            if (owner == null || !loops.TryGetValue(owner, out var loop)) return;
            Backend.Stop(loop.handle);
            loops.Remove(owner);
        }

        public static void StopAllLoops()
        {
            foreach (var l in loops.Values) Backend.Stop(l.handle);
            loops.Clear();
        }

        // ---------------------------------------------------------------- music and ambience

        static Sound music, ambience;
        static object musicHandle, ambienceHandle;

        /// <summary>Plays a music track (null or empty stops it); the same track keeps playing.</summary>
        public static void PlayMusic(Sound s) => Swap(ref music, ref musicHandle, s);

        /// <summary>Plays an ambience loop (null or empty stops it).</summary>
        public static void PlayAmbience(Sound s) => Swap(ref ambience, ref ambienceHandle, s);

        static void Swap(ref Sound current, ref object handle, Sound next)
        {
            if (!Application.isPlaying) return;
            if (next != null && !next.IsSet) next = null;
            if (current == next && (handle != null || next == null)) return;
            if (handle != null) Backend.Stop(handle);
            handle = null;
            current = next;
            if (next != null && !Muted) handle = Backend.Start(next.Event, null, VolumeOf(next));
        }

        /// <summary>Running loops, music and ambience pick up a volume change at once.</summary>
        static void UpdateVolumes()
        {
            foreach (var l in loops.Values) Backend.SetVolume(l.handle, VolumeOf(l.sound));
            if (musicHandle != null) Backend.SetVolume(musicHandle, VolumeOf(music));
            if (ambienceHandle != null) Backend.SetVolume(ambienceHandle, VolumeOf(ambience));
        }

        /// <summary>The audio engine behind Sfx: FMOD when its Unity plugin is installed, else silence.</summary>
        static class Backend
        {
#if FMOD_PRESENT
            static readonly HashSet<string> missing = new HashSet<string>();
            static bool banksTried;
            static Camera listenerCamera;

            /// <summary>Loads the Master banks (strings first, for event paths) unless FMOD already did.</summary>
            static void EnsureBanks()
            {
                if (banksTried) return;
                banksTried = true;
                foreach (var bank in new[] { "Master.strings", "Master" })
                {
                    try
                    {
                        if (!FMODUnity.RuntimeManager.HasBankLoaded(bank)) FMODUnity.RuntimeManager.LoadBank(bank, true);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Sfx: could not load FMOD bank {bank}: {e.Message}. Build the banks in FMOD Studio (File > Build).");
                    }
                }
            }

            /// <summary>FMOD hears the world from the main camera.</summary>
            static void EnsureListener()
            {
                var cam = Camera.main;
                if (cam == null || cam == listenerCamera) return;
                listenerCamera = cam;
                if (cam.GetComponent<FMODUnity.StudioListener>() == null && Object.FindAnyObjectByType<FMODUnity.StudioListener>() == null)
                    cam.gameObject.AddComponent<FMODUnity.StudioListener>();
            }

            static bool Ready(string path)
            {
                EnsureBanks();
                EnsureListener();
                if (missing.Contains(path)) return false;
                try
                {
                    FMODUnity.RuntimeManager.GetEventDescription(path);
                    return true;
                }
                catch (System.Exception)
                {
                    missing.Add(path);
                    Debug.LogWarning($"Sfx: FMOD event {path} not found. Is it in a built bank? (FMOD Studio: assign it to the Master bank, then File > Build.)");
                    return false;
                }
            }

            static FMOD.Studio.EventInstance Create(string path, Vector3? at, float volume)
            {
                var instance = FMODUnity.RuntimeManager.CreateInstance(path);
                instance.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(at ?? (listenerCamera != null ? listenerCamera.transform.position : Vector3.zero)));
                instance.setVolume(volume);
                return instance;
            }

            public static void PlayOneShot(string path, Vector3? at, float volume)
            {
                if (!Ready(path)) return;
                var instance = Create(path, at, volume);
                instance.start();
                instance.release(); // freed once it finishes
            }

            public static object Start(string path, Vector3? at, float volume)
            {
                if (!Ready(path)) return null;
                var instance = Create(path, at, volume);
                instance.start();
                return instance;
            }

            public static void Move(object handle, Vector3 at)
            {
                if (handle is FMOD.Studio.EventInstance instance) instance.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(at));
            }

            public static void SetVolume(object handle, float volume)
            {
                if (handle is FMOD.Studio.EventInstance instance) instance.setVolume(volume);
            }

            public static void Stop(object handle)
            {
                if (!(handle is FMOD.Studio.EventInstance instance)) return;
                instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                instance.release();
            }
#else
            public static void PlayOneShot(string path, Vector3? at, float volume) { }
            public static object Start(string path, Vector3? at, float volume) => null;
            public static void Move(object handle, Vector3 at) { }
            public static void SetVolume(object handle, float volume) { }
            public static void Stop(object handle) { }
#endif
        }
    }
}

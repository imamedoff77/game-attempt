using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sound effects and music. Clips live in Resources/Audio (made by art/audio/generate_sounds.py).
/// A few audio sources are reused in turn, and a sound that was played a moment ago is skipped, so a big fight stays bearable.
/// </summary>
public static class Sfx
{
    private const int Voices = 14;
    private const string PrefKey = "sound.on";

    private static AudioSource[] _voices;
    private static int _next;
    private static AudioSource _music;
    private static readonly Dictionary<string, AudioClip[]> Clips = new Dictionary<string, AudioClip[]>();
    private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
    private static bool _enabled = true;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            try { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); } catch (System.Exception) { }
            if (_music != null) _music.mute = !value;
            if (!value && _voices != null) foreach (AudioSource v in _voices) v.Stop();
        }
    }

    public static void Init(Transform parent)
    {
        try { _enabled = PlayerPrefs.GetInt(PrefKey, 1) != 0; } catch (System.Exception) { }

        _voices = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            _voices[i] = parent.gameObject.AddComponent<AudioSource>();
            _voices[i].playOnAwake = false;
            _voices[i].spatialBlend = 0f;
        }

        Load("rifle", "rifle0", "rifle1", "rifle2");
        Load("cannon", "cannon");
        Load("rocket", "rocket");
        Load("explosion_small", "explosion_small");
        Load("explosion_big", "explosion_big");
        Load("click", "click");
        Load("place", "place");
        Load("ready", "ready");
        Load("victory", "victory");
        Load("defeat", "defeat");
        Load("alarm", "alarm");

        AudioClip music = Resources.Load<AudioClip>("Audio/music");
        if (music != null)
        {
            _music = parent.gameObject.AddComponent<AudioSource>();
            _music.clip = music;
            _music.loop = true;
            _music.volume = 0.28f;
            _music.playOnAwake = false;
            _music.mute = !_enabled;
            _music.Play();
        }
    }

    private static void Load(string name, params string[] files)
    {
        var list = new List<AudioClip>();
        foreach (string f in files)
        {
            AudioClip c = Resources.Load<AudioClip>("Audio/" + f);
            if (c != null) list.Add(c);
            else Debug.LogWarning("[Sfx] missing clip " + f);
        }
        Clips[name] = list.ToArray();
    }

    /// <summary>Music is a little quieter while playing than in the menu.</summary>
    public static void SetMusicVolume(float volume)
    {
        if (_music != null) _music.volume = volume;
    }

    /// <param name="minGap">Seconds to wait before this sound may play again (so thirty rifles do not make thirty voices at once).</param>
    public static void Play(string name, float volume = 1f, float pitchJitter = 0.06f, float minGap = 0f)
    {
        if (!_enabled || _voices == null || !Clips.TryGetValue(name, out AudioClip[] clips) || clips.Length == 0) return;

        float now = Time.unscaledTime;
        if (minGap > 0f && LastPlayed.TryGetValue(name, out float last) && now - last < minGap) return;
        LastPlayed[name] = now;

        AudioSource voice = _voices[_next];
        _next = (_next + 1) % Voices;
        voice.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        voice.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
    }

    /// <summary>A sound from a place on the map: full strength when it is on screen, much quieter when it is not.</summary>
    public static void PlayAt(Vector3 world, string name, float volume = 1f, float minGap = 0f)
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            bool visible = v.z > 0f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f;
            if (!visible) volume *= 0.3f;
        }
        Play(name, volume, 0.07f, minGap);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FreeFlow.Enums;
using FreeFlow.Util;

public class AudioManager : Singleton<AudioManager>
{
    /// <summary>Which source a sound belongs on. A one-shot layers over whatever else is playing;
    /// music owns its source and loops.</summary>
    public enum SoundChannel
    {
        Sfx = 0,
        Music,
    }

    /// <summary>
    /// One row of the sound table: what happened, what it sounds like, and how that clip is meant
    /// to sound relative to the others.
    ///
    /// <see cref="volume"/> and <see cref="pitch"/> are the clip's OWN mix, not a second set of
    /// player controls: source clips are rarely mastered to the same level, and a sound that fires
    /// constantly (a button click) usually wants to sit under one that marks an achievement. The
    /// player's volume slider multiplies on top of this -- see AudioManager.PlaySFX.
    ///
    /// A class rather than a struct so these default sensibly: a struct would give every row added
    /// in the Inspector volume 0 and pitch 0, which is silence and a stopped clip, with nothing to
    /// say why.
    /// </summary>
    [System.Serializable]
    private class SoundEntry
    {
        public SoundType type;
        public AudioClip clip;
        public SoundChannel channel = SoundChannel.Sfx;

        [Range(0f, 1f)] public float volume = 1f;

        // Unity's own range. 1 is the clip as recorded; below is deeper and slower, above is
        // higher and faster, since pitch and playback speed are the same knob here.
        [Range(-3f, 3f)] public float pitch = 1f;
    }

    [SerializeField] private AudioSource bgAudioSource;

    // The first SFX voice, and the template the rest are copied from -- see BuildVoices.
    [SerializeField] private AudioSource sfxAudioSource;

    // The whole sound vocabulary, in one place. Callers name a SoundType; only this table knows
    // which file that is and how it should sound.
    [SerializeField] private SoundEntry[] sounds;

    // How many one-shots can overlap, each with its own pitch.
    //
    // Pitch belongs to the SOURCE, not to PlayOneShot, so a single shared source cannot give two
    // overlapping sounds different pitches -- setting it for a new clip re-pitches whatever is
    // still sounding underneath. One source per concurrent sound is what makes per-clip pitch
    // actually per-clip.
    //
    // Not exposed in the Inspector: this board can produce a tap, a path completing and the level
    // finishing on top of each other and no more, so four is the answer with one spare rather than
    // a number anyone would have grounds to tune. Exposing it would mostly offer the chance to set
    // it to 1, which silently takes per-clip pitch away again with nothing to say why.
    private const int SfxVoiceCount = 4;

    private AudioSource[] voices;
    private int nextVoice;

    private readonly Dictionary<SoundType, SoundEntry> table = new Dictionary<SoundType, SoundEntry>();

    // So a missing or mis-channelled clip is reported once, not on every tap for the rest of the
    // session.
    private readonly HashSet<SoundType> reported = new HashSet<SoundType>();

    private float bgVolume = 0.5f;
    private float sfxVolume = 0.5f;

    private bool isBgMute = false;
    private bool isSfxMute = false;

    private const float SaveDebounceDelay = 0.3f;
    private Coroutine saveDebounceCoroutine;
    private bool hasPendingSave;

    public float BgVolume { get { return bgVolume; } }
    public float SFXVolume { get { return sfxVolume; } }

    public bool IsBgMute { get { return isBgMute; } }
    public bool IsSFXMute { get { return isSfxMute; } }

    private void Start()
    {
        BuildTable();
        BuildVoices();

        AudioData data = SavingSystem.Instance.Load().audioData;

        isBgMute = data.isMusicMute;
        isSfxMute = data.isSoundMute;
        bgVolume = data.musicVolume;
        sfxVolume = data.soundVolume;

        bgAudioSource.mute = isBgMute;
        bgAudioSource.volume = bgVolume;

        ApplySfxSettings();
    }

    /// <summary>Indexes the Inspector's table by SoundType. A type listed twice keeps the FIRST
    /// row and says so: silently honouring the last one would make the table's own order matter in
    /// a way nothing about it suggests.</summary>
    private void BuildTable()
    {
        table.Clear();
        if (sounds == null) { return; }

        foreach (SoundEntry entry in sounds)
        {
            if (entry == null) { continue; }

            if (table.ContainsKey(entry.type))
            {
                Debug.LogWarning("AudioManager: " + entry.type + " is listed more than once -- "
                    + "keeping the first row.");
                continue;
            }

            table.Add(entry.type, entry);
        }
    }

    /// <summary>Builds the pool of one-shot voices, copying the serialized source's own routing so
    /// every voice reaches the same mixer group and sits in the same space as the one the scene was
    /// authored with -- a voice that skipped the mixer would ignore any bus the SFX go through.
    /// </summary>
    private void BuildVoices()
    {
        voices = new AudioSource[SfxVoiceCount];
        voices[0] = sfxAudioSource;

        for (int i = 1; i < SfxVoiceCount; i++)
        {
            GameObject go = new GameObject("SFX Voice " + (i + 1));
            go.transform.SetParent(sfxAudioSource.transform.parent, false);

            AudioSource voice = go.AddComponent<AudioSource>();
            voice.outputAudioMixerGroup = sfxAudioSource.outputAudioMixerGroup;
            voice.spatialBlend = sfxAudioSource.spatialBlend;
            voice.panStereo = sfxAudioSource.panStereo;
            voice.reverbZoneMix = sfxAudioSource.reverbZoneMix;
            voice.bypassEffects = sfxAudioSource.bypassEffects;
            voice.bypassListenerEffects = sfxAudioSource.bypassListenerEffects;
            voice.bypassReverbZones = sfxAudioSource.bypassReverbZones;
            voice.priority = sfxAudioSource.priority;
            voice.playOnAwake = false;
            voice.loop = false;

            voices[i] = voice;
        }
    }

    /// <summary>
    /// Plays a one-shot: PlaySFX(SoundType.PathComplete) and so on.
    ///
    /// The clip's own volume and pitch come from its row in the table; the player's SFX slider
    /// multiplies on top of the volume, so turning the sound down turns everything down in the
    /// proportions the table set. Muted SFX return immediately rather than playing into a muted
    /// source.
    /// </summary>
    public void PlaySFX(SoundType type)
    {
        if (isSfxMute) { return; }

        SoundEntry entry = Resolve(type, SoundChannel.Sfx);
        if (entry == null) { return; }

        AudioSource voice = TakeVoice();
        voice.pitch = entry.pitch;

        // volume here is the SOURCE's (the player's setting); the argument is the clip's own, and
        // Unity multiplies the two.
        voice.volume = sfxVolume;
        voice.PlayOneShot(entry.clip, entry.volume);
    }

    /// <summary>The next voice to play on: a free one if there is one, otherwise the
    /// longest-running, which is the one closest to finishing anyway. Cycling rather than always
    /// taking the first means a burst of sounds spreads across the pool instead of cutting itself
    /// off on one voice.</summary>
    private AudioSource TakeVoice()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource candidate = voices[(nextVoice + i) % voices.Length];
            if (!candidate.isPlaying)
            {
                nextVoice = (nextVoice + i + 1) % voices.Length;
                return candidate;
            }
        }

        AudioSource stolen = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        return stolen;
    }

    /// <summary>Starts a looping track, or does nothing if that track is already the one playing --
    /// so calling it on every screen that wants music does not restart the music.</summary>
    public void PlayMusic(SoundType type)
    {
        SoundEntry entry = Resolve(type, SoundChannel.Music);
        if (entry == null) { return; }

        if (bgAudioSource.clip == entry.clip && bgAudioSource.isPlaying) { return; }

        bgAudioSource.clip = entry.clip;
        bgAudioSource.loop = true;
        bgAudioSource.pitch = entry.pitch;
        bgAudioSource.volume = bgVolume * entry.volume;
        bgAudioSource.Play();
    }

    public void StopMusic()
    {
        bgAudioSource.Stop();
    }

    /// <summary>The table row for <paramref name="type"/>, or null with one explanation if it
    /// cannot be played: no row, no clip on the row, or a row that belongs to the other channel.
    /// Each is a wiring mistake that would otherwise be silence with no reason given.</summary>
    private SoundEntry Resolve(SoundType type, SoundChannel expected)
    {
        SoundEntry entry;
        if (!table.TryGetValue(type, out entry))
        {
            Report(type, "AudioManager: no clip is set up for " + type + ".");
            return null;
        }

        if (entry.clip == null)
        {
            Report(type, "AudioManager: " + type + " has a row but no clip.");
            return null;
        }

        if (entry.channel != expected)
        {
            Report(type, "AudioManager: " + type + " is a " + entry.channel + " sound -- play it with "
                + (entry.channel == SoundChannel.Music ? "PlayMusic" : "PlaySFX") + ".");
            return null;
        }

        return entry;
    }

    private void Report(SoundType type, string message)
    {
        if (!reported.Add(type)) { return; }
        Debug.LogWarning(message);
    }

    public void UpdateBgVolume(float volume)
    {
        isBgMute = (volume <= 0);
        bgAudioSource.mute = isBgMute;

        bgVolume = Mathf.Clamp(volume, 0, 1);

        // Keep whatever trim the playing track's row asked for, rather than flattening it to the
        // slider's raw value.
        bgAudioSource.volume = bgVolume * MusicScale();

        ScheduleSave();
    }

    /// <summary>The clip volume of the track currently playing, so a slider move can reapply it.
    /// 1 when nothing recognisable is playing -- better a track at full slider volume than one
    /// silently scaled by a row it no longer belongs to.</summary>
    private float MusicScale()
    {
        foreach (SoundEntry entry in table.Values)
        {
            if (entry.channel == SoundChannel.Music && entry.clip == bgAudioSource.clip)
            {
                return entry.volume;
            }
        }
        return 1f;
    }

    public void UpdateSFXVolume(float volume)
    {
        isSfxMute = (volume <= 0);
        sfxVolume = Mathf.Clamp(volume, 0, 1);

        ApplySfxSettings();
        ScheduleSave();
    }

    /// <summary>Pushes the player's SFX setting onto every voice. Sounds already in flight follow
    /// it, which is what a player dragging the slider expects to hear.</summary>
    private void ApplySfxSettings()
    {
        if (voices == null) { return; }

        foreach (AudioSource voice in voices)
        {
            if (voice == null) { continue; }
            voice.mute = isSfxMute;
            voice.volume = sfxVolume;
        }
    }

    // sliders fire UpdateBgVolume/UpdateSFXVolume on every onValueChanged tick while being
    // dragged; debounce so the full SaveData read-modify-write only happens once dragging
    // settles, not on every tick
    private void ScheduleSave()
    {
        hasPendingSave = true;

        if (saveDebounceCoroutine != null)
        {
            StopCoroutine(saveDebounceCoroutine);
        }
        saveDebounceCoroutine = StartCoroutine(SaveAfterDelay());
    }

    private IEnumerator SaveAfterDelay()
    {
        yield return new WaitForSeconds(SaveDebounceDelay);
        FlushPendingSave();
    }

    private void FlushPendingSave()
    {
        if (!hasPendingSave) { return; }

        SaveAudioData();
        hasPendingSave = false;
        saveDebounceCoroutine = null;
    }

    private void OnApplicationQuit()
    {
        FlushPendingSave();
    }

    private void OnDisable()
    {
        FlushPendingSave();
    }

    private void SaveAudioData()
    {
        SaveData saveData = SavingSystem.Instance.Load();

        saveData.audioData.isMusicMute = isBgMute;
        saveData.audioData.isSoundMute = isSfxMute;
        saveData.audioData.musicVolume = bgVolume;
        saveData.audioData.soundVolume = sfxVolume;

        SavingSystem.Instance.Save(saveData);
    }
}

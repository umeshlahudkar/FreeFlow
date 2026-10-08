using System;
using System.Collections.Generic;
using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.YouTube
{
    /// <summary>
    /// YouTube's cloud save, through Google's YTGameWrapper. Playables forbids any other save
    /// mechanism, so ProfileManager's slots (progress and settings) travel together as one string --
    /// an <see cref="Envelope"/> -- through saveData/loadData.
    ///
    /// Playables' rules, and how this keeps them:
    /// - Nothing is saved before loadData has answered. Writes made earlier only update memory.
    /// - The wrapper never reports a failed load, so the load gives up after
    ///   <see cref="LoadTimeoutSeconds"/>; saving then stays OFF for the session. Writing first-run
    ///   defaults over a save that merely failed to arrive would wipe the player's progress.
    /// - Save after material progress: every write schedules a save, but a burst of writes within
    ///   <see cref="SaveDelaySeconds"/> becomes one cloud save.
    /// - Save on pause: <see cref="Flush"/>, called by PlatformManager through ProfileManager.
    /// - Under 3 MiB: checked before sending (today's save is a few KB).
    ///
    /// In the Editor there is no SDK, so the same envelope goes to a file under persistentDataPath.
    /// </summary>
    public sealed class YouTubeCloudSaveStorage : ISaveStorage
    {
        public const float LoadTimeoutSeconds = 10f;
        private const float SaveDelaySeconds = 1f;
        private const int MaxSaveLength = 3 * 1024 * 1024;
        private const int EnvelopeVersion = 1;

        [Serializable]
        private class Envelope
        {
            public int version;
            public string[] slots;
            public string[] values;
        }

        private readonly Dictionary<string, string> data = new Dictionary<string, string>();
        private bool loaded;
        private bool savingDisabled;
        private bool dirty;
        private bool saveScheduled;

        public void Load(Action onComplete)
        {
#if UNITY_EDITOR
            Parse(System.IO.File.Exists(EditorSavePath) ? System.IO.File.ReadAllText(EditorSavePath) : "");
            loaded = true;
            onComplete?.Invoke();
#else
            if (YouTubeSdk.Wrapper == null)
            {
                savingDisabled = true;
                YouTubeSdk.LogError("Cloud save: no YTGameWrapper object; saving disabled this session.");
                onComplete?.Invoke();
                return;
            }

            bool answered = false;
            YouTubeSdk.Wrapper.LoadGameSaveData(json =>
            {
                if (answered) { return; }
                answered = true;
                Parse(json);
                loaded = true;
                onComplete?.Invoke();
                // Anything written before the load answered is still pending -- send it now that
                // saving is allowed.
                if (dirty) { ScheduleSave(); }
            });

            YouTubeRunner.Instance.RunAfterRealtime(LoadTimeoutSeconds, () =>
            {
                if (answered) { return; }
                answered = true;
                savingDisabled = true;
                YouTubeSdk.LogError("Cloud save: loadData did not answer in " + LoadTimeoutSeconds + " s; saving disabled this session.");
                onComplete?.Invoke();
            });
#endif
        }

        public bool TryRead(string slot, out string json)
        {
            return data.TryGetValue(slot, out json);
        }

        public void Write(string slot, string json)
        {
            data[slot] = json;
            MarkDirty();
        }

        public void Delete(string slot)
        {
            if (data.Remove(slot)) { MarkDirty(); }
        }

        /// <summary>Sends everything pending now. Does nothing before the load has answered, after a
        /// failed load, or when nothing changed.</summary>
        public void Flush()
        {
            saveScheduled = false;
            if (!loaded || savingDisabled || !dirty) { return; }

            string json = Serialize();
            if (json.Length > MaxSaveLength)
            {
                YouTubeSdk.LogError("Cloud save: " + json.Length + " chars is over the 3 MiB limit; not saved.");
                return;
            }

            dirty = false;
#if UNITY_EDITOR
            System.IO.File.WriteAllText(EditorSavePath, json);
#else
            // The int the wrapper returns is not the real result (its JavaScript returns before
            // YouTube answers), so it is not checked; the wrapper itself logs a failed save to YouTube.
            YouTubeSdk.Wrapper.SendGameSaveData(json);
#endif
        }

        private void MarkDirty()
        {
            dirty = true;
            ScheduleSave();
        }

        private void ScheduleSave()
        {
            if (saveScheduled || !loaded || savingDisabled) { return; }
            saveScheduled = true;
            YouTubeRunner.Instance.RunAfterRealtime(SaveDelaySeconds, Flush);
        }

        private void Parse(string json)
        {
            data.Clear();
            if (string.IsNullOrEmpty(json)) { return; }

            Envelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<Envelope>(json);
            }
            catch (Exception e)
            {
                YouTubeSdk.LogError("Cloud save: could not read the save (" + e.Message + "); starting fresh.");
                return;
            }

            if (envelope == null || envelope.slots == null || envelope.values == null) { return; }

            int count = Mathf.Min(envelope.slots.Length, envelope.values.Length);
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(envelope.slots[i])) { data[envelope.slots[i]] = envelope.values[i]; }
            }
        }

        private string Serialize()
        {
            Envelope envelope = new Envelope
            {
                version = EnvelopeVersion,
                slots = new string[data.Count],
                values = new string[data.Count]
            };

            int i = 0;
            foreach (KeyValuePair<string, string> entry in data)
            {
                envelope.slots[i] = entry.Key;
                envelope.values[i] = entry.Value;
                i++;
            }
            return JsonUtility.ToJson(envelope);
        }

#if UNITY_EDITOR
        private static string EditorSavePath
        {
            get { return System.IO.Path.Combine(Application.persistentDataPath, "YouTubeCloudSave_Editor.json"); }
        }
#endif
    }
}

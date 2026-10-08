using System;
using System.IO;
using UnityEngine;

namespace FreeFlow.Core.Services
{
    /// <summary>The device save backend, and the default when a config names none: one JSON file per
    /// slot under Application.persistentDataPath, written synchronously on every change -- exactly
    /// what ProfileManager did itself before the backend was split out.</summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        public void Load(Action onComplete)
        {
            onComplete?.Invoke();
        }

        public bool TryRead(string slot, out string json)
        {
            string path = PathFor(slot);
            if (!File.Exists(path))
            {
                json = null;
                return false;
            }

            json = File.ReadAllText(path);
            return true;
        }

        public void Write(string slot, string json)
        {
            File.WriteAllText(PathFor(slot), json);
        }

        public void Delete(string slot)
        {
            string path = PathFor(slot);
            if (File.Exists(path)) { File.Delete(path); }
        }

        /// <summary>Every Write already reached the disk, so there is nothing pending.</summary>
        public void Flush() { }

        private static string PathFor(string slot)
        {
            return Path.Combine(Application.persistentDataPath, slot);
        }
    }
}

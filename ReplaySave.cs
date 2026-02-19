using System;
using System.IO;
using UnityEngine;

namespace Board
{
    public class ReplaySave
    {
        private const int MaxBackups = 5;

        public void Save(byte[] data, int slot)
        {
            string path = GetPath(slot);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, data);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }

        public void Save(string sceneId, byte[] data, int slot)
        {
            string path = GetPath(sceneId, slot);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, data);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }

        public byte[] LoadMostRecent()
        {
            byte[] latestData = FindMostRecent(GetPath);
            if (latestData != null)
                return latestData;

            return LoadLegacy();
        }

        public byte[] LoadMostRecent(string sceneId)
        {
            byte[] latestData = FindMostRecent(slot => GetPath(sceneId, slot));
            if (latestData != null)
                return latestData;

            return LoadLegacy();
        }

        private static byte[] LoadLegacy()
        {
            string legacyPath = Application.persistentDataPath + "/replay.sav";
            if (!File.Exists(legacyPath))
                return null;

            try
            {
                return File.ReadAllBytes(legacyPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load legacy replay: {ex}");
                return null;
            }
        }

        private static byte[] FindMostRecent(Func<int, string> getPath)
        {
            DateTime latestTimestamp = DateTime.MinValue;
            byte[] latestData = null;

            for (int i = 0; i < MaxBackups; i++)
            {
                string path = getPath(i);
                if (!File.Exists(path))
                    continue;

                DateTime timestamp = File.GetLastWriteTime(path);
                if (timestamp <= latestTimestamp)
                    continue;

                latestTimestamp = timestamp;
                try
                {
                    latestData = File.ReadAllBytes(path);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to read replay file {path}: {ex}");
                }
            }

            return latestData;
        }

        private static string GetPath(int slot) =>
            $"{Application.persistentDataPath}/replay_{slot}.sav";

        private static string GetPath(string sceneId, int slot) =>
            $"{Application.persistentDataPath}/{sceneId.ToLower()}/replay_{slot}.sav";
    }
}

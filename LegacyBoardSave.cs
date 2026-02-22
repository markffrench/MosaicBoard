using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using UnityEngine;

namespace Board
{
    /// <summary>
    /// Handles the DES-encrypted local save format used by all pre-package game projects.
    /// Identical key, IV, file paths, and 5-slot rotation across flemishproverbs, mosaic,
    /// mosaic_of_the_pharaohs, and retrospective2024.
    ///
    /// Wire up in each project's boot/scene setup:
    ///   var legacySave = new LegacyBoardSave();
    ///   tileBoard.OnSaveRequested += (_, save) => legacySave.Save(save);
    ///   tileBoard.ApplySaveData(legacySave.Load(boardWidth, boardHeight));
    /// </summary>
    public class LegacyBoardSave
    {
        private readonly byte[] key = { 8, 6, 4, 1, 1, 3, 5, 7 };
        private readonly byte[] iv = { 9, 1, 8, 2, 7, 3, 6, 4 };
        private const int MaxBackups = 5;

        private string SaveFilePath => Application.persistentDataPath + "/boardstate_{0}.sav";
        private string ReplayFilePath => Application.persistentDataPath + "/replay_{0}.sav";
        private string LegacySaveFilePath => Application.persistentDataPath + "/boardstate.sav";

        private int currentSaveIndex;

        public void Save(ProgressSave save)
        {
            currentSaveIndex = (currentSaveIndex + 1) % MaxBackups;
            string path = string.Format(SaveFilePath, currentSaveIndex);

            try
            {
                var cryptoServiceProvider = new DESCryptoServiceProvider();

#pragma warning disable SYSLIB0011
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (CryptoStream cryptoStream = new CryptoStream(fs, cryptoServiceProvider.CreateEncryptor(key, iv), CryptoStreamMode.Write))
                {
                    new BinaryFormatter().Serialize(cryptoStream, save);
                }
#pragma warning restore SYSLIB0011

                Debug.Log("Board saved to slot " + path);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }

        /// <summary>
        /// Loads the most recent valid save. Returns null if no save exists or all slots are corrupt.
        /// boardWidth/boardHeight are used to reject saves from a different-sized puzzle.
        /// </summary>
        public ProgressSave Load(int boardWidth, int boardHeight)
        {
            DateTime latestTimestamp = DateTime.MinValue;
            ProgressSave result = null;

            for (int i = 0; i < MaxBackups; i++)
            {
                string path = string.Format(SaveFilePath, i);

                if (TryLoad(path, boardWidth, boardHeight, out ProgressSave backup, out DateTime timestamp))
                {
                    if (timestamp > latestTimestamp)
                    {
                        latestTimestamp = timestamp;
                        currentSaveIndex = i;
                        result = backup;
                    }
                }
            }

            if (result != null)
                return result;

            if (TryLoad(LegacySaveFilePath, boardWidth, boardHeight, out ProgressSave legacySave, out DateTime _))
            {
                Debug.Log("Loaded legacy save (boardstate.sav)");
                return legacySave;
            }

            return null;
        }

        public void Clear()
        {
            for (int i = 0; i < MaxBackups; i++)
            {
                File.Delete(string.Format(SaveFilePath, i));
                File.Delete(string.Format(ReplayFilePath, i));
            }

            File.Delete(LegacySaveFilePath);
            File.Delete(Application.persistentDataPath + "/replay.sav");
        }

        /// <summary>
        /// Redirects the pre-package serialized type name (TileBoard+ProgressSave in Assembly-CSharp)
        /// to the current Board.ProgressSave type.
        /// </summary>
        private class LegacyTypeBinder : SerializationBinder
        {
            public override Type BindToType(string assemblyName, string typeName)
            {
                if (typeName == "TileBoard+ProgressSave")
                    return typeof(ProgressSave);
                return Type.GetType($"{typeName}, {assemblyName}");
            }

            public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                assemblyName = null;
                typeName = null;
            }
        }

        private bool TryLoad(string path, int boardWidth, int boardHeight, out ProgressSave save, out DateTime timestamp)
        {
            save = null;
            timestamp = DateTime.MinValue;

            if (!File.Exists(path))
                return false;

            timestamp = File.GetLastWriteTime(path);

            try
            {
                var cryptoServiceProvider = new DESCryptoServiceProvider();

#pragma warning disable SYSLIB0011
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (CryptoStream cryptoStream = new CryptoStream(fs, cryptoServiceProvider.CreateDecryptor(key, iv), CryptoStreamMode.Read))
                {
                    var formatter = new BinaryFormatter();
                    formatter.Binder = new LegacyTypeBinder();
                    save = (ProgressSave)formatter.Deserialize(cryptoStream);
                }
#pragma warning restore SYSLIB0011

                if (save.width != boardWidth || save.height != boardHeight)
                {
                    Debug.LogError($"Save at {path} is {save.width}x{save.height}, expected {boardWidth}x{boardHeight} — skipping");
                    return false;
                }

                if (float.IsNaN(save.cameraX) || float.IsNaN(save.cameraY) || float.IsNaN(save.cameraZoom))
                {
                    Debug.LogError("Save had NaN camera values, resetting to origin");
                    save.cameraX = 0;
                    save.cameraY = 0;
                    save.cameraZoom = 0;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load save at {path}: {ex}");
                return false;
            }

            return true;
        }
    }
}

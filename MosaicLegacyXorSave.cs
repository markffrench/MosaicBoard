using System;
using System.IO;
using UnityEngine;

namespace Board
{
    /// <summary>
    /// Read-only loader for the original Mega Mosaic (mosaic) XOR save format.
    /// This predates the DES-encrypted format used by all later games and is only
    /// needed as a one-time migration path: if LegacyBoardSave.Load() returns null
    /// (no DES saves found), try this as a last resort before treating the game as new.
    ///
    /// Format of boardstate.sav:
    ///   [0..1]  width  (ushort, little-endian)
    ///   [2..3]  height (ushort, little-endian)
    ///   [4 .. 4+w*h-1]  tile state bytes, XOR'd with "MyJazzyOctopus"
    ///                   indexed as data[x * width + y + 4]  (note: acknowledged index bug
    ///                   in original code — x*width not y*width — preserved for compatibility)
    ///   [end-12..end]   camera x, y, zoom as floats (XOR'd in save but NOT decoded here
    ///                   — original load code also skipped decoding them, so camera will be
    ///                   garbage; a NaN guard is applied and position resets to origin)
    ///
    /// Once loaded, the game writes a fresh DES save via LegacyBoardSave and this path
    /// is never needed again.
    /// </summary>
    public class MosaicLegacyXorSave
    {
        private const string SecretKey = "MyJazzyOctopus";
        private string SavePath => Application.persistentDataPath + "/boardstate.sav";

        /// <summary>
        /// Attempts to load the XOR-format legacy save.
        /// Returns null if the file does not exist, cannot be parsed, or dimensions are zero.
        /// boardWidth/boardHeight are used to reject saves from a different-sized puzzle.
        /// </summary>
        public ProgressSave Load(int boardWidth, int boardHeight)
        {
            string path = SavePath;

            if (!File.Exists(path))
                return null;

            try
            {
                byte[] data = File.ReadAllBytes(path);

                if (data.Length < 4)
                {
                    Debug.LogError("MosaicLegacyXorSave: file too short to contain header");
                    return null;
                }

                ushort width  = (ushort)(data[0] | (data[1] << 8));
                ushort height = (ushort)(data[2] | (data[3] << 8));

                if (width == 0 || height == 0)
                {
                    Debug.LogError("MosaicLegacyXorSave: save header has zero dimensions — corrupted");
                    return null;
                }

                if (width != boardWidth || height != boardHeight)
                {
                    Debug.LogError($"MosaicLegacyXorSave: save is {width}x{height}, expected {boardWidth}x{boardHeight} — skipping");
                    return null;
                }

                const int headerSize = 4;
                int stateByteCount = width * height;

                if (data.Length < headerSize + stateByteCount)
                {
                    Debug.LogError("MosaicLegacyXorSave: file too short to contain full board state");
                    return null;
                }

                // Decode state bytes.
                // Original index: data[x * width + y + 4], XOR key index is the same i.
                // Note: x*width is an acknowledged bug in the original code (should be y*width
                // for row-major order), but it is consistent between save and load so the
                // round-trip is correct. Preserved exactly here.
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int i = x * width + y + headerSize;
                        data[i] ^= (byte)SecretKey[i % SecretKey.Length];
                    }
                }

                byte[] state = new byte[stateByteCount];
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        // Same index as above — preserving the original x*width layout.
                        state[x * width + y] = data[x * width + y + headerSize];
                    }
                }

                // Camera bytes: XOR'd during save but NOT decoded during load in the original
                // code. Reading them as-is produces garbage floats. A NaN guard resets to origin.
                float camX = 0f, camY = 0f, camZoom = 0f;
                if (data.Length >= headerSize + stateByteCount + 12)
                {
                    camX    = BitConverter.ToSingle(data, data.Length - 12);
                    camY    = BitConverter.ToSingle(data, data.Length - 8);
                    camZoom = BitConverter.ToSingle(data, data.Length - 4);
                }

                if (float.IsNaN(camX) || float.IsNaN(camY) || float.IsNaN(camZoom))
                {
                    Debug.LogError("MosaicLegacyXorSave: camera values are NaN (expected for XOR saves) — resetting to origin");
                    camX = camY = camZoom = 0f;
                }

                Debug.Log($"MosaicLegacyXorSave: loaded {width}x{height} board from legacy XOR save");

                return new ProgressSave
                {
                    width      = width,
                    height     = height,
                    state      = state,
                    cameraX    = camX,
                    cameraY    = camY,
                    cameraZoom = camZoom,
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"MosaicLegacyXorSave: failed to load {path}: {ex}");
                return null;
            }
        }
    }
}

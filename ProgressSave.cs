using System;

namespace Board
{
    [Serializable]
    public class ProgressSave
    {
        public int width;
        public int height;
        public byte[] state;
        public float cameraX;
        public float cameraY;
        public float cameraZoom;
    }
}

namespace Board
{
    public interface IBoardSFX
    {
        void TileFlipClear();
        void TileFlipBlack();
        void TileFlipWhite();
        void StartDrag();
        void PlayError();
        void PlayReplayStart();
        void PlayReplayLoop();
        void PlayReplayEnd();
        void PlayHint();
        void PlayClearErrors();
        void RegionComplete();
        void BossRegionComplete();
        void RegionReveal(float pitch);
        void TileReveal(float volume, float pitch);
    }
}

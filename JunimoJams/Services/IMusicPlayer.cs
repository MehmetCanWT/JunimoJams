namespace JunimoJams.Services
{
    public interface IMusicPlayer
    {
        bool IsPlayerRunning();

        TrackInfo GetCurrentTrack();

        void TogglePlayPause();

        void NextTrack();

        void PreviousTrack();
    }
}

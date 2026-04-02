namespace SpotifyValley.Services
{
    public interface IMusicPlayer
    {
        bool IsSpotifyRunning();

        TrackInfo GetCurrentTrack();

        void TogglePlayPause();

        void NextTrack();

        void PreviousTrack();
    }
}

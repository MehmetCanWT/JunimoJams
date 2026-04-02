namespace Spotify_Stardew.Services
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

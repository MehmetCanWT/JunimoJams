using System;
using System.Runtime.InteropServices;
using SpotifyValley.Services.Platform;

namespace SpotifyValley.Services
{
    public class MusicService
    {
        private readonly IMusicPlayer _player;

        public MusicService()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                this._player = new WindowsMusicPlayer();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                this._player = new MacMusicPlayer();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                this._player = new LinuxMusicPlayer();
            }
            else
            {
                this._player = new WindowsMusicPlayer(); // Fallback
            }
        }

        public TrackInfo GetCurrentTrack()
        {
            try
            {
                return this._player.GetCurrentTrack();
            }
            catch
            {
                return new TrackInfo
                {
                    Name = "Error",
                    Artist = "Check Logs",
                    IsPlaying = false
                };
            }
        }

        public void IsSpotifyRunning()
        {
            this._player.IsSpotifyRunning();
        }

        public void TogglePlayPause()
        {
            this._player.TogglePlayPause();
        }

        public void NextTrack()
        {
            this._player.NextTrack();
        }

        public void PreviousTrack()
        {
            this._player.PreviousTrack();
        }
    }
}

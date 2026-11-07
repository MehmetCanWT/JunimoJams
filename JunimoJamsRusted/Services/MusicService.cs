using System;
using System.Runtime.InteropServices;
using JunimoJamsRusted.Services.Platform;

namespace JunimoJamsRusted.Services
{
    public class MusicService
    {
        private readonly IMusicPlayer _player;

        public MusicService(string[]? extraPlayers = null)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (WineDetector.IsRunningUnderWineOrProton())
                    this._player = new LinuxMusicPlayer(extraPlayers);
                else
                    this._player = new WindowsMusicPlayer(extraPlayers);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                this._player = new MacMusicPlayer(extraPlayers);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                this._player = new LinuxMusicPlayer(extraPlayers);
            else
                this._player = new LinuxMusicPlayer(extraPlayers); // Fallback
        }

        public TrackInfo GetCurrentTrack()
        {
            try
            {
                return this._player.GetCurrentTrack();
            }
            catch
            {
                return new TrackInfo { Name = "Error", Artist = "Check Logs", IsPlaying = false };
            }
        }

        public bool IsPlayerRunning() => this._player.IsPlayerRunning();

        public void TogglePlayPause() => this._player.TogglePlayPause();

        public void NextTrack() => this._player.NextTrack();

        public void PreviousTrack() => this._player.PreviousTrack();
    }
}

using System;
using System.Runtime.InteropServices;

namespace JunimoJams.Services.Platform
{
    public static class WineDetector
    {
        private static bool? _isWineCached;
        private static string? _wineVersionCached;

        [DllImport("ntdll.dll", EntryPoint = "wine_get_version", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr WineGetVersionRaw();

        public static bool IsRunningUnderWineOrProton()
        {
            if (_isWineCached.HasValue)
                return _isWineCached.Value;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _isWineCached = false;
                return false;
            }

            try
            {
                IntPtr ptr = WineGetVersionRaw();
                if (ptr != IntPtr.Zero)
                {
                    _wineVersionCached = Marshal.PtrToStringAnsi(ptr);
                    _isWineCached = true;
                    return true;
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Native Windows: ntdll.dll does not export wine_get_version
            }
            catch (DllNotFoundException)
            {
                // Native Linux / non-Windows
            }
            catch
            {
                // Suppress any unexpected interop errors
            }

            _isWineCached = false;
            return false;
        }

        public static string GetWineVersion()
        {
            if (IsRunningUnderWineOrProton() && _wineVersionCached != null)
                return _wineVersionCached;

            return string.Empty;
        }
    }
}

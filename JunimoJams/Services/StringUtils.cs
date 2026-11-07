using System.Collections.Generic;
using System.Text;

namespace JunimoJams.Services
{
    public static class StringUtils
    {
        // Maps non-ASCII Turkish characters to closest ASCII for standard font compatibility
        private static readonly Dictionary<char, char> ReplacementMap = new()
        {
            // Turkish
            { 'ı', 'i' }, { 'İ', 'I' }, { 'ğ', 'g' }, { 'Ğ', 'G' }, { 'ü', 'u' }, { 'Ü', 'U' },
            { 'ş', 's' }, { 'Ş', 'S' }, { 'ö', 'o' }, { 'Ö', 'O' }, { 'ç', 'c' }, { 'Ç', 'C' }
        };

        public static string NormalizeForHud(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(ReplacementMap.TryGetValue(c, out char mapped) ? mapped : c);

            return sb.ToString();
        }
    }
}

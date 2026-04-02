using System.Collections.Generic;
using System.Text;

namespace SpotifyValley.Services
{
    public static class StringUtils
    {
        private static readonly Dictionary<char, char> ReplacementMap = new Dictionary<char, char>
        {
            // Turkish
            { 'ı', 'i' }, { 'İ', 'I' }, { 'ğ', 'g' }, { 'Ğ', 'G' }, { 'ü', 'u' }, { 'Ü', 'U' },
            { 'ş', 's' }, { 'Ş', 'S' }, { 'ö', 'o' }, { 'Ö', 'O' }, { 'ç', 'c' }, { 'Ç', 'C' },
            // Russian (Cyrillic - basic mapping for HUD readability)
            { 'а', 'a' }, { 'б', 'b' }, { 'в', 'v' }, { 'г', 'g' }, { 'д', 'd' }, { 'е', 'e' },
            { 'ё', 'e' }, { 'ж', 'z' }, { 'з', 'z' }, { 'и', 'i' }, { 'й', 'y' }, { 'к', 'k' },
            { 'л', 'l' }, { 'м', 'm' }, { 'н', 'n' }, { 'о', 'o' }, { 'п', 'p' }, { 'р', 'r' },
            { 'с', 's' }, { 'т', 't' }, { 'у', 'u' }, { 'ф', 'f' }, { 'х', 'h' }, { 'ц', 'c' },
            { 'ч', 'c' }, { 'ш', 's' }, { 'щ', 's' }, { 'ы', 'y' }, { 'э', 'e' }, { 'ю', 'u' },
            { 'я', 'a' }
        };

        public static string NormalizeForHud(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            StringBuilder sb = new StringBuilder();
            foreach (char c in text)
            {
                if (ReplacementMap.ContainsKey(c))
                {
                    sb.Append(ReplacementMap[c]);
                }
                else if (c > 127)
                {
                    // For other non-ASCII, we can try to use a generic replacement or just skip
                    // but for now, we'll keep the character and hope for the best
                    sb.Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}

using System.Collections.Generic;
using System.Text;

namespace JunimoJams.Services
{
    public static class StringUtils
    {
        // Maps non-ASCII characters to their closest ASCII equivalents for HUD rendering
        private static readonly Dictionary<char, char> ReplacementMap = new()
        {
            // Turkish
            { 'ı', 'i' }, { 'İ', 'I' }, { 'ğ', 'g' }, { 'Ğ', 'G' }, { 'ü', 'u' }, { 'Ü', 'U' },
            { 'ş', 's' }, { 'Ş', 'S' }, { 'ö', 'o' }, { 'Ö', 'O' }, { 'ç', 'c' }, { 'Ç', 'C' },
            // Russian (Cyrillic lowercase)
            { 'а', 'a' }, { 'б', 'b' }, { 'в', 'v' }, { 'г', 'g' }, { 'д', 'd' }, { 'е', 'e' },
            { 'ё', 'e' }, { 'ж', 'z' }, { 'з', 'z' }, { 'и', 'i' }, { 'й', 'y' }, { 'к', 'k' },
            { 'л', 'l' }, { 'м', 'm' }, { 'н', 'n' }, { 'о', 'o' }, { 'п', 'p' }, { 'р', 'r' },
            { 'с', 's' }, { 'т', 't' }, { 'у', 'u' }, { 'ф', 'f' }, { 'х', 'h' }, { 'ц', 'c' },
            { 'ч', 'c' }, { 'ш', 's' }, { 'щ', 's' }, { 'ы', 'y' }, { 'э', 'e' }, { 'ю', 'u' },
            { 'я', 'a' },
            // Russian (Cyrillic uppercase)
            { 'А', 'A' }, { 'Б', 'B' }, { 'В', 'V' }, { 'Г', 'G' }, { 'Д', 'D' }, { 'Е', 'E' },
            { 'Ё', 'E' }, { 'Ж', 'Z' }, { 'З', 'Z' }, { 'И', 'I' }, { 'Й', 'Y' }, { 'К', 'K' },
            { 'Л', 'L' }, { 'М', 'M' }, { 'Н', 'N' }, { 'О', 'O' }, { 'П', 'P' }, { 'Р', 'R' },
            { 'С', 'S' }, { 'Т', 'T' }, { 'У', 'U' }, { 'Ф', 'F' }, { 'Х', 'H' }, { 'Ц', 'C' },
            { 'Ч', 'C' }, { 'Ш', 'S' }, { 'Щ', 'S' }, { 'Ы', 'Y' }, { 'Э', 'E' }, { 'Ю', 'U' },
            { 'Я', 'A' }
        };

        public static string NormalizeForHud(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(ReplacementMap.TryGetValue(c, out char mapped) ? mapped : c);

            return sb.ToString();
        }
    }
}

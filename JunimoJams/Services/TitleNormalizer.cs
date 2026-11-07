using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JunimoJams.Services
{
    public static class TitleNormalizer
    {
        private static readonly Regex RemasterRegex = new(
            @"\s*[\(\[]\s*(?:(?:19|20)\d{2}\s*)?(?:digital\s*)?(?:remaster(?:ed)?|deluxe(?:\s*edition)?|anniversary(?:\s*edition)?|mono|stereo|re-recorded|live|radio\s*edit|extended\s*mix|original\s*mix|club\s*mix)\b[^\)\]]*[\)\]]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FeatRegex = new(
            @"\s*[\(\[]\s*(?:feat|ft|featuring)\.?\s+([^\)\]]+)[\)\]]|\s+(?:feat|ft|featuring)\.?\s+([^\-]+?)(?=\s*[\-\–\—]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex VideoJunkRegex = new(
            @"\s*[\(\[]\s*(?:official\s*(?:music\s*)?video|lyric\s*video|audio|visualizer|clip\s*officiel|4k(?:\s*60fps)?|hd)\s*[\)\]]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BracketedNoiseRegex = new(
            @"\s*[\(\[].*?[\)\]]",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(
            @"\s+",
            RegexOptions.Compiled);

        public static string CleanTrackTitle(string title, out string? extractedFeature)
        {
            extractedFeature = null;
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            string cleaned = title;

            // 1. Extract featured artist if present
            var featMatch = FeatRegex.Match(cleaned);
            if (featMatch.Success)
            {
                extractedFeature = featMatch.Groups[1].Success ? featMatch.Groups[1].Value : featMatch.Groups[2].Value;
                extractedFeature = extractedFeature.Trim();
                cleaned = FeatRegex.Replace(cleaned, string.Empty);
            }

            // 2. Remove YouTube video tags
            cleaned = VideoJunkRegex.Replace(cleaned, string.Empty);

            // 3. Remove remaster / live / version tags
            cleaned = RemasterRegex.Replace(cleaned, string.Empty);

            // 4. Strip trailing separators (" - ", " / ")
            int dashIdx = cleaned.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0)
            {
                cleaned = cleaned.Substring(0, dashIdx);
            }

            // 5. Clean remaining generic brackets if title is still noisy
            if (cleaned.Contains('(') || cleaned.Contains('['))
            {
                string candidate = BracketedNoiseRegex.Replace(cleaned, string.Empty);
                if (!string.IsNullOrWhiteSpace(candidate))
                    cleaned = candidate;
            }

            // 6. Normalize whitespace
            cleaned = WhitespaceRegex.Replace(cleaned, " ").Trim();
            return cleaned;
        }

        public static string NormalizeUnicode(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // FormD normalization decomposes characters with diacritics
            string normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != UnicodeCategory.NonSpacingMark)
                {
                    // Special Turkish conversion
                    if (c == 'ı') sb.Append('i');
                    else if (c == 'İ') sb.Append('I');
                    else sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
        }
    }
}

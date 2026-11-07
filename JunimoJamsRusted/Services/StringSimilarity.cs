using System;
using System.Collections.Generic;

namespace JunimoJamsRusted.Services
{
    public static class StringSimilarity
    {
        public static double CalculateCompositeScore(
            string targetArtist, string targetTitle,
            string foundArtist, string foundTitle,
            bool isGenericArtist = false)
        {
            if (isGenericArtist)
            {
                // When artist is generic ("YouTube Music"), score is based purely on title precision
                double titleJaro = JaroWinkler(targetTitle, foundTitle);
                double titleDice = SorensenDice(targetTitle, foundTitle);
                double titleJaccard = TokenJaccard(targetTitle, foundTitle);
                return (titleJaro * 0.4) + (titleDice * 0.3) + (titleJaccard * 0.3);
            }

            double artistScore = (JaroWinkler(targetArtist, foundArtist) * 0.6) +
                                 (TokenJaccard(targetArtist, foundArtist) * 0.4);

            double titleScore = (JaroWinkler(targetTitle, foundTitle) * 0.5) +
                                (SorensenDice(targetTitle, foundTitle) * 0.3) +
                                (TokenJaccard(targetTitle, foundTitle) * 0.2);

            return (artistScore * 0.45) + (titleScore * 0.55);
        }

        public static double JaroWinkler(string s1, string s2, double prefixWeight = 0.1)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0.0;

            s1 = s1.ToLowerInvariant().Trim();
            s2 = s2.ToLowerInvariant().Trim();

            if (s1 == s2) return 1.0;

            int len1 = s1.Length;
            int len2 = s2.Length;
            int matchDistance = Math.Max(len1, len2) / 2 - 1;
            if (matchDistance < 0) matchDistance = 0;

            bool[] s1Matches = new bool[len1];
            bool[] s2Matches = new bool[len2];
            int matches = 0;

            for (int i = 0; i < len1; i++)
            {
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(i + matchDistance + 1, len2);

                for (int j = start; j < end; j++)
                {
                    if (s2Matches[j] || s1[i] != s2[j]) continue;
                    s1Matches[i] = true;
                    s2Matches[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0) return 0.0;

            double transpositions = 0;
            int k = 0;
            for (int i = 0; i < len1; i++)
            {
                if (!s1Matches[i]) continue;
                while (!s2Matches[k]) k++;
                if (s1[i] != s2[k]) transpositions++;
                k++;
            }

            double jaro = ((double)matches / len1 +
                           (double)matches / len2 +
                           (matches - transpositions / 2.0) / matches) / 3.0;

            // Winkler prefix scaling
            int prefix = 0;
            for (int i = 0; i < Math.Min(4, Math.Min(len1, len2)); i++)
            {
                if (s1[i] == s2[i]) prefix++;
                else break;
            }

            return jaro + (prefix * prefixWeight * (1.0 - jaro));
        }

        public static double SorensenDice(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            s1 = s1.ToLowerInvariant().Trim();
            s2 = s2.ToLowerInvariant().Trim();
            if (s1 == s2) return 1.0;
            if (s1.Length < 2 || s2.Length < 2) return 0.0;

            var biGrams1 = new HashSet<int>();
            for (int i = 0; i < s1.Length - 1; i++)
                biGrams1.Add((s1[i] << 16) | s1[i + 1]);

            int matches = 0;
            int totalBigrams2 = s2.Length - 1;
            for (int i = 0; i < totalBigrams2; i++)
            {
                int code = (s2[i] << 16) | s2[i + 1];
                if (biGrams1.Contains(code))
                    matches++;
            }

            return (2.0 * matches) / (biGrams1.Count + totalBigrams2);
        }

        public static double TokenJaccard(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            s1 = s1.ToLowerInvariant().Trim();
            s2 = s2.ToLowerInvariant().Trim();
            if (s1 == s2) return 1.0;

            var words1 = new HashSet<string>(s1.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var words2 = new HashSet<string>(s2.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (words1.Count == 0 || words2.Count == 0) return 0.0;

            int intersection = 0;
            foreach (var w in words1)
            {
                if (words2.Contains(w))
                    intersection++;
            }

            int union = words1.Count + words2.Count - intersection;
            return union == 0 ? 0.0 : (double)intersection / union;
        }

        public static int DamerauLevenshtein(ReadOnlySpan<char> s1, ReadOnlySpan<char> s2)
        {
            int n = s1.Length;
            int m = s2.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            // Stackalloc for zero GC pressure up to 256 chars
            Span<int> prevPrev = stackalloc int[m + 1];
            Span<int> prev = stackalloc int[m + 1];
            Span<int> current = stackalloc int[m + 1];

            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                current[0] = i;
                char c1 = s1[i - 1];

                for (int j = 1; j <= m; j++)
                {
                    char c2 = s2[j - 1];
                    int cost = (c1 == c2) ? 0 : 1;

                    int min = Math.Min(prev[j] + 1, Math.Min(current[j - 1] + 1, prev[j - 1] + cost));

                    // Transposition
                    if (i > 1 && j > 1 && c1 == s2[j - 2] && s1[i - 2] == c2)
                    {
                        min = Math.Min(min, prevPrev[j - 2] + cost);
                    }

                    current[j] = min;
                }

                prev.CopyTo(prevPrev);
                current.CopyTo(prev);
            }

            return current[m];
        }
    }
}

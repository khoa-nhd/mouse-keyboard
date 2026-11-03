namespace KeyboardMouse.Core;

/// <summary>
/// Generates fixed-length, prefix-free hint labels from a short home-row
/// alphabet. The alphabet is interleaved left/right so adjacent keystrokes
/// alternate hands as often as possible.
/// </summary>
public static class HintLabels
{
    public const string DefaultAlphabet = "asdfjkl;";

    /// <summary>
    /// Lower-cases, de-duplicates and filters an alphabet to the characters a
    /// hint label can actually be typed with. Fewer than two usable symbols
    /// fall back to <see cref="DefaultAlphabet"/>.
    /// </summary>
    public static string Normalize(string? alphabet)
    {
        var symbols = ReadSymbols(alphabet);
        if (symbols.Count < 2)
        {
            symbols = ReadSymbols(DefaultAlphabet);
        }

        return new string(symbols.ToArray());
    }

    /// <summary>Letters, digits and the punctuation keys hint labels support.</summary>
    public static bool IsSupported(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or
            ';' or ',' or '.' or '\'' or '/' or '-' or '=' or '[' or ']' or '\\' or '`';

    public static IReadOnlyList<string> Generate(int count, string? alphabet = null)
    {
        if (count <= 0)
        {
            return [];
        }

        var symbols = NormalizeAlphabet(alphabet);
        int length = LabelLength(count, symbols.Count);

        var labels = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var chars = new char[length];
            int value = i;

            // First digit changes fastest, so single-letter labels alternate
            // hands and two-letter labels change their first letter often.
            for (int position = 0; position < length; position++)
            {
                chars[position] = symbols[value % symbols.Count];
                value /= symbols.Count;
            }

            labels.Add(new string(chars));
        }

        return labels;
    }

    private static int LabelLength(int count, int alphabetSize)
    {
        long capacity = alphabetSize;
        int length = 1;

        while (capacity < count)
        {
            capacity *= alphabetSize;
            length++;
        }

        return length;
    }

    private static List<char> NormalizeAlphabet(string? alphabet)
    {
        var symbols = ReadSymbols(alphabet);

        if (symbols.Count < 2)
        {
            symbols = ReadSymbols(DefaultAlphabet);
        }

        // Default home row is "asdfjkl;": interleaving produces
        // "a j s k d l f ;" which alternates the physical hands.
        var interleaved = new List<char>(symbols.Count);
        int leftCount = (symbols.Count + 1) / 2;
        for (int i = 0; i < leftCount; i++)
        {
            interleaved.Add(symbols[i]);
            int right = leftCount + i;
            if (right < symbols.Count)
            {
                interleaved.Add(symbols[right]);
            }
        }

        return interleaved;
    }

    private static List<char> ReadSymbols(string? alphabet)
    {
        var symbols = new List<char>();
        var seen = new HashSet<char>();

        foreach (char raw in alphabet ?? string.Empty)
        {
            char symbol = char.ToLowerInvariant(raw);
            if (IsSupported(symbol) && seen.Add(symbol))
            {
                symbols.Add(symbol);
            }
        }

        return symbols;
    }
}

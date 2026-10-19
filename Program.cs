using System.Text;

// TopWords: prints the 20 most frequent words in a file (same result as the Bash pipeline).
// - Reads raw bytes, no text decoding. A word is a run of A-Z/a-z, every other byte is a separator,
//   like `tr -cs 'a-zA-Z'`, so any file (binary, empty, UTF-16...) is safe.
// - Streams the file in blocks, so memory depends on the number of distinct words, not on the file size.
// - Single thread on purpose: simple, and the work is mostly I/O (see README).
internal class Program
{
    private const int Success = 0;
    private const int UsageError = 1;
    private const int FileError = 2;
    private const int UnexpectedError = 3;

    private const int TopCount = 20;
    private const int BufferSize = 64 * 1024;

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: TopWords <file path>");
            return UsageError;
        }

        string path = args[0];

        // Only for a friendly message. The file can still change after this check, the catch below is the real protection.
        if (!TryValidateFile(path, out string error))
        {
            Console.Error.WriteLine(error);
            return FileError;
        }

        try
        {
            Dictionary<string, long> counts = CountWords(path);
            PrintTop(counts, TopCount);
            return Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Cannot read '{path}': {ex.Message}");
            return FileError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return UnexpectedError;
        }
    }

    private static bool TryValidateFile(string path, out string error)
    {
        if (Directory.Exists(path))
        {
            error = $"'{path}' is a folder, not a file.";
            return false;
        }

        if (!File.Exists(path))
        {
            error = $"File '{path}' does not exist.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static Dictionary<string, long> CountWords(string path)
    {
        var counts = new Dictionary<string, long>();
        var word = new StringBuilder();   // outside the loop: a word can continue in the next block
        byte[] buffer = new byte[BufferSize];

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < bytesRead; i++)   // not buffer.Length: the tail of the buffer can be stale
            {
                byte b = buffer[i];

                if (IsAsciiLetter(b))
                {
                    word.Append(char.ToLowerInvariant((char)b));
                }
                else
                {
                    AddWord(counts, word);
                }
            }
        }

        AddWord(counts, word);   // last word, if the file does not end with a separator

        return counts;
    }

    private static bool IsAsciiLetter(byte b) =>
        (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z');   // 91-96 are symbols, not letters

    private static void AddWord(Dictionary<string, long> counts, StringBuilder word)
    {
        if (word.Length == 0)
        {
            return;
        }

        string key = word.ToString();
        counts.TryGetValue(key, out long count);
        counts[key] = count + 1;

        word.Clear();
    }

    private static void PrintTop(Dictionary<string, long> counts, int howMany)
    {
        var top = counts
            .OrderByDescending(e => e.Value)
            .ThenByDescending(e => e.Key, StringComparer.Ordinal)   // equal counts: reverse alphabetical, like `sort -nr`
            .Take(howMany);

        foreach (KeyValuePair<string, long> entry in top)
        {
            Console.WriteLine($"{entry.Value,7} {entry.Key}");
        }
    }
}

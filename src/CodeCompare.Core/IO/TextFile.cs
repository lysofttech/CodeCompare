using System.Text;

namespace CodeCompare.Core.IO;

/// <summary>A decoded text file split into lines, with encoding and line-ending information.</summary>
public sealed class TextFile
{
    private const int BinaryProbeLength = 8000;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static TextFile Empty { get; } = new([], "", "", false);

    private TextFile(string[] lines, string encodingName, string lineEnding, bool isBinary)
    {
        Lines = lines;
        EncodingName = encodingName;
        LineEnding = lineEnding;
        IsBinary = isBinary;
    }

    public string[] Lines { get; }
    public string EncodingName { get; }
    public string LineEnding { get; }
    public bool IsBinary { get; }

    public static TextFile Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static TextFile FromBytes(byte[] data)
    {
        if (IsBinaryData(data)) return new TextFile([], "Binary", "", true);
        var (text, encoding) = Decode(data);
        return new TextFile(SplitLines(text), encoding, DetectLineEnding(text), false);
    }

    public static bool IsBinaryData(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
            return false; // UTF-16 text legitimately contains zero bytes
        return data[..Math.Min(data.Length, BinaryProbeLength)].IndexOf((byte)0) >= 0;
    }

    public static (string Text, string EncodingName) Decode(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return (Encoding.UTF8.GetString(data, 3, data.Length - 3), "UTF-8 BOM");
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return (Encoding.Unicode.GetString(data, 2, data.Length - 2), "UTF-16 LE");
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2), "UTF-16 BE");

        try
        {
            return (StrictUtf8.GetString(data), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(data), "ANSI");
        }
    }

    public static string[] SplitLines(string text)
    {
        if (text.Length == 0) return [];

        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r' && c != '\n') continue;

            lines.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }

        if (start < text.Length) lines.Add(text[start..]);
        return lines.ToArray();
    }

    public static string DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        if (kinds == 0) return "";
        if (kinds > 1) return "Mixed";
        return crlf > 0 ? "CRLF" : lf > 0 ? "LF" : "CR";
    }
}

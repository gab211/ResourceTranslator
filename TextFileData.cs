using System.Text;

namespace ResourceTranslator;

internal sealed record TextFileData(string Text, Encoding Encoding, bool HasBom, string NewLine)
{
    public static TextFileData Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength, hasBom) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new TextFileData(text, encoding, hasBom, newLine);
    }

    public void Write(string path, string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", NewLine);
        var body = Encoding.GetBytes(normalized);
        var preamble = HasBom ? Encoding.GetPreamble() : Array.Empty<byte>();
        using var stream = File.Create(path);
        if (preamble.Length > 0) stream.Write(preamble);
        stream.Write(body);
    }

    private static (Encoding Encoding, int BomLength, bool HasBom) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false), 3, true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode, 2, true);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode, 2, true);
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return (new UTF32Encoding(true, false), 4, true);
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return (new UTF32Encoding(false, false), 4, true);

        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            strictUtf8.GetString(bytes);
            return (new UTF8Encoding(false), 0, false);
        }
        catch
        {
            return (Encoding.Default, 0, false);
        }
    }
}

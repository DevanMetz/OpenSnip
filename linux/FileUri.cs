using System.Text;

namespace OpenSnip;

static class FileUri
{
    public static string FromPath(string path)
    {
        string full = Path.GetFullPath(path);
        var builder = new StringBuilder("file://");
        foreach (byte b in Encoding.UTF8.GetBytes(full))
        {
            bool safe = b == (byte)'/'
                || (b >= (byte)'A' && b <= (byte)'Z')
                || (b >= (byte)'a' && b <= (byte)'z')
                || (b >= (byte)'0' && b <= (byte)'9')
                || b == (byte)'-' || b == (byte)'.' || b == (byte)'_' || b == (byte)'~';
            if (safe) builder.Append((char)b);
            else builder.Append('%').Append(b.ToString("X2"));
        }
        return builder.ToString();
    }
}

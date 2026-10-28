using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace OpenSnip;

static class FileUri
{
    public static readonly DataFormat<byte[]> UriListFormat =
        DataFormat.CreateBytesPlatformFormat("text/uri-list");

    public static DataTransfer CreateDragData(string path, IStorageItem file)
    {
        // Native targets consume URI lists; Avalonia targets can consume the storage item.
        // Keep both representations on one item so a drop represents exactly one file.
        var item = new DataTransferItem();
        item.Set(UriListFormat, Encoding.UTF8.GetBytes(FromPath(path) + "\r\n"));
        if (file != null) item.SetFile(file);
        var data = new DataTransfer();
        data.Add(item);
        return data;
    }

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

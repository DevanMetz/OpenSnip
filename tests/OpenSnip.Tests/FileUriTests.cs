using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace OpenSnip.Tests;

public class FileUriTests
{
    [Theory]
    [InlineData("/tmp/clip.mp4", "file:///tmp/clip.mp4")]
    [InlineData("/tmp/a b#?%\".mp4", "file:///tmp/a%20b%23%3F%25%22.mp4")]
    [InlineData("/tmp/日本語🎥.mp4", "file:///tmp/%E6%97%A5%E6%9C%AC%E8%AA%9E%F0%9F%8E%A5.mp4")]
    [InlineData("/tmp/a\r\nb.mp4", "file:///tmp/a%0D%0Ab.mp4")]
    public void Uri_list_encodes_one_file_with_crlf(string path, string uri)
    {
        using var data = FileUri.CreateDragData(path, null);
        var item = Assert.Single(data.Items);
        Assert.Equal(uri + "\r\n", Encoding.UTF8.GetString(item.TryGetValue(FileUri.UriListFormat)));
        Assert.Equal(uri, FileUri.FromPath(path));
        Assert.Equal("text/uri-list", FileUri.UriListFormat.Identifier);
        Assert.Null(item.TryGetFile());
    }

    [Fact]
    public void Storage_item_and_native_uri_represent_the_same_mp4()
    {
        // Avalonia seals storage implementations behind internal constructors. Use its
        // real local-file implementation without starting a desktop or file picker.
        var type = typeof(IStorageItem).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile", true);
        using var file = (IStorageItem)Activator.CreateInstance(type,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, new object[] { new FileInfo("/tmp/recording.mp4") }, null);
        using var data = FileUri.CreateDragData(file.Path.LocalPath, file);
        var item = Assert.Single(data.Items);
        Assert.Same(file, item.TryGetFile());
        Assert.Equal("file:///tmp/recording.mp4\r\n",
            Encoding.UTF8.GetString(item.TryGetValue(FileUri.UriListFormat)));
        Assert.Equal(2, item.Formats.Count);
    }

    [Fact]
    public void Relative_paths_are_made_absolute()
    {
        string uri = FileUri.FromPath("recording.mp4");
        Assert.Equal(Path.GetFullPath("recording.mp4"), new Uri(uri).LocalPath);
    }

}

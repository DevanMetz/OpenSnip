using Avalonia.Controls;
using Avalonia.Platform;

namespace OpenSnip;

static class IconLoader
{
    static WindowIcon _icon;

    public static WindowIcon Icon
    {
        get
        {
            if (_icon != null) return _icon;
            try
            {
                using var asset = AssetLoader.Open(new Uri("avares://OpenSnip/Assets/app.ico"));
                using var memory = new MemoryStream();
                asset.CopyTo(memory);
                memory.Position = 0;
                _icon = new WindowIcon(memory);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return _icon;
        }
    }
}

using System.IO;

namespace fast_export;

public class FileRenameCommand : FileCommand
{
    public string Source { get; private set; }

    public FileRenameCommand(string src, string dest)
    {
        this.Source = src;
        base.Path = dest;
    }

    public override void RenderCommand(Stream stream)
    {
        stream.WriteLine($"R {Source} {Path}");
    }
}

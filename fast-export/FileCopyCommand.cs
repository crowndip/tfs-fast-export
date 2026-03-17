using System.IO;

namespace fast_export;

public class FileCopyCommand : FileCommand
{
    public string Source { get; private set; }

    public FileCopyCommand(string src, string dest)
    {
        this.Source = src;
        base.Path = dest;
    }

    public override void RenderCommand(Stream stream)
    {
        stream.WriteLine($"C {Source} {Path}");
    }
}

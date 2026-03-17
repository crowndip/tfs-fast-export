using System.IO;

namespace fast_export;

public class FileDeleteCommand : FileCommand
{
    public FileDeleteCommand(string path)
    {
        base.Path = path;
    }

    public override void RenderCommand(Stream stream)
    {
        stream.WriteLine($"D {Path}");
    }
}

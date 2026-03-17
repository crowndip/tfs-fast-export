using System.IO;

namespace fast_export;

public class FileDeleteAllCommand : FileCommand
{
    public override void RenderCommand(Stream stream)
    {
        stream.WriteLine("deleteall");
    }
}

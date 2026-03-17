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
        stream.WriteLine($"C {Quote(Source)} {Quote(Path!)}");
    }

    private static string Quote(string path)
    {
        if (!path.Contains(" ") && !path.Contains("\"") && !path.Contains("\\"))
            return path;
        return "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

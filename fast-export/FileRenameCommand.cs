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
        // git fast-import R is space-delimited, so paths containing spaces must be
        // C-quoted (wrap in double quotes, escape backslashes and double quotes).
        stream.WriteLine($"R {Quote(Source)} {Quote(Path!)}");
    }

    private static string Quote(string path)
    {
        if (!path.Contains(" ") && !path.Contains("\"") && !path.Contains("\\"))
            return path;
        return "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

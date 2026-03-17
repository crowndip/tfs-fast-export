using System.IO;

namespace fast_export;

public abstract class ResetCommand : Command
{
    public string Reference { get; private set; }
    public CommitCommand? From { get; private set; }

    public ResetCommand(string reference, CommitCommand? from)
    {
        this.Reference = reference;
        this.From = from;
    }

    public override void RenderCommand(Stream stream)
    {
        stream.WriteLine($"reset {Reference}");
        if (From != null)
        {
            stream.WriteString("from ");
            From.RenderMarkReference(stream);
            stream.WriteLineFeed();
        }
    }
}

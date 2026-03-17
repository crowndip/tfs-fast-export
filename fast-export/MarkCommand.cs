using System;
using System.IO;

namespace fast_export;

public abstract class MarkCommand : Command
{
    public int? MarkId { get; protected set; }
    private bool _HasBeenRendered;

    public void RenderMarkReference(Stream stream)
    {
        if (!_HasBeenRendered)
            throw new InvalidOperationException("A MarkCommand cannot be referenced if it has not been rendered.");

        stream.WriteString($":{MarkId}");
    }

    protected void RenderMarkCommand(Stream stream)
    {
        if (MarkId != null)
        {
            stream.WriteLine($"mark :{MarkId}");
            _HasBeenRendered = true;
        }
    }
}

namespace fast_export;

public abstract class FileCommand : Command
{
    public string? Path { get; protected set; }
}

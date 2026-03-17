using System;
using System.IO;

namespace fast_export;

public abstract class NameCommand : Command
{
    public abstract string CommandName { get; }
    public string Name { get; private set; }
    public string Email { get; private set; }
    public DateTimeOffset Date { get; private set; }

    protected NameCommand(string name, string email, DateTimeOffset date)
    {
        this.Name = name;
        this.Email = email;
        this.Date = date;
    }

    private static long ToUnixTimestamp(DateTimeOffset dt)
    {
        return (long)(dt.ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;
    }

    public override void RenderCommand(Stream stream)
    {
        var command = CommandName;
        if (!string.IsNullOrEmpty(Name))
            command += " " + Name;
        command += $" <{Email}> ";
        command += $"{ToUnixTimestamp(Date)} +0000";

        stream.WriteLine(command);
    }
}

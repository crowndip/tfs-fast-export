using System;

namespace fast_export;

public class CommitterCommand : NameCommand
{
    public override string CommandName => "committer";

    public CommitterCommand(string name, string email, DateTimeOffset date)
        : base(name, email, date) { }
}

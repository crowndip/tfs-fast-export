using System;

namespace fast_export;

public class AuthorCommand : NameCommand
{
    public override string CommandName => "author";

    public AuthorCommand(string name, string email, DateTimeOffset date)
        : base(name, email, date) { }
}

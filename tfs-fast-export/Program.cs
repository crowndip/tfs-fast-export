using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;

using fast_export;

namespace tfs_fast_export;

class Program
{
    private static HashSet<int> _SkipCommits = new()
    {
        // use for skipping checkins that are unnecessary/outside the scope of branching
        // one example is build templates for TFS
    };

    private static HashSet<int> _BreakCommits = new()
    {
        // use this for debugging when you want to stop at a particular checkin for analysis
    };

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: tfs-fast-export <tfs-collection-url> <tfs-root-path> [DOMAIN\\username] [password]");
            Console.Error.WriteLine("Example: tfs-fast-export http://server:8080/tfs/Collection $/MyProject CORP\\jsmith");
            Console.Error.WriteLine("         (if password is omitted it is prompted securely)");
            Environment.Exit(1);
        }

        var collectionUrl = args[0];
        var tfsRootPath = args[1];

        TfsTeamProjectCollection collection;
        if (args.Length >= 3)
        {
            // Parse optional DOMAIN\username argument.
            var userArg = args[2];
            string domain = "", username = userArg;
            var backslash = userArg.IndexOf('\\');
            if (backslash >= 0)
            {
                domain   = userArg.Substring(0, backslash);
                username = userArg.Substring(backslash + 1);
            }

            // Accept password from args or prompt securely on stderr so that stdout
            // (piped to git fast-import) is not polluted.
            string password;
            if (args.Length >= 4)
            {
                password = args[3];
            }
            else
            {
                Console.Error.Write($"Password for {userArg}: ");
                password = ReadPasswordFromConsole();
                Console.Error.WriteLine();
            }

            var networkCred = new NetworkCredential(username, password, domain);
            collection = new TfsTeamProjectCollection(new Uri(collectionUrl), networkCred);
        }
        else
        {
            collection = new TfsTeamProjectCollection(new Uri(collectionUrl));
        }

        collection.EnsureAuthenticated();
        var versionControl = collection.GetService<VersionControlServer>();

        TfsChangeSet.Initialize(tfsRootPath, versionControl);

        // includeChanges:false — we fetch the full change list per-changeset inside
        // TfsChangeSet using GetChangesForChangeset, which is not subject to the server-side
        // truncation that affects Changeset.Changes (Issue #1).
        var allChanges = versionControl
            .QueryHistory(
                tfsRootPath,
                VersionSpec.Latest,
                0,
                RecursionType.Full,
                null,
                new ChangesetVersionSpec(1),
                VersionSpec.Latest,
                int.MaxValue,
                false,
                false)
            .OfType<Changeset>()
            .OrderBy(x => x.ChangesetId)
            .ToList();

        var outStream = Console.OpenStandardOutput();
        foreach (var changeSet in allChanges)
        {
            if (_SkipCommits.Contains(changeSet.ChangesetId))
                continue;
            if (_BreakCommits.Contains(changeSet.ChangesetId))
                System.Diagnostics.Debugger.Break();

            var commit = new TfsChangeSet(changeSet).ProcessChangeSet();
            if (commit == null)
                continue;

            outStream.RenderCommand(commit);
            outStream.WriteLine($"progress {changeSet.ChangesetId}/{allChanges.Last().ChangesetId}");
        }
        outStream.WriteLine("done");
        outStream.Close();
    }

    // Read a password from the console without echoing characters.
    // Uses Console.Error for the prompt so stdout (piped to git fast-import) is unaffected.
    private static string ReadPasswordFromConsole()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password.Remove(password.Length - 1, 1);
            }
            else
            {
                password.Append(key.KeyChar);
            }
        }
        return password.ToString();
    }
}

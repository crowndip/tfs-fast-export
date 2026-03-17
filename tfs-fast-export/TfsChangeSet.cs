using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;

using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;

using fast_export;

namespace tfs_fast_export;

public class TfsChangeSet
{
    private static Dictionary<string, Tuple<string, CommitCommand?>> _Branches = new();
    private static Dictionary<int, CommitCommand> _Commits = new();
    private static VersionControlServer _VersionControlServer = null!;
    private static string _TfsRootPath = "";

    private Changeset _ChangeSet;

    public static void Initialize(string tfsRootPath, VersionControlServer vcs)
    {
        _TfsRootPath = tfsRootPath;
        _VersionControlServer = vcs;
    }

    public TfsChangeSet(Changeset changeSet)
    {
        _ChangeSet = changeSet;
    }

    private static Dictionary<int, Func<CommitCommand?>> _SpecialCommands = new()
    {
        // use this to do checkin specific actions; one example is when a branch itself changes name
        { 12345, () =>
            {
                _Branches["$/Branch-A/"] = _Branches["$/Branch-B/"];
                _Branches.Remove("$/Branch-A/");
                return null;
            } },
    };

    public CommitCommand? ProcessChangeSet()
    {
        if (_SpecialCommands.ContainsKey(_ChangeSet.ChangesetId))
            return _SpecialCommands[_ChangeSet.ChangesetId]();
        return DoProcessChangeSet();
    }

    private List<FileCommand> fileCommands = new();
    private List<CommitCommand> merges = new();
    private string? branch = null;

    private CommitCommand? DoProcessChangeSet()
    {
        var committer = new CommitterCommand(_ChangeSet.Committer, GetEmailAddressForUser(_ChangeSet.Committer), _ChangeSet.CreationDate);
        var author = _ChangeSet.Committer != _ChangeSet.Owner
            ? new AuthorCommand(_ChangeSet.Owner, GetEmailAddressForUser(_ChangeSet.Owner), _ChangeSet.CreationDate)
            : null;

        // Issue #1: QueryHistory with includeChanges:true truncates Changes server-side
        // (often at ~2000 items). GetChangesForChangeset always returns all changes.
        var allChanges = _VersionControlServer
            .GetChangesForChangeset(_ChangeSet.ChangesetId, true, int.MaxValue, null)
            .Where(c => c.Item.ServerItem.StartsWith(_TfsRootPath))
            .ToList();

        // Issue #7: ChangeType.Add=1, ChangeType.Delete=16, so ascending sort puts adds
        // first — the opposite of the intended order. Sort deletes to the front explicitly.
        var orderedChanges = allChanges
            .Select((x, i) => new { x, i })
            .OrderBy(z => (z.x.ChangeType & ChangeType.Delete) != 0 ? 0 : 1)
            .ThenBy(z => z.i)
            .Select(z => z.x)
            .ToList();

        var deleteBranch = false;
        foreach (var change in orderedChanges)
        {
            var path = GetPath(change.Item.ServerItem);
            if (path == null)
                continue;

            // delete before checking folders; allows deleting an entire subdir with one command
            if ((change.ChangeType & ChangeType.Delete) == ChangeType.Delete)
            {
                if (path == "")
                {
                    // Bug A: FileDeleteCommand("") would emit "D " which is invalid in
                    // git fast-import. Deleting the branch root means all files are gone;
                    // use deleteall to wipe the tree correctly.
                    fileCommands.Add(new FileDeleteAllCommand());
                    deleteBranch = true;
                    break;
                }
                fileCommands.Add(new FileDeleteCommand(path));
                continue;
            }

            if (change.Item.ItemType == ItemType.Folder)
                continue;

            if ((change.ChangeType & ChangeType.Rename) == ChangeType.Rename)
            {
                var history = _VersionControlServer
                    .QueryHistory(
                        change.Item.ServerItem,
                        new ChangesetVersionSpec(_ChangeSet.ChangesetId),
                        change.Item.DeletionId,
                        RecursionType.None,
                        null,
                        null,
                        new ChangesetVersionSpec(_ChangeSet.ChangesetId),
                        int.MaxValue,
                        true,
                        false)
                    .OfType<Changeset>()
                    .ToList();

                // Issue #3: history may have only one entry for a Branch|Rename in a single
                // changeset. Guard before indexing.
                if (history.Count >= 2)
                {
                    var previousChangeset = history[1];
                    // Issue #6: Changes[0] is not guaranteed to be this specific item.
                    // Match by ItemId, which persists across renames.
                    var previousFile =
                        previousChangeset.Changes.FirstOrDefault(c => c.Item.ItemId == change.Item.ItemId)
                        ?? previousChangeset.Changes[0];
                    var previousPath = GetPath(previousFile.Item.ServerItem);

                    // Issue #11: if the source path was already wiped by a deleteall or by a
                    // parent-directory delete earlier in this same commit, git fast-import will
                    // reject the R command ("path … not in branch"). Detect that case and fall
                    // through to FileModifyCommand instead — content is preserved, lineage lost.
                    // Also skip when previousPath is null — the file's prior location is outside
                    // the current branch scope (e.g. cross-branch rename); just add at new path.
                    bool sourceGone =
                        previousPath == null ||
                        fileCommands.Any(fc => fc is FileDeleteAllCommand) ||
                        fileCommands.Any(fc => fc is FileDeleteCommand dc &&
                            (dc.Path == previousPath ||
                             previousPath.StartsWith(dc.Path + "/")));

                    if (!sourceGone)
                    {
                        fileCommands.Add(new FileRenameCommand(previousPath, path));

                        // remove delete commands, since rename will take care of it
                        fileCommands.RemoveAll(fc => fc is FileDeleteCommand && fc.Path == previousPath);
                    }
                }
                // If history.Count < 2 or source is gone, fall through to FileModifyCommand below:
                // file content is exported correctly even though rename lineage is lost.
            }

            var blob = GetDataBlob(change.Item);
            fileCommands.Add(new FileModifyCommand(path, blob));

            if ((change.ChangeType & ChangeType.Branch) == ChangeType.Branch)
            {
                var history = _VersionControlServer.GetBranchHistory(
                    new[] { new ItemSpec(change.Item.ServerItem, RecursionType.None) },
                    new ChangesetVersionSpec(_ChangeSet.ChangesetId));

                var itemHistory = history[0][0];
                var mergedItem = FindMergedItem(itemHistory, _ChangeSet.ChangesetId);

                // Issues #4 & #5: mergedItem or branchInfo can be null.
                if (mergedItem != null)
                {
                    var branchInfo = GetBranch(mergedItem.Relative.BranchFromItem.ServerItem);
                    var previousCommit = branchInfo?.Item2.Item2;
                    if (previousCommit != null && !merges.Contains(previousCommit))
                        merges.Add(previousCommit);
                }
            }

            if ((change.ChangeType & ChangeType.Merge) == ChangeType.Merge)
            {
                var mergeHistory = _VersionControlServer.QueryMergesExtended(
                    new ItemSpec(change.Item.ServerItem, RecursionType.None),
                    new ChangesetVersionSpec(_ChangeSet.ChangesetId),
                    null,
                    new ChangesetVersionSpec(_ChangeSet.ChangesetId)).ToList();

                foreach (var mh in mergeHistory)
                {
                    // Issue #9: use the specific source changeset commit rather than the
                    // branch HEAD. Fall back to branch HEAD when the source is outside scope.
                    if (!_Commits.TryGetValue(mh.SourceItem.Item.ChangesetId, out var sourceCommit))
                    {
                        var branchInfo = GetBranch(mh.SourceItem.Item.ServerItem);
                        sourceCommit = branchInfo?.Item2.Item2;
                    }
                    if (sourceCommit != null && !merges.Contains(sourceCommit))
                        merges.Add(sourceCommit);
                }
            }
        }

        // Issue #2: if every change was a folder (ItemType.Folder → continue), branch is
        // never set. Nothing to commit for folder-only changesets.
        if (branch == null)
            return null;

        var reference = _Branches[branch];
        var commit = new CommitCommand(
            markId: _ChangeSet.ChangesetId,
            reference: reference.Item1,
            committer: committer,
            author: author,
            commitInfo: new DataCommand(_ChangeSet.Comment),
            fromCommit: reference.Item2,
            mergeCommits: merges,
            fileCommands: fileCommands);
        _Commits[_ChangeSet.ChangesetId] = commit;

        // Bug C: removing a branch from _Branches after deletion causes the next changeset
        // that touches the same path to call CreateNewBranch with a deep sub-path, creating
        // a wrong branch and silently dropping all files in that changeset.
        // Keep the entry so subsequent changesets resolve the branch correctly.
        // The deletion commit's tree is already empty (from deleteall above), so new files
        // added in the next changeset will be layered onto an empty tree — which is correct.
        _ = deleteBranch; // handled by deleteall above; branch stays in _Branches
        _Branches[branch] = Tuple.Create(reference.Item1, (CommitCommand?)commit);

        return commit;
    }

    // 10,000,000 to get it out of the way of normal checkins
    private static int _MarkID = 10000001;

    private BlobCommand GetDataBlob(Item item)
    {
        var bytes = new byte[item.ContentLength];
        using var stream = item.DownloadFile();
        int totalRead = 0;
        while (totalRead < bytes.Length)
        {
            int read = stream.Read(bytes, totalRead, bytes.Length - totalRead);
            if (read == 0) throw new EndOfStreamException($"Unexpected end of stream reading item {item.ServerItem}");
            totalRead += read;
        }
        return BlobCommand.BuildBlob(bytes, _MarkID++);
    }

    private static BranchHistoryTreeItem? FindMergedItem(BranchHistoryTreeItem parent, int changeSetId)
    {
        foreach (BranchHistoryTreeItem item in parent.Children)
        {
            if (item.Relative.IsRequestedItem)
                return item;

            var x = FindMergedItem(item, changeSetId);
            if (x != null)
                return x;
        }
        return null;
    }

    private Tuple<string, Tuple<string, CommitCommand?>>? GetBranch(string serverPath)
    {
        // Bug B: return the longest matching prefix so that a nested branch like
        // "$/Root/Sub/" takes priority over "$/Root/" when both are registered.
        KeyValuePair<string, Tuple<string, CommitCommand?>>? best = null;
        foreach (var x in _Branches)
            if (serverPath.StartsWith(x.Key) && (best == null || x.Key.Length > best.Value.Key.Length))
                best = x;
        return best == null ? null : Tuple.Create(best.Value.Key, best.Value.Value);
    }

    private string? GetPath(string serverPath)
    {
        if (branch == null)
        {
            var branchInfo = GetBranch(serverPath);
            if (branchInfo == null)
            {
                CreateNewBranch(serverPath);
                return "";
            }
            else
                branch = branchInfo.Item1;
        }

        if (!serverPath.StartsWith(branch))
            // for now ignore secondary branches; other filemodify commands handle this
            return null;

        // Issue #10: use Substring instead of Replace to avoid replacing all occurrences.
        return serverPath.Substring(branch.Length);
    }

    private void CreateNewBranch(string serverPath)
    {
        // TFS normally sends the branch root folder before its files. If a file arrives
        // first (Issue #8), strip the filename to recover the branch root directory.
        // "$/Proj/Branch/file.cs" → lastSlash=17 > 1 → "$/Proj/Branch/"
        // "$/Branch"              → lastSlash=1  ≤ 1 → "$/Branch/"
        int lastSlash = serverPath.LastIndexOf('/');
        branch = lastSlash > 1
            ? serverPath.Substring(0, lastSlash + 1)
            : serverPath + "/";

        if (!_Branches.ContainsKey(branch))
        {
            _Branches[branch] = Tuple.Create($"refs/heads/{Path.GetFileName(branch.TrimEnd('/'))}", default(CommitCommand));
            fileCommands.Add(new FileDeleteAllCommand());
        }
    }

    #region Active Directory
    private static string ProcessADName(string adName)
    {
        if (string.IsNullOrEmpty(adName))
            return "";

        if (!adName.Contains('\\'))
            return adName;

        var split = adName.Split('\\');
        return split[1];
    }

    private static UserPrincipal GetUserPrincipal(string userName)
    {
        var domainContext = new PrincipalContext(ContextType.Domain);
        var user = UserPrincipal.FindByIdentity(domainContext, IdentityType.SamAccountName, ProcessADName(userName));
        if (user != null)
            return user;
        throw new InvalidOperationException($"Cannot find current user ({userName}) in any domains.");
    }

    private static string GetEmailAddressForUser(string userName)
    {
        try
        {
            return GetUserPrincipal(userName).EmailAddress ?? "no.user@example.com";
        }
        catch
        {
            return "no.user@example.com";
        }
    }
    #endregion
}

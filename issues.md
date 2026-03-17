# TFS Fast Export — Issue Tracker

Issues found during code review and fixed in `TfsChangeSet.cs` / `Program.cs`.

| # | Summary | Severity | Status |
|---|---------|----------|--------|
| 1 | `Changeset.Changes` truncated for large changesets | Critical data loss | ✅ Fixed |
| 2 | Crash: `branch` null for folder-only changesets | Crash | ✅ Fixed |
| 3 | Crash: `history[1]` IndexOutOfRange on rename | Crash | ✅ Fixed |
| 4 | Crash: `mergedItem!` null dereference in Branch block | Crash | ✅ Fixed |
| 5 | Crash: `branchInfo!` null dereference for out-of-scope source | Crash | ✅ Fixed |
| 6 | Wrong rename source: `Changes[0]` may not be the renamed item | Wrong output | ✅ Fixed |
| 7 | `ChangeType` sort puts Add (1) before Delete (16) | Wrong op order | ✅ Fixed |
| 8 | Wrong branch path when first item in new branch is a file | Silent data loss | ✅ Fixed |
| 9 | Merge parent points to branch HEAD, not merged changeset | Wrong history | ✅ Fixed |
| 10 | Dead code: `_WhereClause`; `Replace` → `Substring` | Minor | ✅ Fixed |

---

## #1 — `Changeset.Changes` truncated for large changesets
**Severity:** Critical data loss
**File:** `TfsChangeSet.cs`

`QueryHistory` with `includeChanges: true` populates `Changeset.Changes` from the server, but the
server truncates this collection at a configurable limit (often ~2 000 items per changeset). A
checkin touching 10 000 files silently loses all changes beyond the limit.

**Fix:** Call `VersionControlServer.GetChangesForChangeset(changesetId, true, int.MaxValue, null)`
per changeset inside `DoProcessChangeSet`. Store the `VersionControlServer` reference via
`TfsChangeSet.Initialize()`. Set `includeChanges: false` in the `Program.cs` `QueryHistory` call
(metadata is still populated; only the truncated `Changes` collection is omitted, which we no
longer use).

---

## #2 — Crash: `branch` null for folder-only changesets
**Severity:** Crash (`NullReferenceException`)
**File:** `TfsChangeSet.cs` line 149

When every change in a changeset is a folder (`ItemType.Folder` → `continue` at line 82),
the instance field `branch` is never assigned. `_Branches[branch!]` at line 149 then throws.

**Fix:** After the foreach loop, return `null` early if `branch` is still null (nothing to commit).

---

## #3 — Crash: `history[1]` IndexOutOfRange on rename
**Severity:** Crash (`IndexOutOfRangeException`)
**File:** `TfsChangeSet.cs` line 103

In the rename block, `history[1]` assumes the renamed file has at least two history entries.
A file created and renamed in a single combined `Branch | Rename` changeset may have only one.

**Fix:** Guard with `history.Count >= 2`; if fewer entries exist, skip the rename command and
fall through to a plain `FileModifyCommand` so file content is still exported.

---

## #4 — Crash: `mergedItem!` null dereference in Branch handling
**Severity:** Crash (`NullReferenceException`)
**File:** `TfsChangeSet.cs` line 124

`FindMergedItem` explicitly returns `null` when `IsRequestedItem` is never true in the branch
tree. The `!` null-forgiving operator suppresses the warning but does not prevent the runtime crash.

**Fix:** Null-check `mergedItem`; skip merge parent registration if not found.

---

## #5 — Crash: `branchInfo!` null dereference for out-of-scope branch source
**Severity:** Crash (`NullReferenceException`)
**File:** `TfsChangeSet.cs` line 125

`GetBranch` returns `null` when the branch source is outside `tfsRootPath`. The `!` crashes.

**Fix:** Combined with fix #4: null-check the result and skip if out-of-scope (folded into the
same `if (mergedItem != null)` guard).

---

## #6 — Wrong rename source: `Changes[0]` may not be the renamed item
**Severity:** Wrong path in output
**File:** `TfsChangeSet.cs` line 104

`previousChangeset.Changes[0]` assumes the renamed file is the first change in the prior changeset.
If that changeset touched other files first, the wrong source path is used, producing an incorrect
`R` command in the fast-export stream.

**Fix:** Filter `Changes` by `ItemId` to locate the specific item that was renamed:
`previousChangeset.Changes.FirstOrDefault(c => c.Item.ItemId == change.Item.ItemId)`.
Fall back to `[0]` only if the filter returns nothing.

---

## #7 — `ChangeType` sort puts Add (1) before Delete (16)
**Severity:** Wrong operation order in output
**File:** `TfsChangeSet.cs` line 58

The `ChangeType` enum has `Add = 1`, `Delete = 16`. `OrderBy(ChangeType)` ascending puts Adds
before Deletes — the opposite of the stated intent ("delete before checking folders"). Combined
flag values (e.g., `Delete | Branch = 80`) make the ordering even less predictable.

**Fix:** Sort with an explicit predicate that checks the `Delete` flag:
`.OrderBy(z => (z.x.ChangeType & ChangeType.Delete) != 0 ? 0 : 1)`.

---

## #8 — Wrong branch path when first item in new branch is a file
**Severity:** Silent data loss
**File:** `TfsChangeSet.cs` line 226

`CreateNewBranch` sets `branch = serverPath + "/"`. If the first change encountered in an
unknown-branch changeset is a file (e.g. `$/Branch/file.cs`), `branch` becomes
`"$/Branch/file.cs/"`. Every subsequent item in the branch fails `StartsWith` and is silently
dropped. The next changeset calling `CreateNewBranch` finds the wrong key already in `_Branches`
and skips the `deleteall`, producing a corrupt git tree.

**Fix:** Strip the last path component when the path looks like a file:
`lastSlash > 1 ? serverPath[..lastSlash+1] : serverPath + "/"`.

---

## #9 — Merge parent points to branch HEAD, not the merged-from changeset
**Severity:** Wrong git history (merge parents)
**File:** `TfsChangeSet.cs` lines 141–144

`branchInfo.Item2.Item2` is the most recent commit written to the source branch, not the specific
changeset that was merged. Cherry-pick-style merges produce the wrong parent.

**Fix:** For `ChangeType.Merge`, use `_Commits.TryGetValue(mh.SourceItem.ChangesetId)` to look
up the exact source commit. Fall back to the branch HEAD only when the source changeset is outside
`tfsRootPath` scope (not in `_Commits`). The `ChangeType.Branch` case still uses branch HEAD,
which is correct because branches are always created from the source tip in normal TFS usage.

---

## #10 — Dead code and minor clarity
**Severity:** Minor
**File:** `TfsChangeSet.cs` lines 46, 220

- `_WhereClause` field is always `(x) => true` and has no external setter — removed.
- `serverPath.Replace(branch, "")` replaced with `serverPath.Substring(branch.Length)` to avoid
  replacing all occurrences (safe in practice but semantically wrong).

// Where the space actually went: the large-file finder and the duplicate finder.
// Both work on a folder the user picks, and both delete to the Recycle Bin — the
// files here are the user's own documents, not caches, so an undo must exist.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Cleaner
{
    sealed class SpaceEntry
    {
        public string Path;
        public string Name;      // the file name, kept because the name sort reads it per comparison
        public long Size;
        public DateTime Modified;
        public bool Selected;
        public bool Keep;   // the copy kept in a duplicate group: never deletable
    }

    // Rows are held as groups rather than as one flat list so sorting can
    // reorder duplicate sets without tearing them apart: the copies in a set
    // only mean anything next to each other, and the kept one has to stay at the
    // top of its own group. A big-file scan is the same shape with one file per
    // group and no header row.
    sealed class SpaceGroup
    {
        public int Copies;       // 0 = a plain file row, no header above it
        public long CopySize;
        public readonly List<SpaceEntry> Items = new List<SpaceEntry>();

        // Formatted at render time, not at scan time: a language switch has to
        // retext these along with everything else on the page.
        public string Header
        {
            get
            {
                return Copies > 0
                    ? string.Format(Lang.T("space.dupGroup"), Copies, Util.FormatSize(CopySize))
                    : null;
            }
        }
    }

    public partial class MainForm : Form
    {
        CancelFlag spaceCancel;
        bool spaceScanRunning;
        string spaceFolder;
        readonly List<SpaceGroup> spaceGroups = new List<SpaceGroup>();
        readonly List<SpaceEntry> spaceEntries = new List<SpaceEntry>();

        // Column indexes of the space list, which are also the sort keys.
        const int SpaceColName = 0, SpaceColSize = 1, SpaceColDate = 2, SpaceColPath = 3;
        int spaceSort = SpaceColSize;
        bool spaceSortDesc = true;

        // Anything smaller is noise in a "what is eating my disk" list.
        const long MinBigFile = 50L * 1024 * 1024;
        // Below this, hashing costs more than the space a duplicate would free.
        const long MinDupeFile = 1L * 1024 * 1024;
        const int MaxRows = 500;

        string SpaceFolder
        {
            get
            {
                if (string.IsNullOrEmpty(spaceFolder) || !Directory.Exists(spaceFolder))
                    spaceFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return spaceFolder;
            }
        }

        void PickSpaceFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = SpaceFolder;
                dialog.Description = Lang.T("btn.pickFolder");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                spaceFolder = dialog.SelectedPath;
                UpdateSpaceFolderLabel();
            }
        }

        // The chosen folder shows as the first cell of the page strip; the label
        // keeps the full path for the status bar, where there is room for it.
        void UpdateSpaceFolderLabel()
        {
            if (spaceFolderLabel != null)
                spaceFolderLabel.Text = string.Format(Lang.T("space.folder"), SpaceFolder);
            UpdateSpaceSummary();
            SetStatus(string.Format(Lang.T("space.folder"), SpaceFolder));
        }

        // ---------- scanning ----------

        void StartSpaceScan(bool duplicates)
        {
            if (spaceScanRunning) return;
            spaceScanRunning = true;
            spaceCancel = new CancelFlag();
            CancelFlag cancel = spaceCancel;
            string root = SpaceFolder;
            spaceGroups.Clear();
            spaceEntries.Clear();
            spaceList.Items.Clear();
            UpdateSpaceFolderLabel();
            BeginBusy(string.Format(Lang.T("space.scanning"), Util.ShortenPath(root, 50)));

            ThreadPool.QueueUserWorkItem(delegate
            {
                var rows = new List<SpaceGroup>();
                string summary = "";
                try
                {
                    List<FileRecord> files = WalkFiles(root, duplicates ? MinDupeFile : MinBigFile, cancel);
                    if (duplicates) summary = BuildDuplicateRows(files, rows, cancel);
                    else summary = BuildBigFileRows(files, rows);
                }
                catch { }

                string summaryCopy = summary;
                OnUi(delegate
                {
                    if (spaceCancel != cancel) { EndBusy(null); return; } // superseded
                    spaceScanRunning = false;
                    if (!cancel.Cancelled) FillSpaceList(rows);
                    EndBusy(cancel.Cancelled ? Lang.T("common.cancelled") : summaryCopy);
                });
            });
        }

        sealed class FileRecord
        {
            public string Path;
            public long Size;
            public DateTime Modified;
        }

        // One iterative walk shared by both tools. Reparse points are skipped so a
        // OneDrive placeholder folder or a junction cannot send the walk in circles
        // or count the same tree twice.
        List<FileRecord> WalkFiles(string root, long minSize, CancelFlag cancel)
        {
            var result = new List<FileRecord>();
            var stack = new Stack<string>();
            stack.Push(root);
            int scanned = 0;
            while (stack.Count > 0)
            {
                if (cancel.Cancelled) break;
                string dir = stack.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(dir))
                    {
                        if (cancel.Cancelled) break;
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.Length < minSize) continue;
                            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            var record = new FileRecord();
                            record.Path = file;
                            record.Size = info.Length;
                            // Read here, where the FileInfo is already warm: the
                            // date column would otherwise cost one stat call per
                            // visible row every time the list is re-sorted.
                            record.Modified = info.LastWriteTime;
                            result.Add(record);
                        }
                        catch { }
                    }
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        if (Util.IsReparsePoint(sub)) continue;
                        stack.Push(sub);
                    }
                }
                catch { } // an unreadable folder is skipped, not fatal

                if (++scanned % 200 == 0)
                {
                    string current = dir;
                    OnUi(delegate
                    {
                        if (spaceCancel == cancel && !cancel.Cancelled && spaceScanRunning)
                            SetStatus(string.Format(Lang.T("space.scanning"), Util.ShortenPath(current, 60)));
                    });
                }
            }
            return result;
        }

        // Sorted biggest-first before the cap is applied, whatever the user has
        // the list sorted by: MaxRows is "the 500 files eating the most space",
        // not "the first 500 the walk happened to find". The chosen sort is
        // applied afterwards, to those 500.
        string BuildBigFileRows(List<FileRecord> files, List<SpaceGroup> rows)
        {
            files.Sort(delegate(FileRecord a, FileRecord b) { return b.Size.CompareTo(a.Size); });
            long total = 0;
            foreach (FileRecord file in files) total += file.Size;
            int count = 0;
            foreach (FileRecord file in files)
            {
                if (count >= MaxRows) break;
                var group = new SpaceGroup();
                group.Items.Add(NewEntry(file));
                rows.Add(group);
                count++;
            }
            return string.Format(Lang.T("space.bigFound"), files.Count,
                Util.FormatSize(MinBigFile), Util.FormatSize(total));
        }

        static SpaceEntry NewEntry(FileRecord file)
        {
            var entry = new SpaceEntry();
            entry.Path = file.Path;
            entry.Name = Path.GetFileName(file.Path);
            entry.Size = file.Size;
            entry.Modified = file.Modified;
            return entry;
        }

        // Three passes, cheapest first: group by size, then by a 64 KB prefix hash,
        // then by the full hash. Two multi-gigabyte files that merely share a size
        // are separated by the prefix pass without ever being read in full.
        string BuildDuplicateRows(List<FileRecord> files, List<SpaceGroup> rows, CancelFlag cancel)
        {
            var bySize = new Dictionary<long, List<FileRecord>>();
            foreach (FileRecord file in files)
            {
                List<FileRecord> bucket;
                if (!bySize.TryGetValue(file.Size, out bucket))
                {
                    bucket = new List<FileRecord>();
                    bySize[file.Size] = bucket;
                }
                bucket.Add(file);
            }

            var candidates = new List<List<FileRecord>>();
            foreach (KeyValuePair<long, List<FileRecord>> pair in bySize)
                if (pair.Value.Count > 1) candidates.Add(pair.Value);

            int candidateCount = 0;
            foreach (List<FileRecord> bucket in candidates) candidateCount += bucket.Count;
            int announced = candidateCount;
            OnUi(delegate
            {
                if (spaceCancel == cancel && !cancel.Cancelled && spaceScanRunning)
                    SetStatus(string.Format(Lang.T("space.hashing"), announced));
            });

            // Hashing is the long half of this scan, and until it says something
            // the window looks hung — which is also why Stop felt broken. The
            // count is pushed to the status bar in batches rather than per file:
            // one BeginInvoke per hashed file would cost more than the hashing.
            var groups = new List<List<FileRecord>>();
            int hashed = 0, reported = 0;
            foreach (List<FileRecord> bucket in candidates)
            {
                if (cancel.Cancelled) return Lang.T("common.cancelled");
                foreach (List<FileRecord> prefixGroup in GroupByHash(bucket, 65536, cancel))
                {
                    // A cancelled GroupByHash returns whatever it managed to
                    // bucket, so the groups built from here on are half-measured
                    // — stop before they reach the list rather than after.
                    if (cancel.Cancelled) return Lang.T("common.cancelled");
                    if (prefixGroup.Count < 2) continue;
                    foreach (List<FileRecord> fullGroup in GroupByHash(prefixGroup, 0, cancel))
                        if (fullGroup.Count > 1) groups.Add(fullGroup);
                }
                hashed += bucket.Count;
                if (hashed - reported >= 25 || hashed >= announced)
                {
                    reported = hashed;
                    int done = hashed;
                    OnUi(delegate
                    {
                        if (spaceCancel == cancel && !cancel.Cancelled && spaceScanRunning)
                            SetStatus(string.Format(Lang.T("space.hashingProgress"), done, announced));
                    });
                }
            }
            if (cancel.Cancelled) return Lang.T("common.cancelled");

            groups.Sort(delegate(List<FileRecord> a, List<FileRecord> b)
            {
                long wasteA = a[0].Size * (a.Count - 1), wasteB = b[0].Size * (b.Count - 1);
                return wasteB.CompareTo(wasteA);
            });

            long reclaimable = 0;
            foreach (List<FileRecord> group in groups) reclaimable += group[0].Size * (group.Count - 1);

            // MaxRows counts painted rows, headers included — the same budget the
            // flat list had before groups existed.
            int emitted = 0;
            foreach (List<FileRecord> group in groups)
            {
                if (emitted >= MaxRows) break;
                emitted += group.Count + 1;
                var row = new SpaceGroup();
                row.Copies = group.Count;
                row.CopySize = group[0].Size;
                for (int i = 0; i < group.Count; i++)
                {
                    SpaceEntry entry = NewEntry(group[i]);
                    entry.Keep = i == 0;          // one copy always survives
                    entry.Selected = i > 0;
                    row.Items.Add(entry);
                }
                rows.Add(row);
            }
            return string.Format(Lang.T("space.dupFound"), groups.Count, Util.FormatSize(reclaimable));
        }

        static List<List<FileRecord>> GroupByHash(List<FileRecord> files, long maxBytes, CancelFlag cancel)
        {
            var byHash = new Dictionary<string, List<FileRecord>>(StringComparer.Ordinal);
            foreach (FileRecord file in files)
            {
                if (cancel.Cancelled) break;
                // The flag travels into the hash: a single 8 GB file would
                // otherwise hold the walk here long after Stop was pressed.
                string hash = Util.HashFile(file.Path, maxBytes, cancel);
                if (hash == null) continue; // unreadable or cancelled: never call it a duplicate
                List<FileRecord> bucket;
                if (!byHash.TryGetValue(hash, out bucket))
                {
                    bucket = new List<FileRecord>();
                    byHash[hash] = bucket;
                }
                bucket.Add(file);
            }
            return new List<List<FileRecord>>(byHash.Values);
        }

        // ---------- list ----------

        void FillSpaceList(List<SpaceGroup> rows)
        {
            spaceGroups.Clear();
            spaceGroups.AddRange(rows);
            RenderSpaceList();
        }

        // Sorts the groups by the current key and repaints the list from them.
        // Everything the user has ticked survives, because the rows are rebuilt
        // from the same SpaceEntry objects the selection lives on.
        void RenderSpaceList()
        {
            if (spaceList == null || spaceList.IsDisposed) return;
            SortSpaceGroups();
            spaceEntries.Clear();
            spaceList.BeginUpdate();
            spaceList.Items.Clear();
            foreach (SpaceGroup group in spaceGroups)
            {
                string header = group.Header;
                if (header != null)
                {
                    var headerItem = new ListViewItem(header);
                    headerItem.SubItems.Add("");
                    headerItem.SubItems.Add("");
                    headerItem.SubItems.Add("");
                    headerItem.Tag = null;
                    spaceList.Items.Add(headerItem);
                }
                foreach (SpaceEntry entry in group.Items)
                {
                    spaceEntries.Add(entry);
                    var item = new ListViewItem(entry.Name);
                    item.SubItems.Add(Util.FormatSize(entry.Size));
                    item.SubItems.Add(Util.FormatDate(entry.Modified));
                    item.SubItems.Add(entry.Path);
                    item.Tag = entry;
                    if (entry.Keep) item.ForeColor = Theme.Good;
                    spaceList.Items.Add(item);
                }
            }
            spaceList.EndUpdate();
            spaceList.Refit();
            spaceList.SetSort(spaceSort, spaceSortDesc);
            UpdateSpaceSummary();
        }

        // Clicking a column header. The first click on a column picks the
        // direction that column is actually useful in — biggest and newest
        // first, names and paths A to Z — and each further click flips it.
        void SortSpaceBy(int column)
        {
            if (column < SpaceColName || column > SpaceColPath) return;
            if (column == spaceSort) spaceSortDesc = !spaceSortDesc;
            else
            {
                spaceSort = column;
                spaceSortDesc = column == SpaceColSize || column == SpaceColDate;
            }
            RenderSpaceList();
        }

        // The sort is by group, never across groups: inside a duplicate set the
        // copies keep the order the scan gave them, so the green kept copy stays
        // at the top of its own set wherever that set lands.
        void SortSpaceGroups()
        {
            int column = spaceSort;
            bool descending = spaceSortDesc;
            spaceGroups.Sort(delegate(SpaceGroup a, SpaceGroup b)
            {
                int cmp;
                if (column == SpaceColSize) cmp = GroupSize(a).CompareTo(GroupSize(b));
                else if (column == SpaceColDate) cmp = GroupDate(a).CompareTo(GroupDate(b));
                else if (column == SpaceColPath)
                    cmp = string.Compare(GroupPath(a), GroupPath(b), StringComparison.OrdinalIgnoreCase);
                else cmp = string.Compare(GroupName(a), GroupName(b), StringComparison.OrdinalIgnoreCase);
                if (descending) cmp = -cmp;
                // List.Sort is unstable, so equal keys need a tie-break of their
                // own — without one, re-sorting on the same column shuffles rows
                // that compare equal for no reason the user can see.
                if (cmp == 0) cmp = string.Compare(GroupPath(a), GroupPath(b), StringComparison.OrdinalIgnoreCase);
                return cmp;
            });
        }

        // A group's key is the strongest one it contains: a duplicate set is as
        // big as one copy and as new as its newest copy.
        static long GroupSize(SpaceGroup group)
        {
            long size = 0;
            foreach (SpaceEntry entry in group.Items) if (entry.Size > size) size = entry.Size;
            return size;
        }

        static DateTime GroupDate(SpaceGroup group)
        {
            DateTime when = DateTime.MinValue;
            foreach (SpaceEntry entry in group.Items) if (entry.Modified > when) when = entry.Modified;
            return when;
        }

        static string GroupName(SpaceGroup group)
        {
            return group.Items.Count > 0 ? group.Items[0].Name : "";
        }

        static string GroupPath(SpaceGroup group)
        {
            return group.Items.Count > 0 ? group.Items[0].Path : "";
        }

        void UpdateSpaceSummary()
        {
            if (spaceSummary == null) return;
            int count = 0;
            long bytes = 0;
            foreach (SpaceEntry entry in spaceEntries)
                if (entry.Selected && !entry.Keep) { count++; bytes += entry.Size; }

            FillStrip(spaceStrip,
                new string[]
                {
                    Lang.T("stat.folder"), Lang.T("stat.found"),
                    Lang.T("stat.selected"), Lang.T("stat.freeable")
                },
                new string[]
                {
                    Util.ShortenPath(SpaceFolder, 26),
                    spaceEntries.Count.ToString(),
                    count.ToString(),
                    bytes > 0 ? Util.FormatSize(bytes) : "—"
                },
                new Color[] { Color.Empty, Color.Empty, Color.Empty, bytes > 0 ? Theme.Warn : Color.Empty });

            spaceSummary.Text = spaceEntries.Count == 0
                ? (spaceScanRunning ? "" : Lang.T("space.none"))
                : count + " " + Lang.T("common.selected") + " · " + Util.FormatSize(bytes)
                  + " · " + Lang.T("space.keepOne");
            if (btnSpaceDelete != null) btnSpaceDelete.Enabled = count > 0;
        }

        // ---------- actions ----------

        void RecycleSelectedSpace()
        {
            var paths = new List<string>();
            long bytes = 0;
            foreach (SpaceEntry entry in spaceEntries)
                if (entry.Selected && !entry.Keep) { paths.Add(entry.Path); bytes += entry.Size; }
            if (paths.Count == 0) return;

            if (MessageBox.Show(this, string.Format(Lang.T("space.confirm"), paths.Count, Util.FormatSize(bytes)),
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            Util.RecycleFiles(paths);
            // The shell reports success even when it skipped a locked file, so the
            // count reported back is what actually left the disk.
            int gone = 0;
            foreach (string path in paths)
            {
                try { if (!File.Exists(path)) gone++; }
                catch { }
            }
            string message = string.Format(Lang.T("space.recycled"), gone);
            LogLine(message);
            SetStatus(message);

            // Pruned out of the groups rather than out of the ListView: the rows
            // are rebuilt from the groups on every sort, so a file dropped only
            // from the list would come straight back on the next header click.
            for (int i = spaceGroups.Count - 1; i >= 0; i--)
            {
                List<SpaceEntry> items = spaceGroups[i].Items;
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    try { if (!File.Exists(items[j].Path)) items.RemoveAt(j); }
                    catch { }
                }
                // A duplicate set down to its last copy is no longer a duplicate.
                if (items.Count == 0 || (spaceGroups[i].Copies > 0 && items.Count < 2))
                    spaceGroups.RemoveAt(i);
            }
            RenderSpaceList();
            RefreshDrives();
        }

        void OpenSelectedSpace()
        {
            if (spaceList.SelectedItems.Count == 0) return;
            var entry = spaceList.SelectedItems[0].Tag as SpaceEntry;
            if (entry != null) OpenInExplorer(entry.Path);
        }
    }
}

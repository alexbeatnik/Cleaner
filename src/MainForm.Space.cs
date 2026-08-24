// Where the space actually went: the large-file finder and the duplicate finder.
// Both work on a folder the user picks, and both delete to the Recycle Bin — the
// files here are the user's own documents, not caches, so an undo must exist.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WindowsStalker
{
    sealed class SpaceEntry
    {
        public string Path;
        public long Size;
        public bool Selected;
        public bool Keep;   // the copy kept in a duplicate group: never deletable
    }

    public partial class MainForm : Form
    {
        CancelFlag spaceCancel;
        bool spaceScanRunning;
        string spaceFolder;
        readonly List<SpaceEntry> spaceEntries = new List<SpaceEntry>();

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
            spaceEntries.Clear();
            spaceList.Items.Clear();
            UpdateSpaceFolderLabel();
            BeginBusy(string.Format(Lang.T("space.scanning"), Util.ShortenPath(root, 50)));

            ThreadPool.QueueUserWorkItem(delegate
            {
                var rows = new List<object>(); // string = group header, SpaceEntry = file
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
                    if (spaceCancel != cancel) return;
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
                    OnUi(delegate { SetStatus(string.Format(Lang.T("space.scanning"), Util.ShortenPath(current, 60))); });
                }
            }
            return result;
        }

        string BuildBigFileRows(List<FileRecord> files, List<object> rows)
        {
            files.Sort(delegate(FileRecord a, FileRecord b) { return b.Size.CompareTo(a.Size); });
            long total = 0;
            foreach (FileRecord file in files) total += file.Size;
            int count = 0;
            foreach (FileRecord file in files)
            {
                if (count >= MaxRows) break;
                var entry = new SpaceEntry();
                entry.Path = file.Path;
                entry.Size = file.Size;
                rows.Add(entry);
                count++;
            }
            return string.Format(Lang.T("space.bigFound"), files.Count,
                Util.FormatSize(MinBigFile), Util.FormatSize(total));
        }

        // Three passes, cheapest first: group by size, then by a 64 KB prefix hash,
        // then by the full hash. Two multi-gigabyte files that merely share a size
        // are separated by the prefix pass without ever being read in full.
        string BuildDuplicateRows(List<FileRecord> files, List<object> rows, CancelFlag cancel)
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
            OnUi(delegate { SetStatus(string.Format(Lang.T("space.hashing"), announced)); });

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

            foreach (List<FileRecord> group in groups)
            {
                if (rows.Count >= MaxRows) break;
                rows.Add(string.Format(Lang.T("space.dupGroup"), group.Count, Util.FormatSize(group[0].Size)));
                for (int i = 0; i < group.Count; i++)
                {
                    var entry = new SpaceEntry();
                    entry.Path = group[i].Path;
                    entry.Size = group[i].Size;
                    entry.Keep = i == 0;          // one copy always survives
                    entry.Selected = i > 0;
                    rows.Add(entry);
                }
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

        void FillSpaceList(List<object> rows)
        {
            spaceEntries.Clear();
            spaceList.BeginUpdate();
            spaceList.Items.Clear();
            foreach (object row in rows)
            {
                var header = row as string;
                if (header != null)
                {
                    var headerItem = new ListViewItem(header);
                    headerItem.SubItems.Add("");
                    headerItem.SubItems.Add("");
                    headerItem.Tag = null;
                    spaceList.Items.Add(headerItem);
                    continue;
                }
                var entry = (SpaceEntry)row;
                spaceEntries.Add(entry);
                var item = new ListViewItem(Path.GetFileName(entry.Path));
                item.SubItems.Add(Util.FormatSize(entry.Size));
                item.SubItems.Add(entry.Path);
                item.Tag = entry;
                if (entry.Keep) item.ForeColor = Theme.Good;
                spaceList.Items.Add(item);
            }
            spaceList.EndUpdate();
            spaceList.Refit();
            UpdateSpaceSummary();
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

            for (int i = spaceEntries.Count - 1; i >= 0; i--)
            {
                try { if (!File.Exists(spaceEntries[i].Path)) spaceEntries.RemoveAt(i); }
                catch { }
            }
            for (int i = spaceList.Items.Count - 1; i >= 0; i--)
            {
                var entry = spaceList.Items[i].Tag as SpaceEntry;
                if (entry == null) continue;
                try { if (!File.Exists(entry.Path)) spaceList.Items.RemoveAt(i); }
                catch { }
            }
            UpdateSpaceSummary();
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

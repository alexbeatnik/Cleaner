// Per-analysis state. Replaced wholesale when a new analysis starts (see
// MainForm.Cleaner.cs) so nothing can leak from one run into the next: a
// superseded background walker keeps writing into its own dead object, and the
// UI only ever reads the session it is currently showing.
using System;
using System.Collections.Generic;

namespace WindowsStalker
{
    // What one rule turned up. Entries are the exact things the clean step will
    // delete — the immediate children of a folder rule, or the matched files of a
    // glob rule — never the rule's root itself.
    sealed class RuleResult
    {
        public CleanRule Rule;
        public long Bytes;
        public int Files;
        public readonly List<string> Entries = new List<string>();
        public bool Selected = true;   // ticked in the results list
        public bool Blocked;           // an owning process is running
        public string BlockedBy;       // which one, for the UI

        public bool Empty { get { return Entries.Count == 0 && Bytes == 0; } }
    }

    sealed class CleanScan
    {
        public readonly CancelFlag Cancel = new CancelFlag();
        public readonly List<RuleResult> Results = new List<RuleResult>();
        public DateTime Started = DateTime.Now;
        public bool Finished;
        public bool Cancelled;
        public int RulesTotal;   // for the progress arc
        public int RulesDone;

        public long TotalBytes
        {
            get
            {
                long sum = 0;
                foreach (RuleResult r in Results) sum += r.Bytes;
                return sum;
            }
        }

        public int TotalFiles
        {
            get
            {
                int sum = 0;
                foreach (RuleResult r in Results) sum += r.Files;
                return sum;
            }
        }

        // What the Clean button would actually remove right now: ticked, not
        // blocked by a running process, and with something to delete.
        public long SelectedBytes
        {
            get
            {
                long sum = 0;
                foreach (RuleResult r in Results)
                    if (r.Selected && !r.Blocked) sum += r.Bytes;
                return sum;
            }
        }

        public int SelectedFiles
        {
            get
            {
                int sum = 0;
                foreach (RuleResult r in Results)
                    if (r.Selected && !r.Blocked) sum += r.Files;
                return sum;
            }
        }
    }
}

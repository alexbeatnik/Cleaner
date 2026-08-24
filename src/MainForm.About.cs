// The About dialog: the mark, the version, what the app is, how to get started,
// and the project links — star the repo, browse the releases, follow the author,
// read the licence. Built in code like every other surface here, against the
// same Theme, so it looks like part of the app rather than a system dialog.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WindowsStalker
{
    public partial class MainForm : Form
    {
        void ShowAboutDialog()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Lang.T("about.title");
                dlg.ClientSize = new Size(660, 672);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.BackColor = Theme.Bg;
                dlg.ForeColor = Theme.Text;
                dlg.Font = Font;
                dlg.Icon = Brand.AppIcon;
                Theme.DarkTitleBar(dlg);

                // The mark is drawn, not loaded: Branding.cs is the only place the
                // logo exists, and a PictureBox would need a bitmap of it.
                var mark = new Panel();
                mark.SetBounds(28, 24, 84, 84);
                mark.BackColor = Color.Transparent;
                mark.Paint += delegate(object s, PaintEventArgs e)
                {
                    Brand.PaintMark(e.Graphics, new RectangleF(0, 0, 84, 84));
                };
                dlg.Controls.Add(mark);

                var name = new Label();
                name.Text = Regex.Replace(AppName, "(?<=[a-z])(?=[A-Z])", " ").ToUpperInvariant();
                name.Font = Theme.UiBold(16f);
                name.ForeColor = Theme.Text;
                name.AutoSize = true;
                name.BackColor = Color.Transparent;
                name.Location = new Point(128, 34);
                dlg.Controls.Add(name);

                var tagline = new Label();
                tagline.Text = Lang.T("app.tagline");
                tagline.Font = Theme.Ui(9.5f);
                tagline.ForeColor = Theme.Accent;
                tagline.AutoSize = true;
                tagline.BackColor = Color.Transparent;
                tagline.Location = new Point(131, 66);
                dlg.Controls.Add(tagline);

                var ver = new Label();
                ver.Text = string.Format(Lang.T("about.version"), AppVersion);
                ver.Font = Theme.Ui(8.75f);
                ver.ForeColor = Theme.Muted;
                ver.AutoSize = true;
                ver.BackColor = Color.Transparent;
                ver.Location = new Point(131, 88);
                dlg.Controls.Add(ver);

                var desc = new Label();
                desc.Text = Lang.T("about.desc");
                desc.Font = Theme.Ui(9f);
                desc.ForeColor = Theme.Text;
                desc.BackColor = Color.Transparent;
                desc.SetBounds(28, 128, dlg.ClientSize.Width - 56, 72);
                dlg.Controls.Add(desc);

                var howHeader = new Label();
                howHeader.Text = Lang.T("about.quickStart").ToUpperInvariant();
                howHeader.Font = Theme.UiBold(8f);
                howHeader.ForeColor = Theme.Muted;
                howHeader.AutoSize = true;
                howHeader.BackColor = Color.Transparent;
                howHeader.Location = new Point(28, 208);
                dlg.Controls.Add(howHeader);

                var how = new Label();
                how.Text = Lang.T("about.howTo");
                how.Font = Theme.Ui(8.75f);
                how.ForeColor = Theme.Text;
                how.BackColor = Color.Transparent;
                how.SetBounds(28, 230, dlg.ClientSize.Width - 56, 200);
                dlg.Controls.Add(how);

                // 440, not "right under the steps": about.howTo is seven lines
                // that wrap to twelve in Ukrainian, and the block is sized for
                // the longer of the two rather than the one being read.
                // Accent-coloured links rather than buttons: four of these as
                // buttons would be a wall, and they all do the same small thing.
                int y = 440;
                AddAboutLink(dlg, Lang.T("about.star"), ProjectUrl, y);
                AddAboutLink(dlg, Lang.T("about.releases"), ReleasesUrl, y + 26);
                AddAboutLink(dlg, Lang.T("about.follow"), AuthorUrl, y + 52);
                AddAboutLink(dlg, Lang.T("about.license"), LicenseUrl, y + 78);

                var author = new Label();
                author.Text = Lang.T("about.author");
                author.Font = Theme.Ui(8.25f);
                author.ForeColor = Theme.Muted;
                author.BackColor = Color.Transparent;
                author.SetBounds(28, 548, dlg.ClientSize.Width - 56, 52);
                dlg.Controls.Add(author);

                var buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Height = 54;
                buttons.Padding = new Padding(12);
                buttons.BackColor = Theme.Bg;

                var close = new ModernButton(Lang.T("btn.close"), Theme.Subtle, Theme.CardLine, Theme.Text);
                close.Icon = Ico.Close;
                close.Size = new Size(140, 34);
                close.BackColor = Theme.Bg;
                close.Click += delegate { dlg.Close(); };
                buttons.Controls.Add(close);

                var updates = new ModernButton(Lang.T("btn.checkUpdate"), Theme.Accent, Theme.AccentHot, Theme.OnAccent);
                updates.Icon = Ico.Download;
                updates.Size = new Size(220, 34);
                updates.BackColor = Theme.Bg;
                updates.Click += delegate { dlg.Close(); ShowPage(6); CheckForUpdatesNow(); };
                buttons.Controls.Add(updates);

                dlg.Controls.Add(buttons);
                // ModernButton is an IButtonControl, so CancelButton works here
                // and routes Escape through the same handler the button uses.
                dlg.CancelButton = close;
                dlg.ShowDialog(this);
            }
        }

        static void AddAboutLink(Form dlg, string text, string url, int y)
        {
            var link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.Font = Theme.Ui(9f);
            link.Location = new Point(28, y);
            link.BackColor = Color.Transparent;
            link.LinkColor = Theme.Accent;
            link.ActiveLinkColor = Theme.AccentHot;
            link.VisitedLinkColor = Theme.Accent;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.LinkClicked += delegate { OpenUrl(url); };
            dlg.Controls.Add(link);
        }
    }
}

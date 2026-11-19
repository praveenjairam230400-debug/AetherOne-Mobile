using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AetherOnePC;

public sealed partial class BrowserForm
{
    private Form? aetherTaskManager;
    private void ShowAetherTaskManager()
    {
        if (closing || IsDisposed) return;
        if (aetherTaskManager is { IsDisposed: false })
        {
            aetherTaskManager.Activate();
            return;
        }

        var window = new Form
        {
            Text = "Aether task manager",
            Width = 880,
            Height = 570,
            MinimumSize = new Size(650, 420),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(32, 32, 32),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };
        aetherTaskManager = window;
        var table = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.FromArgb(32, 32, 32),
            GridColor = Color.FromArgb(60, 60, 60),
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false
        };
        table.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(48, 48, 48);
        table.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        table.DefaultCellStyle.BackColor = Color.FromArgb(38, 38, 38);
        table.DefaultCellStyle.ForeColor = Color.White;
        table.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 94, 143);
        table.Columns.Add("name", "Process / tab");
        table.Columns.Add("pid", "PID");
        table.Columns.Add("memory", "Private memory (MiB)");
        table.Columns.Add("state", "State");
        table.Columns[0].FillWeight = 180;
        table.Columns[1].FillWeight = 45;
        table.Columns[2].FillWeight = 75;
        table.Columns[3].FillWeight = 110;

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            Padding = new Padding(12, 10, 12, 8),
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.FromArgb(43, 43, 43)
        };
        var info = new Label
        {
            Text = "Renderer processes are shared by WebView2. A process is not necessarily one tab.",
            AutoSize = true,
            MaximumSize = new Size(530, 0),
            ForeColor = Color.Gainsboro,
            Margin = new Padding(8, 8, 12, 0)
        };
        var refresh = new Button { Text = "Refresh", Width = 92, Height = 32, FlatStyle = FlatStyle.Flat };
        var close = new Button { Text = "Close", Width = 92, Height = 32, FlatStyle = FlatStyle.Flat };
        var export = new Button { Text = "Export timings", Width = 128, Height = 32, FlatStyle = FlatStyle.Flat };
        footer.Controls.Add(refresh);
        footer.Controls.Add(export);
        footer.Controls.Add(close);
        footer.Controls.Add(info);
        window.Controls.Add(table);
        window.Controls.Add(footer);

        void UpdateRows()
        {
            if (window.IsDisposed || closing) return;
            var rows = new List<object[]>();
            var ids = new HashSet<int> { Environment.ProcessId };
            try
            {
                if (environment?.IsCompletedSuccessfully == true)
                {
                    foreach (var child in environment.Result.GetProcessInfos())
                    {
                        ids.Add(child.ProcessId);
                        rows.Add(new object[] { "WebView2 " + child.Kind, child.ProcessId, PrivateMiB(child.ProcessId), "Engine" });
                    }
                }
            }
            catch { /* Runtime can recycle processes while sampling. */ }
            rows.Insert(0, new object[] { "Aether browser shell", Environment.ProcessId, PrivateMiB(Environment.ProcessId), "Application" });
            foreach (var tab in pages.ToArray())
            {
                var stateText = tab == Current ? "Active" :
                    tab.Web == null ? "Unloaded" :
                    tab.Suspended ? "Suspended" : tab.Loading ? "Loading" : "Background";
                rows.Add(new object[] { tab.Title, "—", "Shared", stateText });
            }
            table.Rows.Clear();
            foreach (var row in rows) table.Rows.Add(row);
            window.Text = $"Aether task manager — {ids.Count} known processes, {pages.Count} tabs";
        }

        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => UpdateRows();
        refresh.Click += (_, _) => UpdateRows();
        export.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Export measured Aether navigation timings",
                Filter = "JSON files (*.json)|*.json",
                FileName = "aether-navigation-timings.json"
            };
            if (dialog.ShowDialog(window) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, navigationDiagnostics.ExportJson()); }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        close.Click += (_, _) => window.Close();
        window.FormClosed += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (ReferenceEquals(aetherTaskManager, window)) aetherTaskManager = null;
        };
        UpdateRows();
        timer.Start();
        window.Show(this);
    }

    private static string PrivateMiB(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return (process.PrivateMemorySize64 / 1048576.0).ToString("F1");
        }
        catch { return "Exited"; }
    }
}

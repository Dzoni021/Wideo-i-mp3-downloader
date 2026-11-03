using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmniFetch;

public sealed class MainForm : Form
{
    private readonly TextBox urlBox = new();
    private readonly RadioButton videoRadio = new();
    private readonly RadioButton audioRadio = new();
    private readonly ComboBox qualityBox = new();
    private readonly TextBox folderBox = new();
    private readonly Button browseButton = new();
    private readonly Button downloadButton = new();
    private readonly Button cancelButton = new();
    private readonly ProgressBar progressBar = new();
    private readonly Label progressText = new();
    private readonly ToolStripStatusLabel statusText = new();
    private readonly StatusStrip statusStrip = new();
    private CancellationTokenSource? cancellation;
    private Process? activeProcess;
    private readonly StringBuilder downloaderErrors = new();

    private static readonly Regex ProgressRegex = new(
        @"^download:(?<percent>[0-9.,]+)%\|(?<speed>[^|]*)\|(?<eta>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(uint eventConstant, IntPtr hwnd, int objectId, int childId);

    private const uint EventObjectNameChange = 0x800C;
    private const int ObjectIdClient = unchecked((int)0xFFFFFFFC);

    public MainForm()
    {
        Text = "OmniFetch – preuzimanje medija";
        AccessibleName = "OmniFetch, program za preuzimanje medija";
        AccessibleDescription = "Preuzimanje javno dostupnog videa ili zvuka";
        AccessibleRole = AccessibleRole.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 520);
        Size = new Size(760, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        BuildInterface();
        UpdateQualityOptions();
        videoRadio.CheckedChanged += (_, _) => UpdateQualityOptions();
        audioRadio.CheckedChanged += (_, _) => UpdateQualityOptions();
        downloadButton.Click += async (_, _) => await StartDownloadAsync();
        cancelButton.Click += (_, _) => CancelDownload();
        browseButton.Click += (_, _) => ChooseFolder();
        KeyDown += HandleKeyDown;
        Shown += async (_, _) =>
        {
            urlBox.Focus();
            await InitializeUpdatesAsync();
        };
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var heading = new Label
        {
            Text = "OmniFetch",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            AccessibleName = "OmniFetch",
            AccessibleRole = AccessibleRole.StaticText,
            Margin = new Padding(0, 0, 0, 4),
        };
        root.Controls.Add(heading, 0, 0);

        var subtitle = new Label
        {
            Text = "Jednostavan download javno dostupnog videa i zvuka",
            AutoSize = true,
            AccessibleName = "Jednostavan download javno dostupnog videa i zvuka",
            Margin = new Padding(0, 0, 0, 16),
        };
        root.Controls.Add(subtitle, 0, 1);

        var inputGroup = new GroupBox
        {
            Text = "Adresa i opcije",
            AccessibleName = "Adresa i opcije preuzimanja",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(inputGroup, 0, 2);

        var inputLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 5,
        };
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputGroup.Controls.Add(inputLayout);

        AddLabel(inputLayout, "URL javnog sadržaja:", 0);
        urlBox.Dock = DockStyle.Top;
        urlBox.Margin = new Padding(8, 0, 0, 8);
        urlBox.AccessibleName = "URL javnog videa ili audio sadržaja";
        urlBox.AccessibleDescription = "Nalepite javni URL stranice koju želite da preuzmete.";
        urlBox.TabIndex = 0;
        inputLayout.Controls.Add(urlBox, 1, 0);

        AddLabel(inputLayout, "Tip fajla:", 1);
        var typePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(8, 0, 0, 8) };
        videoRadio.Text = "Video (MP4)";
        videoRadio.Checked = true;
        videoRadio.AutoSize = true;
        videoRadio.AccessibleName = "Video, MP4";
        videoRadio.AccessibleDescription = "Preuzmi video sa zvukom u MP4 formatu.";
        videoRadio.TabIndex = 1;
        audioRadio.Text = "Audio (MP3)";
        audioRadio.AutoSize = true;
        audioRadio.AccessibleName = "Audio, MP3";
        audioRadio.AccessibleDescription = "Izdvoji zvuk i sačuvaj ga kao MP3.";
        audioRadio.TabIndex = 2;
        typePanel.Controls.Add(videoRadio);
        typePanel.Controls.Add(audioRadio);
        inputLayout.Controls.Add(typePanel, 1, 1);

        AddLabel(inputLayout, "Kvalitet:", 2);
        qualityBox.DropDownStyle = ComboBoxStyle.DropDownList;
        qualityBox.Dock = DockStyle.Top;
        qualityBox.Margin = new Padding(8, 0, 0, 8);
        qualityBox.AccessibleName = "Kvalitet preuzimanja";
        qualityBox.AccessibleDescription = "Izaberite maksimalnu rezoluciju ili kvalitet MP3 zvuka.";
        qualityBox.TabIndex = 3;
        inputLayout.Controls.Add(qualityBox, 1, 2);

        AddLabel(inputLayout, "Odredišni folder:", 3);
        var folderPanel = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(8, 0, 0, 8) };
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        folderBox.Dock = DockStyle.Top;
        folderBox.AccessibleName = "Odredišni folder";
        folderBox.AccessibleDescription = "Folder u kome će fajl biti sačuvan.";
        folderBox.TabIndex = 4;
        folderPanel.Controls.Add(folderBox, 0, 0);
        browseButton.Text = "Izaberi folder…";
        browseButton.AutoSize = true;
        browseButton.AccessibleName = "Izaberi odredišni folder";
        browseButton.AccessibleDescription = "Otvori dijalog za izbor foldera.";
        browseButton.TabIndex = 5;
        folderPanel.Controls.Add(browseButton, 1, 0);
        inputLayout.Controls.Add(folderPanel, 1, 3);

        var hint = new Label
        {
            Text = "Unesite javni URL. Za MP3 i spajanje video/audio streamova potreban je FFmpeg.",
            AutoSize = true,
            AccessibleName = "Uputstvo: unesite javni URL. Za MP3 i spajanje video audio streamova potreban je FFmpeg.",
            Margin = new Padding(8, 0, 0, 0),
        };
        inputLayout.Controls.Add(hint, 1, 4);

        var actionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 14),
        };
        root.Controls.Add(actionPanel, 0, 3);
        downloadButton.Text = "Preuzmi";
        downloadButton.AutoSize = true;
        downloadButton.MinimumSize = new Size(110, 34);
        downloadButton.AccessibleName = "Preuzmi";
        downloadButton.AccessibleDescription = "Pokreni preuzimanje. Prečica je Alt plus P.";
        downloadButton.TabIndex = 6;
        downloadButton.UseVisualStyleBackColor = true;
        actionPanel.Controls.Add(downloadButton);
        cancelButton.Text = "Otkaži";
        cancelButton.AutoSize = true;
        cancelButton.MinimumSize = new Size(110, 34);
        cancelButton.Enabled = false;
        cancelButton.AccessibleName = "Otkaži preuzimanje";
        cancelButton.AccessibleDescription = "Prekini trenutno preuzimanje. Prečica je Escape.";
        cancelButton.TabIndex = 7;
        actionPanel.Controls.Add(cancelButton);

        var progressGroup = new GroupBox
        {
            Text = "Napredak",
            AccessibleName = "Napredak preuzimanja",
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(progressGroup, 0, 4);
        var progressLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressGroup.Controls.Add(progressLayout);
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;
        progressBar.Value = 0;
        progressBar.Dock = DockStyle.Top;
        progressBar.AccessibleName = "Procenat preuzimanja";
        progressBar.AccessibleDescription = "Trenutni procenat završenog preuzimanja.";
        progressLayout.Controls.Add(progressBar, 0, 0);
        progressText.Text = "Spremno za preuzimanje.";
        progressText.AutoSize = true;
        progressText.Margin = new Padding(0, 8, 0, 0);
        progressText.AccessibleName = "Status preuzimanja";
        progressText.AccessibleRole = AccessibleRole.StatusBar;
        progressLayout.Controls.Add(progressText, 0, 1);

        statusStrip.AccessibleName = "Statusna traka";
        statusStrip.AccessibleRole = AccessibleRole.StatusBar;
        statusText.Text = "Prečice: Alt+P pokreće preuzimanje, Escape ga otkazuje.";
        statusText.AccessibleName = "Statusna poruka";
        statusStrip.Items.Add(statusText);
        Controls.Add(statusStrip);
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int row)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 8),
            AccessibleName = text,
            AccessibleRole = AccessibleRole.StaticText,
        };
        layout.Controls.Add(label, 0, row);
    }

    private void UpdateQualityOptions()
    {
        qualityBox.Items.Clear();
        if (audioRadio.Checked)
        {
            qualityBox.Items.AddRange(["MP3 320 kbps", "MP3 192 kbps", "MP3 128 kbps"]);
            qualityBox.SelectedIndex = 1;
        }
        else
        {
            qualityBox.Items.AddRange(["Najbolji dostupni", "1080p ili manje", "720p ili manje", "480p ili manje"]);
            qualityBox.SelectedIndex = 0;
        }
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = folderBox.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            folderBox.Text = dialog.SelectedPath;
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.P && e.Alt)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = StartDownloadAsync();
        }
        else if (e.KeyCode == Keys.Escape && cancelButton.Enabled)
        {
            e.Handled = true;
            CancelDownload();
        }
    }

    private async Task InitializeUpdatesAsync()
    {
        SetStatus("Proveravam ažuriranja aplikacije i alata…", 0, "Ažuriranje");
        try
        {
            AppUpdater.ToolUpdateResult tools = await AppUpdater.UpdateToolsAsync(
                message => SetStatus(message, 0, "Ažuriranje"), CancellationToken.None);
            if (tools.YtDlpUpdated || tools.FfmpegUpdated)
                SetStatus("Alati su ažurirani i spremni za rad.", 0, "Ažuriranje završeno");
            else
                SetStatus("Aplikacija i alati su ažurni.", 0, "Spremno");

            AppUpdater.ApplicationUpdate? application = await AppUpdater.CheckApplicationUpdateAsync(CancellationToken.None);
            if (application is null)
                return;

            SystemSounds.Question.Play();
            DialogResult answer = MessageBox.Show(
                this,
                $"Dostupna je nova verzija aplikacije ({application.Tag}). Preuzeti i instalirati ažuriranje sada?",
                "Dostupno ažuriranje",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (answer != DialogResult.Yes)
                return;

            SetStatus("Preuzimam ažuriranje. Program će se zatim ponovo pokrenuti…", 0, "Ažuriranje aplikacije");
            await AppUpdater.InstallApplicationUpdateAsync(application, CancellationToken.None);
            SystemSounds.Asterisk.Play();
            Application.Exit();
        }
        catch (Exception error)
        {
            SetStatus("Automatsko ažuriranje nije uspelo. Možete pokušati ponovo kasnije.", 0, "Ažuriranje nije uspelo");
            SystemSounds.Exclamation.Play();
            Debug.WriteLine(error);
        }
    }

    private async Task StartDownloadAsync()
    {
        if (activeProcess is not null)
            return;

        string url = urlBox.Text.Trim();
        string folder = folderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Unesite URL koji želite da preuzmete.", "Nedostaje URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            urlBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, "Izaberite odredišni folder.", "Nedostaje folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            folderBox.Focus();
            return;
        }

        string? executable = FindExecutable("yt-dlp.exe");
        if (executable is null)
        {
            PlayError("Nije moguće pokrenuti preuzimanje jer nedostaje yt-dlp. Pokrenite proveru ažuriranja ili vratite yt-dlp.exe u folder programa.");
            return;
        }
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")) ||
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "ffprobe.exe")))
        {
            PlayError("Nije moguće obraditi ovaj format jer nedostaje FFmpeg. Pokrenite ažuriranje ili vratite ffmpeg.exe i ffprobe.exe u folder programa.");
            return;
        }

        Directory.CreateDirectory(folder);
        cancellation = new CancellationTokenSource();
        downloaderErrors.Clear();
        SetBusy(true);
        SystemSounds.Beep.Play();
        SetStatus("Preuzimanje je u toku…", 0, "Pokrenuto");

        try
        {
            await Task.Run(() => RunDownloader(executable, url, folder, cancellation.Token), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                SystemSounds.Asterisk.Play();
                SetStatus("Preuzimanje je završeno.", 100, "Završeno");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Preuzimanje je otkazano.", progressBar.Value, "Otkazano");
        }
        catch (Exception error)
        {
            string explanation = ExplainDownloadError(error.Message + " " + downloaderErrors);
            SetStatus(explanation, progressBar.Value, "Greška");
            PlayError(explanation);
        }
        finally
        {
            activeProcess = null;
            cancellation.Dispose();
            cancellation = null;
            SetBusy(false);
        }
    }

    private void PlayError(string explanation)
    {
        SystemSounds.Exclamation.Play();
        SetStatus(explanation, progressBar.Value, "Greška");
        MessageBox.Show(this, explanation, "Preuzimanje nije moguće", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string ExplainDownloadError(string details)
    {
        string text = details.ToLowerInvariant();
        if (text.Contains("private") || text.Contains("login") || text.Contains("sign in") || text.Contains("authentication"))
            return "Preuzimanje nije moguće: sadržaj je privatan ili zahteva prijavu. Program ne zaobilazi prijave i zaštite.";
        if (text.Contains("drm") || text.Contains("encrypted"))
            return "Preuzimanje nije moguće: sadržaj je zaštićen DRM enkripcijom, a program ne zaobilazi takvu zaštitu.";
        if (text.Contains("geo") || text.Contains("country") || text.Contains("region"))
            return "Preuzimanje nije moguće: sadržaj nije dostupan u vašem regionu.";
        if (text.Contains("ffmpeg") || text.Contains("ffprobe"))
            return "Preuzimanje nije moguće: FFmpeg alat nedostaje ili nije kompatibilan. Pokrenite ažuriranje programa.";
        if (text.Contains("unsupported") || text.Contains("not supported"))
            return "Preuzimanje nije moguće: ovaj sajt ili format trenutno nije podržan. Proverite URL i pokušajte ponovo.";
        return "Preuzimanje nije moguće: URL nije javan, sajt trenutno nije podržan ili je sadržaj privremeno nedostupan. Proverite URL i pokušajte ponovo.";
    }

    private void RunDownloader(string executable, string url, string folder, CancellationToken token)
    {
        bool audio = audioRadio.Checked;
        string format = GetFormat(audio);
        var arguments = new List<string>
        {
            "--newline",
            "--no-colors",
            "--no-playlist",
            "--windows-filenames",
            "--progress-template", "download:%(progress._percent_str)s|%(progress._speed_str)s|%(progress.eta)s",
            "--output", Path.Combine(folder, "%(title)s.%(ext)s"),
            "--format", format,
        };
        if (audio)
        {
            arguments.AddRange(["--extract-audio", "--audio-format", "mp3", "--audio-quality", GetAudioQuality()]);
        }
        else
        {
            arguments.AddRange(["--merge-output-format", "mp4"]);
        }
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")))
            arguments.AddRange(["--ffmpeg-location", AppContext.BaseDirectory]);
        arguments.Add(url);

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        activeProcess = process;
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrEmpty(eventArgs.Data))
                ParseOutput(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
                return;
            if (!eventArgs.Data.Contains("[download]", StringComparison.OrdinalIgnoreCase))
            {
                downloaderErrors.AppendLine(eventArgs.Data);
                Post(() => statusText.Text = eventArgs.Data);
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        process.WaitForExit();

        if (token.IsCancellationRequested)
            throw new OperationCanceledException(token);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(downloaderErrors.ToString());
    }

    private void ParseOutput(string line)
    {
        Match match = ProgressRegex.Match(line.Trim());
        if (!match.Success)
            return;
        string number = match.Groups["percent"].Value.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
            return;
        string speed = match.Groups["speed"].Value.Trim();
        string eta = match.Groups["eta"].Value.Trim();
        SetStatus($"Preuzimanje: {percent:0.0}%{(string.IsNullOrEmpty(speed) ? "" : $". Brzina: {speed}")}{(string.IsNullOrEmpty(eta) ? "" : $". Preostalo: {eta}")}.", (int)Math.Clamp(percent, 0, 100), "Preuzimanje");
    }

    private string GetFormat(bool audio)
    {
        if (audio)
            return "bestaudio/best";
        return qualityBox.SelectedItem?.ToString() switch
        {
            "1080p ili manje" => "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best",
            "720p ili manje" => "bestvideo[height<=720]+bestaudio/best[height<=720]/best",
            "480p ili manje" => "bestvideo[height<=480]+bestaudio/best[height<=480]/best",
            _ => "bestvideo+bestaudio/best",
        };
    }

    private string GetAudioQuality() => qualityBox.SelectedItem?.ToString() switch
    {
        "MP3 320 kbps" => "320K",
        "MP3 128 kbps" => "128K",
        _ => "192K",
    };

    private void CancelDownload()
    {
        if (activeProcess is null || cancellation is null)
            return;
        cancellation.Cancel();
        SetStatus("Otkazivanje…", progressBar.Value, "Otkazivanje");
        cancelButton.Enabled = false;
    }

    private void SetBusy(bool busy)
    {
        urlBox.Enabled = !busy;
        videoRadio.Enabled = !busy;
        audioRadio.Enabled = !busy;
        qualityBox.Enabled = !busy;
        folderBox.Enabled = !busy;
        browseButton.Enabled = !busy;
        downloadButton.Enabled = !busy;
        cancelButton.Enabled = busy;
    }

    private void SetStatus(string message, int percent, string shortStatus)
    {
        Post(() =>
        {
            progressBar.Value = Math.Clamp(percent, 0, 100);
            progressText.Text = message;
            progressText.AccessibleName = $"Status preuzimanja: {message}";
            statusText.Text = shortStatus + ". " + message;
            NotifyWinEvent(EventObjectNameChange, progressText.Handle, ObjectIdClient, 0);
            NotifyWinEvent(EventObjectNameChange, statusText.GetCurrentParent()?.Handle ?? Handle, ObjectIdClient, 0);
        });
    }

    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    private static string? FindExecutable(string fileName)
    {
        string local = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(local))
            return local;
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
            return null;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}

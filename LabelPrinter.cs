using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BoutiqueLabels
{
    public sealed class Settings
    {
        public string Site { get; set; }
        public string Token { get; set; }
        public string Printer { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Margin { get; set; }
    }

    public sealed class Job
    {
        public string id { get; set; }
        public string leaseToken { get; set; }
        public string sku { get; set; }
        public string title { get; set; }
        public string size { get; set; }
        public string appearance { get; set; }
        public int basePriceCents { get; set; }
        public int qrSize { get; set; }
        public string matrix { get; set; }
    }

    public sealed class Reply
    {
        public Job job { get; set; }
        public bool accepted { get; set; }
        public string problem { get; set; }
    }

    internal static class LabelPrinter
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KohinoorLabelPrinter");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private const int InitialPollDelayMs = 5000;
        private const int MaxPollDelayMs = 30000;
        private static Uri endpoint;
        private static string token;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 3 && args[0] == "--preview")
                {
                    Job sample = Json.Deserialize<Job>(File.ReadAllText(args[1]));
                    using (var bitmap = new Bitmap(609, 406))
                    {
                        bitmap.SetResolution(203, 203);
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.White);
                            graphics.PageUnit = GraphicsUnit.Inch;
                            DrawLabel(graphics, new RectangleF(0.10f, 0.10f, 2.8f, 1.8f), sample);
                        }
                        bitmap.Save(args[2], ImageFormat.Png);
                    }
                    return 0;
                }
                using (var singleton = new Mutex(false, @"Local\KohinoorLabelPrinter"))
                {
                    try { if (!singleton.WaitOne(0)) return 0; }
                    catch (AbandonedMutexException) { }
                    try
                    {
                        if (args.Length == 1 && args[0] == "--settings")
                        {
                            Application.EnableVisualStyles();
                            Application.SetCompatibleTextRenderingDefault(false);
                            ShowSettings();
                        }
                        else RunWindow();
                    }
                    finally { singleton.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                Log(error.Message);
                return 1;
            }
            return 0;
        }

        private static void RunWindow()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!File.Exists(Path.Combine(DirectoryPath, "config.json")) && !ShowSettings()) return;
            using (var stop = new ManualResetEventSlim())
            using (var window = new Form { Text = "Kohinoor Label Printer", ClientSize = new Size(440, 180), StartPosition = FormStartPosition.CenterScreen, MinimumSize = new Size(390, 200) })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var instructions = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Text = "Keep this window open while printing. Close it when you’re done or before signing out." };
                var status = new Label { Dock = DockStyle.Fill, Text = "Connecting to the store…", Padding = new Padding(0, 10, 0, 0) };
                var close = new Button { AutoSize = true, Text = "Stop helper" };
                var configure = new Button { AutoSize = true, Text = "Settings" };
                var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                actions.Controls.Add(close);
                actions.Controls.Add(configure);
                layout.Controls.Add(instructions, 0, 0);
                layout.Controls.Add(status, 0, 1);
                layout.Controls.Add(actions, 0, 2);
                window.Controls.Add(layout);
                bool finished = false;
                bool changingSettings = false;
                Action start = null;
                close.Click += (sender, args) => window.Close();
                configure.Click += (sender, args) =>
                {
                    if (finished)
                    {
                        ShowSettings();
                        stop.Reset();
                        start();
                        return;
                    }
                    changingSettings = true;
                    configure.Enabled = false;
                    close.Enabled = false;
                    status.Text = "Pausing after the current label…";
                    stop.Set();
                };
                window.FormClosing += (sender, args) =>
                {
                    if (finished || args.CloseReason == CloseReason.WindowsShutDown) return;
                    args.Cancel = true;
                    status.Text = "Stopping after the current label…";
                    close.Enabled = false;
                    stop.Set();
                };
                start = () =>
                {
                    finished = false;
                    configure.Enabled = true;
                    close.Enabled = true;
                    close.Text = "Stop helper";
                    status.Text = "Connecting to the store…";
                    var progress = new Progress<string>(message => { if (!stop.IsSet) status.Text = message; });
                    var worker = new Thread(() =>
                    {
                        string failure = null;
                        try { Run(stop, progress); }
                        catch (Exception error)
                        {
                            Log(error.Message);
                            failure = "Could not start the helper. " + error.Message;
                        }
                        if (!window.IsHandleCreated) return;
                        window.BeginInvoke(new Action(() =>
                        {
                            finished = true;
                            if (changingSettings)
                            {
                                changingSettings = false;
                                ShowSettings();
                                stop.Reset();
                                start();
                            }
                            else if (failure == null || stop.IsSet) window.Close();
                            else
                            {
                                status.Text = failure;
                                close.Text = "Close";
                            }
                        }));
                    });
                    worker.IsBackground = true;
                    worker.SetApartmentState(ApartmentState.STA);
                    worker.Start();
                };
                window.Shown += (sender, args) => start();
                Application.Run(window);
            }
        }

        private static bool ShowSettings()
        {
            var path = Path.Combine(DirectoryPath, "config.json");
            Settings existing = null;
            if (File.Exists(path))
            {
                try { existing = Json.Deserialize<Settings>(File.ReadAllText(path)); }
                catch (Exception error) { Log("Could not read existing settings: " + error.Message); }
            }
            using (var form = new Form { Text = "Label printer settings", ClientSize = new Size(500, 390),
                StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(16), AutoSize = true };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var site = new TextBox { Dock = DockStyle.Fill, Text = existing == null ? "https://kohinoorjewelz.ca" : existing.Site };
                var printer = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (string name in PrinterSettings.InstalledPrinters) printer.Items.Add(name);
                if (existing != null && !String.IsNullOrWhiteSpace(existing.Printer) && !printer.Items.Contains(existing.Printer)) printer.Items.Add(existing.Printer);
                if (existing != null && printer.Items.Contains(existing.Printer)) printer.SelectedItem = existing.Printer;
                else for (int i = 0; i < printer.Items.Count; i++) if (printer.Items[i].ToString().IndexOf("403B", StringComparison.OrdinalIgnoreCase) >= 0) { printer.SelectedIndex = i; break; }
                var width = NumberField(existing == null ? 3m : (decimal)existing.Width, 1.58m, 4.33m);
                var height = NumberField(existing == null ? 2m : (decimal)existing.Height, 1.5m, 8m);
                var margin = NumberField(existing == null ? 0.1m : (decimal)existing.Margin, 0m, 0.25m);
                var password = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
                var note = new Label { AutoSize = true, MaximumSize = new Size(290, 0), Text = existing == null ? "Enter the pairing token configured in the app." : "Leave blank to keep the existing token." };
                AddSetting(layout, 0, "Store app URL", site);
                AddSetting(layout, 1, "Windows printer", printer);
                AddSetting(layout, 2, "Width (inches)", width);
                AddSetting(layout, 3, "Height (inches)", height);
                AddSetting(layout, 4, "Inset (inches)", margin);
                AddSetting(layout, 5, "Pairing token", password);
                layout.Controls.Add(note, 1, 6);
                var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                var save = new Button { Text = "Save", AutoSize = true };
                var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
                var test = new Button { Text = "Test connection", AutoSize = true };
                buttons.Controls.Add(save); buttons.Controls.Add(cancel); buttons.Controls.Add(test);
                layout.Controls.Add(buttons, 1, 7);
                form.Controls.Add(layout);
                form.AcceptButton = save;
                form.CancelButton = cancel;
                test.Click += (sender, args) =>
                {
                    Uri uri;
                    if (!TrySite(site.Text, out uri))
                    {
                        MessageBox.Show(form, "Enter an HTTPS store origin, or a loopback HTTP origin for local testing.", "Check settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    string checkToken = password.Text;
                    if (checkToken.Length == 0 && existing != null && !String.IsNullOrWhiteSpace(existing.Token))
                    {
                        try { checkToken = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(existing.Token), null, DataProtectionScope.CurrentUser)); }
                        catch { MessageBox.Show(form, "Could not read the saved token. Enter it again.", "Check settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    }
                    if (checkToken.Length < 32)
                    {
                        MessageBox.Show(form, "Enter a pairing token with at least 32 characters.", "Check settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    test.Enabled = false;
                    test.Text = "Testing…";
                    var connectionToken = checkToken;
                    var worker = new Thread(() =>
                    {
                        string result = TestConnection(uri, connectionToken);
                        if (!form.IsHandleCreated || form.IsDisposed) return;
                        try { form.BeginInvoke(new Action(() => {
                            test.Enabled = true; test.Text = "Test connection";
                            MessageBox.Show(form, result, "Connection test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        })); } catch (InvalidOperationException) { }
                    });
                    worker.IsBackground = true;
                    worker.Start();
                };
                save.Click += (sender, args) =>
                {
                    try
                    {
                        Uri uri;
                        if (!TrySite(site.Text, out uri))
                            throw new InvalidOperationException("Enter an HTTPS store origin, or a loopback HTTP origin for local testing.");
                        if (printer.SelectedItem == null) throw new InvalidOperationException("Choose an installed Windows printer.");
                        string encrypted = existing == null ? null : existing.Token;
                        if (password.Text.Length > 0)
                        {
                            if (password.Text.Length < 32) throw new InvalidOperationException("The pairing token must contain at least 32 characters.");
                            byte[] bytes = Encoding.UTF8.GetBytes(password.Text);
                            try { encrypted = Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
                            finally { Array.Clear(bytes, 0, bytes.Length); }
                        }
                        if (String.IsNullOrWhiteSpace(encrypted)) throw new InvalidOperationException("Enter a pairing token.");
                        var settings = new Settings { Site = uri.GetLeftPart(UriPartial.Authority), Printer = printer.SelectedItem.ToString(),
                            Width = (double)width.Value, Height = (double)height.Value, Margin = (double)margin.Value, Token = encrypted };
                        Directory.CreateDirectory(DirectoryPath);
                        File.WriteAllText(path, Json.Serialize(settings), new UTF8Encoding(false));
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    }
                    catch (Exception error) { MessageBox.Show(form, error.Message, "Check settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                return form.ShowDialog() == DialogResult.OK;
            }
        }

        private static bool TrySite(string value, out Uri uri)
        {
            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) &&
                (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)) &&
                uri.UserInfo.Length == 0 && uri.PathAndQuery == "/" && uri.Fragment.Length == 0;
        }

        private static string TestConnection(Uri site, string candidateToken)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(new Uri(site, "/api/label-printer"));
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + candidateToken;
                request.AllowAutoRedirect = false;
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                // The app authenticates before validating actions. An invalid action
                // checks the token without claiming a job or updating the heartbeat.
                byte[] bytes = Encoding.UTF8.GetBytes("{\"action\":\"connection-test\"}");
                request.ContentLength = bytes.Length;
                using (var output = request.GetRequestStream()) output.Write(bytes, 0, bytes.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                    return "Unexpected response from the app (HTTP " + (int)response.StatusCode + ").";
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response == null) return "Could not reach the app. Check the URL and network connection.";
                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized) return "The app rejected the pairing token. Check that it matches the app configuration.";
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        try
                        {
                            using (var reader = new StreamReader(response.GetResponseStream()))
                            {
                                var body = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                                if (body.ContainsKey("error") && Object.Equals(body["error"], "Invalid print request."))
                                    return "Connected. The app accepted the pairing token. No label was printed.";
                            }
                        }
                        catch (Exception) { }
                    }
                    return "The app returned HTTP " + (int)response.StatusCode + ". Check the URL and app configuration.";
                }
            }
            catch (Exception) { return "Could not test the connection. Check the URL and network connection."; }
        }

        private static NumericUpDown NumberField(decimal value, decimal min, decimal max)
        {
            return new NumericUpDown { Dock = DockStyle.Fill, DecimalPlaces = 2, Increment = 0.01m,
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)) };
        }

        private static void AddSetting(TableLayoutPanel layout, int row, string label, Control field)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(field, 1, row);
        }

        private static void Run(ManualResetEventSlim stop, IProgress<string> progress)
        {
            var settings = Json.Deserialize<Settings>(File.ReadAllText(Path.Combine(DirectoryPath, "config.json")));
            Uri site;
            if (!Uri.TryCreate(settings.Site, UriKind.Absolute, out site) ||
                (site.Scheme != "https" && !(site.Scheme == "http" && site.IsLoopback)) ||
                site.UserInfo.Length != 0 || site.PathAndQuery != "/" || site.Fragment.Length != 0)
                throw new InvalidOperationException("Enter an HTTPS site origin, or a loopback HTTP origin for local testing.");
            endpoint = new Uri(site, "/api/label-printer");
            token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(settings.Token), null, DataProtectionScope.CurrentUser));
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string lastError = null;
            object pendingReport = null;
            int pollDelay = InitialPollDelayMs;
            while (true)
            {
                try
                {
                    if (pendingReport != null)
                    {
                        if (!Post(pendingReport).accepted) Log("Server rejected a submission report. Check the admin queue.");
                        pendingReport = null;
                    }
                    if (stop.IsSet) return;
                    string problem = null;
                    try { using (CreateDocument(settings)) { } }
                    catch (Exception error) { problem = error.Message.Substring(0, Math.Min(error.Message.Length, 200)); }
                    var poll = Post(new { action = "poll", problem = problem });
                    var job = poll.job;
                    if (job == null)
                    {
                        lastError = null;
                        progress.Report(poll.problem ?? problem ?? "Connected. Waiting for labels.");
                        stop.Wait(pollDelay);
                        pollDelay = Math.Min(pollDelay * 2, MaxPollDelayMs);
                        continue;
                    }
                    progress.Report("Sending label for " + job.sku + " to Windows…");
                    string status = Print(settings, job);
                    if (status != null) pendingReport = new { action = "report", id = job.id, leaseToken = job.leaseToken, status = status };
                    lastError = null;
                    pollDelay = InitialPollDelayMs;
                }
                catch (Exception error)
                {
                    if (error.Message != lastError) Log(error.Message);
                    lastError = error.Message;
                    if (stop.IsSet) return;
                    progress.Report("Could not reach the print queue. " + error.Message);
                    if (stop.Wait(pollDelay)) return;
                    pollDelay = Math.Min(pollDelay * 2, MaxPollDelayMs);
                }
            }
        }

        private static string Print(Settings settings, Job job)
        {
            PrintDocument document;
            try
            {
                ValidateJob(job);
                document = CreateDocument(settings);
            }
            catch (Exception error)
            {
                Log(error.Message);
                return "Failed";
            }
            using (document)
            {
                bool cancelled = false;
                document.EndPrint += (sender, args) => { cancelled = args.Cancel; };
                document.DocumentName = "Kohinoor label " + job.id;
                document.PrintPage += (sender, page) =>
                {
                    page.Graphics.PageUnit = GraphicsUnit.Inch;
                    page.Graphics.TranslateTransform(-page.PageSettings.HardMarginX / 100f, -page.PageSettings.HardMarginY / 100f);
                    DrawLabel(page.Graphics, LabelArea(settings, page.PageSettings), job);
                    page.HasMorePages = false;
                };
                if (!Post(new { action = "begin", id = job.id, leaseToken = job.leaseToken }).accepted) return null;
                try
                {
                    document.Print();
                    return cancelled ? "NeedsReview" : "Submitted";
                }
                catch (Exception error)
                {
                    Log(job.id + ": " + error.Message);
                    return "NeedsReview";
                }
            }
        }

        private static PrintDocument CreateDocument(Settings settings)
        {
            var document = new PrintDocument();
            try
            {
                bool installed = false;
                foreach (string name in PrinterSettings.InstalledPrinters)
                    if (String.Equals(settings.Printer, name, StringComparison.OrdinalIgnoreCase)) installed = true;
                if (!installed) throw new InvalidOperationException("Configured Windows printer was not found.");
                document.PrinterSettings.PrinterName = settings.Printer;
                if (!document.PrinterSettings.IsValid) throw new InvalidOperationException("Configured Windows printer is unavailable.");
                int width = (int)Math.Round(settings.Width * 100);
                int height = (int)Math.Round(settings.Height * 100);
                PaperSize stock = null;
                foreach (PaperSize size in document.PrinterSettings.PaperSizes)
                    if (Math.Abs(size.Width - width) <= 1 && Math.Abs(size.Height - height) <= 1) { stock = size; break; }
                if (stock == null) throw new InvalidOperationException("Configure the matching custom paper size in Windows printer preferences.");
                document.PrintController = new StandardPrintController();
                document.PrinterSettings.Copies = 1;
                document.DefaultPageSettings.PaperSize = stock;
                document.DefaultPageSettings.Landscape = false;
                document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                LabelArea(settings, document.DefaultPageSettings);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        private static RectangleF LabelArea(Settings settings, PageSettings page)
        {
            var driver = page.PrintableArea;
            var printable = new RectangleF(driver.X / 100f, driver.Y / 100f, driver.Width / 100f, driver.Height / 100f);
            float margin = (float)settings.Margin;
            var paper = new RectangleF(margin, margin, (float)settings.Width - 2 * margin, (float)settings.Height - 2 * margin);
            var area = RectangleF.Intersect(printable, paper);
            if (area.Width < 1.3f || area.Height < 1.3f) throw new InvalidOperationException("Printable area is too small for the label.");
            return area;
        }

        private static void ValidateJob(Job job)
        {
            if (job == null || String.IsNullOrWhiteSpace(job.sku) || job.basePriceCents < 1 || job.qrSize < 21 || job.qrSize > 177 ||
                job.matrix == null || job.matrix.Length != job.qrSize * job.qrSize || job.matrix.Trim('0', '1').Length != 0)
                throw new InvalidOperationException("Invalid label data received.");
        }

        private static void DrawLabel(Graphics graphics, RectangleF area, Job job)
        {
            ValidateJob(job);
            graphics.SmoothingMode = SmoothingMode.None;
            bool across = area.Width >= area.Height;
            float box = across ? Math.Min(area.Width * 0.46f, area.Height) : Math.Min(area.Width, area.Height * 0.58f);
            int cells = job.qrSize + 8;
            float step = (float)(Math.Floor(box * graphics.DpiX / cells) / graphics.DpiX);
            if (step * graphics.DpiX < 3) throw new InvalidOperationException("QR is too dense for this label stock.");
            float side = step * cells;
            var x = across ? area.Right - side : area.X + (area.Width - side) / 2;
            var y = across ? area.Y + (area.Height - side) / 2 : area.Bottom - side;
            x = (float)(Math.Floor(x * graphics.DpiX) / graphics.DpiX);
            y = (float)(Math.Floor(y * graphics.DpiY) / graphics.DpiY);
            for (int row = 0; row < job.qrSize; row++)
                for (int column = 0; column < job.qrSize; column++)
                    if (job.matrix[row * job.qrSize + column] == '1')
                        graphics.FillRectangle(Brushes.Black, x + (column + 4) * step, y + (row + 4) * step, step, step);
            var text = across
                ? new RectangleF(area.X, area.Y, x - area.X - 0.06f, area.Height)
                : new RectangleF(area.X, area.Y, area.Width, y - area.Y - 0.06f);
            var title = new RectangleF(text.X, text.Y, text.Width, text.Height * 0.22f);
            var detail = new RectangleF(text.X, title.Bottom, text.Width, text.Height * 0.22f);
            var sku = new RectangleF(text.X, detail.Bottom, text.Width, text.Height * 0.25f);
            var price = new RectangleF(text.X, sku.Bottom, text.Width, text.Bottom - sku.Bottom);
            DrawDetail(graphics, title, job.title, 10);
            string description = String.IsNullOrWhiteSpace(job.appearance) ? job.size :
                String.IsNullOrWhiteSpace(job.size) ? job.appearance : job.appearance + "  ·  " + job.size;
            DrawDetail(graphics, detail, description, 9);
            DrawText(graphics, sku, job.sku, 16, StringAlignment.Center);
            DrawText(graphics, price, "CAD $" + (job.basePriceCents / 100m).ToString("0.00", CultureInfo.InvariantCulture), 19, StringAlignment.Center);
        }

        private static void DrawDetail(Graphics graphics, RectangleF box, string value, int size)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            using (var font = new Font("Arial", size, FontStyle.Regular))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                graphics.DrawString(value, font, Brushes.Black, box, format);
        }

        private static void DrawText(Graphics graphics, RectangleF box, string value, int maxSize, StringAlignment vertical)
        {
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = vertical, FormatFlags = StringFormatFlags.NoWrap })
                for (int size = maxSize; size >= 6; size--)
                    using (var font = new Font("Arial", size, FontStyle.Bold))
                    {
                        var measured = graphics.MeasureString(value, font);
                        if (measured.Width > box.Width || measured.Height > box.Height) continue;
                        graphics.DrawString(value, font, Brushes.Black, box, format);
                        return;
                    }
            throw new InvalidOperationException("SKU or price does not fit on the label.");
        }

        private static Reply Post(object body)
        {
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            request.AllowAutoRedirect = false;
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            var bytes = Encoding.UTF8.GetBytes(Json.Serialize(body));
            request.ContentLength = bytes.Length;
            using (var output = request.GetRequestStream()) output.Write(bytes, 0, bytes.Length);
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
                return Json.Deserialize<Reply>(reader.ReadToEnd());
        }

        private static void Log(string message)
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
            var log = Path.Combine(DirectoryPath, "printer.log");
            if (File.Exists(log) && new FileInfo(log).Length > 250000) File.WriteAllText(log, "");
            File.AppendAllText(log, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
        }
    }
}

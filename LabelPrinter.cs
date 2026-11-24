using System;
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
        public int basePriceCents { get; set; }
        public int qrSize { get; set; }
        public string matrix { get; set; }
    }

    public sealed class Reply
    {
        public Job job { get; set; }
        public bool accepted { get; set; }
    }

    internal static class LabelPrinter
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KohinoorLabelPrinter");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
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
                    try { Run(); }
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

        private static void Run()
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
            while (true)
            {
                try
                {
                    if (pendingReport != null)
                    {
                        if (!Post(pendingReport).accepted) Log("Server rejected a submission report. Check the admin queue.");
                        pendingReport = null;
                    }
                    string problem = null;
                    try { using (CreateDocument(settings)) { } }
                    catch (Exception error) { problem = error.Message.Substring(0, Math.Min(error.Message.Length, 200)); }
                    var job = Post(new { action = "poll", problem = problem }).job;
                    if (job == null)
                    {
                        lastError = null;
                        Thread.Sleep(5000);
                        continue;
                    }
                    string status = Print(settings, job);
                    if (status != null) pendingReport = new { action = "report", id = job.id, leaseToken = job.leaseToken, status = status };
                    lastError = null;
                }
                catch (Exception error)
                {
                    if (error.Message != lastError) Log(error.Message);
                    lastError = error.Message;
                    Thread.Sleep(5000);
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
            var sku = new RectangleF(text.X, text.Y, text.Width, text.Height / 2);
            var price = new RectangleF(text.X, sku.Bottom, text.Width, text.Height / 2);
            DrawText(graphics, sku, job.sku, 18, StringAlignment.Far);
            DrawText(graphics, price, "CAD $" + (job.basePriceCents / 100m).ToString("0.00", CultureInfo.InvariantCulture), 22, StringAlignment.Near);
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

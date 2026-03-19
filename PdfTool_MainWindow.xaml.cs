using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfSharp.Drawing;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Globalization;

// 使用命名空间别名，彻底区分两个不同库中同名的 PdfDocument 类
using WinPdfDoc = Windows.Data.Pdf.PdfDocument;
using SharpPdfDoc = PdfSharp.Pdf.PdfDocument;

namespace PdfCompressTool
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "PDF 文件 (*.pdf)|*.pdf" };
            if (dlg.ShowDialog() == true)
            {
                TxtFilePath.Text = dlg.FileName;
                BtnProcess.IsEnabled = true;
            }
        }

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                TxtLog.ScrollToEnd();
            });
        }

        private async void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            BtnProcess.IsEnabled = false;
            TxtLog.Clear();
            Log("=== 开始深度解析与重构 ===");

            string inputPath = TxtFilePath.Text;
            string dir = Path.GetDirectoryName(inputPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(inputPath);
            long originalSize = new FileInfo(inputPath).Length;

            double targetRatio = 1.0;
            string suffix = "100pct";

            if (CmbRatio.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                // 使用 CultureInfo.InvariantCulture 确保解析安全
                targetRatio = double.Parse(selectedItem.Tag.ToString()!, CultureInfo.InvariantCulture);
                suffix = $"{(int)(targetRatio * 100)}pct";
            }

            try
            {
                Log($"源文件体积: {originalSize / 1024.0:F2} KB");

                // 利用原生渲染引擎加载 PDF
                var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(inputPath));
                var pdfDoc = await WinPdfDoc.LoadFromFileAsync(file);
                if (pdfDoc.PageCount == 0) throw new Exception("该 PDF 没有有效页面。");

                // 【需求 1 落地】：死死锁定第一页的物理尺寸，作为后续所有页的唯一画框标准
                Windows.Foundation.Size templateSize;
                using (var firstPage = pdfDoc.GetPage(0))
                {
                    templateSize = firstPage.Size;
                }
                Log($"[尺寸锁定] 已取【第 1 页】作为全局标准画布: 宽 {templateSize.Width:F1} x 高 {templateSize.Height:F1}");
                Log($"[处理说明] 无论原文档有多少页，所有页面都将被等比缩放并居中填入该尺寸，绝不拉伸。");

                string outputPath = Path.Combine(dir, $"{name}_{suffix}.pdf");

                // 【需求 2 落地】：加入 100% 不压缩模式的判断
                if (Math.Abs(targetRatio - 1.0) < 0.001)
                {
                    Log($"\n>>> 选择 100% (不压缩)：跳过体积限制测算...");
                    Log($">>> 正在以 3.0 倍超清分辨率和 100% 最高画质重构并统一所有页面尺寸...");
                    // 直接调用重绘，使用 3.0 的超清缩放倍率和 100 的最高 JPEG 画质
                    byte[] bestPdfBytes = await GeneratePdfBytesAsync(pdfDoc, templateSize, 3.0, 100);
                    await File.WriteAllBytesAsync(outputPath, bestPdfBytes);
                    Log($"    ✓ 已输出无损画质版本: {Path.GetFileName(outputPath)} (最终大小: {bestPdfBytes.Length / 1024.0:F2} KB)");
                }
                else
                {
                    long targetFileSizeBytes = (long)(originalSize * targetRatio);
                    Log($"\n当前所选目标上限: 原体积的 {targetRatio * 100}% (约 {targetFileSizeBytes / 1024.0:F2} KB)");
                    Log($">>> 启动AI二分法测算，自动逼近体积限制下的最高画质...");
                    await CompressAndSaveAsync(pdfDoc, templateSize, targetFileSizeBytes, outputPath);
                }

                Log("\n🎉 任务圆满完成！");
                MessageBox.Show($"处理成功！{targetRatio * 100}% 的版本已经生成。\n文件路径: {outputPath}", "大功告成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[严重异常] {ex.Message}");
                MessageBox.Show($"处理出错:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnProcess.IsEnabled = true;
            }
        }

        private async Task CompressAndSaveAsync(WinPdfDoc originalDoc, Windows.Foundation.Size targetSize, long targetFileSizeBytes, string outputPath)
        {
            double[] scales = { 3.0, 2.5, 2.0, 1.5, 1.2, 1.0, 0.8, 0.6, 0.4 };
            double bestScale = 0.4;

            foreach (var scale in scales)
            {
                byte[] testBytes = await GeneratePdfBytesAsync(originalDoc, targetSize, scale, 10);
                if (testBytes.Length <= targetFileSizeBytes)
                {
                    bestScale = scale;
                    break;
                }
            }
            Log($"    * 已探测到当前体积下可用最高分辨率倍率: {bestScale}X");

            int lowQ = 10, highQ = 100;
            byte[]? bestPdfBytes = null;
            long bestDiff = long.MaxValue;

            for (int i = 0; i < 7; i++)
            {
                if (lowQ > highQ) break;
                int midQ = (lowQ + highQ) / 2;

                byte[] currentBytes = await GeneratePdfBytesAsync(originalDoc, targetSize, bestScale, midQ);
                long diff = Math.Abs(currentBytes.Length - targetFileSizeBytes);

                if (currentBytes.Length <= targetFileSizeBytes)
                {
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestPdfBytes = currentBytes;
                    }
                    lowQ = midQ + 1;
                }
                else
                {
                    highQ = midQ - 1;
                }
            }

            bestPdfBytes ??= await GeneratePdfBytesAsync(originalDoc, targetSize, bestScale, 10);

            await File.WriteAllBytesAsync(outputPath, bestPdfBytes);
            Log($"    ✓ 已输出: {Path.GetFileName(outputPath)} (最终大小: {bestPdfBytes.Length / 1024.0:F2} KB)");
        }

        private async Task<byte[]> GeneratePdfBytesAsync(WinPdfDoc originalDoc, Windows.Foundation.Size targetSizeDips, double scale, int jpegQuality)
        {
            using var outStream = new MemoryStream();
            using var newPdfDoc = new SharpPdfDoc();

            // 循环处理文档内的所有页面 (不管有几页，都过一遍这里)
            for (uint i = 0; i < originalDoc.PageCount; i++)
            {
                using var page = originalDoc.GetPage(i);
                var origSize = page.Size;

                // 等比例自适应缩放计算，防止拉伸变形
                double widthRatio = targetSizeDips.Width / origSize.Width;
                double heightRatio = targetSizeDips.Height / origSize.Height;
                double fitRatio = Math.Min(widthRatio, heightRatio);

                uint renderWidth = (uint)Math.Max(1, origSize.Width * fitRatio * scale);
                uint renderHeight = (uint)Math.Max(1, origSize.Height * fitRatio * scale);

                var options = new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth = renderWidth,
                    DestinationHeight = renderHeight,
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                };

                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, options);

                var decoder = BitmapDecoder.Create(stream.AsStream(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
                encoder.Frames.Add(decoder.Frames[0]);

                using var jpegStream = new MemoryStream();
                encoder.Save(jpegStream);
                jpegStream.Position = 0;

                var pdfPage = newPdfDoc.AddPage();

                // 【核心要求 1 彻底限制】：强制要求新生成的任何页面，长宽严格等于第一页的物理尺寸
                pdfPage.Width = XUnit.FromPoint(targetSizeDips.Width * 72.0 / 96.0);
                pdfPage.Height = XUnit.FromPoint(targetSizeDips.Height * 72.0 / 96.0);

                using var xImage = XImage.FromStream(jpegStream);
                using var gfx = XGraphics.FromPdfPage(pdfPage);

                double drawWidth = origSize.Width * fitRatio * 72.0 / 96.0;
                double drawHeight = origSize.Height * fitRatio * 72.0 / 96.0;

                // 居中偏移算法 (Letterboxing)，将等比缩放后的画面精准居中贴在这张标准尺寸的纸上
                double drawX = (pdfPage.Width.Point - drawWidth) / 2.0;
                double drawY = (pdfPage.Height.Point - drawHeight) / 2.0;

                gfx.DrawImage(xImage, drawX, drawY, drawWidth, drawHeight);
            }

            newPdfDoc.Save(outStream);
            return outStream.ToArray();
        }
    }
}

using System.Windows;

namespace MultiToolApp
{
    public partial class Launcher : Window
    {
        public Launcher()
        {
            InitializeComponent();
        }

        private void BtnPdfTool_Click(object sender, RoutedEventArgs e)
        {
            // 实例化并显示 PDF 工具窗口
            PdfCompressTool.MainWindow pdfWindow = new PdfCompressTool.MainWindow();
            pdfWindow.Show();
            
            // 可选：隐藏主启动界面
            // this.Hide();
            // pdfWindow.Closed += (s, args) => this.Show();
        }

        private void BtnIdPhotoTool_Click(object sender, RoutedEventArgs e)
        {
            // 实例化并显示证件照工具窗口
            IdPhotoEditor.MainWindow idPhotoWindow = new IdPhotoEditor.MainWindow();
            idPhotoWindow.Show();
            
            // 可选：隐藏主启动界面
            // this.Hide();
            // idPhotoWindow.Closed += (s, args) => this.Show();
        }
    }
}

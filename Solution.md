# WPF 多功能工具箱整合方案

通过对您提供的四个文件进行分析，我已成功区分出两个独立的 WPF 应用程序代码，并为您设计了一个统一的总启动界面（Launcher）。

## 一、代码区分与归属

根据命名空间和功能特征，四个文件可以明确划分为两个项目：

### 1. PDF 压缩与尺寸统一工具
**命名空间：** `PdfCompressTool`
* **界面文件：** `pasted_file_VTrd7Q_MainWindow.xaml`
* **代码文件：** `pasted_file_COmqBo_MainWindow.xaml.cs`
* **主要功能：** 读取 PDF 文件，锁定第一页尺寸，并根据选择的压缩率重构并输出新的 PDF 文件。

### 2. 智能证件照换底系统
**命名空间：** `IdPhotoEditor`
* **界面文件：** `pasted_file_4fUPDV_MainWindow1.xaml`
* **代码文件：** `pasted_file_ErHQBA_MainWindow1.xaml.cs`
* **主要功能：** 读取图片，利用 OpenCV 进行智能抠图提取透明度掩码，并实时更换红、白、蓝等底色。

## 二、总界面（Launcher）设计方案

为了将这两个程序整合在一起，我设计了一个名为 `Launcher` 的主窗口，作为程序的唯一入口。用户可以在此界面点击按钮，分别调起对应的子程序窗口。

### 1. Launcher.xaml (总界面 XAML)
```xml
<Window x:Class="MultiToolApp.Launcher"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="多功能工具箱" Height="350" Width="500" WindowStartupLocation="CenterScreen">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <TextBlock Text="请选择要运行的工具" FontSize="24" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,0,0,30" Foreground="#333333"/>

        <StackPanel Grid.Row="1" Orientation="Vertical" VerticalAlignment="Center" HorizontalAlignment="Center" Width="300">
            <Button x:Name="BtnPdfTool" Content="📄 PDF 压缩与尺寸统一工具" Height="60" FontSize="16" FontWeight="Bold" Margin="0,0,0,20" Click="BtnPdfTool_Click" Background="#0078D7" Foreground="White" Cursor="Hand"/>
            <Button x:Name="BtnIdPhotoTool" Content="🖼️ 智能证件照换底系统" Height="60" FontSize="16" FontWeight="Bold" Click="BtnIdPhotoTool_Click" Background="#28A745" Foreground="White" Cursor="Hand"/>
        </StackPanel>
    </Grid>
</Window>
```

### 2. Launcher.xaml.cs (总界面后端代码)
```csharp
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
        }

        private void BtnIdPhotoTool_Click(object sender, RoutedEventArgs e)
        {
            // 实例化并显示证件照工具窗口
            IdPhotoEditor.MainWindow idPhotoWindow = new IdPhotoEditor.MainWindow();
            idPhotoWindow.Show();
        }
    }
}
```

## 三、项目整合建议

要使此方案在 Visual Studio 中完美运行，请按照以下步骤配置您的 WPF 项目：

1. **创建主项目：** 创建一个新的 WPF 应用程序项目（例如命名为 `MultiToolApp`）。
2. **设置启动项：** 将 `App.xaml` 中的 `StartupUri` 修改为指向总界面：`StartupUri="Launcher.xaml"`。
3. **引入依赖：** 确保您的项目中通过 NuGet 安装了两个子程序所需的全部依赖包：
   * `PdfSharp`
   * `OpenCvSharp4` 和 `OpenCvSharp4.WpfExtensions`
   * `OpenCvSharp4.runtime.win` (用于 Windows 下的 OpenCV 运行库)
4. **合并代码：**
   * 将 PDF 工具的两个文件添加到项目中（保持其 `PdfCompressTool` 命名空间）。
   * 将证件照工具的两个文件添加到项目中（建议将其类名从 `MainWindow` 改为 `IdPhotoWindow`，以避免与 PDF 工具同名冲突，虽然它们在不同的命名空间下，但改名能让代码更清晰；在上述示例代码中，我通过指定完整命名空间 `PdfCompressTool.MainWindow` 和 `IdPhotoEditor.MainWindow` 解决了命名冲突）。
   * 将我为您编写的 `Launcher.xaml` 和 `Launcher.xaml.cs` 添加到项目中。

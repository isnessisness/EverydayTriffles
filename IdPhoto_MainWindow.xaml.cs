using System;
using System.Windows;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace IdPhotoEditor
{
    public partial class MainWindow : System.Windows.Window
    {
        private Mat? _originalMat;
        private Mat? _alphaMask; // 【核心升级】缓存抠取出的透明度权重掩码，用于瞬间换色
        private Mat? _resultMat;

        public MainWindow()
        {
            InitializeComponent();
        }

        // 1. 读取照片
        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "请选择任意底色的证件照片"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ReleaseMats(); // 读取新图前，必须释放旧内存并清空旧的蒙版缓存

                _originalMat = Cv2.ImRead(openFileDialog.FileName, ImreadModes.Color);
                ImgOriginal.Source = _originalMat.ToWriteableBitmap();
                ImgResult.Source = null;
            }
        }

        // 2. 按钮触发：提取并换底
        private void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            ProcessImage();
        }

        // 3. 实时预览：当用户点击单选框切换底色时触发
        private void Color_Changed(object sender, RoutedEventArgs e)
        {
            // 如果图片已经加载，且已经抠过图（掩码不为空），点击单选框直接毫无延迟地重绘图片
            if (this.IsLoaded && _originalMat != null && _alphaMask != null)
            {
                ProcessImage();
            }
        }

        // --- 统筹调度的核心方法 ---
        private void ProcessImage()
        {
            if (_originalMat == null || _originalMat.Empty())
            {
                MessageBox.Show("请先读取一张照片！", "提示");
                return;
            }

            try
            {
                // 1. 如果是新图片第一次点击，执行最耗时的抠图操作，并把掩码缓存起来
                if (_alphaMask == null)
                {
                    _alphaMask = ExtractAlphaMask(_originalMat);
                }

                // 2. 获取目标底色
                Scalar targetColor = GetSelectedColor();

                // 3. 瞬间执行图层融合矩阵运算
                _resultMat?.Dispose();
                _resultMat = BlendBackground(_originalMat, _alphaMask, targetColor);

                // 输出渲染到界面
                ImgResult.Source = _resultMat.ToWriteableBitmap();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"处理失败: {ex.Message}");
            }
        }

        // 保存结果
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_resultMat == null || _resultMat.Empty())
            {
                MessageBox.Show("还没有处理好的照片可以保存！", "提示");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "PNG 图像 (推荐，无损保留发丝羽化细节)|*.png|JPEG 图像|*.jpg",
                FileName = "新底色证件照.png"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                Cv2.ImWrite(saveFileDialog.FileName, _resultMat);
                MessageBox.Show("保存成功！", "提示");
            }
        }

        /// <summary>
        /// 获取当前选中的目标底色
        /// 【注意避坑】OpenCV底层色彩永远是 BGR (蓝,绿,红) 顺序，需要倒过来填入 Scalar！
        /// </summary>
        private Scalar GetSelectedColor()
        {
            if (RbRed.IsChecked == true) return new Scalar(0, 0, 255);       // 标准红 (R:255 G:0 B:0)
            if (RbWhite.IsChecked == true) return new Scalar(255, 255, 255);   // 标准白 (R:255 G:255 B:255)
            if (RbDarkBlue.IsChecked == true) return new Scalar(219, 142, 67);    // 严谨深蓝 (R:67 G:142 B:219)

            return new Scalar(243, 191, 0); // 默认常用浅蓝 (R:0 G:191 B:243)
        }

        /// <summary>
        /// 核心算法 1：提取透明度掩码 (自适应任何颜色的底，哪怕是有阴影或偏色)
        /// </summary>
        private Mat ExtractAlphaMask(Mat src)
        {
            Mat img = src.Clone();
            Mat mask = new Mat(img.Rows + 2, img.Cols + 2, MatType.CV_8UC1, new Scalar(0));

            // 扩大容差到 40：只要原图背景左右相似，不管是渐变红、劣质的灰白、带有暗角的深蓝，都能当作同一背景
            Scalar diff = new Scalar(40, 40, 40);
            FloodFillFlags flags = FloodFillFlags.Link4 | (FloodFillFlags)(255 << 8) | FloodFillFlags.MaskOnly | FloodFillFlags.FixedRange;

            // "不问原背景是什么颜色"，由于只有底色才会贴着最上方的边缘，
            // 算法直接把左上角和右上角的像素设为"起点颜色"，往人像边缘蔓延，就能自适应吃干净任何背景！
            Cv2.FloodFill(img, mask, new OpenCvSharp.Point(0, 0), new Scalar(255), out _, diff, diff, flags);
            Cv2.FloodFill(img, mask, new OpenCvSharp.Point(img.Cols - 1, 0), new Scalar(255), out _, diff, diff, flags);

            using Mat bgMask = new Mat(mask, new OpenCvSharp.Rect(1, 1, img.Cols, img.Rows));

            // 抗溢色："红底转白底"或"蓝底转红底"最怕发丝里残留上一张图底色的杂边（红光/蓝光污染）。
            // 原理：使用膨胀操作 (Dilate) ，使得背景掩码往"人里面"强行吞进 2~3 个像素边缘。
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
            using Mat dilatedMask = new Mat();
            Cv2.Dilate(bgMask, dilatedMask, kernel);

            // 高斯羽化处理，让发丝边缘过度呈现柔滑的半透明态
            using Mat blurMask = new Mat();
            Cv2.GaussianBlur(dilatedMask, blurMask, new OpenCvSharp.Size(5, 5), 0);

            // 转为 0.0~1.0 的浮点权重图以供随后的"图层相乘融合"使用
            Mat alpha = new Mat();
            blurMask.ConvertTo(alpha, MatType.CV_32FC1, 1.0 / 255.0);

            Mat alpha3 = new Mat();
            Cv2.CvtColor(alpha, alpha3, ColorConversionCodes.GRAY2BGR);

            img.Dispose();
            mask.Dispose();
            alpha.Dispose();

            // 返回最纯净的通用人像轮廓透明蒙版
            return alpha3;
        }

        /// <summary>
        /// 核心算法 2：矩阵运算极速贴合新纯色画板
        /// </summary>
        private Mat BlendBackground(Mat src, Mat alpha3, Scalar targetBgr)
        {
            using Mat srcFloat = new Mat();
            src.ConvertTo(srcFloat, MatType.CV_32FC3);

            using Mat bgFloat = new Mat(src.Size(), MatType.CV_32FC3, targetBgr);

            // 前景权重 = (1.0 - alpha图透明度)
            using Mat inverseAlpha = new Mat();
            Cv2.Subtract(new Scalar(1.0, 1.0, 1.0), alpha3, inverseAlpha);

            // 生成被镂空的纯净前景
            using Mat foreground = new Mat();
            Cv2.Multiply(srcFloat, inverseAlpha, foreground);

            // 生成带人形空洞的目标纯色背景
            using Mat background = new Mat();
            Cv2.Multiply(bgFloat, alpha3, background);

            // 前景画与后景画完美叠加
            using Mat resultFloat = new Mat();
            Cv2.Add(foreground, background, resultFloat);

            Mat finalResult = new Mat();
            resultFloat.ConvertTo(finalResult, MatType.CV_8UC3);

            return finalResult;
        }

        // 切换不同图片前，需清空旧图相关的资源缓存！
        private void ReleaseMats()
        {
            _originalMat?.Dispose();
            _originalMat = null;

            _alphaMask?.Dispose();
            _alphaMask = null;

            _resultMat?.Dispose();
            _resultMat = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            ReleaseMats();
            base.OnClosed(e);
        }
    }
}

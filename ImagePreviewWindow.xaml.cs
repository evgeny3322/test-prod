using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AIInterviewAssistant.WPF
{
    /// <summary>
    /// Interaction logic for ImagePreviewWindow.xaml
    /// </summary>
    public partial class ImagePreviewWindow : Window
    {
        public bool SendRequested { get; private set; }
        private readonly string _imagePath;

        public ImagePreviewWindow(string imagePath)
        {
            InitializeComponent();
            
            _imagePath = imagePath;
            SendRequested = false;

            // Load the image
            try
            {
                if (File.Exists(imagePath))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.EndInit();
                    
                    PreviewImage.Source = bitmap;
                    
                    // Display image info
                    var fileInfo = new FileInfo(imagePath);
                    ImageInfoTextBlock.Text = 
                        $"File: {Path.GetFileName(imagePath)}\n" +
                        $"Size: {fileInfo.Length / 1024} KB\n" +
                        $"Dimensions: {bitmap.PixelWidth}x{bitmap.PixelHeight} pixels";
                    
                    // Set initial question
                    QuestionTextBox.Text = "Что изображено на этом фото? Опиши подробно.";
                }
                else
                {
                    MessageBox.Show(
                        $"Файл не найден: {imagePath}",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка при загрузке изображения: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Close();
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendRequested = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SendRequested = false;
            Close();
        }
    }
}
using AIInterviewAssistant.WPF.Services.Interfaces;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace AIInterviewAssistant.WPF.Services
{
    public class ScreenshotService : IScreenshotService
    {
        // Maximum dimensions for screenshots to prevent oversized uploads
        private const int MaxWidth = 1920;
        private const int MaxHeight = 1080;
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB limit

        public Bitmap CaptureScreen()
        {
            try
            {
                // Determine the bounds of all screens
                Rectangle bounds = new Rectangle(0, 0, 0, 0);
                foreach (Screen screen in Screen.AllScreens)
                {
                    bounds = Rectangle.Union(bounds, screen.Bounds);
                }

                Debug.WriteLine($"[DEBUG] Capturing screenshot with dimensions: {bounds.Width}x{bounds.Height}");

                // Create the screenshot
                Bitmap screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(screenshot))
                {
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                // Resize the image if it's too large
                if (screenshot.Width > MaxWidth || screenshot.Height > MaxHeight)
                {
                    return ResizeImage(screenshot, MaxWidth, MaxHeight);
                }

                return screenshot;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to capture screenshot: {ex.Message}");
                throw new Exception($"Failed to capture screenshot: {ex.Message}", ex);
            }
        }

        public string SaveScreenshot(Bitmap bitmap, string? filePath = null)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            try
            {
                // If path is not specified, create path in the screenshots directory
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    string screenshotsDir = GetScreenshotsDirectory();
                    filePath = Path.Combine(screenshotsDir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                }
                else
                {
                    // Ensure file extension is .png
                    string extension = Path.GetExtension(filePath);
                    if (string.IsNullOrEmpty(extension) || !extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        filePath = Path.ChangeExtension(filePath, ".png");
                    }
                }

                Debug.WriteLine($"[DEBUG] Saving screenshot to: {filePath}");

                // Create directory if it doesn't exist
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Save the image as PNG for better quality
                bitmap.Save(filePath, ImageFormat.Png);

                // Check if the file size is too large
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    Debug.WriteLine($"[DEBUG] File size too large: {fileInfo.Length} bytes. Reducing quality...");
                    
                    // If PNG is too large, try saving with JPEG compression
                    string jpegPath = Path.ChangeExtension(filePath, ".jpg");
                    using (EncoderParameters encoderParameters = new EncoderParameters(1))
                    {
                        // Start with high quality and reduce until file size is acceptable
                        long quality = 90;
                        bool fileSizeOk = false;
                        
                        while (!fileSizeOk && quality >= 60)
                        {
                            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                            ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                            
                            // Delete previous attempt if it exists
                            if (File.Exists(jpegPath))
                                File.Delete(jpegPath);
                                
                            bitmap.Save(jpegPath, jpegEncoder, encoderParameters);
                            
                            FileInfo jpegInfo = new FileInfo(jpegPath);
                            fileSizeOk = jpegInfo.Length <= MaxFileSizeBytes;
                            
                            if (!fileSizeOk)
                            {
                                quality -= 10;
                                Debug.WriteLine($"[DEBUG] Reducing JPEG quality to {quality}, current size: {jpegInfo.Length} bytes");
                            }
                        }
                        
                        // If even the lowest quality is too large, resize the image
                        if (!fileSizeOk)
                        {
                            Debug.WriteLine("[DEBUG] Even with compression, file is too large. Resizing and trying again...");
                            
                            // Delete the PNG and JPEG files
                            File.Delete(filePath);
                            File.Delete(jpegPath);
                            
                            // Resize the image by 50%
                            using (Bitmap resized = ResizeImage(bitmap, bitmap.Width / 2, bitmap.Height / 2))
                            {
                                // Try saving with medium quality
                                encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 75L);
                                ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                                resized.Save(jpegPath, jpegEncoder, encoderParameters);
                                
                                // If successful, use the JPEG path
                                filePath = jpegPath;
                            }
                        }
                        else
                        {
                            // Use the JPEG path if it's successful
                            filePath = jpegPath;
                        }
                    }
                }

                Debug.WriteLine($"[DEBUG] Screenshot saved successfully at: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to save screenshot: {ex.Message}");
                throw new Exception($"Failed to save screenshot: {ex.Message}", ex);
            }
        }

        public string CaptureAndSaveScreen(string? filePath = null)
        {
            try
            {
                using (Bitmap screenshot = CaptureScreen())
                {
                    return SaveScreenshot(screenshot, filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to capture and save screenshot: {ex.Message}");
                throw new Exception($"Failed to capture and save screenshot: {ex.Message}", ex);
            }
        }
        
        public async Task<bool> CaptureScreenshotAsync(string filePath)
        {
            try
            {
                Debug.WriteLine($"[DEBUG] Starting async screenshot capture to: {filePath}");
                
                using (Bitmap screenshot = CaptureScreen())
                {
                    // Perform the save operation in a separate thread
                    string savedPath = await Task.Run(() => SaveScreenshot(screenshot, filePath));
                    
                    // Verify the file exists
                    bool fileExists = File.Exists(savedPath);
                    Debug.WriteLine($"[DEBUG] Screenshot saved to: {savedPath}, file exists: {fileExists}");
                    
                    if (fileExists)
                    {
                        // Check if we saved to a different path than requested (e.g., due to format change)
                        if (!string.Equals(savedPath, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"[DEBUG] Note: Final path differs from requested path due to format conversion");
                            // Copy the file to the original path if needed
                            if (File.Exists(filePath))
                                File.Delete(filePath);
                                
                            File.Copy(savedPath, filePath);
                        }
                        
                        // Log file details
                        var fileInfo = new FileInfo(filePath);
                        Debug.WriteLine($"[DEBUG] Final screenshot file size: {fileInfo.Length} bytes");
                    }
                    
                    return fileExists;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to capture screenshot asynchronously: {ex.Message}");
                Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                return false;
            }
        }
        
        public string GetScreenshotsDirectory()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string screenshotsDir = Path.Combine(appDataPath, "AIInterviewAssistant", "Screenshots");
            
            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
                Debug.WriteLine($"[DEBUG] Created screenshots directory: {screenshotsDir}");
            }
            
            return screenshotsDir;
        }

        // Helper method to resize an image
        private Bitmap ResizeImage(Bitmap image, int maxWidth, int maxHeight)
        {
            // Calculate new dimensions while maintaining aspect ratio
            double ratioX = (double)maxWidth / image.Width;
            double ratioY = (double)maxHeight / image.Height;
            double ratio = Math.Min(ratioX, ratioY);
            
            int newWidth = (int)(image.Width * ratio);
            int newHeight = (int)(image.Height * ratio);
            
            Debug.WriteLine($"[DEBUG] Resizing image from {image.Width}x{image.Height} to {newWidth}x{newHeight}");
            
            Bitmap newImage = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
            
            using (Graphics graphics = Graphics.FromImage(newImage))
            {
                // Set high quality resizing
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                
                graphics.DrawImage(image, 0, 0, newWidth, newHeight);
            }
            
            return newImage;
        }

        // Helper method to get image codec
        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            
            throw new ArgumentException($"Codec for {format} not found", nameof(format));
        }
    }
}
using System.Drawing;
using System.Threading.Tasks;

namespace AIInterviewAssistant.WPF.Services.Interfaces
{
    public interface IScreenshotService
    {
        /// <summary>
        /// Создает скриншот всего экрана
        /// </summary>
        /// <returns>Объект изображения</returns>
        Bitmap CaptureScreen();
        
        /// <summary>
        /// Сохраняет скриншот в файл
        /// </summary>
        /// <param name="bitmap">Изображение для сохранения</param>
        /// <param name="filePath">Путь для сохранения файла (опционально)</param>
        /// <returns>Путь к сохраненному файлу</returns>
        string SaveScreenshot(Bitmap bitmap, string? filePath = null);
        
        /// <summary>
        /// Создает и сохраняет скриншот в одной операции
        /// </summary>
        /// <param name="filePath">Путь для сохранения файла (опционально)</param>
        /// <returns>Путь к сохраненному файлу</returns>
        string CaptureAndSaveScreen(string? filePath = null);
        
        /// <summary>
        /// Асинхронно создает и сохраняет скриншот
        /// </summary>
        /// <param name="filePath">Путь для сохранения файла</param>
        /// <returns>True если операция выполнена успешно, иначе False</returns>
        Task<bool> CaptureScreenshotAsync(string filePath);
        
        /// <summary>
        /// Возвращает директорию для хранения скриншотов
        /// </summary>
        /// <returns>Путь к директории скриншотов</returns>
        string GetScreenshotsDirectory();
    }
}
using System.Threading.Tasks;

namespace AIInterviewAssistant.WPF.Services.Interfaces
{
    public interface IAIService
    {
        /// <summary>
        /// Выполняет аутентификацию в сервисе AI
        /// </summary>
        /// <returns>True если аутентификация успешна, иначе False</returns>
        Task<bool> AuthAsync();

        /// <summary>
        /// Отправляет вопрос в сервис AI и получает ответ
        /// </summary>
        /// <param name="question">Текст вопроса</param>
        /// <returns>Текст ответа от AI</returns>
        Task<string> SendQuestionAsync(string question);

        /// <summary>
        /// Загружает файл в сервис AI
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <returns>Идентификатор загруженного файла или null в случае ошибки</returns>
        Task<string?> UploadFileAsync(string filePath);

        /// <summary>
        /// Отправляет вопрос в сервис AI с приложенным файлом и получает ответ
        /// </summary>
        /// <param name="question">Текст вопроса</param>
        /// <param name="fileId">Идентификатор ранее загруженного файла</param>
        /// <returns>Текст ответа от AI</returns>
        Task<string> SendQuestionWithFileAsync(string question, string fileId);

        // Добавьте это в Services/Interface/IAIService.cs
        /// <summary>
        /// Отправляет скриншот напрямую в AI и получает ответ
        /// </summary>
        /// <param name="filePath">Путь к файлу скриншота</param>
        /// <param name="question">Вопрос или инструкция для AI (опционально)</param>
        /// <returns>Текст ответа от AI</returns>
        Task<string> SendScreenshotDirectlyAsync(string filePath, string question = "");
    }
}

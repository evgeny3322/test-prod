using System;
using System.Threading.Tasks;

namespace AIInterviewAssistant.WPF.Services.Interfaces
{
    public interface IRecognizeService : IDisposable
    {
        /// <summary>
        /// Асинхронно загружает модель распознавания речи из указанного пути
        /// </summary>
        /// <param name="modelPath">Путь к директории с моделью Vosk</param>
        /// <exception cref="System.ArgumentException">Возникает, если путь пустой или null</exception>
        /// <exception cref="System.IO.DirectoryNotFoundException">Возникает, если директория не существует</exception>
        /// <exception cref="System.Exception">Возникает при ошибке загрузки модели</exception>
        Task LoadModelAsync(string modelPath);
        
        /// <summary>
        /// Асинхронно распознает речь из аудиофайла
        /// </summary>
        /// <param name="filePath">Путь к аудиофайлу WAV</param>
        /// <returns>Строка JSON с результатом распознавания</returns>
        /// <exception cref="System.InvalidOperationException">Возникает, если модель не загружена</exception>
        /// <exception cref="System.IO.FileNotFoundException">Возникает, если файл не найден</exception>
        /// <exception cref="System.ObjectDisposedException">Возникает, если сервис уже освобожден</exception>
        Task<string> RecognizeSpeechAsync(string filePath);
    }
}
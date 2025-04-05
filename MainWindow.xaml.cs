using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AIInterviewAssistant.WPF.Models;
using AIInterviewAssistant.WPF.Services;
using AIInterviewAssistant.WPF.Services.Interfaces;
using NAudio.Wave;
using SharpHook;
using SharpHook.Native;
// Use aliases to disambiguate
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfDragEventArgs = System.Windows.DragEventArgs; // Add this alias
using WinFormsDragEventArgs = System.Windows.Forms.DragEventArgs; // Add this alias
namespace AIInterviewAssistant.WPF
{
    public partial class MainWindow : Window
    {
        private readonly IRecognizeService _recognizeService;
        private readonly IAIService _aiService;
        private readonly IScreenshotService _screenshotService;
        private IWaveIn? _micCapture;
        private WasapiLoopbackCapture? _desktopCapture;
        private WaveFileWriter? _desktopAudioWriter;
        private WaveFileWriter? _micAudioWriter;
        private bool _inProgress;
        private bool _modelLoaded;
private bool _isLeftAltPressed = false;
private bool _isRightAltPressed = false;
        // Add these fields to MainWindow class
        private string _currentSolution = string.Empty;
        private string _currentExplanation = string.Empty;
        private string _alternativeSolution = string.Empty;
        private bool _isWaitingForScreenshot = false;
        private DateTime _lastScreenshotTime = DateTime.MinValue;
        private readonly int _cooldownPeriodMs = 1000; // 1 second cooldown between screenshots

        // Переменные для автоматических скриншотов
        private DispatcherTimer _autoScreenshotTimer;
        private bool _autoScreenshotEnabled = false;
        private string _lastScreenshotFilePath = string.Empty;
        private CancellationTokenSource _autoScreenshotCancellation;

        // Добавляем ссылку на глобальный хук для корректного освобождения
        private TaskPoolGlobalHook? _globalHook;

        public MainWindow()
        {
            InitializeComponent();
this.AllowDrop = true;
this.Drop += MainWindow_Drop;
this.DragEnter += MainWindow_DragEnter;
            // Инициализация сервисов
            _recognizeService = new RecognizeService();
            _aiService = new GigaChatService();
            _screenshotService = new ScreenshotService();
 _autoScreenshotTimer = new DispatcherTimer();
    _autoScreenshotCancellation = new CancellationTokenSource();
            // Настройка глобальных хоткеев
            _globalHook = new TaskPoolGlobalHook();
            _globalHook.KeyPressed += OnKeyPressed;
            _globalHook.KeyReleased += OnKeyReleased;
            _globalHook.RunAsync();

            // Инициализация состояния UI
            LoadButton.IsEnabled = true;
            SendManuallyButton.IsEnabled = false;
            TakeScreenshotButton.IsEnabled = false;
            SendScreenshotButton.IsEnabled = false;
            RunDiagnosticsButton.IsEnabled = false;
            StatusLabel.Content = "Ready";

            // Добавляем обработчик закрытия окна
            this.Closing += MainWindow_Closing;

            // Инициализация всплывающих подсказок
            InitializeTooltips();
        }

        // Метод инициализации подсказок - перемещен на уровень класса (вне конструктора)
        private void InitializeTooltips()
        {
            // Обновляем всплывающие подсказки для кнопок
            TakeScreenshotButton.ToolTip =
                "Сделать скриншот экрана (также можно использовать клавишу Print Screen)";
            SendScreenshotButton.ToolTip = "Отправить последний скриншот в AI для анализа";
            RunDiagnosticsButton.ToolTip = "Запустить диагностику проблем со скриншотами";

            // Обновляем текст кнопок для большей ясности
            SendScreenshotButton.Content = "Отправить скриншот в AI";
        }

private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Освободить все аудио ресурсы
            StopAndDisposeAudio();

            // Остановить глобальный хук
            if (_globalHook != null)
            {
                try
                {
                    _globalHook.KeyPressed -= OnKeyPressed;
                    _globalHook.KeyReleased -= OnKeyReleased;
                    _globalHook.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка при освобождении хука: {ex.Message}");
                }
            }

            // Освободить другие ресурсы
            _recognizeService?.Dispose();

            // Явно вызываем сборщик мусора
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void StopAndDisposeAudio()
        {
            try
            {
                // Останавливаем и освобождаем захват микрофона
                if (_micCapture != null)
                {
                    // Нам не нужно проверять RecordingState, просто пытаемся остановить запись
                    _micCapture.StopRecording();
                    _micCapture.Dispose();
                    _micCapture = null;
                }

                // Останавливаем и освобождаем захват звука с рабочего стола
                if (_desktopCapture != null)
                {
                    // Нам не нужно проверять RecordingState, просто пытаемся остановить запись
                    _desktopCapture.StopRecording();
                    _desktopCapture.Dispose();
                    _desktopCapture = null;
                }

                // Закрываем все файлы аудиозаписи
                if (_micAudioWriter != null)
                {
                    _micAudioWriter.Dispose();
                    _micAudioWriter = null;
                }

                if (_desktopAudioWriter != null)
                {
                    _desktopAudioWriter.Dispose();
                    _desktopAudioWriter = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при освобождении аудио ресурсов: {ex.Message}");
            }
        }

        private void ForceCloseApplication()
        {
            try
            {
                // Сначала пытаемся закрыть приложение нормально
                WpfApplication.Current.Shutdown();
            }
            catch
            {
                // Если не удалось, принудительно завершаем процесс
                Process.GetCurrentProcess().Kill();
            }
        }
private void MainWindow_DragEnter(object sender, WpfDragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0)
        {
            string extension = Path.GetExtension(files[0]).ToLowerInvariant();
            if (new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }.Contains(extension))
            {
                e.Effects = DragDropEffects.Copy;
            }
        }
    }
    e.Handled = true;
}

private void MainWindow_Drop(object sender, WpfDragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0)
        {
            string filePath = files[0];
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            if (new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }.Contains(extension))
            {
                _selectedImagePath = filePath;
                SelectedImagePathText.Text = Path.GetFileName(_selectedImagePath);
                SendImageButton.IsEnabled = true;
                StatusLabel.Content = "Изображение загружено методом Drag & Drop";
                
                Debug.WriteLine($"[DEBUG] Изображение загружено через Drag & Drop: {_selectedImagePath}");
            }
            else
            {
                StatusLabel.Content = "Неподдерживаемый формат файла";
                WpfMessageBox.Show(
                    "Перетащенный файл имеет неподдерживаемый формат. Пожалуйста, используйте JPG, PNG, BMP или GIF.",
                    "Неподдерживаемый формат",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }
    }
}
        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
{
    // Track Alt key states
    if (e.Data.KeyCode == KeyCode.VcLeftAlt)
    {
        _isLeftAltPressed = true;
    }
    else if (e.Data.KeyCode == KeyCode.VcRightAlt)
    {
        _isRightAltPressed = true;
    }

    // If PrtSc is pressed and not in cooldown period
    if (e.Data.KeyCode == KeyCode.VcPrintScreen && _modelLoaded && !_inProgress)
    {
        // Check if we're within the cooldown period
        if ((DateTime.Now - _lastScreenshotTime).TotalMilliseconds < _cooldownPeriodMs)
        {
            Debug.WriteLine("[DEBUG] Screenshot request ignored - cooldown period active");
            return;
        }

        _lastScreenshotTime = DateTime.Now;

        Dispatcher.Invoke(async () =>
        {
            await CaptureAndProcessScreenshotAsync();
        });
    }
    // If Alt+1 is pressed, show solution
    else if (e.Data.KeyCode == KeyCode.Vc1 && IsAltPressed())
    {
        Dispatcher.Invoke(() =>
        {
            ShowSolution();
        });
    }
    // If Alt+2 is pressed, show explanation
    else if (e.Data.KeyCode == KeyCode.Vc2 && IsAltPressed())
    {
        Dispatcher.Invoke(() =>
        {
            ShowExplanation();
        });
    }
    // If Alt+3 is pressed, show alternative solution
    else if (e.Data.KeyCode == KeyCode.Vc3 && IsAltPressed())
    {
        Dispatcher.Invoke(() =>
        {
            ShowAlternativeSolution();
        });
    }
}
private bool IsAltPressed()
{
    // Return the tracked state of Alt keys
    return _isLeftAltPressed || _isRightAltPressed;
}
        private async Task CaptureAndProcessScreenshotAsync()
        {
            try
            {
                if (!_modelLoaded)
                {
                    Debug.WriteLine("[ERROR] Model not loaded - cannot process screenshot");
                    StatusLabel.Content = "Error: Model not loaded";
                    return;
                }

                StatusLabel.Content = "Capturing screenshot...";
                OperationProgressBar.Visibility = Visibility.Visible;
                _isWaitingForScreenshot = true;

                // Generate a filename with timestamp
                string screenshotsDir = _screenshotService.GetScreenshotsDirectory();
                string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string filePath = Path.Combine(screenshotsDir, fileName);

                Debug.WriteLine($"[DEBUG] Taking screenshot to: {filePath}");

                // Take screenshot
                bool success = await _screenshotService.CaptureScreenshotAsync(filePath);

                if (success && File.Exists(filePath))
                {
                    // Log file details
                    FileInfo fileInfo = new FileInfo(filePath);
                    Debug.WriteLine(
                        $"[DEBUG] Screenshot saved: {filePath}, size: {fileInfo.Length} bytes"
                    );
                    StatusLabel.Content = $"Processing screenshot ({fileInfo.Length / 1024} KB)...";

                    // Send to GigaChat for solution
                    string solutionPrompt =
                        "I'm in a coding interview. This screenshot shows a programming problem. "
                        + "Please solve this problem with clear, correct code. Provide ONLY the code solution, no explanations.";

                    string solution = await _aiService.SendScreenshotDirectlyAsync(
                        filePath,
                        solutionPrompt
                    );
                    _currentSolution = solution;

                    // Send to GigaChat for explanation (in background)
                    Task.Run(async () =>
                    {
                        string explanationPrompt =
                            "I'm in a coding interview. This screenshot shows a programming problem. "
                            + "Please explain how to solve this problem step by step, including the reasoning and approach.";

                        string explanation = await _aiService.SendScreenshotDirectlyAsync(
                            filePath,
                            explanationPrompt
                        );
                        _currentExplanation = explanation;

                        Dispatcher.Invoke(() =>
                        {
                            Debug.WriteLine("[DEBUG] Explanation processed successfully");
                        });
                    });

                    // Send to GigaChat for alternative solution (in background)
                    Task.Run(async () =>
                    {
                        string alternativePrompt =
                            "I'm in a coding interview. This screenshot shows a programming problem. "
                            + "Please provide an alternative solution or optimization approach to this problem. Focus on efficient implementation.";

                        string alternative = await _aiService.SendScreenshotDirectlyAsync(
                            filePath,
                            alternativePrompt
                        );
                        _alternativeSolution = alternative;

                        Dispatcher.Invoke(() =>
                        {
                            Debug.WriteLine("[DEBUG] Alternative solution processed successfully");
                        });
                    });

                    // Update UI
                    Dispatcher.Invoke(() =>
                    {
                        StatusLabel.Content = "Solution ready! Press Alt+1 to view";
                        OperationProgressBar.Visibility = Visibility.Collapsed;
                        _isWaitingForScreenshot = false;
                    });
                }
                else
                {
                    StatusLabel.Content = "Screenshot failed";
                    OperationProgressBar.Visibility = Visibility.Collapsed;
                    _isWaitingForScreenshot = false;
                    Debug.WriteLine("[ERROR] Screenshot capture failed or file doesn't exist");
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "Error during screenshot processing";
                OperationProgressBar.Visibility = Visibility.Collapsed;
                _isWaitingForScreenshot = false;
                Debug.WriteLine($"[ERROR] Screenshot processing exception: {ex.Message}");
                Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            }
        }

        // Add these methods to display the solutions
        private void ShowSolution()
        {
            if (string.IsNullOrEmpty(_currentSolution))
            {
                StatusLabel.Content = "No solution available yet";
                return;
            }

            // Display in helper window
            ShowSolutionInHelperWindow(
                _currentSolution,
                SolutionDisplayWindow.SolutionType.Solution
            );
            StatusLabel.Content = "Showing solution (Alt+2 for explanation)";
        }
// Добавьте это поле в класс MainWindow
private string _selectedImagePath = string.Empty;

// Добавьте эти методы в класс MainWindow
private void BrowseImageButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        // Создаем диалог выбора файла
        Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы|*.*",
            CheckFileExists = true
        };

        // Показываем диалог и получаем результат
        if (openFileDialog.ShowDialog() == true)
        {
            string selectedPath = openFileDialog.FileName;
            
            // Показываем окно предпросмотра
            var previewWindow = new ImagePreviewWindow(selectedPath);
            previewWindow.Owner = this;
            previewWindow.ShowDialog();
            
            // Если пользователь нажал "Отправить"
            if (previewWindow.SendRequested)
            {
                _selectedImagePath = selectedPath;
                
                // Обновляем UI
                SelectedImagePathText.Text = Path.GetFileName(_selectedImagePath);
                SendImageButton.IsEnabled = true;
                StatusLabel.Content = "Изображение выбрано";
                
                // Автоматически запускаем отправку
                SendImageButton_Click(sender, e);
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[ERROR] Ошибка при выборе файла: {ex.Message}");
        StatusLabel.Content = "Ошибка выбора файла";
        WpfMessageBox.Show(
            $"Ошибка при выборе файла: {ex.Message}",
            "Ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
    }
}

private async void SendImageButton_Click(object sender, RoutedEventArgs e)
{
    if (string.IsNullOrEmpty(_selectedImagePath) || !File.Exists(_selectedImagePath))
    {
        StatusLabel.Content = "Файл не выбран или не существует";
        return;
    }

    try
    {
        // Проверяем файл на размер и формат
        FileInfo fileInfo = new FileInfo(_selectedImagePath);
        Debug.WriteLine($"[DEBUG] Отправка изображения: {_selectedImagePath}, размер: {fileInfo.Length / 1024} KB");
        
        // Проверка размера файла (10 МБ макс)
        if (fileInfo.Length > 10 * 1024 * 1024)
        {
            StatusLabel.Content = "Файл слишком большой";
            WpfMessageBox.Show(
                "Размер файла превышает 10 МБ. Пожалуйста, выберите файл меньшего размера или сожмите его.",
                "Файл слишком большой",
                MessageBoxButton.OK, 
                MessageBoxImage.Warning
            );
            return;
        }

        // Проверка формата файла
        string extension = Path.GetExtension(_selectedImagePath).ToLowerInvariant();
        if (!new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }.Contains(extension))
        {
            StatusLabel.Content = "Неподдерживаемый формат";
            WpfMessageBox.Show(
                "Выбран неподдерживаемый формат файла. Пожалуйста, выберите JPG, PNG, BMP или GIF.",
                "Неподдерживаемый формат",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        // Отключаем кнопки на время обработки
        BrowseImageButton.IsEnabled = false;
        SendImageButton.IsEnabled = false;
        StatusLabel.Content = "Отправка изображения...";
        OperationProgressBar.Visibility = Visibility.Visible;

        // Отправляем изображение в AI
        string question = string.IsNullOrWhiteSpace(InputTextBox.Text)
            ? "Что изображено на этом фото? Опиши подробно."
            : InputTextBox.Text;

        // Копируем файл во временную директорию для обработки
        string tempImagePath = CopyImageToTempDirectory(_selectedImagePath);
        
        // Отправляем изображение и получаем ответ от AI
        string aiResponse = await _aiService.SendScreenshotDirectlyAsync(tempImagePath, question);

        // Обновляем UI с результатом
        OutputTextBox.Text = aiResponse;
        StatusLabel.Content = "Ответ получен";
        OperationProgressBar.Visibility = Visibility.Collapsed;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[ERROR] Ошибка при отправке изображения: {ex.Message}");
        Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
        StatusLabel.Content = "Ошибка отправки";
        OperationProgressBar.Visibility = Visibility.Collapsed;
        WpfMessageBox.Show(
            $"Ошибка при отправке изображения: {ex.Message}",
            "Ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
    }
    finally
    {
        // Восстанавливаем состояние кнопок
        BrowseImageButton.IsEnabled = true;
        SendImageButton.IsEnabled = !string.IsNullOrEmpty(_selectedImagePath) && File.Exists(_selectedImagePath);
    }
}

// Вспомогательный метод для копирования изображения во временную директорию
private string CopyImageToTempDirectory(string sourcePath)
{
    try
    {
        // Создаем временную директорию, если она не существует
        string tempDir = Path.Combine(Path.GetTempPath(), "AIInterviewAssistant");
        if (!Directory.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }

        // Создаем имя для копии файла
        string fileName = $"image_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(sourcePath)}";
        string destPath = Path.Combine(tempDir, fileName);

        // Копируем файл
        File.Copy(sourcePath, destPath, true);
        Debug.WriteLine($"[DEBUG] Изображение скопировано во временную директорию: {destPath}");

        return destPath;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[ERROR] Ошибка при копировании изображения: {ex.Message}");
        // В случае ошибки, возвращаем исходный путь
        return sourcePath;
    }
}
        private void ShowExplanation()
        {
            if (string.IsNullOrEmpty(_currentExplanation))
            {
                StatusLabel.Content = "Explanation not ready yet";
                return;
            }

            // Display in helper window
            ShowSolutionInHelperWindow(
                _currentExplanation,
                SolutionDisplayWindow.SolutionType.Explanation
            );
            StatusLabel.Content = "Showing explanation";
        }

        private void ShowAlternativeSolution()
        {
            if (string.IsNullOrEmpty(_alternativeSolution))
            {
                StatusLabel.Content = "Alternative solution not ready yet";
                return;
            }

            // Display in helper window
            ShowSolutionInHelperWindow(
                _alternativeSolution,
                SolutionDisplayWindow.SolutionType.Alternative
            );
            StatusLabel.Content = "Showing alternative solution";
        }

        // Add hotkey configuration method
        private void ConfigureHotkeysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Simple configuration dialog - in a real app, this would be more comprehensive
                string message =
                    "To change hotkeys, edit the following in App.xaml.cs:\n\n"
                    + "SolutionHotkey = \"Alt+1\"\n"
                    + "ExplanationHotkey = \"Alt+2\"\n"
                    + "AlternativeHotkey = \"Alt+3\"\n\n"
                    + "The screenshot capture key (PrtSc) is system-defined and cannot be changed.";

                WpfMessageBox.Show(
                    message,
                    "Hotkey Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Error in hotkey configuration: {ex.Message}");
                WpfMessageBox.Show(
                    $"Error configuring hotkeys: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
{
    // Track Alt key releases
    if (e.Data.KeyCode == KeyCode.VcLeftAlt)
    {
        _isLeftAltPressed = false;
    }
    else if (e.Data.KeyCode == KeyCode.VcRightAlt)
    {
        _isRightAltPressed = false;
    }

    // Если отпущена клавиша F2 и выполняется запись
    if (e.Data.KeyCode == KeyCode.VcF2 && _inProgress)
    {
        Dispatcher.Invoke(() =>
        {
            StopRecording();
        });
    }
}


        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (
                string.IsNullOrWhiteSpace(PositionTextBox.Text)
                || string.IsNullOrWhiteSpace(ModelPathTextBox.Text)
            )
            {
                WpfMessageBox.Show(
                    "Please fill in both Position and ModelPath fields.",
                    "Input required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                // Disable UI during loading
                LoadButton.IsEnabled = false;
                StatusLabel.Content = "Loading model...";

                // Load the speech recognition model asynchronously
                await _recognizeService.LoadModelAsync(ModelPathTextBox.Text);

                // Set initial prompt with specified position
                string initialPrompt = string.Format(
                    WpfApplication.Current.Properties["InitialPromptTemplate"] as string
                        ?? "You are a professional {0}.",
                    PositionTextBox.Text
                );

                // Send initial prompt to AI
                StatusLabel.Content = "Initializing AI...";
                string aiResponse = await _aiService.SendQuestionAsync(initialPrompt);

                if (!string.IsNullOrEmpty(aiResponse))
                {
                    OutputTextBox.Text = aiResponse;
                    _modelLoaded = true;
                    SendManuallyButton.IsEnabled = true;
                    TakeScreenshotButton.IsEnabled = true;
                    RunDiagnosticsButton.IsEnabled = true;
                    StatusLabel.Content = "Ready";
                }
                else
                {
                    StatusLabel.Content = "AI initialization failed";
                    WpfMessageBox.Show(
                        "Failed to initialize AI with the specified position.",
                        "AI Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "Error";
                WpfMessageBox.Show(
                    $"Error: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                LoadButton.IsEnabled = true;
            }
        }

        private async void SendManuallyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_modelLoaded)
            {
                WpfMessageBox.Show(
                    "Model is not loaded. Please load the model first.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                return;
            }

            if (string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                WpfMessageBox.Show(
                    "Please enter a question.",
                    "Input required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                // Disable UI during processing
                SendManuallyButton.IsEnabled = false;
                StatusLabel.Content = "Sending question...";

                // Send question to AI
                string aiResponse = await _aiService.SendQuestionAsync(InputTextBox.Text);

                if (!string.IsNullOrEmpty(aiResponse))
                {
                    OutputTextBox.Text = aiResponse;
                    StatusLabel.Content = "Ready";
                }
                else
                {
                    StatusLabel.Content = "AI response failed";
                    WpfMessageBox.Show(
                        "Failed to get response from AI.",
                        "AI Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "Error";
                WpfMessageBox.Show(
                    $"Error: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                SendManuallyButton.IsEnabled = true;
            }
        }

        private void StartRecording()
        {
            if (_inProgress || !_modelLoaded)
                return;

            try
            {
                _inProgress = true;
                StatusLabel.Content = "Recording...";

                // Создаем временные WAV файлы для записи
                string tempDir = Path.GetTempPath();
                string micWavFile = Path.Combine(
                    tempDir,
                    $"mic_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
                );
                string desktopWavFile = Path.Combine(
                    tempDir,
                    $"desktop_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
                );

                // Настройка записи с микрофона
                _micCapture = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 1), // 16kHz, Mono - формат для Vosk
                };

                _micCapture.DataAvailable += (s, args) =>
                {
                    _micAudioWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                };

                _micAudioWriter = new WaveFileWriter(micWavFile, _micCapture.WaveFormat);

                // Настройка записи с рабочего стола (системные звуки)
                _desktopCapture = new WasapiLoopbackCapture();
                _desktopCapture.DataAvailable += (s, args) =>
                {
                    _desktopAudioWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                };

                _desktopAudioWriter = new WaveFileWriter(
                    desktopWavFile,
                    _desktopCapture.WaveFormat
                );

                // Начинаем запись
                _micCapture.StartRecording();
                _desktopCapture.StartRecording();

                // Установка таймера для автоматической остановки записи через заданный интервал
                int maxSeconds = (int)(
                    WpfApplication.Current.Properties["MaximumRecordLengthInSeconds"] ?? 20
                );
                Task.Delay(TimeSpan.FromSeconds(maxSeconds))
                    .ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_inProgress)
                            {
                                StopRecording();
                            }
                        });
                    });
            }
            catch (Exception ex)
            {
                _inProgress = false;
                StatusLabel.Content = "Recording error";
                WpfMessageBox.Show(
                    $"Error starting recording: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }yyyyMMdd_HHmmss}.wav"
                );
                string desktopWavFile = Path.Combine(
                    tempDir,
                    $"desktop_{DateTime.Now:

        private void StartRecording()
        {
            if (_inProgress || !_modelLoaded)
                return;

            try
            {
                _inProgress = true;
                StatusLabel.Content = "Recording...";

                // Создаем временные WAV файлы для записи
                string tempDir = Path.GetTempPath();
                string micWavFile = Path.Combine(
                    tempDir,
                    $"mic_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
                );
                string desktopWavFile = Path.Combine(
                    tempDir,
                    $"desktop_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
                );

                // Настройка записи с микрофона
                _micCapture = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 1), // 16kHz, Mono - формат для Vosk
                };

                _micCapture.DataAvailable += (s, args) =>
                {
                    _micAudioWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                };

                _micAudioWriter = new WaveFileWriter(micWavFile, _micCapture.WaveFormat);

                // Настройка записи с рабочего стола (системные звуки)
                _desktopCapture = new WasapiLoopbackCapture();
                _desktopCapture.DataAvailable += (s, args) =>
                {
                    _desktopAudioWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                };

                _desktopAudioWriter = new WaveFileWriter(
                    desktopWavFile,
                    _desktopCapture.WaveFormat
                );

                // Начинаем запись
                _micCapture.StartRecording();
                _desktopCapture.StartRecording();

                // Установка таймера для автоматической остановки записи через заданный интервал
                int maxSeconds = (int)(
                    WpfApplication.Current.Properties["MaximumRecordLengthInSeconds"] ?? 20
                );
                Task.Delay(TimeSpan.FromSeconds(maxSeconds))
                    .ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_inProgress)
                            {
                                StopRecording();
                            }
                        });
                    });
            }
            catch (Exception ex)
            {
                _inProgress = false;
                StatusLabel.Content = "Recording error";
                WpfMessageBox.Show(
                    $"Error starting recording: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void StopRecording()
        {
            if (!_inProgress)
                return;

            try
            {
                StatusLabel.Content = "Processing...";

                // Останавливаем запись
                _micCapture?.StopRecording();
                _desktopCapture?.StopRecording();

                // Закрываем файлы
                string micWavFile = string.Empty;
                if (_micAudioWriter != null)
                {
                    micWavFile = _micAudioWriter.Filename;
                    _micAudioWriter.Dispose();
                    _micAudioWriter = null;
                }

                if (_desktopAudioWriter != null)
                {
                    _desktopAudioWriter.Dispose();
                    _desktopAudioWriter = null;
                }

                _micCapture?.Dispose();
                _micCapture = null;

                _desktopCapture?.Dispose();
                _desktopCapture = null;

                _inProgress = false;

                // Проверяем, что файл записи микрофона существует
                if (string.IsNullOrEmpty(micWavFile) || !File.Exists(micWavFile))
                {
                    StatusLabel.Content = "Recording failed";
                    WpfMessageBox.Show(
                        "Recording failed: No audio file created.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // Распознаем речь
                StatusLabel.Content = "Recognizing speech...";
                string recognitionResult = await _recognizeService.RecognizeSpeechAsync(micWavFile);

                // Парсим результат
                RecognizeSpeechDto? speechDto = null;
                try
                {
                    speechDto = JsonSerializer.Deserialize<RecognizeSpeechDto>(recognitionResult);
                }
                catch (JsonException)
                {
                    StatusLabel.Content = "Recognition parsing failed";
                    WpfMessageBox.Show(
                        "Failed to parse recognition result.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                if (speechDto == null || string.IsNullOrWhiteSpace(speechDto.Text))
                {
                    StatusLabel.Content = "No speech detected";
                    WpfMessageBox.Show(
                        "No speech was detected in the recording.",
                        "Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                // Отображаем распознанный текст
                InputTextBox.Text = speechDto.Text;

                // Отправляем запрос в AI
                StatusLabel.Content = "Sending to AI...";
                string aiResponse = await _aiService.SendQuestionAsync(speechDto.Text);

                if (!string.IsNullOrEmpty(aiResponse))
                {
                    OutputTextBox.Text = aiResponse;
                    StatusLabel.Content = "Ready";
                }
                else
                {
                    StatusLabel.Content = "AI response failed";
                    WpfMessageBox.Show(
                        "Failed to get response from AI.",
                        "AI Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }

                // Удаляем временные файлы
                try
                {
                    if (File.Exists(micWavFile))
                        File.Delete(micWavFile);
                }
                catch
                { /* Игнорируем ошибки при удалении временных файлов */
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "Error";
                WpfMessageBox.Show(
                    $"Error processing recording: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void TakeScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button during processing
                TakeScreenshotButton.IsEnabled = false;
                StatusLabel.Content = "Taking screenshot...";

                // Generate filename with timestamp
                string screenshotsDir = _screenshotService.GetScreenshotsDirectory();
                string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string filePath = Path.Combine(screenshotsDir, fileName);

                Debug.WriteLine($"[DEBUG] Taking screenshot to: {filePath}");

                // Take screenshot
                bool success = await _screenshotService.CaptureScreenshotAsync(filePath);

                if (success && File.Exists(filePath))
                {
                    _lastScreenshotFilePath = filePath;
                    SendScreenshotButton.IsEnabled = true;

                    // Log file details
                    FileInfo fileInfo = new FileInfo(filePath);
                    Debug.WriteLine(
                        $"[DEBUG] Screenshot saved: {filePath}, size: {fileInfo.Length} bytes"
                    );
                    StatusLabel.Content = $"Screenshot saved ({fileInfo.Length / 1024} KB)";
                }
                else
                {
                    StatusLabel.Content = "Screenshot failed";
                    Debug.WriteLine("[ERROR] Screenshot capture failed or file doesn't exist");
                    WpfMessageBox.Show(
                        "Failed to capture screenshot. Please try again.",
                        "Screenshot Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "Error";
                Debug.WriteLine($"[ERROR] Screenshot capture exception: {ex.Message}");
                Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                WpfMessageBox.Show(
                    $"Error taking screenshot: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                TakeScreenshotButton.IsEnabled = true;
            }
        }

        private async void SendScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastScreenshotFilePath))
            {
                Debug.WriteLine("[ERROR] No screenshot path available");
                WpfMessageBox.Show(
                    "No screenshot available. Please take a screenshot first.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            if (!File.Exists(_lastScreenshotFilePath))
            {
                Debug.WriteLine($"[ERROR] Screenshot file not found: {_lastScreenshotFilePath}");
                WpfMessageBox.Show(
                    $"Screenshot file not found: {_lastScreenshotFilePath}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            // Log file details before sending
            try
            {
                FileInfo fileInfo = new FileInfo(_lastScreenshotFilePath);
                Debug.WriteLine($"[DEBUG] Sending screenshot: {_lastScreenshotFilePath}");
                Debug.WriteLine(
                    $"[DEBUG] File size: {fileInfo.Length} bytes, Last modified: {fileInfo.LastWriteTime}"
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Error getting file info: {ex.Message}");
            }

            try
            {
                // Disable button during processing
                SendScreenshotButton.IsEnabled = false;
                StatusLabel.Content = "Uploading screenshot...";

                // Verify the AI service is initialized
                if (_aiService == null)
                {
                    Debug.WriteLine("[ERROR] AI service is not initialized");
                    StatusLabel.Content = "AI service not initialized";
                    WpfMessageBox.Show(
                        "AI service is not initialized. Please restart the application.",
                        "Service Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // Check if the file is too large before sending
                var fileInfo = new FileInfo(_lastScreenshotFilePath);
                if (fileInfo.Length > 5 * 1024 * 1024) // 5MB is a common limit
                {
                    Debug.WriteLine(
                        $"[WARNING] File size is large: {fileInfo.Length} bytes, attempting to compress"
                    );

                    // Try to compress the image before sending
                    string compressedPath = Path.Combine(
                        Path.GetDirectoryName(_lastScreenshotFilePath) ?? "",
                        $"compressed_{Path.GetFileName(_lastScreenshotFilePath)}"
                    );

                    try
                    {
                        // Load the image
                        using (var originalImage = Image.FromFile(_lastScreenshotFilePath))
                        using (var bitmap = new Bitmap(originalImage))
                        {
                            // Save with compression
                            using (var encoderParams = new EncoderParameters(1))
                            {
                                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 70L);

                                // Get JPEG encoder
                                ImageCodecInfo jpegEncoder = null;
                                foreach (var encoder in ImageCodecInfo.GetImageEncoders())
                                {
                                    if (encoder.FormatID == ImageFormat.Jpeg.Guid)
                                    {
                                        jpegEncoder = encoder;
                                        break;
                                    }
                                }

                                if (jpegEncoder != null)
                                {
                                    // Save as JPEG with compression
                                    bitmap.Save(compressedPath, jpegEncoder, encoderParams);

                                    // Check if compression was successful
                                    var compressedInfo = new FileInfo(compressedPath);
                                    Debug.WriteLine(
                                        $"[DEBUG] Compressed image size: {compressedInfo.Length} bytes"
                                    );

                                    if (compressedInfo.Length < fileInfo.Length)
                                    {
                                        // Use the compressed file instead
                                        _lastScreenshotFilePath = compressedPath;
                                        Debug.WriteLine(
                                            $"[DEBUG] Using compressed image: {compressedPath}"
                                        );
                                    }
                                    else
                                    {
                                        // Compression didn't help, delete the file
                                        File.Delete(compressedPath);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception compressionEx)
                    {
                        Debug.WriteLine(
                            $"[ERROR] Failed to compress image: {compressionEx.Message}"
                        );
                        // Continue with original file if compression fails
                    }
                }

                // Upload screenshot to AI service
                Debug.WriteLine("[DEBUG] Starting file upload to AI service");
                string? fileId = await _aiService.UploadFileAsync(_lastScreenshotFilePath);
                Debug.WriteLine($"[DEBUG] Upload result - File ID: {fileId ?? "null"}");

                if (!string.IsNullOrEmpty(fileId))
                {
                    // Get input text or use default question
                    string question = string.IsNullOrWhiteSpace(InputTextBox.Text)
                        ? "What can you see in this screenshot? Describe the content."
                        : InputTextBox.Text;

                    StatusLabel.Content = "Sending question with screenshot...";
                    Debug.WriteLine($"[DEBUG] Sending question with file ID: {fileId}");
                    Debug.WriteLine($"[DEBUG] Question: {question}");

                    // Send question with file ID
                    string aiResponse = await _aiService.SendQuestionWithFileAsync(
                        question,
                        fileId
                    );

                    if (!string.IsNullOrEmpty(aiResponse))
                    {
                        OutputTextBox.Text = aiResponse;
                        StatusLabel.Content = "Ready";
                        Debug.WriteLine("[DEBUG] Received AI response successfully");
                    }
                    else
                    {
                        StatusLabel.Content = "AI response failed";
                        Debug.WriteLine("[ERROR] AI returned empty response");
                        WpfMessageBox.Show(
                            "Failed to get response from AI. The service might be experiencing issues.",
                            "AI Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
                else
                {
                    StatusLabel.Content = "Upload failed";
                    Debug.WriteLine("[ERROR] File upload failed - no file ID returned");
                    WpfMessageBox.Show(
                        "Failed to upload screenshot. Please check your internet connection and try again.",
                        "Upload Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "Error";
                Debug.WriteLine($"[ERROR] Send screenshot exception: {ex.Message}");
                Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                WpfMessageBox.Show(
                    $"Error sending screenshot: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                SendScreenshotButton.IsEnabled = true;
            }
        }

        private void ShowSolutionInHelperWindow(
            string content,
            SolutionDisplayWindow.SolutionType type
        )
        {
            // Check if helper window display is enabled
            bool displayHelperWindow = (bool)(
                WpfApplication.Current.Properties["DisplayHelperWindowOnHotkey"] ?? true
            );

            if (!displayHelperWindow)
            {
                // If disabled, show in the main window's output textbox instead
                OutputTextBox.Text = content;
                return;
            }

            // Create and configure new helper window
            var helperWindow = new SolutionDisplayWindow();

            // Set opacity from application settings
            double opacity = (double)(
                WpfApplication.Current.Properties["HelperWindowOpacity"] ?? 0.9
            );
            helperWindow.Opacity = opacity;

            // Set size from application settings
            double width = (double)(WpfApplication.Current.Properties["HelperWindowWidth"] ?? 400);
            double height = (double)(
                WpfApplication.Current.Properties["HelperWindowHeight"] ?? 300
            );
            helperWindow.Width = width;
            helperWindow.Height = height;

            // Set content and show window
            helperWindow.SetContent(content, type);
            helperWindow.Show();
        }

        private async void RunScreenshotDiagnostics()
        {
            Debug.WriteLine("[DIAGNOSTICS] Starting screenshot upload diagnostics");
            StatusLabel.Content = "Running diagnostics...";

            try
            {
                // 1. Test basic screenshot functionality
                Debug.WriteLine("[DIAGNOSTICS] Testing screenshot capture");
                string testFileName = $"test_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string testFilePath = Path.Combine(
                    _screenshotService.GetScreenshotsDirectory(),
                    testFileName
                );

                bool captureSuccess = await _screenshotService.CaptureScreenshotAsync(testFilePath);
                Debug.WriteLine($"[DIAGNOSTICS] Screenshot capture result: {captureSuccess}");

                if (!captureSuccess || !File.Exists(testFilePath))
                {
                    Debug.WriteLine("[DIAGNOSTICS] Basic screenshot capture failed");
                    StatusLabel.Content = "Screenshot capture failed";
                    return;
                }

                // 2. Check file properties
                var fileInfo = new FileInfo(testFilePath);
                Debug.WriteLine(
                    $"[DIAGNOSTICS] File info - Size: {fileInfo.Length} bytes, Created: {fileInfo.CreationTime}"
                );

                if (fileInfo.Length == 0)
                {
                    Debug.WriteLine("[DIAGNOSTICS] Screenshot file is empty");
                    StatusLabel.Content = "Screenshot file is empty";
                    return;
                }

                // 3. Test GigaChat authentication
                Debug.WriteLine("[DIAGNOSTICS] Testing GigaChat authentication");
                bool authResult = await _aiService.AuthAsync();
                Debug.WriteLine($"[DIAGNOSTICS] Authentication result: {authResult}");

                if (!authResult)
                {
                    Debug.WriteLine("[DIAGNOSTICS] GigaChat authentication failed");
                    StatusLabel.Content = "GigaChat auth failed";
                    return;
                }

                // 4. Test file upload
                Debug.WriteLine("[DIAGNOSTICS] Testing file upload");
                string? fileId = await _aiService.UploadFileAsync(testFilePath);
                Debug.WriteLine(
                    $"[DIAGNOSTICS] File upload result: {(fileId != null ? "Success" : "Failed")}"
                );

                if (string.IsNullOrEmpty(fileId))
                {
                    Debug.WriteLine("[DIAGNOSTICS] File upload failed - no file ID returned");
                    StatusLabel.Content = "File upload failed";
                    return;
                }

                // 5. Test sending a message with the file
                Debug.WriteLine("[DIAGNOSTICS] Testing sending message with file");
                string testQuestion = "What can you see in this diagnostic test image?";
                string response = await _aiService.SendQuestionWithFileAsync(testQuestion, fileId);

                if (string.IsNullOrEmpty(response))
                {
                    Debug.WriteLine("[DIAGNOSTICS] Failed to get response for file");
                    StatusLabel.Content = "API response failed";
                    return;
                }

                Debug.WriteLine("[DIAGNOSTICS] Successfully received response for file");
                Debug.WriteLine($"[DIAGNOSTICS] Response length: {response.Length} characters");

                // 6. Cleanup
                try
                {
                    File.Delete(testFilePath);
                    Debug.WriteLine("[DIAGNOSTICS] Cleaned up test file");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DIAGNOSTICS] Failed to delete test file: {ex.Message}");
                }

                // 7. All tests passed
                Debug.WriteLine("[DIAGNOSTICS] All diagnostic tests passed successfully");
                StatusLabel.Content = "Diagnostics: All tests passed";

                // Show success message
                WpfMessageBox.Show(
                    "All screenshot and API tests passed successfully.\n\nThe feature should now work correctly.",
                    "Diagnostics Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                // Log detailed error information
                Debug.WriteLine($"[DIAGNOSTICS] Exception during diagnostics: {ex.Message}");
                Debug.WriteLine($"[DIAGNOSTICS] Stack trace: {ex.StackTrace}");

                // Check for specific types of errors
                if (ex is UnauthorizedAccessException)
                {
                    Debug.WriteLine("[DIAGNOSTICS] Permission error - check folder access rights");
                    StatusLabel.Content = "Diagnostics: Permission error";
                }
                else if (ex is HttpRequestException)
                {
                    Debug.WriteLine("[DIAGNOSTICS] Network error - check internet connection");
                    StatusLabel.Content = "Diagnostics: Network error";
                }
                else if (ex is TaskCanceledException)
                {
                    Debug.WriteLine(
                        "[DIAGNOSTICS] Request timeout - server too slow or not responding"
                    );
                    StatusLabel.Content = "Diagnostics: Request timeout";
                }
                else if (ex is JsonException)
                {
                    Debug.WriteLine(
                        "[DIAGNOSTICS] JSON parsing error - unexpected API response format"
                    );
                    StatusLabel.Content = "Diagnostics: API format error";
                }
                else
                {
                    StatusLabel.Content = "Diagnostics: Error";
                }

                // Show error details to user
                WpfMessageBox.Show(
                    $"Diagnostic test failed with error:\n\n{ex.Message}\n\nSee debug log for more details.",
                    "Diagnostics Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void RunDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusLabel.Content = "Запуск диагностики...";
                RunDiagnosticsButton.IsEnabled = false;

                // Проверка настроек экрана
                string screenInfo = $"Количество экранов: {Screen.AllScreens.Length}\n";
                foreach (var screen in Screen.AllScreens)
                {
                    screenInfo +=
                        $"- Экран: {screen.DeviceName}, Разрешение: {screen.Bounds.Width}x{screen.Bounds.Height}\n";
                }

                // Тестирование создания скриншота
                string testFilePath = Path.Combine(
                    _screenshotService.GetScreenshotsDirectory(),
                    "test_screenshot.png"
                );
                bool captureSuccess = await _screenshotService.CaptureScreenshotAsync(testFilePath);

                // Проверка наличия директории для скриншотов
                string screenshotsDir = _screenshotService.GetScreenshotsDirectory();
                bool dirExists = Directory.Exists(screenshotsDir);

                // Сбор результатов диагностики
                string diagnosticResult =
                    $"=== Результаты диагностики ===\n"
                    + $"Время: {DateTime.Now}\n\n"
                    + $"Информация об экранах:\n{screenInfo}\n"
                    + $"Директория скриншотов: {screenshotsDir} (существует: {dirExists})\n"
                    + $"Тестовый скриншот: {(captureSuccess ? "создан успешно" : "не удалось создать")}\n";

                if (captureSuccess && File.Exists(testFilePath))
                {
                    var fileInfo = new FileInfo(testFilePath);
                    diagnosticResult +=
                        $"Размер тестового скриншота: {fileInfo.Length / 1024} KB\n";
                }

                // Вывод результатов
                OutputTextBox.Text = diagnosticResult;
                StatusLabel.Content = "Диагностика завершена";

                // Показываем сообщение пользователю
                WpfMessageBox.Show(
                    "Диагностика завершена. Результаты отображены в окне вывода.",
                    "Диагностика",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "Ошибка диагностики";
                WpfMessageBox.Show(
                    $"Ошибка при выполнении диагностики: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                RunDiagnosticsButton.IsEnabled = true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows; // Added this to resolve MessageBoxButton and MessageBoxImage
using AIInterviewAssistant.WPF.Services.Interfaces;
// Use aliases to disambiguate
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace AIInterviewAssistant.WPF.Services
{
    public class GigaChatService : IAIService
    {
        private const string OAuthUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
        private const string ChatCompletionsUrl =
            "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";
        private const string UploadFileUrl = "https://gigachat.devices.sberbank.ru/api/v1/files";

        private string _accessToken = string.Empty;
        private DateTime _lastAuthTime = DateTime.MinValue;
        private readonly object _lockObject = new object();

        // Класс для десериализации ответа с токеном
        private class TokenResponse
        {
            public string access_token { get; set; } = string.Empty;
            public long expires_at { get; set; }
        }

        // Классы для десериализации ответа API
        private class CompletionResponse
        {
            public Choice[] choices { get; set; } = Array.Empty<Choice>();

            public class Choice
            {
                public Message message { get; set; } = new Message();
                public string finish_reason { get; set; } = string.Empty;
                public int index { get; set; }
            }

            public class Message
            {
                public string role { get; set; } = string.Empty;
                public string content { get; set; } = string.Empty;
            }
        }

        // Класс для десериализации ответа загрузки файла
        private class FileUploadResponse
        {
            public string id { get; set; } = string.Empty;
            public string object_type { get; set; } = string.Empty;
            public int bytes { get; set; }
            public long created_at { get; set; }
            public string filename { get; set; } = string.Empty;
            public string purpose { get; set; } = string.Empty;
        }

        public async Task<bool> AuthAsync()
        {
            try
            {
                // Получаем данные аутентификации из настроек приложения
                string clientId =
                    WpfApplication.Current.Properties["GigaChatClientId"] as string ?? string.Empty;
                string clientSecret =
                    WpfApplication.Current.Properties["GigaChatClientSecret"] as string
                    ?? string.Empty;
                string scope =
                    WpfApplication.Current.Properties["GigaChatScope"] as string
                    ?? "GIGACHAT_API_PERS";

                // Логируем информацию о настройках для отладки
                Debug.WriteLine(
                    $"[DEBUG] Auth settings - ClientID: {clientId}, Secret: {clientSecret?.Substring(0, 4)}***, Scope: {scope}"
                );

                // Проверяем наличие необходимых данных
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    Debug.WriteLine("[ERROR] ClientId is empty");
                    WpfMessageBox.Show(
                        "ClientId отсутствует или пустой.",
                        "Ошибка авторизации",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                if (string.IsNullOrWhiteSpace(clientSecret))
                {
                    Debug.WriteLine("[ERROR] ClientSecret is empty");
                    WpfMessageBox.Show(
                        "ClientSecret отсутствует или пустой.",
                        "Ошибка авторизации",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                // Формируем Base64 ключ авторизации из client_id и client_secret
                string authKey = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")
                );
                Debug.WriteLine($"[DEBUG] Base64 Auth Key: {authKey}");

                // Создаем HTTP клиент с отключенной проверкой сертификата для отладки
                using (HttpClient client = new HttpClient(GetInsecureHandler()))
                {
                    // Генерируем уникальный RqUID
                    string rquid = Guid.NewGuid().ToString();
                    Debug.WriteLine($"[DEBUG] Using RqUID: {rquid}");

                    // Настраиваем заголовки запроса согласно документации
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("Authorization", $"Basic {authKey}");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                    client.DefaultRequestHeaders.Add("RqUID", rquid);

                    // Формируем данные запроса
                    var content = new FormUrlEncodedContent(
                        new Dictionary<string, string> { { "scope", scope } }
                    );

                    Debug.WriteLine($"[DEBUG] Sending auth request to {OAuthUrl}");

                    // Отправляем запрос на авторизацию
                    HttpResponseMessage response = await client.PostAsync(OAuthUrl, content);

                    string responseContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[DEBUG] Response status: {response.StatusCode}");
                    Debug.WriteLine($"[DEBUG] Response content: {responseContent}");

                    // Обрабатываем ответ
                    if (response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var tokenData = JsonSerializer.Deserialize<TokenResponse>(
                                responseContent
                            );

                            if (tokenData != null && !string.IsNullOrEmpty(tokenData.access_token))
                            {
                                _accessToken = tokenData.access_token;
                                _lastAuthTime = DateTime.Now;
                                Debug.WriteLine(
                                    $"[DEBUG] Received access token: {_accessToken.Substring(0, 15)}..."
                                );
                                Debug.WriteLine(
                                    $"[DEBUG] Token expires at: {tokenData.expires_at}"
                                );
                                return true;
                            }
                            else
                            {
                                Debug.WriteLine(
                                    "[ERROR] Token data is null or access_token is empty"
                                );
                                WpfMessageBox.Show(
                                    "Получен пустой токен от сервера.",
                                    "Ошибка авторизации",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error
                                );
                            }
                        }
                        catch (JsonException jsonEx)
                        {
                            Debug.WriteLine($"[ERROR] JSON parsing error: {jsonEx.Message}");
                            WpfMessageBox.Show(
                                $"Ошибка разбора ответа сервера: {jsonEx.Message}",
                                "Ошибка JSON",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                        }
                    }
                    else
                    {
                        Debug.WriteLine(
                            $"[ERROR] Auth failed with status code: {response.StatusCode}"
                        );
                        WpfMessageBox.Show(
                            $"Авторизация не удалась. Код: {response.StatusCode}\nОтвет: {responseContent}",
                            "Ошибка авторизации",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXCEPTION] Auth Exception: {ex.Message}");
                Debug.WriteLine($"[EXCEPTION] Stack trace: {ex.StackTrace}");
                WpfMessageBox.Show(
                    $"Исключение при авторизации: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return false;
            }
        }

        public async Task<string> SendQuestionAsync(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return string.Empty;
            }

            // Проверяем наличие токена и его срок действия (токен действует 30 минут)
            if (
                string.IsNullOrEmpty(_accessToken)
                || (DateTime.Now - _lastAuthTime).TotalMinutes > 29
            )
            {
                Debug.WriteLine("[DEBUG] Token is missing or expired, requesting new token");
                if (!await AuthAsync())
                {
                    return "Ошибка авторизации. Проверьте настройки GigaChat.";
                }
            }

            try
            {
                // Создаем HTTP клиент
                using (HttpClient client = new HttpClient(GetInsecureHandler()))
                {
                    // Настраиваем заголовки запроса согласно документации
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    // Формируем данные запроса в формате JSON согласно документации
                    var requestData = new
                    {
                        model = "GigaChat",
                        messages = new[] { new { role = "user", content = question } },
                        temperature = 0.7,
                        max_tokens = 2048,
                    };

                    string jsonRequest = JsonSerializer.Serialize(requestData);
                    Debug.WriteLine($"[DEBUG] Request data: {jsonRequest}");

                    var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                    Debug.WriteLine($"[DEBUG] Sending question to {ChatCompletionsUrl}");

                    // Отправляем запрос на генерацию ответа
                    HttpResponseMessage response = await client.PostAsync(
                        ChatCompletionsUrl,
                        content
                    );

                    string responseContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[DEBUG] Response status: {response.StatusCode}");
                    Debug.WriteLine(
                        $"[DEBUG] Response content preview: {(responseContent.Length > 100 ? responseContent.Substring(0, 100) + "..." : responseContent)}"
                    );

                    // Обрабатываем ответ
                    if (response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var completionResponse = JsonSerializer.Deserialize<CompletionResponse>(
                                responseContent
                            );

                            if (
                                completionResponse != null
                                && completionResponse.choices != null
                                && completionResponse.choices.Length > 0
                            )
                            {
                                string result = completionResponse.choices[0].message.content;
                                Debug.WriteLine(
                                    $"[DEBUG] Successfully parsed response, content length: {result.Length}"
                                );
                                return result;
                            }

                            Debug.WriteLine("[ERROR] No valid choices in completion response");
                            return "Нет ответа от GigaChat.";
                        }
                        catch (JsonException jsonEx)
                        {
                            Debug.WriteLine($"[ERROR] JSON parsing error: {jsonEx.Message}");
                            return $"Ошибка разбора ответа: {jsonEx.Message}";
                        }
                    }
                    else
                    {
                        // Если статус 401 (Unauthorized), пробуем обновить токен и повторить запрос
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            Debug.WriteLine("[DEBUG] Unauthorized error, trying to refresh token");
                            if (await AuthAsync())
                            {
                                // Рекурсивно вызываем метод снова после обновления токена
                                return await SendQuestionAsync(question);
                            }
                        }

                        Debug.WriteLine(
                            $"[ERROR] Request failed with status code: {response.StatusCode}"
                        );
                        return $"Ошибка запроса: {response.StatusCode} - {responseContent}";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXCEPTION] Send question exception: {ex.Message}");
                Debug.WriteLine($"[EXCEPTION] Stack trace: {ex.StackTrace}");
                return $"Исключение: {ex.Message}";
            }
        }

        // Improved methods for GigaChatService.cs

        // Updated methods for GigaChatService.cs - Replace these methods in your existing file

        public async Task<string?> UploadFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Debug.WriteLine($"[ERROR] File path is null or empty");
                return null;
            }

            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"[ERROR] File not found: {filePath}");
                return null;
            }

            // Check file size before attempting upload
            FileInfo fileInfo = new FileInfo(filePath);
            Debug.WriteLine(
                $"[DEBUG] Uploading file: {filePath}, Size: {fileInfo.Length} bytes, Type: {GetMimeType(filePath)}"
            );

            // Check if file is too large (10MB limit is common for many APIs)
            if (fileInfo.Length > 10 * 1024 * 1024)
            {
                Debug.WriteLine($"[ERROR] File is too large for upload: {fileInfo.Length} bytes");
                return null;
            }

            // Check token and authenticate if needed
            if (
                string.IsNullOrEmpty(_accessToken)
                || (DateTime.Now - _lastAuthTime).TotalMinutes > 29
            )
            {
                Debug.WriteLine("[DEBUG] Token is missing or expired, requesting new token");
                bool authSuccess = await AuthAsync();
                if (!authSuccess)
                {
                    Debug.WriteLine("[ERROR] Authentication failed, cannot upload file");
                    return null;
                }
            }

            try
            {
                // Create HttpClient with timeout
                using (HttpClient client = new HttpClient(GetInsecureHandler()))
                {
                    // Set timeout to avoid hanging on slow uploads
                    client.Timeout = TimeSpan.FromMinutes(2);

                    // Configure request headers
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json")
                    );

                    // Create MultipartFormDataContent for file upload
                    using (var content = new MultipartFormDataContent())
                    {
                        // Read file bytes
                        byte[] fileBytes = File.ReadAllBytes(filePath);
                        var fileContent = new ByteArrayContent(fileBytes);

                        // Determine MIME type
                        string contentType = GetMimeType(filePath);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

                        // Add file and purpose field to form data
                        content.Add(fileContent, "file", Path.GetFileName(filePath));
                        content.Add(new StringContent("general"), "purpose");

                        Debug.WriteLine(
                            $"[DEBUG] Starting file upload: {Path.GetFileName(filePath)} (MIME: {contentType})"
                        );

                        // Send upload request
                        var response = await client.PostAsync(UploadFileUrl, content);
                        string responseContent = await response.Content.ReadAsStringAsync();

                        Debug.WriteLine($"[DEBUG] Upload response status: {response.StatusCode}");
                        Debug.WriteLine($"[DEBUG] Upload response content: {responseContent}");

                        if (response.IsSuccessStatusCode)
                        {
                            try
                            {
                                var fileResponse = JsonSerializer.Deserialize<FileUploadResponse>(
                                    responseContent
                                );
                                if (fileResponse != null && !string.IsNullOrEmpty(fileResponse.id))
                                {
                                    Debug.WriteLine(
                                        $"[DEBUG] Successfully uploaded file, id: {fileResponse.id}"
                                    );
                                    return fileResponse.id;
                                }
                                else
                                {
                                    Debug.WriteLine(
                                        "[ERROR] File upload response is null or id is empty"
                                    );
                                    return null;
                                }
                            }
                            catch (JsonException jsonEx)
                            {
                                Debug.WriteLine($"[ERROR] JSON parsing error: {jsonEx.Message}");
                                Debug.WriteLine($"[ERROR] Response content: {responseContent}");
                                return null;
                            }
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            // If token expired, try to refresh and retry upload
                            Debug.WriteLine("[DEBUG] Unauthorized error, trying to refresh token");
                            if (await AuthAsync())
                            {
                                return await UploadFileAsync(filePath);
                            }
                            else
                            {
                                Debug.WriteLine("[ERROR] Token refresh failed, cannot upload file");
                                return null;
                            }
                        }
                        else
                        {
                            // Handle other error status codes
                            Debug.WriteLine(
                                $"[ERROR] File upload failed with status code: {response.StatusCode}"
                            );
                            Debug.WriteLine($"[ERROR] Response content: {responseContent}");

                            // Parse error message if possible
                            try
                            {
                                var errorResponse = JsonSerializer.Deserialize<JsonDocument>(
                                    responseContent
                                );
                                if (
                                    errorResponse != null
                                    && errorResponse.RootElement.TryGetProperty(
                                        "error",
                                        out var errorElement
                                    )
                                )
                                {
                                    string errorMessage = errorElement.ToString();
                                    Debug.WriteLine($"[ERROR] API error message: {errorMessage}");
                                }
                            }
                            catch (JsonException)
                            {
                                // Ignore parsing errors for error responses
                            }

                            return null;
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[ERROR] File upload timed out");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXCEPTION] File upload exception: {ex.Message}");
                Debug.WriteLine($"[EXCEPTION] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        // Добавьте этот метод в GigaChatService.cs
        public async Task<string> SendScreenshotDirectlyAsync(string filePath, string question = "")
        {
            try
            {
                // Проверяем файл
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    Debug.WriteLine($"[ERROR] Screenshot file not found: {filePath}");
                    return "Ошибка: файл скриншота не найден";
                }

                // Если вопрос не указан, используем стандартный вопрос о содержимом изображения
                if (string.IsNullOrWhiteSpace(question))
                {
                    question = "Опиши содержимое этого изображения. Что на нем изображено?";
                }

                // Загружаем файл изображения в GigaChat
                Debug.WriteLine($"[DEBUG] Uploading screenshot: {filePath}");
                string? fileId = await UploadFileAsync(filePath);

                if (string.IsNullOrEmpty(fileId))
                {
                    Debug.WriteLine("[ERROR] Failed to upload screenshot");
                    return "Ошибка: не удалось загрузить скриншот";
                }

                // Отправляем запрос с идентификатором файла и вопросом
                Debug.WriteLine($"[DEBUG] Sending question with screenshot, fileId: {fileId}");
                string response = await SendQuestionWithFileAsync(question, fileId);

                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXCEPTION] Error sending screenshot: {ex.Message}");
                return $"Ошибка при отправке скриншота: {ex.Message}";
            }
        }

        public async Task<string> SendQuestionWithFileAsync(string question, string fileId)
        {
            if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(fileId))
            {
                Debug.WriteLine("[ERROR] Question or fileId is empty");
                return string.Empty;
            }

            // Check token and authenticate if needed
            if (
                string.IsNullOrEmpty(_accessToken)
                || (DateTime.Now - _lastAuthTime).TotalMinutes > 29
            )
            {
                Debug.WriteLine("[DEBUG] Token is missing or expired, requesting new token");
                if (!await AuthAsync())
                {
                    Debug.WriteLine("[ERROR] Authentication failed");
                    return "Ошибка авторизации. Проверьте настройки GigaChat.";
                }
            }

            try
            {
                // Create HTTP client with timeout
                using (HttpClient client = new HttpClient(GetInsecureHandler()))
                {
                    // Set timeout to avoid hanging on slow responses
                    client.Timeout = TimeSpan.FromMinutes(3);

                    // Configure request headers
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    // Create request data
                    var requestData = new
                    {
                        model = "GigaChat",
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = question,
                                attachments = new[] { fileId },
                            },
                        },
                        temperature = 0.7,
                        max_tokens = 2048,
                    };

                    string jsonRequest = JsonSerializer.Serialize(requestData);
                    Debug.WriteLine($"[DEBUG] Request data with file: {jsonRequest}");

                    var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                    Debug.WriteLine($"[DEBUG] Sending question with file to {ChatCompletionsUrl}");

                    // Send request and get response
                    HttpResponseMessage response = await client.PostAsync(
                        ChatCompletionsUrl,
                        content
                    );

                    string responseContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[DEBUG] Response status: {response.StatusCode}");
                    Debug.WriteLine(
                        $"[DEBUG] Response content preview: {(responseContent.Length > 100 ? responseContent.Substring(0, 100) + "..." : responseContent)}"
                    );

                    // Process response
                    if (response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var completionResponse = JsonSerializer.Deserialize<CompletionResponse>(
                                responseContent
                            );

                            if (
                                completionResponse != null
                                && completionResponse.choices != null
                                && completionResponse.choices.Length > 0
                            )
                            {
                                string result = completionResponse.choices[0].message.content;
                                Debug.WriteLine(
                                    $"[DEBUG] Successfully parsed response, content length: {result.Length}"
                                );
                                return result;
                            }

                            Debug.WriteLine("[ERROR] No valid choices in completion response");
                            return "Нет ответа от GigaChat.";
                        }
                        catch (JsonException jsonEx)
                        {
                            Debug.WriteLine($"[ERROR] JSON parsing error: {jsonEx.Message}");
                            Debug.WriteLine($"[ERROR] Response content: {responseContent}");
                            return $"Ошибка разбора ответа: {jsonEx.Message}";
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        // If token expired, try to refresh and retry
                        Debug.WriteLine("[DEBUG] Unauthorized error, trying to refresh token");
                        if (await AuthAsync())
                        {
                            // Recursively call method again after token refresh
                            return await SendQuestionWithFileAsync(question, fileId);
                        }
                        else
                        {
                            Debug.WriteLine("[ERROR] Token refresh failed");
                            return "Ошибка обновления токена авторизации.";
                        }
                    }
                    else
                    {
                        Debug.WriteLine(
                            $"[ERROR] Request failed with status code: {response.StatusCode}"
                        );

                        // Try to get detailed error message
                        string errorDetail = "";
                        try
                        {
                            var errorResponse = JsonSerializer.Deserialize<JsonDocument>(
                                responseContent
                            );
                            if (
                                errorResponse != null
                                && errorResponse.RootElement.TryGetProperty(
                                    "error",
                                    out var errorElement
                                )
                            )
                            {
                                errorDetail = $": {errorElement}";
                            }
                        }
                        catch { }

                        return $"Ошибка запроса: {response.StatusCode}{errorDetail}";
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[ERROR] Request timed out");
                return "Запрос превысил время ожидания. Попробуйте позже.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXCEPTION] Send question with file exception: {ex.Message}");
                Debug.WriteLine($"[EXCEPTION] Stack trace: {ex.StackTrace}");
                return $"Исключение: {ex.Message}";
            }
        }

        // Вспомогательный метод для отключения проверки SSL сертификатов (для отладки)
        private HttpClientHandler GetInsecureHandler()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    Debug.WriteLine(
                        $"[DEBUG] SSL Certificate validation bypassed, errors: {string.Join(", ", errors)}"
                    );
                    return true;
                },
            };
            return handler;
        }

        // Вспомогательный метод для определения MIME типа файла
        private string GetMimeType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tiff" or ".tif" => "image/tiff",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" =>
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" =>
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                _ => "application/octet-stream", // Default for unknown types
            };
        }
    }
}

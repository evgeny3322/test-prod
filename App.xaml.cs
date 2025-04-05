using System.Diagnostics;
using System.Windows;
// Use aliases to disambiguate
using WpfApplication = System.Windows.Application;

namespace AIInterviewAssistant.WPF
{
    // Explicitly inherit from the WPF Application class
    public partial class App : WpfApplication
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Existing settings...

            // Add screenshot and assistant settings
            Properties["ScreenshotCooldownMs"] = 1000; // 1 second cooldown between screenshots
            Properties["SolutionHotkey"] = "Alt+1";
            Properties["ExplanationHotkey"] = "Alt+2";
            Properties["AlternativeHotkey"] = "Alt+3";

            // Customize prompts for different response types
            Properties["SolutionPromptTemplate"] =
                "I'm in a coding interview. This screenshot shows a programming problem. Please solve this problem with clear, correct code. Provide ONLY the code solution, no explanations.";
            Properties["ExplanationPromptTemplate"] =
                "I'm in a coding interview. This screenshot shows a programming problem. Please explain how to solve this problem step by step, including the reasoning and approach.";
            Properties["AlternativePromptTemplate"] =
                "I'm in a coding interview. This screenshot shows a programming problem. Please provide an alternative solution or optimization approach to this problem. Focus on efficient implementation.";
 // Настройки GigaChat API
            Properties["GigaChatClientId"] = "c5285186-98c4-4da7-a5d8-fc58f4c2e1fc";
            Properties["GigaChatClientSecret"] = "a01afe84-a40c-48d3-9ebb-9ad000343d1e";
            Properties["GigaChatScope"] = "GIGACHAT_API_PERS";
            // Add helper window configuration
            Properties["DisplayHelperWindowOnHotkey"] = true;
            Properties["HelperWindowOpacity"] = 0.9;
            Properties["HelperWindowWidth"] = 400;
            Properties["HelperWindowHeight"] = 300;
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            // Убедимся, что все потоки завершены перед выходом
            try
            {
                // Даем шанс корректно завершиться всем ресурсам
                System.Threading.Thread.Sleep(200);

                // Принудительное завершение если что-то осталось
                Process currentProcess = Process.GetCurrentProcess();

                // Проверяем, что процесс все еще существует
                if (!currentProcess.HasExited)
                {
                    currentProcess.Kill();
                }
            }
            catch
            {
                // Игнорируем ошибки при попытке завершения процесса
            }
        }
    }
}

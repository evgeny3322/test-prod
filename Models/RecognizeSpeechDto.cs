using System.Text.Json.Serialization;

namespace AIInterviewAssistant.WPF.Models
{
    public class RecognizeSpeechDto
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }
}
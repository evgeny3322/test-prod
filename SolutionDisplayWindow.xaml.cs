using System.Windows;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;

namespace AIInterviewAssistant.WPF
{
    /// <summary>
    /// Interaction logic for SolutionDisplayWindow.xaml
    /// </summary>
    public partial class SolutionDisplayWindow : Window
    {
        // Enum to differentiate between solution types
        public enum SolutionType
        {
            Solution,
            Explanation,
            Alternative
        }

        public SolutionDisplayWindow()
        {
            InitializeComponent();
        }

        public SolutionDisplayWindow(string solutionText, SolutionType type = SolutionType.Solution) : this()
        {
            SolutionTextBox.Text = solutionText;
            
            // Set title based on solution type
            Title = type switch
            {
                SolutionType.Solution => "Code Solution",
                SolutionType.Explanation => "Problem Explanation",
                SolutionType.Alternative => "Alternative Approach",
                _ => "Solution Display"
            };
        }

        public void SetContent(string content, SolutionType type)
        {
            SolutionTextBox.Text = content;
            
            // Set title based on solution type
            Title = type switch
            {
                SolutionType.Solution => "Code Solution",
                SolutionType.Explanation => "Problem Explanation",
                SolutionType.Alternative => "Alternative Approach",
                _ => "Solution Display"
            };
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SolutionTextBox.Text))
            {
                WpfClipboard.SetText(SolutionTextBox.Text);
                WpfMessageBox.Show("Text copied to clipboard.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
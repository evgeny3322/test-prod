using System.Windows;

namespace AIInterviewAssistant.WPF
{
    /// <summary>
    /// Interaction logic for SolutionDisplayWindow.xaml
    /// </summary>
    public partial class SolutionDisplayWindow : Window
    {
        public SolutionDisplayWindow()
        {
            InitializeComponent();
        }

        public SolutionDisplayWindow(string solutionText, string title = "Solution Display") : this()
        {
            SolutionTextBox.Text = solutionText;
            Title = title;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SolutionTextBox.Text))
            {
                Clipboard.SetText(SolutionTextBox.Text);
                MessageBox.Show("Text copied to clipboard.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using tiny11_ui.ViewModels;

namespace tiny11_ui.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                // ViewModel'den kapatma izni al
                bool canClose = viewModel.HandleWindowClosing();
                if (!canClose)
                {
                    e.Cancel = true; // Kapatmayı engelle
                }
            }
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}

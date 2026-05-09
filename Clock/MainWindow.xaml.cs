using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Clock
{
    public partial class MainWindow : Window
    {
        private bool _isDarkMode = false;
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;
            RootBorder.Background = new SolidColorBrush(Colors.GhostWhite);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        // Theme and app state
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void WindowControls_MouseEnter(object sender, MouseEventArgs e) => WindowControlsPanel.Opacity = 1;
        private void WindowControls_MouseLeave(object sender, MouseEventArgs e) => WindowControlsPanel.Opacity = 0;

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            RootBorder.Background = new SolidColorBrush(_isDarkMode ? Color.FromRgb(30,30,30) : Colors.GhostWhite);
            this.Foreground = new SolidColorBrush(_isDarkMode ? Colors.White : Colors.Black);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_viewModel.CanCloseApp())
            {
                e.Cancel = true;
                return;
            }
            _viewModel.SaveData();
        }
    }
}
using System.Windows;

namespace ResourceCleaner
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var dialog = new FolderDialogService();
            DataContext = new MainViewModel(dialog);
        }
    }
}

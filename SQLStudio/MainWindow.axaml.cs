using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SQLStudio.ViewModels;

namespace SQLStudio
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            DataContext = vm;

            vm.PropertyChanged += ViewModelOnPropertyChanged;
        }

        private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MainViewModel vm)
            {
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.QueryResultColumns))
            {
                Dispatcher.UIThread.Post(() => UpdateQueryResultsColumns(vm));
            }
        }

        private void UpdateQueryResultsColumns(MainViewModel vm)
        {
            if (QueryResultsGrid == null)
            {
                return;
            }

            QueryResultsGrid.Columns.Clear();

            foreach (var columnName in vm.QueryResultColumns)
            {
                QueryResultsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = columnName,
                    Binding = new Avalonia.Data.Binding($"[{columnName}]")
                });
            }
        }

        private void OnChatInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MainViewModel vm)
            {
                vm.SendChatMessageCommand.Execute(null);
            }
        }
    }
}
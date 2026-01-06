using System;
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
                vm.HideTableSuggestions();
                vm.SendChatMessageCommand.Execute(null);
            }
            else if (e.Key == Key.Escape && DataContext is MainViewModel vm2)
            {
                vm2.HideTableSuggestions();
            }
        }

        private void OnChatInputTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not MainViewModel vm)
                return;

            // 如果正在选择表，跳过处理
            if (vm.SuppressTableSuggestion)
                return;

            var text = textBox.Text ?? "";
            
            // 找到最后一个@的位置（不依赖光标位置）
            var lastAtIndex = text.LastIndexOf('@');
            
            if (lastAtIndex >= 0)
            {
                // 检查@后面的内容
                var afterAt = text.Substring(lastAtIndex + 1);
                // 如果@后面没有空格，显示建议
                if (!afterAt.Contains(' '))
                {
                    vm.UpdateTableSuggestions(afterAt);
                    return;
                }
            }

            // 如果没有找到@或者@后面有空格，隐藏建议
            vm.HideTableSuggestions();
        }

        private void OnTablesListBoxDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not ListBox listBox || DataContext is not MainViewModel vm)
                return;

            var selectedTable = listBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedTable))
            {
                vm.ShowTableStructureCommand.Execute(selectedTable);
            }
        }
    }
}
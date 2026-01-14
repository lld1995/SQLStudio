using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SQLStudio.ViewModels;

namespace SQLStudio
{
    public partial class MainWindow : Window
    {
        private ChatMessage? _lastTrackedMessage;
        private bool _isNearBottom = true;
        private const double ScrollThreshold = 50; // 距离底部多少像素内认为在底部

        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            DataContext = vm;

            vm.PropertyChanged += ViewModelOnPropertyChanged;
            vm.ChatMessages.CollectionChanged += ChatMessagesOnCollectionChanged;
            
            // 监听ScrollViewer滚动事件
            ChatScrollViewer.ScrollChanged += OnChatScrollViewerScrollChanged;
        }

        private void OnChatScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            
            // 检查是否在底部附近
            _isNearBottom = sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - ScrollThreshold;
        }

        private void ChatMessagesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 新消息添加时
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                // 取消跟踪旧的消息
                if (_lastTrackedMessage != null)
                {
                    _lastTrackedMessage.PropertyChanged -= OnLastMessagePropertyChanged;
                }

                // 跟踪新消息
                _lastTrackedMessage = e.NewItems[^1] as ChatMessage;
                if (_lastTrackedMessage != null)
                {
                    _lastTrackedMessage.PropertyChanged += OnLastMessagePropertyChanged;
                }

                // 添加新消息时滚动到底部
                ScrollToBottomIfNeeded();
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // 清空时取消跟踪
                if (_lastTrackedMessage != null)
                {
                    _lastTrackedMessage.PropertyChanged -= OnLastMessagePropertyChanged;
                    _lastTrackedMessage = null;
                }
            }
        }

        private void OnLastMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 当最后一条消息的内容变化时（流式输出），滚动到底部
            if (e.PropertyName is nameof(ChatMessage.Content) or nameof(ChatMessage.SqlGenerationContent) or nameof(ChatMessage.TableAnalysisContent))
            {
                ScrollToBottomIfNeeded();
            }
        }

        private void ScrollToBottomIfNeeded()
        {
            if (!_isNearBottom) return;

            Dispatcher.UIThread.Post(() =>
            {
                ChatScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Background);
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

        private void OnQaChatInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MainViewModel vm)
            {
                vm.SendQaMessageCommand.Execute(null);
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

        private async void OnExportQueryResultClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || !vm.CanExportQueryResult)
                return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                    return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "导出查询结果为CSV",
                    SuggestedFileName = $"query_result_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    FileTypeChoices = new[]
                    {
                        FilePickerFileTypes.All,
                        new FilePickerFileType("CSV文件")
                        {
                            Patterns = new[] { "*.csv" },
                            MimeTypes = new[] { "text/csv" }
                        }
                    }
                });

                if (file != null)
                {
                    var filePath = file.TryGetLocalPath() ?? file.Path.ToString();
                    vm.ExportQueryResultToCsv(filePath);
                    vm.StatusMessage = $"已成功导出到: {filePath}";
                }
            }
            catch (Exception ex)
            {
                if (DataContext is MainViewModel vm2)
                {
                    vm2.StatusMessage = $"导出失败: {ex.Message}";
                }
            }
        }
    }
}
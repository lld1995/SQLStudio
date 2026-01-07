using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SQLStudio.Core.AI;
using SQLStudio.Core.Database;
using SQLStudio.Core.Services;
using ColumnInfo = SQLStudio.Core.Database.ColumnInfo;

namespace SQLStudio.ViewModels;

public enum ChatHistoryMode
{
    Complete,  // 完整模式 - 包含所有历史消息
    Improved   // 改进模式 - 仅使用上次生成的SQL
}

public enum WorkflowStep
{
    DatabaseConnection = 0,
    AiConfiguration = 1,
    SqlWorkspace = 2
}

public partial class MainViewModel : ObservableObject
{
    private readonly ConnectionManager _connectionManager;
    private readonly SqlAgentService _sqlAgentService;
    private readonly AppSettingsService _settingsService;
    private readonly ScenarioKnowledgeService _knowledgeService;
    private CancellationTokenSource? _chatCancellationTokenSource;
    
    private const string DefaultConnectionId = "default";

    [ObservableProperty]
    private WorkflowStep _currentStep = WorkflowStep.DatabaseConnection;

    public bool IsStep1 => CurrentStep == WorkflowStep.DatabaseConnection;
    public bool IsStep2 => CurrentStep == WorkflowStep.AiConfiguration;
    public bool IsStep3 => CurrentStep == WorkflowStep.SqlWorkspace;

    public bool CanGoToStep2 => IsConnected && !string.IsNullOrEmpty(SelectedDatabase);
    public bool CanGoToStep3 => IsConnected && !string.IsNullOrEmpty(SelectedAiModel);

    partial void OnCurrentStepChanged(WorkflowStep value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoToStep2));
        OnPropertyChanged(nameof(CanGoToStep3));
    }

    [ObservableProperty]
    private string _host = "192.168.191.2";

    [ObservableProperty]
    private int _port = 30309;

    [ObservableProperty]
    private string _username = "root";

    [ObservableProperty]
    private string _password = "West#123";

    [ObservableProperty]
    private DatabaseType _selectedDatabaseType = DatabaseType.MySQL;

    [ObservableProperty]
    private string _aiApiKey = "none";

    [ObservableProperty]
    private string? _selectedAiModel;

    [ObservableProperty]
    private string _aiEndpoint = "http://192.168.191.2:30010/v1";

    [ObservableProperty]
    private bool _isLoadingModels;

    [ObservableProperty]
    private string _userQuery = "";

    [ObservableProperty]
    private string _chatInput = "";

    [ObservableProperty]
    private ChatHistoryMode _selectedHistoryMode = ChatHistoryMode.Complete;

    public ObservableCollection<ChatHistoryMode> HistoryModes { get; } = new(Enum.GetValues<ChatHistoryMode>());

    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();

    [ObservableProperty]
    private string _generatedSql = "";

    [ObservableProperty]
    private string _executionLog = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private DataTable? _queryResult;

    public ObservableCollection<Dictionary<string, object?>> QueryResultRows { get; } = new();

    public List<string> QueryResultColumns { get; private set; } = new();

    partial void OnQueryResultChanged(DataTable? value)
    {
        QueryResultRows.Clear();
        QueryResultColumns.Clear();

        if (value != null)
        {
            foreach (DataColumn col in value.Columns)
            {
                QueryResultColumns.Add(col.ColumnName);
            }

            foreach (DataRow row in value.Rows)
            {
                var dict = new Dictionary<string, object?>();
                foreach (DataColumn col in value.Columns)
                {
                    dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                }
                QueryResultRows.Add(dict);
            }
        }

        OnPropertyChanged(nameof(QueryResultColumns));
    }

    [ObservableProperty]
    private int _maxRetries = 3;

    [ObservableProperty]
    private string? _selectedDatabase;

    [ObservableProperty]
    private string? _selectedTable;

    // 表结构查看相关属性
    [ObservableProperty]
    private bool _isTableStructureVisible;

    [ObservableProperty]
    private string _tableStructureTitle = "";

    [ObservableProperty]
    private ObservableCollection<ColumnInfo> _tableStructureColumns = new();

    public ObservableCollection<DatabaseType> DatabaseTypes { get; } = new(Enum.GetValues<DatabaseType>());
    public ObservableCollection<string> ExecutionHistory { get; } = new();
    public ObservableCollection<string> Databases { get; } = new();
    public ObservableCollection<string> Tables { get; } = new();
    public ObservableCollection<string> AiModels { get; } = new();

    // @提及表功能相关属性
    [ObservableProperty]
    private bool _isTableSuggestionVisible;

    [ObservableProperty]
    private string _tableSearchText = "";

    [ObservableProperty]
    private string? _selectedSuggestionTable;

    public ObservableCollection<string> FilteredTables { get; } = new();

    // 场景知识管理相关属性
    [ObservableProperty]
    private bool _isKnowledgeManagementVisible;

    [ObservableProperty]
    private ObservableCollection<ScenarioKnowledge> _knowledgeList = new();

    [ObservableProperty]
    private ScenarioKnowledge? _selectedKnowledge;

    [ObservableProperty]
    private string _knowledgeTitle = "";

    [ObservableProperty]
    private string _knowledgeContent = "";

    [ObservableProperty]
    private string _knowledgeKeywords = "";

    [ObservableProperty]
    private bool _isEditingKnowledge;

    [ObservableProperty]
    private bool _isExtractingKeywords;

    public MainViewModel()
    {
        _connectionManager = new ConnectionManager();
        _knowledgeService = new ScenarioKnowledgeService();
        _sqlAgentService = new SqlAgentService(_connectionManager, _knowledgeService);
        _settingsService = new AppSettingsService();
        LoadSettings();
        LoadKnowledgeList();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        
        // Database settings
        _host = settings.Database.Host;
        _port = settings.Database.Port;
        _username = settings.Database.Username;
        _password = settings.Database.Password;
        _selectedDatabaseType = settings.Database.DatabaseType;
        
        // AI settings
        _aiApiKey = settings.Ai.ApiKey;
        _aiEndpoint = settings.Ai.Endpoint;
        
        // Add saved model to collection so it can be selected
        if (!string.IsNullOrEmpty(settings.Ai.SelectedModel))
        {
            AiModels.Add(settings.Ai.SelectedModel);
            _selectedAiModel = settings.Ai.SelectedModel;
            
            // Configure AI service with saved settings
            _sqlAgentService.ConfigureAi(new AiServiceConfig
            {
                Provider = AiProvider.OpenAI,
                ApiKey = _aiApiKey,
                ModelId = _selectedAiModel,
                Endpoint = string.IsNullOrEmpty(_aiEndpoint) ? null : _aiEndpoint
            });
        }
    }

    private void SaveSettings()
    {
        var settings = new AppSettings
        {
            Database = new DatabaseSettings
            {
                Host = Host,
                Port = Port,
                Username = Username,
                Password = Password,
                DatabaseType = SelectedDatabaseType,
                SelectedDatabase = SelectedDatabase
            },
            Ai = new AiSettings
            {
                ApiKey = AiApiKey,
                Endpoint = AiEndpoint,
                SelectedModel = SelectedAiModel
            }
        };
        _settingsService.Save(settings);
    }

    partial void OnSelectedDatabaseTypeChanged(DatabaseType value)
    {
        Port = DatabaseConnectorFactory.GetDefaultPort(value);
    }

    partial void OnSelectedDatabaseChanged(string? value)
    {
        OnPropertyChanged(nameof(CanGoToStep2));
        if (!string.IsNullOrEmpty(value) && IsConnected)
        {
            _ = LoadTablesAsync();
        }
    }

    partial void OnSelectedAiModelChanged(string? value)
    {
        OnPropertyChanged(nameof(CanGoToStep3));
        if (IsConnected && !string.IsNullOrEmpty(value))
        {
            ReconfigureAiService();
        }
    }

    private void ReconfigureAiService()
    {
        _sqlAgentService.ConfigureAi(new AiServiceConfig
        {
            Provider = AiProvider.OpenAI,
            ApiKey = AiApiKey,
            ModelId = SelectedAiModel ?? "gpt-4o",
            Endpoint = string.IsNullOrEmpty(AiEndpoint) ? null : AiEndpoint
        });
        AppendLog($"✓ AI model changed to: {SelectedAiModel}");
        
        SaveSettings();
    }

    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(AiApiKey))
        {
            StatusMessage = "Please enter API Key first";
            return;
        }

        try
        {
            IsLoadingModels = true;
            StatusMessage = "Fetching models...";
            
            // Save before clearing - ComboBox may reset selection on clear
            var previousModel = SelectedAiModel;
            AiModels.Clear();

            var modelService = new OpenAiModelService();
            var models = await modelService.GetModelsAsync(AiApiKey, AiEndpoint);

            foreach (var model in models)
            {
                AiModels.Add(model);
            }

            if (AiModels.Count > 0)
            {
                if (!string.IsNullOrEmpty(previousModel) && AiModels.Contains(previousModel))
                {
                    SelectedAiModel = previousModel;
                }
                else
                {
                    SelectedAiModel = AiModels[0];
                }
                StatusMessage = $"Found {AiModels.Count} models";
            }
            else
            {
                StatusMessage = "No chat models found";
            }

            AppendLog($"✓ Loaded {AiModels.Count} AI models");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to fetch models: {ex.Message}";
            AppendLog($"✗ Failed to fetch models: {ex.Message}");
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            IsExecuting = true;
            StatusMessage = "Connecting...";
            Databases.Clear();
            Tables.Clear();
            SelectedDatabase = null;

            var config = new DatabaseConnectionConfig
            {
                Host = Host,
                Port = Port,
                Username = Username,
                Password = Password
            };

            await _connectionManager.CreateConnectionAsync(
                DefaultConnectionId,
                SelectedDatabaseType,
                config);

            IsConnected = true;
            StatusMessage = $"Connected to {SelectedDatabaseType}://{Host}:{Port}";
            AppendLog($"✓ Connected to {SelectedDatabaseType}://{Host}:{Port}");
            
            SaveSettings();

            await LoadDatabasesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            AppendLog($"✗ Connection failed: {ex.Message}");
            IsConnected = false;
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task LoadDatabasesAsync()
    {
        try
        {
            var connector = _connectionManager.GetConnection(DefaultConnectionId);
            if (connector == null) return;

            StatusMessage = "Loading databases...";
            var databases = await connector.GetDatabasesAsync();
            
            Databases.Clear();
            foreach (var db in databases)
            {
                Databases.Add(db);
            }
            
            StatusMessage = $"Found {databases.Count} databases";
            AppendLog($"✓ Loaded {databases.Count} databases");
        }
        catch (Exception ex)
        {
            AppendLog($"✗ Failed to load databases: {ex.Message}");
        }
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            var connector = _connectionManager.GetConnection(DefaultConnectionId);
            if (connector == null || string.IsNullOrEmpty(SelectedDatabase)) return;

            StatusMessage = $"Switching to database: {SelectedDatabase}...";
            await connector.UseDatabaseAsync(SelectedDatabase);
            
            StatusMessage = "Loading tables...";
            var tables = await connector.GetTablesAsync();
            
            Tables.Clear();
            foreach (var table in tables)
            {
                Tables.Add(table);
            }
            
            StatusMessage = $"Database: {SelectedDatabase} - {tables.Count} tables";
            AppendLog($"✓ Switched to [{SelectedDatabase}], loaded {tables.Count} tables");
        }
        catch (Exception ex)
        {
            AppendLog($"✗ Failed to load tables: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            await _connectionManager.RemoveConnectionAsync(DefaultConnectionId);
            IsConnected = false;
            Databases.Clear();
            Tables.Clear();
            SelectedDatabase = null;
            CurrentStep = WorkflowStep.DatabaseConnection;
            StatusMessage = "Disconnected";
            AppendLog("✓ Disconnected");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Disconnect failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExecuteQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(UserQuery))
        {
            StatusMessage = "Please enter a query";
            return;
        }

        if (!IsConnected)
        {
            StatusMessage = "Not connected to database";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedAiModel))
        {
            StatusMessage = "Please select an AI model first (click '获取模型')";
            return;
        }

        try
        {
            IsExecuting = true;
            StatusMessage = "Generating and executing SQL...";
            GeneratedSql = "";
            QueryResult = null;

            var executor = _sqlAgentService.CreateExecutor(DefaultConnectionId, new SqlAgentOptions
            {
                MaxRetries = MaxRetries
            });

            var streamingOutput = new System.Text.StringBuilder();

            executor.OnPromptSending += (_, e) =>
            {
                AppendLog($"═══════════════════════════════════════");
                AppendLog($"📝 User Query: {e.UserQuery}");
                AppendLog($"───────────────────────────────────────");
                AppendLog($"📤 System Prompt:\n{e.SystemPrompt}");
                AppendLog($"───────────────────────────────────────");
                AppendLog($"📤 User Prompt:\n{e.UserPrompt}");
                AppendLog($"═══════════════════════════════════════");
                AppendLog($"🤖 AI Response:");
            };

            executor.OnStreaming += (_, e) =>
            {
                streamingOutput.Append(e.Token);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ExecutionLog += e.Token;
                });
            };

            executor.OnSqlGenerated += (_, e) =>
            {
                GeneratedSql = e.Sql;
                AppendLog($"\n[Attempt {e.AttemptNumber}] ✓ SQL extracted successfully");
            };

            executor.OnSqlExecuted += (_, e) =>
            {
                if (e.ExecutionResult?.Success == true)
                {
                    AppendLog($"[Attempt {e.AttemptNumber}] ✓ Execution successful ({e.ExecutionResult.ExecutionTime.TotalMilliseconds:F2}ms, {e.ExecutionResult.AffectedRows} rows)");
                }
                else
                {
                    AppendLog($"[Attempt {e.AttemptNumber}] ✗ Execution failed: {e.ExecutionResult?.ErrorMessage}");
                }
            };

            executor.OnRetrying += (_, e) =>
            {
                streamingOutput.Clear();
                AppendLog($"\n[Retry {e.AttemptNumber}/{e.MaxAttempts}] Regenerating SQL due to error: {e.ErrorMessage}");
                AppendLog($"🤖 AI Response:");
            };

            var result = await executor.ExecuteAsync(UserQuery);

            if (result.Success)
            {
                StatusMessage = $"Query executed successfully in {result.TotalAttempts} attempt(s)";
                QueryResult = result.ExecutionResult?.Data;
                ExecutionHistory.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {UserQuery}");
            }
            else
            {
                StatusMessage = $"Query failed after {result.TotalAttempts} attempts";
                AppendLog($"Final error: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"✗ Error: {ex.Message}");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task SendChatMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput))
            return;

        if (!IsConnected)
        {
            StatusMessage = "Not connected to database";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedAiModel))
        {
            StatusMessage = "Please select an AI model first (click '获取模型')";
            return;
        }

        var conversationHistory = new List<SqlGenerationHistory>();
        if (SelectedHistoryMode == ChatHistoryMode.Complete)
        {
            // 完整模式：包含所有历史消息
            foreach (var msg in ChatMessages)
            {
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    conversationHistory.Add(new SqlGenerationHistory
                    {
                        Role = msg.IsUser ? "user" : "assistant",
                        Content = msg.IsUser ? msg.Content : (msg.Content + (string.IsNullOrEmpty(msg.Sql) ? "" : $"\n```sql\n{msg.Sql}\n```"))
                    });
                }
            }
        }
        else
        {
            // 改进模式：仅使用上次生成的SQL作为上下文
            if (!string.IsNullOrEmpty(GeneratedSql))
            {
                conversationHistory.Add(new SqlGenerationHistory
                {
                    Role = "assistant",
                    Content = $"上次生成的SQL:\n```sql\n{GeneratedSql}\n```"
                });
            }
        }

        // 解析@提及的表名
        var specifiedTables = ParseMentionedTables(ChatInput);
        var cleanQuery = specifiedTables.Count > 0 ? RemoveTableMentions(ChatInput) : ChatInput;

        var userMessage = new ChatMessage(ChatInput, true);
        ChatMessages.Add(userMessage);
        var userQuery = cleanQuery;
        ChatInput = "";
        HideTableSuggestions();

        var aiMessage = new ChatMessage("", false) { IsStreaming = true };
        ChatMessages.Add(aiMessage);

        _chatCancellationTokenSource?.Cancel();
        _chatCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _chatCancellationTokenSource.Token;

        try
        {
            IsExecuting = true;
            StatusMessage = "Generating SQL...";
            GeneratedSql = "";
            QueryResult = null;

            var executor = _sqlAgentService.CreateExecutor(DefaultConnectionId, new SqlAgentOptions
            {
                MaxRetries = MaxRetries
            });

            // 步骤变更事件
            executor.OnStepChanged += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.CurrentStep = e.Message;
                    StatusMessage = e.Message;
                    
                    switch (e.Step)
                    {
                        case ExecutionStep.AnalyzingTables:
                            aiMessage.UpdateStep(0, StepStatus.InProgress, "正在分析...");
                            break;
                        case ExecutionStep.GeneratingSql:
                            aiMessage.UpdateStep(0, StepStatus.Completed);
                            aiMessage.UpdateStep(1, StepStatus.InProgress, "正在生成...");
                            break;
                        case ExecutionStep.ExecutingSql:
                            aiMessage.UpdateStep(1, StepStatus.Completed);
                            aiMessage.UpdateStep(2, StepStatus.InProgress, "正在执行...");
                            break;
                        case ExecutionStep.Completed:
                            aiMessage.UpdateStep(2, StepStatus.Completed, "完成");
                            break;
                        case ExecutionStep.Failed:
                            aiMessage.UpdateStep(2, StepStatus.Failed, "失败");
                            break;
                        case ExecutionStep.Retrying:
                            aiMessage.UpdateStep(2, StepStatus.Failed, "重试中...");
                            break;
                    }
                });
                AppendLog($"📍 {e.Message}");
            };

            // 表分析开始事件
            executor.OnTableAnalysisStarted += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.TotalTableCount = e.TotalTables;
                    aiMessage.TableAnalysisContent = "";
                });
                AppendLog($"🔍 开始分析表结构 (共 {e.TotalTables} 个表)");
            };

            // 表分析完成事件
            executor.OnTableAnalysisCompleted += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.AnalyzedTables = e.SelectedTables;
                    aiMessage.SelectedTableCount = e.SelectedTables.Count;
                    aiMessage.TableAnalysisReasoning = e.Reasoning;
                    aiMessage.UpdateStep(0, StepStatus.Completed, $"选中 {e.SelectedTables.Count}/{e.TotalTables} 个表");
                });
                AppendLog($"✓ 表分析完成: 选中 {e.SelectedTables.Count} 个表 - {string.Join(", ", e.SelectedTables)}");
                if (!string.IsNullOrEmpty(e.Reasoning))
                {
                    AppendLog($"  原因: {e.Reasoning}");
                }
            };

            executor.OnPromptSending += (_, e) =>
            {
                AppendLog($"📝 Chat Query: {e.UserQuery}");
                AppendLog($"📊 使用 {e.FilteredTableCount}/{e.TotalTableCount} 个表生成SQL");
            };

            // 流式输出事件 - 区分表分析和SQL生成阶段
            executor.OnStreaming += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (e.Phase == "TableAnalysis")
                    {
                        aiMessage.AppendTableAnalysis(e.Token);
                    }
                    else
                    {
                        aiMessage.AppendSqlGeneration(e.Token);
                        aiMessage.AppendContent(e.Token);
                    }
                });
            };

            executor.OnSqlGenerated += (_, e) =>
            {
                GeneratedSql = e.Sql;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.Sql = e.Sql;
                });
                AppendLog($"✓ SQL已生成");
            };

            executor.OnSqlExecuted += (_, e) =>
            {
                if (e.ExecutionResult?.Success == true)
                {
                    AppendLog($"✓ 执行成功 ({e.ExecutionResult.ExecutionTime.TotalMilliseconds:F2}ms, {e.ExecutionResult.AffectedRows} 行)");
                }
                else
                {
                    AppendLog($"✗ 执行失败: {e.ExecutionResult?.ErrorMessage}");
                }
            };

            executor.OnRetrying += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.Content += $"\n\n--- 重试 {e.AttemptNumber}/{e.MaxAttempts} ---\n错误: {e.ErrorMessage}\n\n";
                });
                AppendLog($"🔄 重试 {e.AttemptNumber}/{e.MaxAttempts}: {e.ErrorMessage}");
            };

            var result = await executor.ExecuteAsync(userQuery, null, conversationHistory, cancellationToken, specifiedTables);

            aiMessage.IsStreaming = false;

            if (result.Success)
            {
                StatusMessage = $"执行成功 (使用 {result.AnalyzedTables.Count} 个表)";
                QueryResult = result.ExecutionResult?.Data;
            }
            else
            {
                StatusMessage = $"执行失败: {result.ErrorMessage}";
                aiMessage.IsError = true;
                if (string.IsNullOrEmpty(aiMessage.Content))
                {
                    aiMessage.Content = $"错误: {result.ErrorMessage}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generation stopped";
            aiMessage.IsStreaming = false;
            if (string.IsNullOrEmpty(aiMessage.Content))
            {
                aiMessage.Content = "[Stopped by user]";
            }
            else
            {
                aiMessage.Content += "\n[Stopped by user]";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            aiMessage.IsStreaming = false;
            aiMessage.IsError = true;
            aiMessage.Content = $"Error: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
            _chatCancellationTokenSource?.Dispose();
            _chatCancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void StopGeneration()
    {
        _chatCancellationTokenSource?.Cancel();
        StatusMessage = "Stopping...";
    }

    [RelayCommand]
    private void ClearChat()
    {
        ChatMessages.Clear();
        GeneratedSql = "";
    }

    [RelayCommand]
    private void UseSql(string? sql)
    {
        if (!string.IsNullOrEmpty(sql))
        {
            GeneratedSql = sql;
        }
    }

    [RelayCommand]
    private async Task ExecuteSqlDirectlyAsync()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql))
        {
            StatusMessage = "No SQL to execute";
            return;
        }

        if (!IsConnected)
        {
            StatusMessage = "Not connected to database";
            return;
        }

        try
        {
            IsExecuting = true;
            StatusMessage = "Executing SQL...";

            var connector = _connectionManager.GetConnection(DefaultConnectionId);
            if (connector == null)
            {
                StatusMessage = "Connection not found";
                return;
            }

            var result = await connector.ExecuteQueryAsync(GeneratedSql);

            if (result.Success)
            {
                StatusMessage = $"Executed successfully ({result.ExecutionTime.TotalMilliseconds:F2}ms, {result.AffectedRows} rows)";
                QueryResult = result.Data;
                AppendLog($"✓ Direct execution successful");
            }
            else
            {
                StatusMessage = $"Execution failed: {result.ErrorMessage}";
                AppendLog($"✗ Direct execution failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        ExecutionLog = "";
    }

    [RelayCommand]
    private void GoToStep(WorkflowStep step)
    {
        CurrentStep = step;
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep == WorkflowStep.DatabaseConnection && IsConnected)
        {
            CurrentStep = WorkflowStep.AiConfiguration;
        }
        else if (CurrentStep == WorkflowStep.AiConfiguration && !string.IsNullOrEmpty(SelectedAiModel))
        {
            CurrentStep = WorkflowStep.SqlWorkspace;
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep == WorkflowStep.SqlWorkspace)
        {
            CurrentStep = WorkflowStep.AiConfiguration;
        }
        else if (CurrentStep == WorkflowStep.AiConfiguration)
        {
            CurrentStep = WorkflowStep.DatabaseConnection;
        }
    }

    // @提及表功能方法
    public void UpdateTableSuggestions(string searchText)
    {
        TableSearchText = searchText;
        FilteredTables.Clear();

        if (string.IsNullOrEmpty(searchText))
        {
            foreach (var table in Tables)
            {
                FilteredTables.Add(table);
            }
        }
        else
        {
            foreach (var table in Tables.Where(t => 
                t.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredTables.Add(table);
            }
        }

        IsTableSuggestionVisible = FilteredTables.Count > 0;
    }

    public void ShowTableSuggestions()
    {
        FilteredTables.Clear();
        foreach (var table in Tables)
        {
            FilteredTables.Add(table);
        }
        IsTableSuggestionVisible = Tables.Count > 0;
    }

    public void HideTableSuggestions()
    {
        IsTableSuggestionVisible = false;
        FilteredTables.Clear();
    }

    // 标志位：选择表后抑制TextChanged处理
    public bool SuppressTableSuggestion { get; set; }

    [RelayCommand]
    private void SelectSuggestionTable(string? tableName)
    {
        if (string.IsNullOrEmpty(tableName))
            return;

        // 设置标志位抑制TextChanged重新显示弹出框
        SuppressTableSuggestion = true;

        // 找到最后一个@的位置，替换为@表名
        var lastAtIndex = ChatInput.LastIndexOf('@');
        if (lastAtIndex >= 0)
        {
            ChatInput = ChatInput.Substring(0, lastAtIndex) + "@" + tableName + " ";
        }
        else
        {
            ChatInput += "@" + tableName + " ";
        }

        HideTableSuggestions();
        
        // 延迟重置标志，确保TextChanged事件处理完毕
        Dispatcher.UIThread.Post(() => SuppressTableSuggestion = false);
    }

    // 解析@提及的表名
    private List<string> ParseMentionedTables(string input)
    {
        var mentionedTables = new List<string>();
        var regex = new System.Text.RegularExpressions.Regex(@"@(\w+)");
        var matches = regex.Matches(input);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var tableName = match.Groups[1].Value;
            // 验证是否是有效的表名
            if (Tables.Any(t => t.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
            {
                var actualTableName = Tables.First(t => t.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (!mentionedTables.Contains(actualTableName))
                {
                    mentionedTables.Add(actualTableName);
                }
            }
        }

        return mentionedTables;
    }

    // 从输入中移除@提及，返回纯净的查询文本
    private string RemoveTableMentions(string input)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"@\w+\s*");
        return regex.Replace(input, "").Trim();
    }

    // 显示表结构
    [RelayCommand]
    private async Task ShowTableStructureAsync(string? tableName)
    {
        if (string.IsNullOrEmpty(tableName) || !IsConnected)
            return;

        try
        {
            StatusMessage = $"Loading structure for {tableName}...";
            var connector = _connectionManager.GetConnection(DefaultConnectionId);
            if (connector == null) return;

            var columns = await connector.GetTableColumnsAsync(tableName);
            
            TableStructureTitle = $"表结构: {tableName}";
            TableStructureColumns.Clear();
            foreach (var col in columns)
            {
                TableStructureColumns.Add(col);
            }
            IsTableStructureVisible = true;
            StatusMessage = $"Loaded {columns.Count} columns for {tableName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load table structure: {ex.Message}";
            AppendLog($"✗ Failed to load table structure: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CloseTableStructure()
    {
        IsTableStructureVisible = false;
    }

    private void AppendLog(string message)
    {
        ExecutionLog += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }

    // 场景知识管理方法
    private void LoadKnowledgeList()
    {
        KnowledgeList.Clear();
        var allKnowledge = _knowledgeService.GetAll();
        foreach (var knowledge in allKnowledge)
        {
            KnowledgeList.Add(knowledge);
        }
    }

    [RelayCommand]
    private void ShowKnowledgeManagement()
    {
        IsKnowledgeManagementVisible = true;
        LoadKnowledgeList();
    }

    [RelayCommand]
    private void CloseKnowledgeManagement()
    {
        IsKnowledgeManagementVisible = false;
        ClearKnowledgeForm();
    }

    [RelayCommand]
    private void NewKnowledge()
    {
        ClearKnowledgeForm();
        IsEditingKnowledge = false;
        SelectedKnowledge = null;
    }

    [RelayCommand]
    private void EditKnowledge(ScenarioKnowledge? knowledge)
    {
        if (knowledge == null) return;

        SelectedKnowledge = knowledge;
        KnowledgeTitle = knowledge.Title;
        KnowledgeContent = knowledge.Content;
        KnowledgeKeywords = string.Join(", ", knowledge.Keywords ?? new List<string>());
        IsEditingKnowledge = true;
    }

    [RelayCommand]
    private void SaveKnowledge()
    {
        if (string.IsNullOrWhiteSpace(KnowledgeTitle) || string.IsNullOrWhiteSpace(KnowledgeContent))
        {
            StatusMessage = "标题和内容不能为空";
            return;
        }

        var keywords = KnowledgeKeywords
            .Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        if (IsEditingKnowledge && SelectedKnowledge != null)
        {
            // 更新现有知识
            var updated = new ScenarioKnowledge
            {
                Id = SelectedKnowledge.Id,
                Title = KnowledgeTitle,
                Content = KnowledgeContent,
                Keywords = keywords,
                CreatedAt = SelectedKnowledge.CreatedAt
            };
            if (_knowledgeService.Update(updated))
            {
                StatusMessage = "场景知识已更新";
                LoadKnowledgeList();
                ClearKnowledgeForm();
            }
            else
            {
                StatusMessage = "更新失败";
            }
        }
        else
        {
            // 添加新知识
            var newKnowledge = new ScenarioKnowledge
            {
                Title = KnowledgeTitle,
                Content = KnowledgeContent,
                Keywords = keywords
            };
            _knowledgeService.Add(newKnowledge);
            StatusMessage = "场景知识已添加";
            LoadKnowledgeList();
            ClearKnowledgeForm();
        }
    }

    [RelayCommand]
    private void DeleteKnowledge(ScenarioKnowledge? knowledge)
    {
        if (knowledge == null) return;

        if (_knowledgeService.Delete(knowledge.Id))
        {
            StatusMessage = "场景知识已删除";
            LoadKnowledgeList();
            if (SelectedKnowledge?.Id == knowledge.Id)
            {
                ClearKnowledgeForm();
            }
        }
        else
        {
            StatusMessage = "删除失败";
        }
    }

    private void ClearKnowledgeForm()
    {
        KnowledgeTitle = "";
        KnowledgeContent = "";
        KnowledgeKeywords = "";
        IsEditingKnowledge = false;
        SelectedKnowledge = null;
    }

    partial void OnSelectedKnowledgeChanged(ScenarioKnowledge? value)
    {
        if (value != null)
        {
            // 当从列表选择知识时，自动进入编辑模式
            KnowledgeTitle = value.Title;
            KnowledgeContent = value.Content;
            KnowledgeKeywords = string.Join(", ", value.Keywords ?? new List<string>());
            IsEditingKnowledge = true;
        }
        else
        {
            // 当取消选择时，如果不是新建模式，清空表单
            if (!IsEditingKnowledge)
            {
                ClearKnowledgeForm();
            }
        }
    }

    [RelayCommand]
    private async Task ExtractKeywordsAsync()
    {
        if (string.IsNullOrWhiteSpace(KnowledgeTitle) && string.IsNullOrWhiteSpace(KnowledgeContent))
        {
            StatusMessage = "请先输入标题或内容";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedAiModel))
        {
            StatusMessage = "请先配置AI模型";
            return;
        }

        try
        {
            IsExtractingKeywords = true;
            StatusMessage = "正在提取关键词...";

            var chatService = _sqlAgentService.GetChatService();
            if (chatService == null)
            {
                StatusMessage = "AI服务未配置";
                return;
            }

            var prompt = $@"请根据以下场景知识的标题和内容，提取3-8个关键词。关键词应该：
1. 能够准确反映场景知识的核心内容
2. 便于用户通过提问匹配到该场景知识
3. 使用中文，简洁明了
4. 用逗号分隔，不要包含其他文字

标题：{KnowledgeTitle}
内容：{KnowledgeContent}

请只返回关键词，用逗号分隔，例如：用户,订单,查询,统计";

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage("你是一个关键词提取专家，能够从文本中提取准确的关键词。");
            chatHistory.AddUserMessage(prompt);

            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["temperature"] = 0.3,
                    ["max_tokens"] = 200
                }
            };

            var response = await chatService.GetChatMessageContentsAsync(chatHistory, settings);
            var keywords = response.FirstOrDefault()?.Content?.Trim() ?? "";

            // 移除think标签及其内容（包括多行）
            keywords = System.Text.RegularExpressions.Regex.Replace(
                keywords, 
                @"<think>.*?</think>", 
                "", 
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 移除其他可能的XML/HTML标签
            keywords = System.Text.RegularExpressions.Regex.Replace(
                keywords, 
                @"<[^>]+>", 
                "");

            // 移除markdown格式标记
            keywords = keywords.Replace("**", "").Replace("*", "")
                             .Replace("__", "").Replace("_", "")
                             .Replace("```", "").Replace("`", "");

            // 移除常见的标签文字
            keywords = keywords.Replace("关键词：", "").Replace("关键词:", "")
                             .Replace("关键词是：", "").Replace("关键词是:", "")
                             .Replace("提取的关键词：", "").Replace("提取的关键词:", "")
                             .Replace("关键词列表：", "").Replace("关键词列表:", "");

            // 移除代码块标记
            keywords = keywords.Replace("```", "").Replace("`", "");

            // 如果响应包含其他文字，尝试提取关键词部分（在冒号或换行后）
            if (keywords.Contains("：") || keywords.Contains(":"))
            {
                var parts = keywords.Split(new[] { '：', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    keywords = parts.Last().Trim();
                }
            }

            // 如果包含换行，取第一行（通常是关键词）
            if (keywords.Contains("\n"))
            {
                var lines = keywords.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                keywords = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && 
                    !l.Contains("关键词") && 
                    !l.Contains("提取") &&
                    !l.Contains("：") &&
                    !l.Contains(":"))?.Trim() ?? lines.FirstOrDefault()?.Trim() ?? "";
            }

            // 移除可能的引号、括号等
            keywords = keywords.Trim('"', '\'', '`', '（', '）', '(', ')', '[', ']', '【', '】');

            // 移除多余的空格和标点
            keywords = System.Text.RegularExpressions.Regex.Replace(keywords, @"\s+", " ");
            keywords = keywords.Trim(' ', '，', ',', '。', '.', '；', ';');

            if (!string.IsNullOrWhiteSpace(keywords))
            {
                KnowledgeKeywords = keywords;
                StatusMessage = "关键词提取成功";
            }
            else
            {
                StatusMessage = "未能提取到关键词，请手动输入";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"提取关键词失败: {ex.Message}";
        }
        finally
        {
            IsExtractingKeywords = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLStudio.Core.AI;
using SQLStudio.Core.Database;
using SQLStudio.Core.Services;

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

    public ObservableCollection<DatabaseType> DatabaseTypes { get; } = new(Enum.GetValues<DatabaseType>());
    public ObservableCollection<string> ExecutionHistory { get; } = new();
    public ObservableCollection<string> Databases { get; } = new();
    public ObservableCollection<string> Tables { get; } = new();
    public ObservableCollection<string> AiModels { get; } = new();

    public MainViewModel()
    {
        _connectionManager = new ConnectionManager();
        _sqlAgentService = new SqlAgentService(_connectionManager);
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
            AiModels.Clear();

            var modelService = new OpenAiModelService();
            var models = await modelService.GetModelsAsync(AiApiKey, AiEndpoint);

            foreach (var model in models)
            {
                AiModels.Add(model);
            }

            if (AiModels.Count > 0)
            {
                SelectedAiModel = AiModels[0];
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

            ReconfigureAiService();

            IsConnected = true;
            StatusMessage = $"Connected to {SelectedDatabaseType}://{Host}:{Port}";
            AppendLog($"✓ Connected to {SelectedDatabaseType}://{Host}:{Port}");

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

        var userMessage = new ChatMessage(ChatInput, true);
        ChatMessages.Add(userMessage);
        var userQuery = ChatInput;
        ChatInput = "";

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

            executor.OnPromptSending += (_, e) =>
            {
                AppendLog($"📝 Chat Query: {e.UserQuery}");
            };

            executor.OnStreaming += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.AppendContent(e.Token);
                });
            };

            executor.OnSqlGenerated += (_, e) =>
            {
                GeneratedSql = e.Sql;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.Sql = e.Sql;
                });
            };

            executor.OnSqlExecuted += (_, e) =>
            {
                if (e.ExecutionResult?.Success == true)
                {
                    AppendLog($"✓ Execution successful ({e.ExecutionResult.ExecutionTime.TotalMilliseconds:F2}ms, {e.ExecutionResult.AffectedRows} rows)");
                }
                else
                {
                    AppendLog($"✗ Execution failed: {e.ExecutionResult?.ErrorMessage}");
                }
            };

            var result = await executor.ExecuteAsync(userQuery, null, conversationHistory, cancellationToken);

            aiMessage.IsStreaming = false;

            if (result.Success)
            {
                StatusMessage = $"Query executed successfully";
                QueryResult = result.ExecutionResult?.Data;
            }
            else
            {
                StatusMessage = $"Query failed: {result.ErrorMessage}";
                aiMessage.IsError = true;
                if (string.IsNullOrEmpty(aiMessage.Content))
                {
                    aiMessage.Content = $"Error: {result.ErrorMessage}";
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

    private void AppendLog(string message)
    {
        ExecutionLog += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }
}

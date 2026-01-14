using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SQLStudio.ViewModels;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _content = "";

    [ObservableProperty]
    private string? _sql;

    [ObservableProperty]
    private bool _isUser;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    private string _currentStep = "";

    [ObservableProperty]
    private string _tableAnalysisContent = "";

    [ObservableProperty]
    private string _sqlGenerationContent = "";

    [ObservableProperty]
    private List<string> _analyzedTables = new();

    [ObservableProperty]
    private string? _tableAnalysisReasoning;

    [ObservableProperty]
    private int _totalTableCount;

    [ObservableProperty]
    private int _selectedTableCount;

    [ObservableProperty]
    private bool _showSteps = true;

    public ObservableCollection<ExecutionStepInfo> Steps { get; } = new();

    public ChatMessage(string content, bool isUser)
    {
        Content = content;
        IsUser = isUser;
        
        if (!isUser)
        {
            InitializeSteps();
        }
    }

    private void InitializeSteps()
    {
        Steps.Add(new ExecutionStepInfo { StepName = "分析表结构", Status = StepStatus.Pending });
        Steps.Add(new ExecutionStepInfo { StepName = "生成SQL", Status = StepStatus.Pending });
        Steps.Add(new ExecutionStepInfo { StepName = "执行SQL", Status = StepStatus.Pending });
    }

    public void UpdateStep(int stepIndex, StepStatus status, string? detail = null)
    {
        if (stepIndex >= 0 && stepIndex < Steps.Count)
        {
            Steps[stepIndex].Status = status;
            if (detail != null)
            {
                Steps[stepIndex].Detail = detail;
            }
        }
    }

    // 节流相关字段
    private readonly StringBuilder _contentBuffer = new();
    private readonly StringBuilder _tableAnalysisBuffer = new();
    private readonly StringBuilder _sqlGenerationBuffer = new();
    private Timer? _flushTimer;
    private readonly object _bufferLock = new();
    private const int FlushIntervalMs = 50; // 50ms刷新一次UI

    public void AppendContent(string token)
    {
        lock (_bufferLock)
        {
            _contentBuffer.Append(token);
            EnsureFlushTimer();
        }
    }

    public void AppendTableAnalysis(string token)
    {
        lock (_bufferLock)
        {
            _tableAnalysisBuffer.Append(token);
            EnsureFlushTimer();
        }
    }

    public void AppendSqlGeneration(string token)
    {
        lock (_bufferLock)
        {
            _sqlGenerationBuffer.Append(token);
            EnsureFlushTimer();
        }
    }

    private void EnsureFlushTimer()
    {
        _flushTimer ??= new Timer(_ => FlushBuffers(), null, FlushIntervalMs, Timeout.Infinite);
    }

    private void FlushBuffers()
    {
        string contentToAdd;
        string tableAnalysisToAdd;
        string sqlGenerationToAdd;

        lock (_bufferLock)
        {
            contentToAdd = _contentBuffer.ToString();
            tableAnalysisToAdd = _tableAnalysisBuffer.ToString();
            sqlGenerationToAdd = _sqlGenerationBuffer.ToString();
            
            _contentBuffer.Clear();
            _tableAnalysisBuffer.Clear();
            _sqlGenerationBuffer.Clear();
            
            _flushTimer?.Dispose();
            _flushTimer = null;
        }

        if (string.IsNullOrEmpty(contentToAdd) && 
            string.IsNullOrEmpty(tableAnalysisToAdd) && 
            string.IsNullOrEmpty(sqlGenerationToAdd))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(contentToAdd))
                Content += contentToAdd;
            if (!string.IsNullOrEmpty(tableAnalysisToAdd))
                TableAnalysisContent += tableAnalysisToAdd;
            if (!string.IsNullOrEmpty(sqlGenerationToAdd))
                SqlGenerationContent += sqlGenerationToAdd;
        });
    }

    /// <summary>
    /// 强制刷新所有缓冲区（在流式输出结束时调用）
    /// </summary>
    public void FlushAllBuffers()
    {
        FlushBuffers();
    }
}

public partial class ExecutionStepInfo : ObservableObject
{
    [ObservableProperty]
    private string _stepName = "";

    [ObservableProperty]
    private StepStatus _status = StepStatus.Pending;

    [ObservableProperty]
    private string _detail = "";
}

public enum StepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

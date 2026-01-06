using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public void AppendContent(string token)
    {
        Content += token;
    }

    public void AppendTableAnalysis(string token)
    {
        TableAnalysisContent += token;
    }

    public void AppendSqlGeneration(string token)
    {
        SqlGenerationContent += token;
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

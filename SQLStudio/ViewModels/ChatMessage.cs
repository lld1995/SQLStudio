using System;
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

    public ChatMessage(string content, bool isUser)
    {
        Content = content;
        IsUser = isUser;
    }

    public void AppendContent(string token)
    {
        Content += token;
    }
}

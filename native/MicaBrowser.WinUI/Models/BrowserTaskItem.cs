using MicaBrowser.Helpers;

namespace MicaBrowser.Models;

public sealed class BrowserTaskItem : ObservableObject
{
    private string _cpu = "0%";
    private string _memory = "0 MB";
    private string _detail = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public long MemoryBytes { get; set; }

    public string Cpu
    {
        get => _cpu;
        set => SetProperty(ref _cpu, value);
    }

    public string Memory
    {
        get => _memory;
        set => SetProperty(ref _memory, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}

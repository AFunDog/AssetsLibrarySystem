using System.Collections.ObjectModel;
using System.ComponentModel;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.BackgroundTasks;

public interface IBackgroundTaskService : INotifyPropertyChanged
{
    ObservableCollection<BackgroundTaskEntry> Tasks { get; }

    bool HasActiveTaskSummary { get; }

    string ActiveTaskSummary { get; }

    string BeginTask(string title, string stageText, string? detailText = null);

    void UpdateTask(string taskId, string stageText, string? detailText = null);

    void CompleteTask(string taskId, string? stageText = null, string? detailText = null);

    void FailTask(string taskId, string detailText, string? stageText = null);
}

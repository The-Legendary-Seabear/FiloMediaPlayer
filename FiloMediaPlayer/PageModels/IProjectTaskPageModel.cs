using CommunityToolkit.Mvvm.Input;
using FiloMediaPlayer.Models;

namespace FiloMediaPlayer.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}
using System.ComponentModel;

namespace VirtualDisplayDriver.ExampleApp.Services;

public interface INavigationService : INotifyPropertyChanged
{
    object? CurrentViewModel { get; }
    void NavigateTo<T>() where T : class;
    void NavigateTo(Type viewModelType);
}

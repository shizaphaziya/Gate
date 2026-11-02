using CommunityToolkit.Mvvm.ComponentModel;
using Gate.Client.Services;
using Gate.Shared;

namespace Gate.Client.ViewModels;

public partial class ServiceItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private ServiceState _state;

    [ObservableProperty]
    private bool _canManage;

    [ObservableProperty]
    private bool _canViewLogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    private bool _isSelected;

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(CardBorderBrush));
    }

    public string CardBorderBrush => IsSelected
        ? (ThemeService.CurrentTheme == AppTheme.Pink ? "#FF4081" : "#3B82F6")
        : (ThemeService.CurrentTheme == AppTheme.Light ? "#E5E7EB" : ThemeService.CurrentTheme == AppTheme.Pink ? "#F8BBD0" : "#2B2D3A");

    public ServiceItemViewModel(ServiceInfoDto dto)
    {
        _name = dto.Name;
        _description = dto.Description;
        _state = dto.State;
        _canManage = dto.CanManage;
        _canViewLogs = dto.CanViewLogs;
    }

    public string StateText => State switch
    {
        ServiceState.Active => "Active",
        ServiceState.Inactive => "Inactive",
        ServiceState.Failed => "Failed",
        _ => "Unknown"
    };

    public string StatusColor => State switch
    {
        ServiceState.Active => "#10B981",    // Green
        ServiceState.Inactive => "#EF4444",  // Red
        ServiceState.Failed => "#DC2626",    // Dark Red
        _ => "#6B7280"                       // Gray
    };
}

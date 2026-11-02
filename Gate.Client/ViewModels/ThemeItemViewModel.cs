using Gate.Client.Services;
using Material.Icons;

namespace Gate.Client.ViewModels;

public class ThemeItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public MaterialIconKind Icon { get; set; }
    public AppTheme Theme { get; set; }
}

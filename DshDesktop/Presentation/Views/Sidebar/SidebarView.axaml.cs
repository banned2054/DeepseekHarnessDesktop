using Avalonia.Controls;

namespace DshDesktop.Presentation.Views.Sidebar;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
    }

    /// <summary>同步左栏内容表面的宽度上限；列宽上限由 MainWindow 的 clamp 逻辑统一计算。</summary>
    public void SetSurfaceMaxWidth(double maxWidth) => Surface.MaxWidth = maxWidth;
}

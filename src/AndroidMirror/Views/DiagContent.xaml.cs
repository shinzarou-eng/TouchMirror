using System.Windows.Controls;
using TouchMirror.ViewModels;

namespace TouchMirror.Views;

public partial class DiagContent : UserControl
{
    public DiagContent()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.DebugScrollToEnd += () => ReportBox.ScrollToEnd();
        };
    }
}

using Wpf.Ui.Controls;

namespace TouchMirror.Services;

public static class PluginIcons
{
    public static SymbolRegular For(string? id) => id switch
    {
        "almanax" => SymbolRegular.CalendarLtr24,
        "automute" => SymbolRegular.SpeakerMute24,
        "bridge" => SymbolRegular.PlugConnected24,
        "checklist" => SymbolRegular.TaskListLtr24,
        "guides" => SymbolRegular.BookOpenGlobe24,
        "hud" => SymbolRegular.DataUsage24,
        "reconnect" => SymbolRegular.ArrowSync24,
        "session-log" => SymbolRegular.DocumentText24,
        "splits" => SymbolRegular.SplitHorizontal24,
        "uptime" => SymbolRegular.Timer24,
        _ => SymbolRegular.PuzzlePiece24
    };
}

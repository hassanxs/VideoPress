using System.Windows;
using System.Windows.Input;
using VideoPress.Models;
using VideoPress.ViewModels;
using WinForms = System.Windows.Forms;

namespace VideoPress.Views;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;

    // ── Drag-to-reorder state ─────────────────────────────────────────────────
    private System.Windows.Point _dragStart;
    private VideoFile?           _dragItem;
    private bool                 _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        Drop     += OnFileDrop;
        DragOver += OnDragOver;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            VM.AddFilesCommand.Execute(files);
    }

    private void DropBorder_Click(object sender, MouseButtonEventArgs e)
        => OpenFilePicker();

    private void BrowseFiles_Click(object sender, RoutedEventArgs e)
        => OpenFilePicker();

    private void OpenFilePicker()
    {
        using var dlg = new WinForms.OpenFileDialog
        {
            Title       = "Select video files to compress",
            Multiselect = true,
            Filter      = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv;*.flv;*.ts;*.3gp|All files|*.*"
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            VM.AddFilesCommand.Execute(dlg.FileNames);
    }

    // ── Comparison overlay ────────────────────────────────────────────────────

    private void CompareBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is VideoFile vf)
            VM.OpenCompareCommand.Execute(vf);
    }

    private void CompareOverlay_Close(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => VM.CloseCompareCommand.Execute(null);

    private void CompareOverlay_StopClose(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => e.Handled = true;

    private void CompareClose_Click(object sender, RoutedEventArgs e)
        => VM.CloseCompareCommand.Execute(null);

    // ── File card expand/collapse ─────────────────────────────────────────────

    private void FileCard_ExpandToggle(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VideoFile vf)
            VM.ToggleExpandCommand.Execute(vf);
    }

    // ── Info button — expands card and scrolls into view ─────────────────────

    private void InfoBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is VideoFile vf)
        {
            // Expand the card if not already expanded
            if (!vf.IsExpanded)
                VM.ToggleExpandCommand.Execute(vf);

            // Scroll the ListBox item into view
            var lb = FindVisualChild<System.Windows.Controls.ListBox>(this);
            lb?.ScrollIntoView(vf);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    // ── Drag-to-reorder handlers ──────────────────────────────────────────────

    private void FileList_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _isDragging = false;

        // Find the VideoFile from the clicked element
        var el = e.OriginalSource as DependencyObject;
        while (el != null)
        {
            if (el is System.Windows.Controls.ListBoxItem item &&
                item.Content is VideoFile vf)
            {
                _dragItem = vf;
                return;
            }
            el = System.Windows.Media.VisualTreeHelper.GetParent(el);
        }
        _dragItem = null;
    }

    private void FileList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragItem == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            return;

        var pos  = e.GetPosition(null);
        var diff = _dragStart - pos;

        if (_isDragging || (
            Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
        {
            _isDragging = true;
            DragDrop.DoDragDrop(FileListBox, _dragItem, DragDropEffects.Move);
            _dragItem   = null;
            _isDragging = false;
        }
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(VideoFile))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(VideoFile))) return;

        var dragged = (VideoFile)e.Data.GetData(typeof(VideoFile));
        var fromIdx = VM.Files.IndexOf(dragged);

        // Find drop target item
        var el = e.OriginalSource as DependencyObject;
        VideoFile? target = null;
        while (el != null)
        {
            if (el is System.Windows.Controls.ListBoxItem item &&
                item.Content is VideoFile vf && vf != dragged)
            {
                target = vf;
                break;
            }
            el = System.Windows.Media.VisualTreeHelper.GetParent(el);
        }

        var toIdx = target != null ? VM.Files.IndexOf(target) : VM.Files.Count - 1;
        VM.MoveFileCommand.Execute(new[] { fromIdx, toIdx });
    }

    // ── Preset panel ──────────────────────────────────────────────────────────

    private void PresetBackdrop_Click(object sender, MouseButtonEventArgs e)
        => VM.ShowPresetsPanel = false;

    private void PresetItem_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Preset preset)
            VM.ApplyPresetCommand.Execute(preset);
    }

    private void PresetNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            VM.SavePresetCommand.Execute(null);
    }

    // ── Theme picker ──────────────────────────────────────────────────────────

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        => ThemePopup.IsOpen = !ThemePopup.IsOpen;

    private void ThemeOption_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string choice)
        {
            VM.SetThemeCommand.Execute(choice);
            ThemePopup.IsOpen = false;
        }
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    private void ClearLog_Click(object sender, RoutedEventArgs e) => VM.LogText = "";

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(VM.LogText))
            System.Windows.Clipboard.SetText(VM.LogText);
    }
}

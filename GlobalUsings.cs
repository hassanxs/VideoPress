// ── Resolve ALL WPF vs WinForms vs System.Drawing ambiguities globally ──────
// UseWPF + UseWindowsForms causes these namespaces to flood every file.
// This file pins every ambiguous type to the WPF/Media version project-wide.

// System.Windows vs System.Windows.Forms
global using Application       = System.Windows.Application;
global using Clipboard         = System.Windows.Clipboard;
global using DataFormats       = System.Windows.DataFormats;
global using DragDropEffects   = System.Windows.DragDropEffects;
global using DragEventArgs     = System.Windows.DragEventArgs;
global using MessageBox        = System.Windows.MessageBox;
global using MessageBoxButton  = System.Windows.MessageBoxButton;
global using MessageBoxImage   = System.Windows.MessageBoxImage;

// System.Windows.Data vs System.Windows.Forms
global using Binding           = System.Windows.Data.Binding;

// System.Drawing vs System.Windows.Media
global using Color             = System.Windows.Media.Color;
global using ColorConverter    = System.Windows.Media.ColorConverter;
global using KeyEventArgs     = System.Windows.Input.KeyEventArgs;
global using AppTheme = VideoPress.Services.AppTheme;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;
using System.Diagnostics;
using OpenUtau.App.Views;

namespace OpenUtau.App.Controls {
    public partial class DictionaryEditorControl : UserControl {
        public DictionaryEditorViewModel ViewModel { get; } = new DictionaryEditorViewModel();

        public static readonly StyledProperty<UVoicePart?> PartProperty =
            AvaloniaProperty.Register<DictionaryEditorControl, UVoicePart?>(nameof(Part));

        public UVoicePart? Part {
            get => GetValue(PartProperty);
            set => SetValue(PartProperty, value);
        }

        private System.Collections.Specialized.INotifyCollectionChanged? _trackedRows;
        private bool _isInternalMove = false;
        private DispatcherTimer _autoScrollTimer;
        private double _scrollVelocityY = 0;
        private ScrollViewer? _activeScrollViewer;
        private ScrollViewer? _gridScrollViewer;
        private ScrollViewer? _objectsScrollViewer;

        public DictionaryEditorControl() {
            InitializeComponent();
            ViewModel.ShowParseError.RegisterHandler(DoShowParseErrorAsync);

            _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;

            ViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ViewModel.SelectedCategory)) {
                    Dispatcher.UIThread.Post(() => {
                        RebuildGridColumns(ViewModel.SelectedCategory);
                        
                        if (ViewModel.SelectedCategory != null && ViewModel.SelectedCategory.Columns.Count > 0) {
                            ViewModel.ReplaceColumn = ViewModel.SelectedCategory.Columns[0];
                        }

                        if (_trackedRows != null) {
                            _trackedRows.CollectionChanged -= Rows_CollectionChanged;
                        }
                        if (ViewModel.SelectedCategory != null) {
                            _trackedRows = ViewModel.SelectedCategory.Rows;
                            _trackedRows.CollectionChanged += Rows_CollectionChanged;
                        }
                    }, DispatcherPriority.Normal);
                }
            };
            
            ViewModel.ColumnsChanged += () => {
                Dispatcher.UIThread.Post(() => {
                    RebuildGridColumns(ViewModel.SelectedCategory);
                    
                    if (ViewModel.SelectedCategory != null && ViewModel.SelectedCategory.Columns.Count > 0) {
                        if (string.IsNullOrEmpty(ViewModel.ReplaceColumn) || !ViewModel.SelectedCategory.Columns.Contains(ViewModel.ReplaceColumn)) {
                            ViewModel.ReplaceColumn = ViewModel.SelectedCategory.Columns[0];
                        }
                    } else {
                        ViewModel.ReplaceColumn = null;
                    }
                }, DispatcherPriority.Normal);
            };

            this.Loaded += (s, e) => {
                LoadDictionaryForPart(Part);
                var objectsListBox = this.FindControl<ListBox>("ObjectsListBox");
                if (objectsListBox != null) {
                    objectsListBox.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, ObjectsList_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
                    objectsListBox.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, ObjectsList_PointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
                    objectsListBox.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, ObjectsList_PointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
                }
            };
            EditorGrid.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, EditorGrid_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            EditorGrid.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, EditorGrid_PointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            EditorGrid.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, EditorGrid_PointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private void AutoScrollTimer_Tick(object? sender, EventArgs e) {
            if (_activeScrollViewer != null && _scrollVelocityY != 0) {
                double maxScroll = Math.Max(0, _activeScrollViewer.Extent.Height - _activeScrollViewer.Viewport.Height);
                if (maxScroll > 0) {
                    double newY = Math.Max(0, Math.Min(_activeScrollViewer.Offset.Y + _scrollVelocityY, maxScroll));
                    if (_activeScrollViewer.Offset.Y != newY) {
                        _activeScrollViewer.Offset = new Vector(_activeScrollViewer.Offset.X, newY);
                    }
                }
            }
        }

        private void Rows_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            if (_isInternalMove) return;

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null && e.NewItems.Count == 1) {
                var newRow = e.NewItems[0] as DynamicYamlRow;
                if (newRow == null || newRow.IsComment) return;

                bool isEmpty = true;
                foreach (var val in newRow.GetData().Values) {
                    if (!string.IsNullOrWhiteSpace(val)) {
                        isEmpty = false;
                        break;
                    }
                }
                if (isEmpty) {
                    Dispatcher.UIThread.Post(() => {
                        EditorGrid.SelectedItem = newRow;
                        EditorGrid.ScrollIntoView(newRow, null);
                        
                        if (EditorGrid.Columns.Count > 0) {
                            EditorGrid.CurrentColumn = EditorGrid.Columns[0];
                            EditorGrid.BeginEdit();
                        }
                    }, DispatcherPriority.Background); 
                }
            }
        }

        private void EditorGrid_LoadingRow(object? sender, Avalonia.Controls.DataGridRowEventArgs e) {
            e.Row.Header = (e.Row.Index + 1).ToString();
        }

        private void EditorGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e) {
            if (e.Row.DataContext is DynamicYamlRow row) {
                if (!row.IsComment) {
                    string colName = e.Column.Header?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(colName)) {
                        string val = row[colName];
                        if (val != null && val.Contains(",")) {
                            
                            bool inQuotes = false;
                            var sb = new System.Text.StringBuilder();
                            
                            foreach (char c in val) {
                                if (c == '"') inQuotes = !inQuotes;
                                
                                if (c == ',' && !inQuotes) {
                                    sb.Append(' ');
                                } else {
                                    sb.Append(c);
                                }
                            }
                            string cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                            Dispatcher.UIThread.Post(() => {
                                row[colName] = cleaned;
                            }, DispatcherPriority.Normal);
                        }
                    }
                }

                CheckAndRemoveEmptyRow(row);
            }
        }

        private void CheckAndRemoveEmptyRow(DynamicYamlRow row) {
            bool hasValidData = false;

            if (row.IsComment) {
                string text = row.CommentText?.Trim() ?? "";
                if (!string.IsNullOrEmpty(text) && text != "#" && text != "," && text != "# ,") {
                    hasValidData = true;
                }
            } else {
                foreach (var val in row.GetData().Values) {
                    string cleanVal = val?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(cleanVal) && cleanVal != ",") {
                        hasValidData = true;
                        break;
                    }
                }
            }

            // If empty, silently remove it
            if (!hasValidData) {
                Dispatcher.UIThread.Post(() => {
                    ViewModel.SelectedCategory?.Rows.Remove(row);
                    ViewModel.RefreshIndices?.Invoke();
                }, DispatcherPriority.Normal);
            }
        }

        private void CommentGrid_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
            if (sender is Grid grid && grid.DataContext is DynamicYamlRow row) {
                var gridControl = this.FindControl<DataGrid>("EditorGrid");
                if (gridControl == null) return;

                var point = e.GetCurrentPoint(grid);

                if (point.Properties.IsRightButtonPressed) {
                    if (!gridControl.SelectedItems.Contains(row)) {
                        gridControl.SelectedItem = row;
                    }
                    return; 
                }

                var modifiers = e.KeyModifiers;
                
                if (modifiers.HasFlag(KeyModifiers.Control)) {
                    if (gridControl.SelectedItems.Contains(row)) gridControl.SelectedItems.Remove(row);
                    else gridControl.SelectedItems.Add(row);
                } 
                else if (modifiers.HasFlag(KeyModifiers.Shift)) {
                    var lastSelected = gridControl.SelectedItem as DynamicYamlRow;
                    int startIndex = ViewModel.SelectedCategory?.Rows.IndexOf(lastSelected ?? row) ?? 0;
                    int endIndex = ViewModel.SelectedCategory?.Rows.IndexOf(row) ?? 0;
                    
                    gridControl.SelectedItems.Clear();
                    int min = Math.Min(startIndex, endIndex);
                    int max = Math.Max(startIndex, endIndex);
                    for (int i = min; i <= max; i++) {
                        if (ViewModel.SelectedCategory?.Rows.Count > i) {
                            gridControl.SelectedItems.Add(ViewModel.SelectedCategory.Rows[i]);
                        }
                    }
                } 
                else {
                    gridControl.SelectedItem = row;
                }
            }
        }

        private void CommentGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
            if (sender is Grid grid && grid.DataContext is DynamicYamlRow row) {
                ViewModel.SelectedRow = row;
                row.IsEditingComment = true; 
                
                if (row.CommentText == "# New Comment..." || row.CommentText == "# New comment...") {
                    row.CommentText = "# ";
                }
                
                Dispatcher.UIThread.Post(() => {
                    var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
                    if (textBox != null) {
                        textBox.Focus();
                        textBox.CaretIndex = textBox.Text?.Length ?? 0;
                    }
                }, DispatcherPriority.Normal);
            }
        }

        private void CommentTextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
            if (sender is TextBox tb && tb.DataContext is DynamicYamlRow row) {
                row.IsEditingComment = false; 
                CheckAndRemoveEmptyRow(row); 
            }
        }

        private void CommentTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e) {
            if (e.Key == Avalonia.Input.Key.Enter || e.Key == Avalonia.Input.Key.Escape) {
                if (sender is TextBox tb && tb.DataContext is DynamicYamlRow row) {
                    row.IsEditingComment = false; 
                    CheckAndRemoveEmptyRow(row); 
                }
                e.Handled = true; 
            }
        }

        private void CommentTextBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) {
            if (sender is TextBox tb && tb.DataContext is DynamicYamlRow row) {
                row.CommentText = tb.Text ?? "";
            }
        }

        private void EditorGrid_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e) {
            if (EditorGrid.SelectedItem != null) {
                EditorGrid.ScrollIntoView(EditorGrid.SelectedItem, null);
            }
        }

        private void EditorGrid_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
            var point = e.GetCurrentPoint(this);
            
            if (point.Properties.IsLeftButtonPressed) {
                _dragStartPoint = point.Position;
                _isDragging = false;
                
                var visual = e.Source as Avalonia.Visual;
                while (visual != null && !(visual is DataGridRow)) {
                    visual = visual.GetVisualParent() as Avalonia.Visual;
                }
                
                if (visual is DataGridRow row && row.DataContext is DynamicYamlRow dataRow) {
                    // multi-selection
                    if (EditorGrid.SelectedItems.Contains(dataRow)) {
                        _draggedRows = EditorGrid.SelectedItems.Cast<DynamicYamlRow>().ToList();
                    } else {
                        // single row we clicked
                        _draggedRows = new List<DynamicYamlRow> { dataRow };
                    }
                } else {
                    _draggedRows = null;
                }
            }
        }

        private void EditorGrid_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e) {
            _isDragging = false;
            _draggedRows = null;
            _autoScrollTimer.Stop();
            _scrollVelocityY = 0;
        }
        
        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);
            if (DataContext is DictionaryEditorViewModel vm) {
                vm.RefreshIndices = () => {
                    var grid = this.FindControl < DataGrid > ("EditorGrid");
                    if (grid == null || vm.SelectedCategory == null) return;

                    Dispatcher.UIThread.Post(() => {
                        foreach(var row in grid.GetVisualDescendants().OfType < DataGridRow > ()) {
                            if (row.DataContext is DynamicYamlRow item) {
                                int realIndex = vm.SelectedCategory.Rows.IndexOf(item);

                                if (realIndex >= 0) {
                                    row.Header = (realIndex + 1).ToString();
                                }
                            }
                        }
                    }, DispatcherPriority.Loaded);
                };
            }
        }
        
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == PartProperty) {
                Log.Information("DictionaryEditor: PartProperty changed in UI.");
                LoadDictionaryForPart((UVoicePart?)change.NewValue);
            }
        }

        private void RebuildGridColumns(YamlCategory? category) {
            var grid = this.FindControl<DataGrid>("EditorGrid");
            if (grid == null) return;

            var currentData = grid.ItemsSource;
            grid.ItemsSource = null;

            grid.Columns.Clear();
            if (category != null) {
                foreach (var colName in category.Columns) {
                    var column = new DataGridTextColumn {
                        Header = colName,
                        Binding = new Binding($"[{colName}]"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                    };
                    grid.Columns.Add(column);
                }
            }
            grid.ItemsSource = currentData;
        }

        private void OnRefreshClicked(object? sender, RoutedEventArgs e) {
            Log.Information("DictionaryEditor: Refresh button clicked.");
            LoadDictionaryForPart(Part);
        }

        private void OnOpenFileClicked(object? sender, RoutedEventArgs e) {
            string filePath = ViewModel.GetSelectedFileFullPath();

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) {
                try {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                } catch (Exception ex) {
                    Serilog.Log.Error(ex, $"DictionaryEditor: Failed to open file in external editor: {filePath}");
                }
            }
        }

        private void LoadDictionaryForPart(UVoicePart? part) {
            Log.Information("--- DictionaryEditor: Attempting to load dictionary ---");

            if (part == null) {
                Log.Information("DictionaryEditor: ABORT - Part is null.");
                ViewModel.ClearContext();
                return;
            }

            var project = DocManager.Inst.Project;
            if (project == null || part.trackNo >= project.tracks.Count) {
                ViewModel.ClearContext();
                return;
            }

            var track = project.tracks[part.trackNo];
            var singer = track.Singer;
            
            var allFiles = new List<string>();
            string singerLoc = singer?.Location ?? "";

            // Singer Directory
            if (!string.IsNullOrEmpty(singerLoc) && Directory.Exists(singerLoc)) {
                Log.Information($"DictionaryEditor: Found singer '{singer?.Name}'. Location path is: '{singerLoc}'");
                allFiles.AddRange(Directory.GetFiles(singerLoc, "*.*", SearchOption.AllDirectories));
            } else {
                Log.Information("DictionaryEditor: No singer loaded on track. Searching plugins folder only.");
            }

            // Plugins Directory
            string pluginsDir = OpenUtau.Core.PathManager.Inst.PluginsPath;
            if (Directory.Exists(pluginsDir)) {
                allFiles.AddRange(Directory.GetFiles(pluginsDir, "*.*", SearchOption.AllDirectories));
            }

            if (allFiles.Count == 0) {
                ViewModel.ClearContext();
                return;
            }

            var excludedFiles = new HashSet<string> { "character.yaml", "dsconfig.yaml", "vocoder.yaml" };
            var validFiles = allFiles
                .Where(f => {
                    string fileName = Path.GetFileName(f).ToLower();
                    bool isValidYaml = fileName.EndsWith(".yaml") && !excludedFiles.Contains(fileName);
                    bool isPresamp = fileName == "presamp.ini";
                    
                    return isValidYaml || isPresamp;
                })
                .ToList();

            var displayNames = new List<string>();
            var fileMap = new Dictionary<string, string>();

            void ProcessGroup(List<string> files, bool isPlugin) {
                var grouped = files.GroupBy(f => Path.GetFileName(f).ToLower());
                
                foreach (var group in grouped) {
                    if (group.Count() == 1) {
                        var filePath = group.First();
                        string fileName = Path.GetFileName(filePath);
                        string displayName = isPlugin ? $"{fileName} (plugins)" : fileName;
                        
                        displayNames.Add(displayName);
                        fileMap[displayName] = filePath;
                    } else {
                        foreach (var filePath in group) {
                            string fileName = Path.GetFileName(filePath);
                            string folderName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";
                            
                            string displayName;
                            if (isPlugin) {
                                displayName = $"{fileName} ({folderName} plugin)";
                            } else {
                                bool isRoot = false;
                                try {
                                    isRoot = !string.IsNullOrEmpty(singerLoc) && Path.GetFullPath(Path.GetDirectoryName(filePath)!) == Path.GetFullPath(singerLoc);
                                } catch { }
                                
                                displayName = isRoot ? fileName : $"{fileName} ({folderName})";
                            }

                            string finalName = displayName;
                            int counter = 1;
                            while (fileMap.ContainsKey(finalName)) {
                                finalName = $"{displayName} ({counter++})";
                            }

                            displayNames.Add(finalName);
                            fileMap[finalName] = filePath;
                        }
                    }
                }
            }

            var singerFilesList = validFiles.Where(f => !f.StartsWith(pluginsDir)).ToList();
            var pluginFilesList = validFiles.Where(f => f.StartsWith(pluginsDir)).ToList();
            
            ProcessGroup(singerFilesList, false);
            ProcessGroup(pluginFilesList, true);
            
            Log.Information($"DictionaryEditor: Found {displayNames.Count} valid dictionary/presamp files.");
            
            string targetFileName = "";
            var currentPhonemizer = track.Phonemizer; 
            
            if (currentPhonemizer != null) {
                string phonemizerName = currentPhonemizer.GetType().Name;
                try {
                    if (phonemizerName.ToLower().Contains("presamp")) {
                        targetFileName = "presamp.ini";
                    } else {
                        var type = currentPhonemizer.GetType();
                        var flags = System.Reflection.BindingFlags.Instance | 
                                    System.Reflection.BindingFlags.Public | 
                                    System.Reflection.BindingFlags.NonPublic;

                        var prop = type.GetProperty("YamlFileName", flags);
                        if (prop != null) {
                            targetFileName = prop.GetValue(currentPhonemizer) as string ?? "";
                        }
                        if (string.IsNullOrEmpty(targetFileName)) {
                            var method = type.GetMethod("GetDictionaryName", flags);
                            if (method != null) {
                                targetFileName = method.Invoke(currentPhonemizer, null) as string ?? "";
                            }
                        }
                    }
                } catch (Exception ex) {
                    Log.Warning(ex, $"DictionaryEditor: Could not extract target file name from phonemizer '{phonemizerName}'");
                }
                if (!string.IsNullOrEmpty(targetFileName)) {
                    Log.Information($"DictionaryEditor: Auto-detected target dictionary '{targetFileName}' for phonemizer '{phonemizerName}'");
                }
            }
            
            ViewModel.SetSingerContext(singerLoc, fileMap, targetFileName);
        }

        private async System.Threading.Tasks.Task DoShowParseErrorAsync(ReactiveUI.IInteractionContext<OpenUtau.App.ViewModels.DictionaryErrorWindowViewModel, bool> interaction) {
            var dialog = new DictionaryErrorWindow {
                DataContext = interaction.Input
            };

            bool userSavedEdits = false;
            var topLevelWindow = Avalonia.Controls.TopLevel.GetTopLevel(this) as Window;
            
            if (topLevelWindow != null) {
                userSavedEdits = await dialog.ShowDialog<bool>(topLevelWindow);
            } else {
                dialog.Show();
            }
            interaction.SetOutput(userSavedEdits);
        }

        private bool _isDragging = false;
        private Point _dragStartPoint;
        private List<DynamicYamlRow>? _draggedRows;

        private async void EditorGrid_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e) {
            if (_draggedRows == null || _draggedRows.Count == 0 || ViewModel.SelectedCategory == null) return;
            var point = e.GetCurrentPoint(this);
            
            if (point.Properties.IsLeftButtonPressed && !_isDragging) {
                if (Math.Abs(point.Position.Y - _dragStartPoint.Y) > 10) {
                    _isDragging = true;
                    
                    var dragData = new DataObject();
                    dragData.Set("RowData", _draggedRows);
                    
                    await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
                    
                    _isDragging = false;
                    _draggedRows = null;
                    _autoScrollTimer.Stop();
                    _scrollVelocityY = 0;
                }
            }
        }
        
        private void EditorGrid_DragOver(object? sender, DragEventArgs e) {
            if (e.Data.Contains("RowData")) {
                e.DragEffects = DragDropEffects.Move;

                if (_gridScrollViewer == null) {
                    var rowsPresenter = EditorGrid.GetVisualDescendants()
                        .OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>()
                        .FirstOrDefault();
                        
                    if (rowsPresenter != null) {
                        var parent = rowsPresenter.GetVisualParent();
                        while (parent != null && !(parent is ScrollViewer)) {
                            parent = parent.GetVisualParent();
                        }
                        _gridScrollViewer = parent as ScrollViewer;
                    }
                }

                if (_gridScrollViewer != null) {
                    var position = e.GetPosition(_gridScrollViewer);
                    double edgeThreshold = 40.0;

                    if (position.Y < edgeThreshold) {
                        _scrollVelocityY = -15.0; // Scroll UP
                        _activeScrollViewer = _gridScrollViewer;
                        _autoScrollTimer.Start();
                    } else if (position.Y > _gridScrollViewer.Bounds.Height - edgeThreshold) {
                        _scrollVelocityY = 15.0; // Scroll DOWN
                        _activeScrollViewer = _gridScrollViewer;
                        _autoScrollTimer.Start();
                    } else {
                        _scrollVelocityY = 0;
                        _autoScrollTimer.Stop();
                    }
                }
            } else {
                e.DragEffects = DragDropEffects.None;
            }
        }
        
        private void EditorGrid_Drop(object? sender, DragEventArgs e) {
            if (e.Data.Contains("RowData") && e.Data.Get("RowData") is List<DynamicYamlRow> draggedRows) {
                var visual = e.Source as Avalonia.Visual;
                while (visual != null && !(visual is DataGridRow)) {
                    visual = visual.GetVisualParent() as Avalonia.Visual;
                }
                
                if (visual is DataGridRow targetRow && targetRow.DataContext is DynamicYamlRow targetDataRow) {
                    var rows = ViewModel.SelectedCategory?.Rows;
                    if (rows == null) return;

                    if (draggedRows.Contains(targetDataRow)) {
                        _isDragging = false;
                        _draggedRows = null;
                        _autoScrollTimer.Stop();
                        _scrollVelocityY = 0;
                        return;
                    }
                    int targetIndex = rows.IndexOf(targetDataRow);
                    if (targetIndex != -1) {
                        _isInternalMove = true; 
                        
                        foreach (var row in draggedRows) {
                            rows.Remove(row);
                        }
                        targetIndex = rows.IndexOf(targetDataRow);
                        if (targetIndex == -1) targetIndex = rows.Count;

                        for (int i = 0; i < draggedRows.Count; i++) {
                            rows.Insert(targetIndex + i, draggedRows[i]);
                        }
                        _isInternalMove = false; 
                        EditorGrid.SelectedItems.Clear();
                        foreach (var row in draggedRows) {
                            EditorGrid.SelectedItems.Add(row);
                        }
                        
                        if (draggedRows.Count > 0) {
                            EditorGrid.ScrollIntoView(draggedRows[0], null); 
                        }
                        EditorGrid.Focus();
                        ViewModel.RefreshIndices?.Invoke();
                    }
                }
            }
            _isDragging = false;
            _draggedRows = null;
            _autoScrollTimer.Stop();
            _scrollVelocityY = 0;
        }

        private bool _isDraggingObj = false;
        private Point _dragStartPointObj;
        private YamlCategory? _draggedObj;

        private void ObjectsList_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed) {
                _dragStartPointObj = point.Position;
                _isDraggingObj = false;
                
                var visual = e.Source as Avalonia.Visual;
                while (visual != null && !(visual is ListBoxItem)) {
                    visual = visual.GetVisualParent() as Avalonia.Visual;
                }
                
                if (visual is ListBoxItem item && item.DataContext is YamlCategory cat) {
                    _draggedObj = cat;
                } else {
                    _draggedObj = null;
                }
            }
        }
        
        private async void ObjectsList_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e) {
            if (_draggedObj == null) return;
            var point = e.GetCurrentPoint(this);
            
            if (point.Properties.IsLeftButtonPressed && !_isDraggingObj) {
                if (Math.Abs(point.Position.X - _dragStartPointObj.X) > 3 || Math.Abs(point.Position.Y - _dragStartPointObj.Y) > 3) {
                    _isDraggingObj = true;
                    var dragData = new DataObject();
                    dragData.Set("CategoryData", _draggedObj);
                    
                    await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
                    
                    _isDraggingObj = false;
                    _draggedObj = null;
                    _autoScrollTimer.Stop();
                    _scrollVelocityY = 0;
                }
            }
        }

        private void ObjectsList_DragOver(object? sender, DragEventArgs e) {
            if (e.Data.Contains("CategoryData")) {
                e.DragEffects = DragDropEffects.Move;

                var listbox = this.FindControl<ListBox>("ObjectsListBox");
                if (_objectsScrollViewer == null && listbox != null) {
                    _objectsScrollViewer = listbox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                }

                if (_objectsScrollViewer != null) {
                    var position = e.GetPosition(_objectsScrollViewer);
                    double edgeThreshold = 30.0;

                    if (position.Y < edgeThreshold) {
                        _scrollVelocityY = -10.0; // Scroll UP
                        _activeScrollViewer = _objectsScrollViewer;
                        _autoScrollTimer.Start();
                    } else if (position.Y > _objectsScrollViewer.Bounds.Height - edgeThreshold) {
                        _scrollVelocityY = 10.0; // Scroll DOWN
                        _activeScrollViewer = _objectsScrollViewer;
                        _autoScrollTimer.Start();
                    } else {
                        _scrollVelocityY = 0;
                        _autoScrollTimer.Stop();
                    }
                }
            } else {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private void ObjectsList_Drop(object? sender, DragEventArgs e) {
            if (e.Data.Contains("CategoryData") && e.Data.Get("CategoryData") is YamlCategory draggedCat) {
                var visual = e.Source as Avalonia.Visual;
                while (visual != null && !(visual is ListBoxItem)) {
                    visual = visual.GetVisualParent() as Avalonia.Visual;
                }
                
                if (visual is ListBoxItem targetItem && targetItem.DataContext is YamlCategory targetCat) {
                    var categories = ViewModel.Categories;
                    int oldIndex = categories.IndexOf(draggedCat);
                    int newIndex = categories.IndexOf(targetCat);
                    if (oldIndex != -1 && newIndex != -1 && oldIndex != newIndex) {
                        categories.RemoveAt(oldIndex);
                        categories.Insert(newIndex, draggedCat);
                        
                        ViewModel.SelectedCategory = draggedCat;
                    }
                }
            }
            _isDraggingObj = false;
            _draggedObj = null;
            _autoScrollTimer.Stop();
            _scrollVelocityY = 0;
        }

        private void ObjectsList_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e) {
            _isDraggingObj = false;
            _draggedObj = null;
            _autoScrollTimer.Stop();
            _scrollVelocityY = 0;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace OpenUtau.App.ViewModels {
    public class DynamicYamlRow : ReactiveObject {
        private readonly Dictionary<string, string> _data = new();
        public string PrimaryColumnKey { get; }

        public DynamicYamlRow(string primaryColumnKey = "Key") {
            PrimaryColumnKey = primaryColumnKey;
        }

        public string this[string key] {
            get => _data.ContainsKey(key) ? _data[key] : string.Empty;
            set {
                _data[key] = value;
                this.RaisePropertyChanged("Item");
                this.RaisePropertyChanged(nameof(IsComment));
                this.RaisePropertyChanged(nameof(IsNotComment));
                this.RaisePropertyChanged(nameof(CommentText));
            }
        }

        public bool IsComment {
            get {
                string val = _data.ContainsKey(PrimaryColumnKey) ? _data[PrimaryColumnKey] : "";
                return val.TrimStart().StartsWith("#") || val.TrimStart().StartsWith(";");
            }
        }
        
        public bool IsNotComment => !IsComment;

        public string CommentText {
            get => _data.ContainsKey(PrimaryColumnKey) ? _data[PrimaryColumnKey] : "";
            set {
                _data[PrimaryColumnKey] = value;
                this.RaisePropertyChanged("Item");
                this.RaisePropertyChanged(nameof(IsComment));
                this.RaisePropertyChanged(nameof(IsNotComment));
                this.RaisePropertyChanged(nameof(CommentText));
            }
        }

        private bool _isEditingComment = false;
        public bool IsEditingComment {
            get => _isEditingComment;
            set {
                _isEditingComment = value;
                this.RaisePropertyChanged(nameof(IsEditingComment));
                this.RaisePropertyChanged(nameof(IsNotEditingComment));
            }
        }
        public bool IsNotEditingComment => !IsEditingComment;

        public Dictionary<string, string> GetData() => _data;
    }
    public class PresampSyntaxException : System.Exception {
        public int LineNumber { get; }
        public PresampSyntaxException(string message, int lineNumber) : base(message) {
            LineNumber = lineNumber;
        }
    }
    public class YamlCategory : ReactiveObject {
        public string Name { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
        public HashSet<string> ListColumns { get; set; } = new();
        public bool IsDictionaryFormat { get; set; } = false;
        public bool IsRootScalars { get; set; } = false;
        public ObservableRangeCollection<DynamicYamlRow> Rows { get; } = new();
    }
    public class DictionaryEditorViewModel : ViewModelBase {
        private string _currentDirectory = string.Empty;
        private System.Text.Encoding _currentPresampEncoding = System.Text.Encoding.UTF8;
        private Dictionary<string, string> _filePaths = new();
        public ObservableCollection<string> AvailableFiles { get; } = new();
        private string _selectedFile = string.Empty;
        public string SelectedFile { get => _selectedFile; set => this.RaiseAndSetIfChanged(ref _selectedFile, value); }
        public string CurrentFileType => !string.IsNullOrEmpty(SelectedFile) && SelectedFile.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? "ini" : "yaml";

        public ObservableCollection<YamlCategory> Categories { get; } = new();
        private YamlCategory? _selectedCategory;
        public YamlCategory? SelectedCategory { get => _selectedCategory; set => this.RaiseAndSetIfChanged(ref _selectedCategory, value); }
        public event Action? ColumnsChanged;
        public Action? RefreshIndices { get; set; }
        public Action<DynamicYamlRow>? ScrollToRow { get; set; }
        private DynamicYamlRow? _selectedRow;
        public DynamicYamlRow? SelectedRow { get => _selectedRow; set => this.RaiseAndSetIfChanged(ref _selectedRow, value); }
        private bool _isCreatingNewCategory;
        public bool IsCreatingNewCategory { get => _isCreatingNewCategory; set => this.RaiseAndSetIfChanged(ref _isCreatingNewCategory, value); }
        private string _newCategoryName = string.Empty;
        public string NewCategoryName { get => _newCategoryName; set => this.RaiseAndSetIfChanged(ref _newCategoryName, value); }
        private string _newCategoryColumns = string.Empty;
        public string NewCategoryColumns { get => _newCategoryColumns; set => this.RaiseAndSetIfChanged(ref _newCategoryColumns, value); }
        private bool _isManagingColumns;
        public bool IsManagingColumns { get => _isManagingColumns; set => this.RaiseAndSetIfChanged(ref _isManagingColumns, value); }
        private string _manageColumnName = string.Empty;
        public string ManageColumnName { get => _manageColumnName; set => this.RaiseAndSetIfChanged(ref _manageColumnName, value); }
        private bool _isConfirmingDelete;
        public bool IsConfirmingDelete { get => _isConfirmingDelete; set => this.RaiseAndSetIfChanged(ref _isConfirmingDelete, value); }
        private bool _isCreatingNewFile;
        public bool IsCreatingNewFile { get => _isCreatingNewFile; set => this.RaiseAndSetIfChanged(ref _isCreatingNewFile, value); }
        private string _newFileName = string.Empty;
        public string NewFileName { get => _newFileName; set => this.RaiseAndSetIfChanged(ref _newFileName, value); }
        private string? _replaceColumn;
        public string? ReplaceColumn { get => _replaceColumn; set => this.RaiseAndSetIfChanged(ref _replaceColumn, value); }
        private string _findText = string.Empty;
        public string FindText { get => _findText; set => this.RaiseAndSetIfChanged(ref _findText, value); }
        private string _replaceText = string.Empty;
        public string ReplaceText { get => _replaceText; set => this.RaiseAndSetIfChanged(ref _replaceText, value); }
        private bool _useRegex;
        public bool UseRegex { get => _useRegex; set => this.RaiseAndSetIfChanged(ref _useRegex, value); }
        private List<List<string>> _clipboardData = new();
        private bool _isManagingObjects;
        public bool IsManagingObjects { get => _isManagingObjects; set => this.RaiseAndSetIfChanged(ref _isManagingObjects, value); }
        private bool _canCopyToVoicebank;
        public bool CanCopyToVoicebank { get => _canCopyToVoicebank; set => this.RaiseAndSetIfChanged(ref _canCopyToVoicebank, value); }
        private void UpdateCopyToVoicebankState() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory) || !_filePaths.ContainsKey(SelectedFile)) {
                CanCopyToVoicebank = false;
                return;
            }
            string absolutePath = _filePaths[SelectedFile];
            if (absolutePath.StartsWith(_currentDirectory, StringComparison.OrdinalIgnoreCase)) {
                CanCopyToVoicebank = false;
                return;
            }
            string fileName = Path.GetFileName(absolutePath);
            try {
                var existingFiles = Directory.GetFiles(_currentDirectory, fileName, SearchOption.AllDirectories);
                CanCopyToVoicebank = existingFiles.Length == 0;
                
            } catch {
                string targetPath = Path.Combine(_currentDirectory, fileName);
                CanCopyToVoicebank = !File.Exists(targetPath);
            }
        }

        public void CopyToVoicebank() {
            if (!CanCopyToVoicebank || string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            
            if (_filePaths.TryGetValue(SelectedFile, out string? sourcePath) && sourcePath != null) {
                string fileName = Path.GetFileName(sourcePath);
                string targetPath = Path.Combine(_currentDirectory, fileName);
                
                try {
                    File.Copy(sourcePath, targetPath, false);
                    if (!AvailableFiles.Contains(fileName)) {
                        AvailableFiles.Add(fileName);
                    }
                    _filePaths[fileName] = targetPath;
                    SelectedFile = fileName;
                } catch (Exception ex) {
                    Serilog.Log.Error(ex, $"DictionaryEditor: Failed to copy {sourcePath} to {targetPath}");
                }
            }
        }
        private bool _isConfirmingDeleteCategory;
        public bool IsConfirmingDeleteCategory {
            get => _isConfirmingDeleteCategory;
            set => this.RaiseAndSetIfChanged(ref _isConfirmingDeleteCategory, value);
        }
        public void ToggleConfirmDeleteCategoryPanel() {
            if (SelectedCategory == null) return;
            
            IsConfirmingDeleteCategory = !IsConfirmingDeleteCategory;
            if (IsConfirmingDeleteCategory) {
                IsCreatingNewCategory = false;
                IsManagingColumns = false;
                IsManagingObjects = false;
                IsCreatingNewFile = false;
                IsConfirmingDelete = false;
            }
        }
        public void ConfirmDeleteCategory() {
            DeleteSelectedCategory(); 
            IsConfirmingDeleteCategory = false;
        }
        public void ToggleManageObjectsPanel() {
            IsManagingObjects = !IsManagingObjects;
            IsCreatingNewFile = false;
            IsCreatingNewCategory = false;
            IsConfirmingDelete = false;
            IsManagingColumns = false;
        }

        public void DeselectAll() {
            SelectedRow = null;
        }
        private DynamicYamlRow CloneRow(DynamicYamlRow original) {
            var category = SelectedCategory;
            string firstCol = category?.Columns.FirstOrDefault() ?? "Key";
            var clone = new DynamicYamlRow(firstCol);
            if (category != null) {
                foreach (var col in category.Columns) {
                    clone[col] = original[col];
                }
            }
            return clone;
        }
        public void CopyRow(object? itemsObj) {
            if (itemsObj is System.Collections.IList items && SelectedCategory != null) {
                _clipboardData.Clear();
                foreach (var item in items) {
                    if (item is DynamicYamlRow row) {
                        var rowData = new List<string>();
                        if (row.IsComment) {
                            rowData.Add(row.CommentText ?? "");
                        } else {
                            foreach (var col in SelectedCategory.Columns) {
                                rowData.Add(row[col] ?? "");
                            }
                        }
                        _clipboardData.Add(rowData);
                    }
                }
            }
        }
        public void PasteRow() {
            if (SelectedCategory == null || _clipboardData.Count == 0) return;
            foreach (var rowData in _clipboardData) {
                var newRow = new DynamicYamlRow();
                if (rowData.Count == 1 && rowData[0].StartsWith("#")) {
                    newRow.CommentText = rowData[0];
                } else {
                    for (int i = 0; i < SelectedCategory.Columns.Count && i < rowData.Count; i++) {
                        string colName = SelectedCategory.Columns[i];
                        newRow[colName] = rowData[i];
                    }
                }
                SelectedCategory.Rows.Add(newRow);
            }
            RefreshIndices?.Invoke();
        }
        public void CutRow(object? itemsObj) {
            if (itemsObj is System.Collections.IList items && SelectedCategory != null) {
                CopyRow(itemsObj);
                var toRemove = items.Cast<DynamicYamlRow>().ToList();
                foreach (var row in toRemove) {
                    SelectedCategory.Rows.Remove(row);
                }
                RefreshIndices?.Invoke();
            }
        }
        public void DeleteSelectedRow(object? parameter) {
            var category = SelectedCategory;
            if (category == null) return;
            if (parameter is System.Collections.IList selectedItems && selectedItems.Count > 0) {
                var itemsToDelete = selectedItems.Cast<DynamicYamlRow>().ToList();
                foreach (var item in itemsToDelete) {
                    category.Rows.Remove(item);
                }
            }
            else if (parameter is DynamicYamlRow singleRow) {
                category.Rows.Remove(singleRow);
            }
            else if (SelectedRow != null) {
                category.Rows.Remove(SelectedRow);
            }
            RefreshIndices?.Invoke();
        }

        public DictionaryEditorViewModel() {
            this.WhenAnyValue(x => x.SelectedFile)
                .Subscribe(new AnonymousObserver<string>(file => {
                    this.RaisePropertyChanged(nameof(CurrentFileType)); 
                    if (!string.IsNullOrEmpty(file)) {
                        LoadSelectedFile(); 
                    }
                    UpdateCopyToVoicebankState();
                }));
        }
        public void ExecuteSelectDuplicates(object? parameter) {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrEmpty(ReplaceColumn)) return;

            if (parameter is System.Collections.IList selectedItems) {
                selectedItems.Clear();
                var seenValues = new HashSet<string>();

                foreach (var row in category.Rows) {
                    if (row.IsComment) continue;
                    string currentVal = row[ReplaceColumn];
                    if (string.IsNullOrEmpty(currentVal)) continue; 
                    if (seenValues.Contains(currentVal)) {
                        selectedItems.Add(row);
                    } 
                    else {
                        seenValues.Add(currentVal);
                    }
                }
            }
        }
        private void Find(bool searchUp) {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;

            int startIndex = 0;
            if (SelectedRow != null) {
                startIndex = category.Rows.IndexOf(SelectedRow);
                startIndex += searchUp ? -1 : 1;
            }
            int count = category.Rows.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++) {
                int offset = searchUp ? -i : i;
                int index = (startIndex + offset) % count;
                if (index < 0) index += count;

                var row = category.Rows[index];
                string currentVal = row[ReplaceColumn];
                if (string.IsNullOrEmpty(currentVal)) continue;

                bool isMatch = false;
                if (UseRegex) {
                    try { isMatch = System.Text.RegularExpressions.Regex.IsMatch(currentVal, FindText); } catch { }
                } else {
                    isMatch = currentVal.Contains(FindText);
                }

                if (isMatch) {
                    SelectedRow = row;
                    ScrollToRow?.Invoke(row);
                    return; 
                }
            }
        }
        public void ExecuteFindNext() => Find(searchUp: false);
        public void ExecuteFindPrevious() => Find(searchUp: true);
        public void ExecuteFindAll(object? parameter) {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;

            if (parameter is System.Collections.IList selectedItems) {
                selectedItems.Clear(); 
                foreach (var row in category.Rows) {
                    string currentVal = row[ReplaceColumn];
                    if (string.IsNullOrEmpty(currentVal)) continue;

                    bool isMatch = false;
                    if (UseRegex) {
                        try { isMatch = System.Text.RegularExpressions.Regex.IsMatch(currentVal, FindText); } catch { }
                    } else {
                        isMatch = currentVal.Contains(FindText);
                    }
                    if (isMatch) selectedItems.Add(row);
                }
            }
        }
        public void ExecuteReplace(object? parameter) {
            if (SelectedCategory == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;
            bool replacedMultiple = false;

            if (parameter is System.Collections.IList selectedItems && selectedItems.Count > 1) {
                var itemsToProcess = selectedItems.Cast<DynamicYamlRow>().ToList();
                
                foreach (var row in itemsToProcess) {
                    string currentVal = row[ReplaceColumn];
                    if (!string.IsNullOrEmpty(currentVal)) {
                        if (UseRegex) {
                            try { row[ReplaceColumn] = System.Text.RegularExpressions.Regex.Replace(currentVal, FindText, ReplaceText); } catch { }
                        } else {
                            row[ReplaceColumn] = currentVal.Replace(FindText, ReplaceText);
                        }
                    }
                }
                replacedMultiple = true;
            } 
            else if (SelectedRow != null) {
                string currentVal = SelectedRow[ReplaceColumn];
                if (!string.IsNullOrEmpty(currentVal)) {
                    if (UseRegex) {
                        try { SelectedRow[ReplaceColumn] = System.Text.RegularExpressions.Regex.Replace(currentVal, FindText, ReplaceText); } catch { }
                    } else {
                        SelectedRow[ReplaceColumn] = currentVal.Replace(FindText, ReplaceText);
                    }
                }
            }
            if (!replacedMultiple) {
                ExecuteFindNext();
            }
        }
        public void ExecuteReplaceAll() {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;
            foreach (var row in category.Rows) {
                string currentVal = row[ReplaceColumn];
                if (string.IsNullOrEmpty(currentVal)) continue;

                if (UseRegex) {
                    try { row[ReplaceColumn] = System.Text.RegularExpressions.Regex.Replace(currentVal, FindText, ReplaceText); } catch { }
                } else {
                    row[ReplaceColumn] = currentVal.Replace(FindText, ReplaceText);
                }
            }
        }
        public void ToggleNewFilePanel() {
            IsCreatingNewFile = !IsCreatingNewFile;
            IsCreatingNewCategory = false;
            IsManagingColumns = false;
            IsConfirmingDelete = false;
            NewFileName = string.Empty;
        }
        public void ToggleConfirmDeletePanel() {
            if (string.IsNullOrEmpty(SelectedFile)) return;
            IsConfirmingDelete = !IsConfirmingDelete;
            IsCreatingNewFile = false;
            IsCreatingNewCategory = false;
            IsManagingColumns = false;
        }
        public void ToggleNewCategoryPanel() {
            IsCreatingNewCategory = !IsCreatingNewCategory;
            IsCreatingNewFile = false;
            IsManagingColumns = false;
            IsConfirmingDelete = false;
            NewCategoryName = string.Empty;
            NewCategoryColumns = string.Empty;
        }
        public void ToggleManageColumnsPanel() {
            IsManagingColumns = !IsManagingColumns;
            IsCreatingNewFile = false;
            IsCreatingNewCategory = false;
            IsConfirmingDelete = false;
            ManageColumnName = string.Empty;
        }

        public string GetSelectedFileFullPath() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return string.Empty;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                return Path.Combine(_currentDirectory, relativePath);
            }
            return string.Empty;
        }
        public void ConfirmNewFile() {
            if (string.IsNullOrWhiteSpace(NewFileName) || string.IsNullOrEmpty(_currentDirectory)) return;
            string fileName = NewFileName.Trim();
            if (!fileName.EndsWith(".yaml") && !fileName.EndsWith(".yml")) {
                fileName += ".yaml";
            }
            string filePath = Path.Combine(_currentDirectory, fileName);
            if (!File.Exists(filePath)) {
                File.WriteAllText(filePath, "# Created with OpenUtau Dictionary Editor\n");
                AvailableFiles.Add(fileName);
                _filePaths[fileName] = fileName; 
            }
            SelectedFile = fileName;
            LoadYaml(filePath); 
            ToggleNewFilePanel();
            NewFileName = string.Empty;
        }
        public void DeleteSelectedFile() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string filePath = Path.Combine(_currentDirectory, relativePath);
                if (File.Exists(filePath)) {
                    File.Delete(filePath);
                }
            }
            AvailableFiles.Remove(SelectedFile);
            if (AvailableFiles.Count > 0) SelectedFile = AvailableFiles[0];
            else ClearContext();
        }
        public void ConfirmDeleteFile() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string filePath = Path.Combine(_currentDirectory, relativePath);
                if (File.Exists(filePath)) {
                    File.Delete(filePath);
                }
            }
            AvailableFiles.Remove(SelectedFile);
            if (AvailableFiles.Count > 0) SelectedFile = AvailableFiles[0];
            else ClearContext();
            IsConfirmingDelete = false;
        }

        public void ConfirmNewCategory() {
            if (string.IsNullOrWhiteSpace(NewCategoryName)) return;
            string catName = NewCategoryName.Trim();
            bool isRoot = catName.Equals("Metadata", StringComparison.OrdinalIgnoreCase);
            List<string> columns = new List<string>();
            if (isRoot) {
                columns = new List<string> { "Key", "Value" };
            } else {
                if (string.IsNullOrWhiteSpace(NewCategoryColumns)) return;
                columns = NewCategoryColumns.Split(',').Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
                if (columns.Count == 0) return;
            }
            var newCat = new YamlCategory { 
                Name = catName, 
                Columns = columns,
                IsRootScalars = isRoot,
            };
            // Root scalars (Metadata) should always sit at the very top of the list
            if (isRoot) {
                Categories.Insert(0, newCat);
            } else {
                Categories.Add(newCat);
            }
            SelectedCategory = newCat;
            ToggleNewCategoryPanel();
        }

        public void DeleteSelectedCategory() {
            if (SelectedCategory != null) {
                Categories.Remove(SelectedCategory);
                SelectedCategory = Categories.FirstOrDefault();
            }
        }

        public void AddNewColumn() {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrWhiteSpace(ManageColumnName)) return;
            var columnsToAdd = ManageColumnName.Split(',')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            bool changed = false;
            foreach (var col in columnsToAdd) {
                if (!category.Columns.Contains(col)) {
                    category.Columns.Add(col);
                    changed = true;
                }
            }
            if (changed) {
                ColumnsChanged?.Invoke();
            }
            
            ManageColumnName = string.Empty;
        }

        public void RemoveColumn() {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrWhiteSpace(ManageColumnName)) return;
            var columnsToRemove = ManageColumnName.Split(',')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            bool changed = false;
            foreach (var col in columnsToRemove) {
                if (category.Columns.Contains(col)) {
                    category.Columns.Remove(col);
                    changed = true;
                }
            }
            if (changed) {
                ColumnsChanged?.Invoke();
            }
            ManageColumnName = string.Empty;
        }

        public void AddNewRow() {
            var category = SelectedCategory;
            if (category == null) return;
            string firstCol = category.Columns.FirstOrDefault() ?? "Key";
            var newRow = new DynamicYamlRow(firstCol);

            if (SelectedRow != null) {
                int index = category.Rows.IndexOf(SelectedRow);
                if (index >= 0) {
                    category.Rows.Insert(index + 1, newRow);
                    SelectedRow = newRow;
                    RefreshIndices?.Invoke(); 
                    return;
                }
            }
            category.Rows.Add(newRow);
            SelectedRow = newRow;
            RefreshIndices?.Invoke(); 
        }

        public void AddNewCommentRow() {
            var category = SelectedCategory;
            if (CurrentFileType != "yaml" || category == null) return;
            string firstCol = category.Columns.FirstOrDefault() ?? "Key";
            var newRow = new DynamicYamlRow(firstCol);
            newRow[firstCol] = "# New Comment...";

            if (SelectedRow != null) {
                int index = category.Rows.IndexOf(SelectedRow);
                if (index >= 0) {
                    category.Rows.Insert(index + 1, newRow);
                    SelectedRow = newRow;
                    RefreshIndices?.Invoke(); 
                    return;
                }
            }
            category.Rows.Add(newRow);
            SelectedRow = newRow;
            RefreshIndices?.Invoke(); 
        }
        public void SetSingerContext(string dir, Dictionary<string, string> fileMap, string targetFileName = "") {
            _currentDirectory = dir; 
            _filePaths = fileMap;
            AvailableFiles.Clear();
            foreach (var name in fileMap.Keys) {
                AvailableFiles.Add(name);
            }
            
            if (AvailableFiles.Count > 0) {
                if (!string.IsNullOrEmpty(targetFileName)) {
                    string? match = AvailableFiles.FirstOrDefault(f => 
                        f.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) || 
                        f.StartsWith(targetFileName + " ", StringComparison.OrdinalIgnoreCase));
                    
                    if (match != null) {
                        SelectedFile = match;
                        return;
                    }
                }
                SelectedFile = AvailableFiles[0];
            }
        }
        public void ClearContext() {
            _currentDirectory = string.Empty;
            AvailableFiles.Clear();
            Categories.Clear();
        }

        public void LoadPresamp(string filePath) {
            Categories.Clear();
            if (!System.IO.File.Exists(filePath)) return;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            byte[] rawBytes = System.IO.File.ReadAllBytes(filePath);
            var strictUtf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

            try {
                strictUtf8.GetString(rawBytes);
                _currentPresampEncoding = new System.Text.UTF8Encoding(true);
            } 
            catch (System.Text.DecoderFallbackException) {
                _currentPresampEncoding = System.Text.Encoding.GetEncoding("shift_jis");
            }
            string[] lines = System.IO.File.ReadAllLines(filePath, _currentPresampEncoding);

            var parsedCategories = new List<YamlCategory>();
            var categoryRowsMap = new Dictionary<YamlCategory, List<DynamicYamlRow>>();
            var existingKeysMap = new Dictionary<YamlCategory, HashSet<string>>();

            YamlCategory? currentCategory = null;
            int currentLineNumber = 0;

            try {
                foreach (var rawLine in lines) {
                    currentLineNumber++;
                    string lineToProcess = rawLine.TrimEnd('\r', '\n');
                    if (string.IsNullOrEmpty(lineToProcess)) continue;
                    if (lineToProcess.TrimStart().StartsWith(";") || lineToProcess.TrimStart().StartsWith("#")) continue;

                    string headerCheck = lineToProcess.Trim();
                    if (headerCheck.StartsWith("[") && headerCheck.EndsWith("]")) {
                        string sectionName = headerCheck.Substring(1, headerCheck.Length - 2);
                        currentCategory = parsedCategories.FirstOrDefault(c => c.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
                        
                        if (currentCategory == null) {
                            currentCategory = new YamlCategory { Name = sectionName };
                            if (sectionName == "VOWEL") currentCategory.Columns = new List<string> { "ID", "Base", "Phonemes", "Vol" };
                            else if (sectionName == "CONSONANT") currentCategory.Columns = new List<string> { "ID", "Phonemes", "Crossfade" };
                            else if (sectionName == "REPLACE" || sectionName == "ALIAS") currentCategory.Columns = new List<string> { "Key", "Value" };
                            else currentCategory.Columns = new List<string> { "Value" };

                            parsedCategories.Add(currentCategory);
                            categoryRowsMap[currentCategory] = new List<DynamicYamlRow>();
                            existingKeysMap[currentCategory] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }
                        continue;
                    }

                    if (currentCategory == null) {
                        throw new PresampSyntaxException("Data row found before any [Category] header was declared.", currentLineNumber);
                    }

                    string firstCol = currentCategory.Columns.FirstOrDefault() ?? "Key";
                    string rowKey = "";
                    var rowData = new Dictionary<string, string>();

                    if (currentCategory.Name == "VOWEL") {
                        var parts = lineToProcess.Split('=');
                        if (parts.Length < 4) throw new PresampSyntaxException("Malformed VOWEL entry. Expected format: ID=Base=Phonemes=Vol", currentLineNumber);
                        rowKey = parts[0];
                        rowData["ID"] = parts[0];
                        rowData["Base"] = parts[1];
                        rowData["Phonemes"] = parts[2];
                        rowData["Vol"] = parts[3];
                    } 
                    else if (currentCategory.Name == "CONSONANT") {
                        var parts = lineToProcess.Split('=');
                        if (parts.Length < 3) throw new PresampSyntaxException("Malformed CONSONANT entry. Expected format: ID=Phonemes=Crossfade", currentLineNumber);
                        rowKey = parts[0];
                        rowData["ID"] = parts[0];
                        rowData["Phonemes"] = parts[1];
                        rowData["Crossfade"] = parts[2];
                    } 
                    else if (currentCategory.Name == "REPLACE" || currentCategory.Name == "ALIAS") {
                        var parts = lineToProcess.Split(new[] { '=' }, 2);
                        if (parts.Length < 2) throw new PresampSyntaxException($"Malformed {currentCategory.Name} entry. Expected format: Key=Value", currentLineNumber);
                        rowKey = parts[0].TrimEnd();
                        rowData["Key"] = rowKey;
                        rowData["Value"] = parts[1];
                    } 
                    else {
                        rowKey = lineToProcess;
                        rowData["Value"] = lineToProcess;
                    }

                    // Fast O(1) duplicate key detection
                    if (existingKeysMap[currentCategory].Contains(rowKey)) {
                        throw new PresampSyntaxException($"Duplicate entry found for ID/Key: '{rowKey}'. Each entry must be unique.", currentLineNumber);
                    }
                    existingKeysMap[currentCategory].Add(rowKey);

                    var newRow = new DynamicYamlRow(firstCol);
                    foreach (var kvp in rowData) {
                        newRow[kvp.Key] = kvp.Value;
                    }
                    categoryRowsMap[currentCategory].Add(newRow);
                }

                // Batch assign all pre-built rows before attaching categories to the ObservableCollection
                foreach (var cat in parsedCategories) {
                    if (categoryRowsMap.TryGetValue(cat, out var rows)) {
                        if (cat.Rows is ObservableRangeCollection<DynamicYamlRow> rangeCol) {
                            rangeCol.AddRange(rows);
                        } else {
                            foreach (var r in rows) cat.Rows.Add(r);
                        }
                    }
                    Categories.Add(cat);
                }
                
                if (Categories.Count > 0) SelectedCategory = Categories[0];
                ColumnsChanged?.Invoke(); 
                
            } catch (PresampSyntaxException preEx) {
                Serilog.Log.Error(preEx, $"Presamp Syntax Error in: {filePath}");
                Categories.Clear();
                ProcessParseError("Presamp INI", preEx.Message, preEx.LineNumber, filePath);
                
            } catch (System.Exception ex) {
                Serilog.Log.Error(ex, $"Failed to parse Presamp: {filePath}");
                Categories.Clear();
                ProcessParseError("Presamp INI", $"A fatal parsing error occurred: {ex.Message}", currentLineNumber, filePath);
            }
        }

        public void SavePresamp(string filePath) {
            var lines = new List<string>();
            foreach (var cat in Categories) {
                lines.Add($"[{cat.Name}]");
                foreach (var row in cat.Rows) {
                    if (cat.Name == "VOWEL") lines.Add($"{row["ID"]}={row["Base"]}={row["Phonemes"]}={row["Vol"]}");
                    else if (cat.Name == "CONSONANT") lines.Add($"{row["ID"]}={row["Phonemes"]}={row["Crossfade"]}");
                    else if (cat.Name == "REPLACE" || cat.Name == "ALIAS") lines.Add($"{row["Key"]}={row["Value"]}");
                    else {
                        if (cat.Columns.Count > 0) {
                            string firstCol = cat.Columns[0];
                            string val = row[firstCol] ?? "";
                            if (!string.IsNullOrEmpty(val)) lines.Add(val);
                        }
                    }
                }
            }
            File.WriteAllLines(filePath, lines, _currentPresampEncoding);
        }

        public void LoadSelectedFile() {
            if (string.IsNullOrEmpty(SelectedFile) || !_filePaths.ContainsKey(SelectedFile)) {
                Categories.Clear();
                return;
            }
            string fullPath = _filePaths[SelectedFile];
            if (!System.IO.File.Exists(fullPath)) {
                Categories.Clear();
                return;
            }
            if (fullPath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) {
                LoadPresamp(fullPath);
            } else {
                LoadYaml(fullPath);
            }
        }

        public void SaveCurrentFile() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string targetPath = Path.Combine(_currentDirectory, relativePath);
                if (SelectedFile.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) SavePresamp(targetPath);
                else SaveYaml(); 
            }
        }

        public void LoadYaml(string filePath) {
            Categories.Clear();
            if (!File.Exists(filePath)) return;

            try {
                var yamlContent = File.ReadAllText(filePath);
                string[] rawLines = yamlContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var yaml = new YamlDotNet.RepresentationModel.YamlStream();
                yaml.Load(new StringReader(yamlContent));

                if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlDotNet.RepresentationModel.YamlMappingNode rootMapping) return;
                
                var parsedCategories = new List<YamlCategory>();
                YamlCategory? metaCategory = null;
                var metaRows = new List<DynamicYamlRow>();
                
                int lastProcessedLine = 1;

                var sortedRoots = rootMapping.Children.OrderBy(kvp => kvp.Key.Start.Line).ToList();

                foreach (var kvp in sortedRoots) {
                    var rootKeyNode = kvp.Key as YamlDotNet.RepresentationModel.YamlScalarNode;
                    string rootKey = rootKeyNode?.Value ?? "";
                    var rootValue = kvp.Value;
                    int currentStartLine = kvp.Key.Start.Line;
                    
                    var preComments = new List<string>();
                    for (int i = lastProcessedLine; i < currentStartLine; i++) {
                        if (i >= 1 && i <= rawLines.Length) {
                            string gapLine = rawLines[i - 1].TrimStart();
                            if (gapLine.StartsWith("#")) preComments.Add(gapLine);
                        }
                    }
                    
                    lastProcessedLine = currentStartLine + 1;

                    if (rootValue is YamlDotNet.RepresentationModel.YamlSequenceNode seqNode) {
                        var category = new YamlCategory { Name = rootKey };
                        var allColumns = new HashSet<string>();
                        var categoryRows = new List<DynamicYamlRow>();

                        foreach (var rowNode in seqNode.Children) {
                            if (rowNode is YamlDotNet.RepresentationModel.YamlMappingNode rowDict) {
                                foreach (var keyNode in rowDict.Children.Keys) {
                                    if (keyNode is YamlDotNet.RepresentationModel.YamlScalarNode scalarKey) {
                                        allColumns.Add(scalarKey.Value ?? "");
                                    }
                                }
                            }
                        }
                        category.Columns = allColumns.ToList();

                        if (category.Columns.Count > 0) {
                            string firstCol = category.Columns[0];
                            foreach (var c in preComments) {
                                var cRow = new DynamicYamlRow(firstCol);
                                cRow[firstCol] = c;
                                categoryRows.Add(cRow);
                            }

                            var sortedRows = seqNode.Children.OrderBy(n => n.Start.Line).ToList();
                            
                            foreach (var rowNode in sortedRows) {
                                int actualLine = rowNode.Start.Line;
                                for (int i = lastProcessedLine; i < actualLine; i++) {
                                    if (i >= 1 && i <= rawLines.Length) {
                                        string gapLine = rawLines[i - 1].TrimStart();
                                        if (gapLine.StartsWith("#")) {
                                            var cRow = new DynamicYamlRow(firstCol);
                                            cRow[firstCol] = gapLine;
                                            categoryRows.Add(cRow);
                                        }
                                    }
                                }
                                
                                if (rowNode is YamlDotNet.RepresentationModel.YamlMappingNode rowDict) {
                                    var row = new DynamicYamlRow(firstCol);
                                    foreach (var col in category.Columns) {
                                        var keyMatch = rowDict.Children.Keys.FirstOrDefault(k => (k as YamlDotNet.RepresentationModel.YamlScalarNode)?.Value == col);
                                        if (keyMatch != null) {
                                            var valNode = rowDict.Children[keyMatch];
                                            if (valNode is YamlDotNet.RepresentationModel.YamlSequenceNode listNode) {
                                                var formattedList = listNode.Children.Select(x => {
                                                    var scalar = x as YamlDotNet.RepresentationModel.YamlScalarNode;
                                                    string s = scalar?.Value ?? "";
                                                    if (scalar != null && (scalar.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalar.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted)) return $"\"{s}\"";
                                                    return s;
                                                });
                                                row[col] = string.Join(" ", formattedList);
                                                category.ListColumns.Add(col);
                                            } else if (valNode is YamlDotNet.RepresentationModel.YamlScalarNode scalarVal) {
                                                string s = scalarVal.Value ?? "";
                                                if (scalarVal.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalarVal.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted) row[col] = $"\"{s}\"";
                                                else row[col] = s;
                                            }
                                        }
                                    }
                                    categoryRows.Add(row);
                                }
                                lastProcessedLine = Math.Max(lastProcessedLine, rowNode.End.Line + 1);
                            }
                        }

                        if (category.Rows is ObservableRangeCollection<DynamicYamlRow> rangeCol) {
                            rangeCol.AddRange(categoryRows);
                        } else {
                            foreach (var r in categoryRows) category.Rows.Add(r);
                        }
                        parsedCategories.Add(category);

                    } else if (rootValue is YamlDotNet.RepresentationModel.YamlMappingNode dictNode) {
                        var category = new YamlCategory { Name = rootKey, Columns = new List<string> { "Key", "Value" }, IsDictionaryFormat = true };
                        var categoryRows = new List<DynamicYamlRow>();

                        foreach (var c in preComments) {
                            var cRow = new DynamicYamlRow("Key");
                            cRow["Key"] = c;
                            categoryRows.Add(cRow);
                        }

                        var sortedInner = dictNode.Children.OrderBy(k => k.Key.Start.Line).ToList();

                        foreach (var innerKvp in sortedInner) {
                            int actualLine = innerKvp.Key.Start.Line;
                            for (int i = lastProcessedLine; i < actualLine; i++) {
                                if (i >= 1 && i <= rawLines.Length) {
                                    string gapLine = rawLines[i - 1].TrimStart();
                                    if (gapLine.StartsWith("#")) {
                                        var cRow = new DynamicYamlRow("Key");
                                        cRow["Key"] = gapLine;
                                        categoryRows.Add(cRow);
                                    }
                                }
                            }
                            
                            var row = new DynamicYamlRow("Key");
                            var innerKeyNode = innerKvp.Key as YamlDotNet.RepresentationModel.YamlScalarNode;
                            string keyStr = innerKeyNode?.Value ?? "";
                            if (innerKeyNode != null && (innerKeyNode.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || innerKeyNode.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted)) row["Key"] = $"\"{keyStr}\"";
                            else row["Key"] = keyStr;

                            var innerValNode = innerKvp.Value;
                            if (innerValNode is YamlDotNet.RepresentationModel.YamlSequenceNode listNode) {
                                var formattedList = listNode.Children.Select(x => {
                                    var scalar = x as YamlDotNet.RepresentationModel.YamlScalarNode;
                                    string s = scalar?.Value ?? "";
                                    if (scalar != null && (scalar.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalar.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted)) return $"\"{s}\"";
                                    return s;
                                });
                                row["Value"] = string.Join(" ", formattedList);
                                category.ListColumns.Add("Value");
                            } else if (innerValNode is YamlDotNet.RepresentationModel.YamlScalarNode scalarVal) {
                                string s = scalarVal.Value ?? "";
                                if (scalarVal.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalarVal.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted) row["Value"] = $"\"{s}\"";
                                else row["Value"] = s;
                            }
                            categoryRows.Add(row);
                            
                            lastProcessedLine = Math.Max(lastProcessedLine, innerKvp.Value.End.Line + 1);
                        }

                        if (category.Rows is ObservableRangeCollection<DynamicYamlRow> rangeCol) {
                            rangeCol.AddRange(categoryRows);
                        } else {
                            foreach (var r in categoryRows) category.Rows.Add(r);
                        }
                        parsedCategories.Add(category);

                    } else if (rootValue is YamlDotNet.RepresentationModel.YamlScalarNode scalarRoot) {
                        if (metaCategory == null) {
                            metaCategory = new YamlCategory { Name = "Metadata", Columns = new List<string> { "Key", "Value" }, IsRootScalars = true };
                        }

                        foreach (var c in preComments) {
                            var cRow = new DynamicYamlRow("Key") { ["Key"] = c };
                            metaRows.Add(cRow);
                        }

                        var row = new DynamicYamlRow("Key") { ["Key"] = rootKey };
                        string s = scalarRoot.Value ?? "";
                        if (scalarRoot.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalarRoot.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted) row["Value"] = $"\"{s}\"";
                        else row["Value"] = s;
                        metaRows.Add(row);
                        
                        lastProcessedLine = Math.Max(lastProcessedLine, scalarRoot.End.Line + 1);
                    }
                    
                    lastProcessedLine = Math.Max(lastProcessedLine, rootValue.End.Line + 1);
                }

                // If metadata scalars existed, finalize and prepend to list
                if (metaCategory != null) {
                    if (metaCategory.Rows is ObservableRangeCollection<DynamicYamlRow> rangeCol) {
                        rangeCol.AddRange(metaRows);
                    } else {
                        foreach (var r in metaRows) metaCategory.Rows.Add(r);
                    }
                    parsedCategories.Insert(0, metaCategory);
                }

                if (parsedCategories.Count > 0) {
                    var lastCat = parsedCategories.Last();
                    string firstCol = lastCat.Columns.FirstOrDefault() ?? "Key";
                    for (int i = lastProcessedLine; i <= rawLines.Length; i++) {
                        if (i >= 1 && i <= rawLines.Length) {
                            string gapLine = rawLines[i - 1].TrimStart();
                            if (gapLine.StartsWith("#")) {
                                var cRow = new DynamicYamlRow(firstCol);
                                cRow[firstCol] = gapLine;
                                lastCat.Rows.Add(cRow);
                            }
                        }
                    }
                }

                // Add prepared categories into Categories all at once
                foreach (var cat in parsedCategories) {
                    Categories.Add(cat);
                }

                if (Categories.Count > 0) SelectedCategory = Categories[0];
            } catch (YamlDotNet.Core.YamlException yamlEx) {
                Serilog.Log.Error(yamlEx, $"YAML Syntax Error in: {filePath}");
                Categories.Clear();
                string msg = yamlEx.InnerException?.Message ?? yamlEx.Message;
                ProcessParseError("YAML", msg, yamlEx.Start.Line, filePath);
            } catch (System.Exception ex) {
                Serilog.Log.Error(ex, $"Fatal YAML Parsing Error in: {filePath}");
                Categories.Clear();
                int errorLine = 1;
                string customMessage = ex.Message;
                try {
                    string[] fileLines = System.IO.File.ReadAllLines(filePath);
                    for (int i = 0; i < fileLines.Length; i++) {
                        string l = fileLines[i];
                        if (l.TrimStart().StartsWith("#")) continue;
                        int curly = l.Count(c => c == '{') - l.Count(c => c == '}');
                        int square = l.Count(c => c == '[') - l.Count(c => c == ']');
                        if (curly != 0 || square != 0) {
                            errorLine = i + 1;
                            customMessage = "Mismatched brackets detected ('{', '}', '[', or ']').";
                            break;
                        }
                    }
                } catch { }
                ProcessParseError("YAML", $"Fatal Parser Error: {customMessage}", errorLine, filePath);
            }
        }

        public void SaveYaml() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            var dictToSave = new Dictionary<string, object>();

            foreach (var cat in Categories) {
                if (cat.IsRootScalars) {
                    foreach (var row in cat.Rows) {
                        string key = row["Key"] ?? "";
                        if (key.TrimStart().StartsWith("#")) {
                            dictToSave[$"__comment_{Guid.NewGuid():N}__"] = key.TrimStart();
                            continue;
                        }
                        string val = row["Value"] ?? "";
                        if (!string.IsNullOrWhiteSpace(key)) {
                            if (double.TryParse(val, out double numVal)) dictToSave[key] = numVal;
                            else dictToSave[key] = val;
                        }
                    }
                } else if (cat.IsDictionaryFormat) {
                    var dictNode = new Dictionary<string, object>();
                    foreach (var row in cat.Rows) {
                        string key = row["Key"] ?? "";
                        if (key.TrimStart().StartsWith("#")) {
                            dictNode[$"__comment_{Guid.NewGuid():N}__"] = key.TrimStart();
                            continue;
                        }
                        string val = row["Value"] ?? "";
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        if (!string.IsNullOrWhiteSpace(val)) {
                            string trimmedVal = val.Trim();
                            bool isExplicitList = trimmedVal.StartsWith("[") && trimmedVal.EndsWith("]");
                            
                            // NEW: Protects fallbacks from being split if it uses the dictionary format
                            bool isFallbacksBlock = cat.Name.Equals("fallbacks", StringComparison.OrdinalIgnoreCase);
                            bool isTimingsBlock = cat.Name.Equals("timings", StringComparison.OrdinalIgnoreCase);
                            
                            var matches = System.Text.RegularExpressions.Regex.Matches(trimmedVal, @"\""[^\""]*\""|[^ ,]+");
                            
                            if (isExplicitList || (matches.Count > 1 && !isFallbacksBlock && !isTimingsBlock)) {
                                dictNode[key] = matches.Cast<System.Text.RegularExpressions.Match>()
                                                       .Select(m => m.Value.Trim('[', ']'))
                                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                                       .ToList();
                            } else {
                                dictNode[key] = trimmedVal; 
                            }
                        } else {
                            dictNode[key] = val;
                        }
                    }
                    dictToSave[cat.Name] = dictNode;
                } else {
                    var rowList = new List<Dictionary<string, object>>();
                    foreach (var row in cat.Rows) {
                        string firstColVal = row[cat.Columns.FirstOrDefault() ?? "Key"] ?? "";
                        if (firstColVal.TrimStart().StartsWith("#")) {
                            var commentRow = new Dictionary<string, object>();
                            commentRow["__full_row_comment__"] = firstColVal.TrimStart();
                            rowList.Add(commentRow);
                            continue;
                        }

                        var newRow = new Dictionary<string, object>();
                        foreach (var col in cat.Columns) {
                            string val = row[col] ?? "";
                            if (string.IsNullOrWhiteSpace(val)) continue;

                            string trimmedVal = val.Trim();
                            bool isExplicitList = trimmedVal.StartsWith("[") && trimmedVal.EndsWith("]");
                            bool isPhonemesColumn = col.Equals("phonemes", StringComparison.OrdinalIgnoreCase);
                            
                            bool isGraphemeColumn = col.Equals("grapheme", StringComparison.OrdinalIgnoreCase) || col.Equals("graphemes", StringComparison.OrdinalIgnoreCase);
                            
                            bool isFallbacksBlock = cat.Name.Equals("fallbacks", StringComparison.OrdinalIgnoreCase);

                            bool isTimingsBlock = cat.Name.Equals("timings", StringComparison.OrdinalIgnoreCase);
                            
                            var matches = System.Text.RegularExpressions.Regex.Matches(trimmedVal, @"\""[^\""]*\""|[^ ,]+");
                            
                            // It becomes a list IF: explicit brackets, phonemes...
                            // OR (multiple items AND it is NOT the grapheme column AND NOT in fallbacks AND NOT in timings)
                            if (isExplicitList || isPhonemesColumn || (matches.Count > 1 && !isGraphemeColumn && !isFallbacksBlock && !isTimingsBlock)) {
                                newRow[col] = matches.Cast<System.Text.RegularExpressions.Match>()
                                                     .Select(m => m.Value.Trim('[', ']'))
                                                     .Where(s => !string.IsNullOrWhiteSpace(s))
                                                     .ToList();
                            } else {
                                newRow[col] = trimmedVal; 
                            }
                        }
                        if (newRow.Count > 0) rowList.Add(newRow);
                    }
                    dictToSave[cat.Name] = rowList;
                }
            }

            var serializer = new SerializerBuilder()
                .DisableAliases()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .WithIndentedSequences()
                .WithEventEmitter(next => new BracketStyleEmitter(next))
                .Build();

            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string rawYaml = serializer.Serialize(dictToSave);
                
                rawYaml = System.Text.RegularExpressions.Regex.Replace(
                    rawYaml,
                    @"^([ \t]*)-\s*\{?\s*__full_row_comment__:\s*(?:>-\s*)?(?:""|')?(.*?)(?:""|')?\s*\}?\s*$",
                    m => $"{m.Groups[1].Value}{m.Groups[2].Value}",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                rawYaml = System.Text.RegularExpressions.Regex.Replace(
                    rawYaml,
                    @"^([ \t]*)\{?\s*__comment_[a-f0-9]+__:\s*(?:>-\s*)?(?:""|')?(.*?)(?:""|')?\s*\}?\s*$",
                    m => $"{m.Groups[1].Value}{m.Groups[2].Value}",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                rawYaml = System.Text.RegularExpressions.Regex.Replace(
                    rawYaml,
                    @"(?m)^(?=[a-zA-Z0-9_]+:)", 
                    "\n"
                );
                rawYaml = rawYaml.TrimStart('\n');

                File.WriteAllText(Path.Combine(_currentDirectory, relativePath), rawYaml);
            }
        }
        public Interaction<DictionaryErrorWindowViewModel, bool> ShowParseError { get; } = new();
        private void ProcessParseError(string formatName, string detailedMessage, int errorLineNumber, string filePath) {
            
            var targetEncoding = filePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) 
                ? _currentPresampEncoding 
                : System.Text.Encoding.UTF8;

            var errorVm = new DictionaryErrorWindowViewModel {
                ErrorTitle = $"{formatName} {ThemeManager.GetString("dict.error.syntax")}",
                ErrorMessage = $"{ThemeManager.GetString("dict.error.line.error")} {errorLineNumber}:\n{detailedMessage}",
                FilePath = filePath,
                FileEncoding = targetEncoding
            };
            
            try {
                string[] lines = System.IO.File.ReadAllLines(filePath, targetEncoding);
                errorVm.FullFileLines = lines;
                
                int errLineIdx = errorLineNumber - 1; 
                
                for (int i = 0; i < lines.Length; i++) {
                    errorVm.ErrorContextLines.Add(new ParseErrorLineContext {
                        LineNumber = i + 1,
                        ActualLineIndex = i,
                        Text = lines[i],
                        IsErrorLine = (i == errLineIdx)
                    });
                }
            } catch {
                errorVm.ErrorContextLines.Add(new ParseErrorLineContext { 
                    LineNumber = errorLineNumber, 
                    Text = $"{ThemeManager.GetString("dict.error.could.not.load.context")}", 
                    IsErrorLine = true 
                });
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                ShowParseError.Handle(errorVm)
                    .Subscribe(new AnonymousObserver<bool>(didSave => {
                        if (didSave) {
                            LoadSelectedFile();
                        }
                    }));
            }, Avalonia.Threading.DispatcherPriority.Normal);
        }
    }

    public class ObservableRangeCollection<T> : ObservableCollection<T> {
        private bool _suppressNotification = false;

        protected override void OnCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            if (!_suppressNotification) {
                base.OnCollectionChanged(e);
            }
        }

        public void AddRange(IEnumerable<T> collection) {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            _suppressNotification = true;
            try {
                foreach (var item in collection) {
                    Items.Add(item);
                }
            } finally {
                _suppressNotification = false;
                OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                    System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            }
        }
    }

    internal sealed class AnonymousObserver<T> : IObserver<T> {
        private readonly Action<T> _onNext;
        private readonly Action<Exception>? _onError;
        private readonly Action? _onCompleted;
        public AnonymousObserver(Action<T> onNext, Action<Exception>? onError = null, Action? onCompleted = null) {
            _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));
            _onError = onError;
            _onCompleted = onCompleted;
        }
        public void OnNext(T value) => _onNext(value);
        public void OnError(Exception error) => _onError?.Invoke(error);
        public void OnCompleted() => _onCompleted?.Invoke();
    }
    
    public class BracketStyleEmitter : ChainedEventEmitter {
        private int _depth = 0;
        public BracketStyleEmitter(IEventEmitter nextEmitter) : base(nextEmitter) { }
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter) {
            if (eventInfo.Source.Value is string val && val.Length >= 2 && val.StartsWith("\"") && val.EndsWith("\"")) {
                string strippedValue = val.Substring(1, val.Length - 2);
                var newSource = new YamlDotNet.Serialization.ObjectDescriptor(strippedValue, typeof(string), typeof(string));
                var newEventInfo = new ScalarEventInfo(newSource) { Style = ScalarStyle.DoubleQuoted };
                base.Emit(newEventInfo, emitter);
                return;
            }
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter) {
            _depth++;
            eventInfo.Style = _depth >= 3 ? MappingStyle.Flow : MappingStyle.Block;
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(MappingEndEventInfo eventInfo, IEmitter emitter) {
            _depth--;
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter) {
            _depth++;
            eventInfo.Style = _depth >= 3 ? SequenceStyle.Flow : SequenceStyle.Block;
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(SequenceEndEventInfo eventInfo, IEmitter emitter) {
            _depth--;
            base.Emit(eventInfo, emitter);
        }
    }
}
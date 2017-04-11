﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AcManager.Tools.Helpers;
using AcManager.Tools.Managers.Presets;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Windows.Controls;
using JetBrains.Annotations;

namespace AcManager.Controls {
    public class UserPresetsControl : Control, IHierarchicalItemPreviewProvider {
        internal class ChangedPresetEventArgs : EventArgs {
            public string Key { get; }

            public string Value { get; }

            public ChangedPresetEventArgs(string key, string value) {
                Key = key;
                Value = value;
            }
        }
        
        public static bool OptionSmartChangedHandling = true;

        private static readonly Dictionary<string, WeakList<UserPresetsControl>> Instances = new Dictionary<string, WeakList<UserPresetsControl>>();

        private static event EventHandler<ChangedPresetEventArgs> PresetSelected;

        static UserPresetsControl() {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(UserPresetsControl), new FrameworkPropertyMetadata(typeof(UserPresetsControl)));
        }

        public UserPresetsControl() {
            SaveCommand = new DelegateCommand(SaveExecute, SaveCanExecute);
            Loaded += UserPresetsControl_Loaded;
            Unloaded += UserPresetsControl_Unloaded;
        }

        [CanBeNull]
        public static string GetCurrentFilename(string key) {
            return ValuesStorage.GetString("__userpresets_p_" + key);
        }
        
        public static bool IsChanged(string key) {
            return ValuesStorage.GetBool("__userpresets_c_" + key);
        }

        [NotNull]
        private static IEnumerable<UserPresetsControl> GetInstance(string key) {
            return (IEnumerable<UserPresetsControl>)Instances.GetValueOrDefault(key) ?? new UserPresetsControl[0];
        }

        public static bool HasInstance(string key) {
            return GetInstance(key).Any();
        }

        public static void RescanCategory([NotNull] string category, bool reloadPresets) {
            foreach (var c in Instances.Values.SelectMany(x => x)) {
                if (c?._presetable?.PresetableCategory == category) {
                    c.UpdateSavedPresets();

                    if (reloadPresets && c._selectedPresetFilename != null) {
                        var entry = c.SavedPresets.FirstOrDefault(x => FileUtils.ArePathsEqual(x.Filename, c._selectedPresetFilename));
                        if (entry == null) {
                            Logging.Warning($@"Can’t set preset to “{c._selectedPresetFilename}”, entry not found");
                        } else if (!ReferenceEquals(c.CurrentUserPreset, entry)) {
                            c.CurrentUserPreset = entry;
                        } else {
                            c.SelectionChanged(entry);
                        }
                    }
                }
            }
        }

        public static bool LoadPreset(string key, string filename) {
            ValuesStorage.Set("__userpresets_p_" + key, filename);
            ValuesStorage.Set("__userpresets_c_" + key, false);

            var r = false;
            foreach (var c in GetInstance(key)) {
                c.UpdateSavedPresets();

                var entry = c.SavedPresets.FirstOrDefault(x => FileUtils.ArePathsEqual(x.Filename, filename));
                Logging.Debug(entry?.Filename);
                if (entry == null) {
                    Logging.Warning($@"Can’t set preset to “{filename}”, entry not found");
                } else if (!ReferenceEquals(c.CurrentUserPreset, entry)) {
                    c.CurrentUserPreset = entry;
                    Logging.Debug("CurrentUserPreset=entry");
                } else {
                    c.SelectionChanged(entry);
                    Logging.Debug("SelectionChanged(entry)");
                }

                r = true;
            }

            return r;
        }

        public static bool LoadPreset(string key, string filename, string serialized, bool changed) {
            ValuesStorage.Set("__userpresets_p_" + key, filename);
            ValuesStorage.Set("__userpresets_c_" + key, changed);

            var r = false;
            foreach (var c in GetInstance(key)) {
                c.UpdateSavedPresets();

                var entry = c.SavedPresets.FirstOrDefault(x => FileUtils.ArePathsEqual(x.Filename, filename));
                c.CurrentUserPreset = entry;
                c.UserPresetable?.ImportFromPresetData(serialized);
                c.SetChanged(changed);
                r = true;
            }

            return r;
        }

        public static bool LoadSerializedPreset([NotNull] string key, [NotNull] string serialized) {
            ValuesStorage.Remove("__userpresets_p_" + key);
            ValuesStorage.Set("__userpresets_c_" + key, false);

            var r = false;
            foreach (var c in GetInstance(key)) {
                c.CurrentUserPreset = null;
                c.UserPresetable?.ImportFromPresetData(serialized);
                r = true;
            }

            return r;
        }

        public static bool LoadBuiltInPreset([NotNull] string key, [NotNull] string presetName) {
            return LoadBuiltInPreset(key, key, presetName);
        }

        public static bool LoadBuiltInPreset([NotNull] string key, [NotNull] string category, [NotNull] string presetName) {
            var filename = Path.Combine(PresetsManager.Instance.GetDirectory(category), presetName) + PresetsManager.FileExtension;
            return LoadPreset(key, filename);
        }

        private bool _loaded;

        private void UserPresetsControl_Loaded(object sender, RoutedEventArgs e) {
            if (_loaded) return;
            _loaded = true;
            PresetSelected += UserPresetsControl_PresetSelected;
            PresetsManager.PresetSaved += UserPresetsControl_PresetSaved;
        }

        private void UserPresetsControl_Unloaded(object sender, RoutedEventArgs e) {
            if (!_loaded) return;
            _loaded = false;
            PresetSelected -= UserPresetsControl_PresetSelected;
            PresetsManager.PresetSaved -= UserPresetsControl_PresetSaved;
        }

        private void UserPresetsControl_PresetSelected(object sender, ChangedPresetEventArgs args) {
            if (ReferenceEquals(sender, this) || _presetable == null || _presetable.PresetableKey != args.Key) return;
            SwitchTo(args.Value);
        }

        private void UserPresetsControl_PresetSaved(object sender, PresetSavedEventArgs args) {
            if (_presetable == null || _presetable.PresetableKey != args.Key) return;
            _currentUserPresetData = null;
            SelectedPresetFilename = args.Filename;
            SetChanged(false);
        }

        private string _selectedPresetFilename;

        public string SelectedPresetFilename {
            get {
                return _selectedPresetFilename ?? ValuesStorage.GetString("__userpresets_p_" + _presetable.PresetableKey)
                        ?? string.Empty;
            }
            set {
                if (Equals(value, _selectedPresetFilename)) return;
                _selectedPresetFilename = value;
                if (_presetable != null) {
                    ValuesStorage.Set("__userpresets_p_" + _presetable.PresetableKey, value);
                }
            }
        }

        public static readonly DependencyProperty ShowSaveButtonProperty = DependencyProperty.Register(nameof(ShowSaveButton), typeof(bool),
                typeof(UserPresetsControl), new FrameworkPropertyMetadata(true));

        public bool ShowSaveButton {
            get { return (bool)GetValue(ShowSaveButtonProperty); }
            set { SetValue(ShowSaveButtonProperty, value); }
        }

        public static readonly DependencyProperty CurrentUserPresetProperty = DependencyProperty.Register("CurrentUserPreset", typeof(ISavedPresetEntry),
            typeof(UserPresetsControl), new PropertyMetadata(OnCurrentUserPresetChanged));

        private static void OnCurrentUserPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            ((UserPresetsControl)d).SelectionChanged((ISavedPresetEntry)e.NewValue);
        }

        public ISavedPresetEntry CurrentUserPreset {
            get { return (ISavedPresetEntry)GetValue(CurrentUserPresetProperty); }
            set { SetValue(CurrentUserPresetProperty, value); }
        }

        private bool _ignoreNext, _partiallyIgnoreNext;

        private void SelectionChanged(ISavedPresetEntry entry) {
            if (_ignoreNext) {
                _ignoreNext = false;
                return;
            }

            if (entry == null) return;
            SelectedPresetFilename = entry.Filename;
            _currentUserPresetData = null;

            if (!_partiallyIgnoreNext && _presetable != null) {
                PresetSelected?.Invoke(this, new ChangedPresetEventArgs(_presetable.PresetableKey, entry.ToString()));

                try {
                    _presetable.ImportFromPresetData(entry.ReadData());
                } catch (Exception) {
                    return; // TODO: Informing
                }
            }

            SetChanged(false);
        }

        private void SwitchTo(string value) {
            _partiallyIgnoreNext = true;
            CurrentUserPreset = SavedPresets.FirstOrDefault(x => x.ToString() == value);
            _partiallyIgnoreNext = false;
        }

        public ICommand SaveCommand { get; }

        public bool SaveCanExecute() {
            return _presetable != null && _presetable.CanBeSaved;
        }

        public void SaveExecute() {
            if (_presetable == null) return;
            PresetsManager.Instance.SavePresetUsingDialog(_presetable.PresetableKey, _presetable.PresetableCategory,
                    _presetable.ExportToPresetData(), CurrentUserPreset?.Filename);
        }

        public void SwitchToNext() {
            var presets = SavedPresets;
            
            var selectedId = presets.FindIndex(x => x.Filename == SelectedPresetFilename);
            if (selectedId == -1) {
                var defaultPreset = (_presetable as IUserPresetableDefaultPreset)?.DefaultPreset;
                CurrentUserPreset = presets.FirstOrDefault(x => x.DisplayName == defaultPreset) ?? presets.FirstOrDefault();
            } else if (++selectedId >= presets.Count) {
                CurrentUserPreset = presets.FirstOrDefault();
            } else {
                CurrentUserPreset = presets.ElementAtOrDefault(selectedId);
            }
        }

        public void SwitchToPrevious() {
            var presets = SavedPresets;

            var selectedId = presets.FindIndex(x => x.Filename == SelectedPresetFilename);
            if (selectedId == -1) {
                var defaultPreset = (_presetable as IUserPresetableDefaultPreset)?.DefaultPreset;
                CurrentUserPreset = presets.FirstOrDefault(x => x.DisplayName == defaultPreset) ?? presets.FirstOrDefault();
            } else if (--selectedId < 0) {
                CurrentUserPreset = presets.LastOrDefault();
            } else {
                CurrentUserPreset = presets.ElementAtOrDefault(selectedId);
            }
        }

        public static readonly DependencyPropertyKey PreviewProviderPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewProvider), typeof(IPresetsPreviewProvider),
                typeof(UserPresetsControl), new PropertyMetadata(null));

        public static readonly DependencyProperty PreviewProviderProperty = PreviewProviderPropertyKey.DependencyProperty;

        public IPresetsPreviewProvider PreviewProvider => (IPresetsPreviewProvider)GetValue(PreviewProviderProperty);

        public static readonly DependencyProperty UserPresetableProperty = DependencyProperty.Register("UserPresetable", typeof(IUserPresetable),
            typeof(UserPresetsControl), new PropertyMetadata(OnUserPresetableChanged));

        private static void OnUserPresetableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            ((UserPresetsControl)d).OnUserPresetableChanged((IUserPresetable)e.OldValue, (IUserPresetable)e.NewValue);
        }

        private static readonly DependencyPropertyKey SavedPresetsPropertyKey = DependencyProperty.RegisterReadOnly("SavedPresets", 
            typeof(ObservableCollection<ISavedPresetEntry>), typeof(UserPresetsControl), null);
        public static readonly DependencyProperty SavedPresetsProperty = SavedPresetsPropertyKey.DependencyProperty;

        public ObservableCollection<ISavedPresetEntry> SavedPresets => (ObservableCollection<ISavedPresetEntry>)GetValue(SavedPresetsProperty);

        private static readonly DependencyPropertyKey SavedPresetsGroupedPropertyKey = DependencyProperty.RegisterReadOnly("SavedPresetsGrouped", 
            typeof(HierarchicalGroup), typeof(UserPresetsControl), null);
        public static readonly DependencyProperty SavedPresetsGroupedProperty = SavedPresetsGroupedPropertyKey.DependencyProperty;

        public HierarchicalGroup SavedPresetsGrouped => (HierarchicalGroup)GetValue(SavedPresetsGroupedProperty);

        private IUserPresetable _presetable;

        private void OnUserPresetableChanged(IUserPresetable oldValue, IUserPresetable newValue) {
            if (oldValue != null) {
                Instances.GetList(oldValue.PresetableKey).Remove(this);
            }

            if (newValue != null) {
                Instances.GetList(newValue.PresetableKey).Add(this);
            }

            if (_presetable != null) {
                PresetsManager.Instance.Watcher(_presetable.PresetableCategory).Update -= Presets_Update;
                _presetable.Changed -= Presetable_Changed;
            }

            _presetable = newValue;
            SetValue(PreviewProviderPropertyKey, _presetable as IPresetsPreviewProvider);
            if (_presetable == null) return;

            PresetsManager.Instance.Watcher(_presetable.PresetableCategory).Update += Presets_Update;
            _presetable.Changed += Presetable_Changed;
            UpdateSavedPresets();
            SetChanged();
        }

        private static string GetHead(string filename, string from) {
            for (int i = filename.LastIndexOf(Path.DirectorySeparatorChar), t; i > 0; i = t) {
                t = filename.LastIndexOf(Path.DirectorySeparatorChar, i - 1);
                if (t <= from.Length + 1) {
                    return filename.Substring(0, i);
                }
            }
            return from;
        }

        public class InnerSavedPresetEntry : ISavedPresetEntry {
            private readonly ISavedPresetEntry _baseEntry;

            public InnerSavedPresetEntry([NotNull] ISavedPresetEntry baseEntry, [NotNull] IUserPresetableCustomDisplay customDisplay) {
                _baseEntry = baseEntry;
                DisplayName = customDisplay.GetDisplayName(baseEntry.DisplayName, ReadData());
            }

            public event PropertyChangedEventHandler PropertyChanged {
                add { _baseEntry.PropertyChanged += value; }
                remove { _baseEntry.PropertyChanged -= value; }
            }

            public bool Equals(ISavedPresetEntry other) {
                return _baseEntry.Equals(other);
            }

            public string DisplayName { get; }

            public string Filename => _baseEntry.Filename;

            private string _data;

            public string ReadData() {
                return _data ?? (_data = _baseEntry.ReadData());
            }

            public void SetParent(string baseDirectory) {
                _baseEntry.SetParent(baseDirectory);
            }
        }

        private static ISavedPresetEntry Fix(ISavedPresetEntry entry, string parent, [CanBeNull] IUserPresetableCustomDisplay customDisplay) {
            entry.SetParent(parent);

            if (customDisplay != null) {
                entry = new InnerSavedPresetEntry(entry, customDisplay);
            }
            
            return entry;
        }

        private class UserPresetableComparer : IComparer<ISavedPresetEntry> {
            private readonly IUserPresetableCustomSorting _sorting;

            public UserPresetableComparer([NotNull] IUserPresetableCustomSorting sorting) {
                _sorting = sorting;
            }

            public int Compare(ISavedPresetEntry x, ISavedPresetEntry y) {
                if (x == null || y == null) return 0;
                return _sorting.Compare(x.DisplayName, x.ReadData(), y.DisplayName, y.ReadData());
            }
        }

        public static IEnumerable<object> GroupPresets(IEnumerable<ISavedPresetEntry> entries, string mainDirectory, 
                [CanBeNull] IUserPresetableCustomDisplay customDisplay, [CanBeNull] IUserPresetableCustomSorting sorting) {
            var list = entries.Select(x => new {
                Entry = x,
                Directory = GetHead(x.Filename, mainDirectory)
            }).ToList();

            foreach (var directory in list.Select(x => x.Directory).Where(x => x != mainDirectory).Distinct()) {
                var directoryValue = directory;
                var subList = list.Where(x => x.Directory == directoryValue).Select(x => x.Entry).ToList();
                if (subList.Count > 1){
                    yield return new HierarchicalGroup(Path.GetFileName(directory), GroupPresets(subList, directory, customDisplay, sorting));
                } else if (list.Any()) {
                    yield return Fix(subList[0], mainDirectory, customDisplay);
                }
            }

            var enumerable = list.Where(x => x.Directory == mainDirectory);
            if (sorting != null) {
                var sorted = enumerable.Select(x => x.Entry).ToList();
                sorted.Sort(new UserPresetableComparer(sorting));
                for (var i = 0; i < sorted.Count; i++) {
                    yield return Fix(sorted[i], mainDirectory, customDisplay);
                }
            } else {
                foreach (var entry in enumerable) {
                    yield return Fix(entry.Entry, mainDirectory, customDisplay);
                }
            }
        }

        public static IEnumerable<object> GroupPresets(string presetableKey) {
            return GroupPresets(presetableKey, null, null);
        }

        public static IEnumerable<object> GroupPresets(string presetableKey, [CanBeNull] IUserPresetableCustomDisplay customDisplay,
                [CanBeNull] IUserPresetableCustomSorting sorting) {
            return GroupPresets(PresetsManager.Instance.GetSavedPresets(presetableKey), PresetsManager.Instance.GetDirectory(presetableKey), 
                customDisplay, sorting);
        }

        private void UpdateSavedPresets() {
            if (_presetable == null) return;

            var presets = new ObservableCollection<ISavedPresetEntry>(PresetsManager.Instance.GetSavedPresets(_presetable.PresetableCategory));
            SetValue(SavedPresetsPropertyKey, presets);
            SetValue(SavedPresetsGroupedPropertyKey, new HierarchicalGroup("",
                    GroupPresets(presets, PresetsManager.Instance.GetDirectory(_presetable.PresetableCategory), _presetable as IUserPresetableCustomDisplay,
                            _presetable as IUserPresetableCustomSorting)));
            
            _ignoreNext = true;
            var defaultPreset = (_presetable as IUserPresetableDefaultPreset)?.DefaultPreset;
            CurrentUserPreset = presets.FirstOrDefault(x => x.Filename == SelectedPresetFilename) ??
                    (defaultPreset == null ? null : presets.FirstOrDefault(x => x.DisplayName == defaultPreset));
            _ignoreNext = false;
        }

        private static readonly DependencyPropertyKey ChangedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Changed), 
            typeof(bool), typeof(UserPresetsControl), null);
        public static readonly DependencyProperty ChangedProperty = ChangedPropertyKey.DependencyProperty;

        public bool Changed => (bool)GetValue(ChangedProperty);

        private void SetChanged(bool? value = null) {
            if (_presetable == null || Changed == value) return;
            
            if (value == true && _comboBox != null) {
                _comboBox.SelectedItem = null;
            }

            var key = $@"__userpresets_c_{_presetable.PresetableKey}";
            if (value.HasValue) {
                ValuesStorage.Set(key, value.Value);
            } else {
                value = ValuesStorage.GetBool(key);
            }
            
            SetValue(ChangedPropertyKey, value.Value);
        }

        private string _currentUserPresetData;

        private void Presetable_Changed(object sender, EventArgs e) {
            if (OptionSmartChangedHandling) {
                if (_currentUserPresetData == null) {
                    _currentUserPresetData = CurrentUserPreset?.ReadData();
                }

                var actualData = _presetable.ExportToPresetData();
                SetChanged(actualData != _currentUserPresetData);
            } else {
                SetChanged(true);
            }
        }

        private void Presets_Update(object sender, EventArgs e) {
            UpdateSavedPresets();
        }

        private HierarchicalComboBox _comboBox;

        public override void OnApplyTemplate() {
            if (_comboBox != null) {
                _comboBox.ItemSelected -= OnSelectionChanged;
                _comboBox.PreviewProvider = null;
            }

            _comboBox = GetTemplateChild(@"PART_ComboBox") as HierarchicalComboBox;
            if (_comboBox != null) {
                _comboBox.ItemSelected += OnSelectionChanged;
                _comboBox.PreviewProvider = this;
            }

            base.OnApplyTemplate();
        }

        private void OnSelectionChanged(object sender, SelectedItemChangedEventArgs e) {
            var entry = _comboBox?.SelectedItem as ISavedPresetEntry;
            if (entry == null) return;

            if (!ReferenceEquals(CurrentUserPreset, entry)) {
                CurrentUserPreset = entry;
                foreach (var c in GetInstance(_presetable.PresetableKey).ApartFrom(this)) {
                    var en = c.SavedPresets.FirstOrDefault(x => FileUtils.ArePathsEqual(x.Filename, entry.Filename));
                    if (en == null) {
                        Logging.Warning($@"Can’t set preset to “{entry.Filename}”, entry not found");
                    } else if (!ReferenceEquals(c.CurrentUserPreset, en)) {
                        c.CurrentUserPreset = en;
                    } else {
                        c.SelectionChanged(en);
                    }
                }
            } else {
                SelectionChanged(entry);
            }
        }

        public IUserPresetable UserPresetable {
            get { return (IUserPresetable)GetValue(UserPresetableProperty); }
            set { SetValue(UserPresetableProperty, value); }
        }

        public object GetPreview(object item) {
            var p = _presetable as IPresetsPreviewProvider;
            if (p == null) return null;

            var data = (item as ISavedPresetEntry)?.ReadData();
            if (data == null) return null;

            return p.GetPreview(data);
        }
    }
}

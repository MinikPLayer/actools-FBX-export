﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AcManager.Controls.Helpers;
using AcManager.CustomShowroom;
using AcManager.Pages.Dialogs;
using AcManager.Tools;
using AcManager.Tools.AcErrors;
using AcManager.Tools.Filters.Testers;
using AcManager.Tools.Helpers;
using AcManager.Tools.Managers;
using AcManager.Tools.Managers.Presets;
using AcManager.Tools.Objects;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Dialogs;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using FirstFloor.ModernUI.Windows.Controls;
using StringBasedFilter;

namespace AcManager.Pages.ContentTools {
    public partial class BatchPreviewsUpdater {
        #region Loading
        protected override async Task<bool> LoadAsyncOverride(IProgress<AsyncProgressEntry> progress, CancellationToken cancellation) {
            await CarsManager.Instance.EnsureLoadedAsync();

            Entries = new ChangeableObservableCollection<CarObjectEntry>(
                    CarsManager.Instance.Enabled.OrderBy(x => x.DisplayName).Select(x => new CarObjectEntry(x)));

            for (var i = 0; i < Entries.Count; i++) {
                var entry = Entries[i];
                progress.Report(new AsyncProgressEntry($"Loading skins ({entry.Car.DisplayName})…", i, Entries.Count));
                await entry.Car.SkinsManager.EnsureLoadedAsync();

                entry.SelectedSkins = entry.Car.EnabledOnlySkins.Where(x => x.HasError(AcErrorType.CarSkin_PreviewIsMissing)).ToList();
            }

            Entries.ItemPropertyChanged += OnEntryPropertyChanged;
            SelectedEntry = Entries.FirstOrDefault();
            UpdateTotalSelected();
            return Entries.Any();
        }
        #endregion

        #region Entries, data
        private bool FilterTest(object o) {
            return _filter?.Test(((CarObjectEntry)o).Car) != false;
        }

        private void OnEntryPropertyChanged(object sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(CarObjectEntry.SelectedSkins):
                    UpdateTotalSelected();
                    if (ReferenceEquals(sender, SelectedEntry)) {
                        UpdateSkinsListSelection();
                    }
                    break;
            }
        }

        private DelegateCommand _selectAllCarsCommand;

        public DelegateCommand SelectAllCarsCommand => _selectAllCarsCommand ?? (_selectAllCarsCommand = new DelegateCommand(() => {
            foreach (var entry in Entries.Where(FilterTest)) {
                entry.IsSelected = true;
            }
        }));

        private DelegateCommand _removeSelectionFromCarsCommand;

        public DelegateCommand RemoveSelectionFromCarsCommand => _removeSelectionFromCarsCommand ?? (_removeSelectionFromCarsCommand = new DelegateCommand(() => {
            foreach (var entry in Entries.Where(FilterTest)) {
                entry.IsSelected = false;
            }
        }));

        protected override void InitializeOverride(Uri uri) {
            InitializeComponent();
            InputBindings.AddRange(new[] {
                new InputBinding(SelectAllCarsCommand, new KeyGesture(Key.A, ModifierKeys.Control)),
                new InputBinding(RemoveSelectionFromCarsCommand, new KeyGesture(Key.D, ModifierKeys.Control)),
            });
        }

        private BetterListCollectionView _entriesView;

        public BetterListCollectionView EntriesView {
            get => _entriesView;
            set => this.Apply(value, ref _entriesView);
        }

        private ChangeableObservableCollection<CarObjectEntry> _entries;

        public ChangeableObservableCollection<CarObjectEntry> Entries {
            get => _entries;
            set {
                if (Equals(value, _entries)) return;
                _entries = value;
                OnPropertyChanged();

                var view = new BetterListCollectionView(value);
                using (view.DeferRefresh()) {
                    view.Filter = FilterTest;
                }

                EntriesView = view;

            }
        }

        private string _filterValue;
        private IFilter<CarObject> _filter;

        public string FilterValue {
            get => _filterValue;
            set {
                if (Equals(value, _filterValue)) return;
                _filterValue = value;
                _filter = string.IsNullOrWhiteSpace(value) ? null : Filter.Create(CarObjectTester.Instance, value);
                EntriesView?.Refresh();
                OnPropertyChanged();
            }
        }

        private CarObjectEntry _selectedEntry;

        public CarObjectEntry SelectedEntry {
            get => _selectedEntry;
            set {
                if (Equals(value, _selectedEntry)) return;

                if (_selectedEntry != null) {
                    _selectedEntry.IsCurrent = false;
                }

                _selectedEntry = value;

                if (_selectedEntry != null) {
                    _selectedEntry.IsCurrent = true;
                    UpdateSkinsListSelection();
                }

                OnPropertyChanged();
            }
        }

        private int _totalSelected;

        public int TotalSelected {
            get => _totalSelected;
            set {
                if (Equals(value, _totalSelected)) return;
                _totalSelected = value;
                OnPropertyChanged();
                _updatePreviewsCommand?.RaiseCanExecuteChanged();
                _updatePreviewsOptionsCommand?.RaiseCanExecuteChanged();
            }
        }

        private void UpdateTotalSelected() {
            TotalSelected = Entries.Sum(x => x.SelectedSkins.Count);
        }

        public class CarObjectEntry : NotifyPropertyChanged {
            public CarObject Car { get; }

            public CarObjectEntry(CarObject car) {
                Car = car;
            }

            private ICollection<CarSkinObject> _selectedSkins = new List<CarSkinObject>(0);

            public ICollection<CarSkinObject> SelectedSkins {
                get => _selectedSkins;
                set {
                    if (Equals(value, _selectedSkins)) return;
                    _selectedSkins = value;
                    OnPropertyChanged();

                    if (value.Count == Car.EnabledOnlySkins.Count) {
                        SetIsSelected(true);
                    } else if (value.Count == 0) {
                        SetIsSelected(false);
                    } else {
                        SetIsSelected(null);
                    }
                }
            }

            private bool SetIsSelected(bool? value) {
                if (value == _isSelected) return false;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(IsSelectedReadOnly));
                return true;
            }

            private bool? _isSelected = false;

            public bool? IsSelected {
                get => _isSelected;
                set {
                    if (!SetIsSelected(value)) return;

                    if (IsSelected == true) {
                        SelectedSkins = Car.EnabledOnlySkins;
                    } else if (IsSelected == false) {
                        SelectedSkins = new List<CarSkinObject>(0);
                    }
                }
            }

            public bool? IsSelectedReadOnly {
                get => _isSelected;

                // ReSharper disable once ValueParameterNotUsed
                set => OnPropertyChanged();
            }

            private bool _isCurrent;

            public bool IsCurrent {
                get => _isCurrent;
                set => Apply(value, ref _isCurrent);
            }
        }
        #endregion

        #region Controls behavior
        private bool _ignore;

        private void UpdateSkinsListSelection() {
            if (_ignore) return;
            _ignore = true;

            try {
                SkinsList.SelectedItems.Clear();

                var value = SelectedEntry;
                if (value == null) return;
                foreach (var skin in value.SelectedSkins) {
                    SkinsList.SelectedItems.Add(skin);
                }
            } finally {
                _ignore = false;
            }
        }

        private void OnSkinsListSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_ignore) return;
            _ignore = true;

            try {
                SelectedEntry.SelectedSkins = SkinsList.SelectedItems.OfType<CarSkinObject>().ToList();
            } finally {
                _ignore = false;
            }
        }

        /*private void OnListBoxMouseDown(object sender, MouseButtonEventArgs e) {
            var listBox = (ListBox)sender;
            listBox.SelectionMode = Keyboard.Modifiers == ModifierKeys.None ? SelectionMode.Multiple : SelectionMode.Extended;
        }

        private async void OnListBoxMouseUp(object sender, EventArgs e) {
            var listBox = (ListBox)sender;
            await Task.Delay(1);
            listBox.SelectionMode = SelectionMode.Extended;
        }*/

        private void OnCarsListCheckboxClick(object sender, RoutedEventArgs e) {
            e.Handled = true;

            if (((FrameworkElement)sender).DataContext is CarObjectEntry item) {
                var isSelected = item.IsSelected != true;

                try {
                    if (Keyboard.Modifiers == ModifierKeys.Shift && SelectedEntry != item) {
                        var itemIndex = Entries.IndexOf(item);
                        var focusedIndex = Entries.IndexOf(SelectedEntry);

                        if (itemIndex != -1 && focusedIndex != -1) {
                            foreach (var entry in Entries.Skip(Math.Min(itemIndex, focusedIndex))
                                                         .Take((itemIndex - focusedIndex).Abs())
                                                         .Where(FilterTest)) {
                                entry.IsSelected = isSelected;
                            }
                            return;
                        }
                    }

                    item.IsSelected = isSelected;
                } finally {
                    SelectedEntry = item;
                }
            }
        }

        private void OnCarsListKeyDown(object sender, KeyEventArgs e) {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;

            switch (e.Key) {
                case Key.A:
                    SelectAllCarsCommand.Execute();
                    break;
                case Key.D:
                    RemoveSelectionFromCarsCommand.Execute();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        /*private void OnSkinsListKeyDown(object sender, KeyEventArgs e) {
            if (Keyboard.Modifiers != ModifierKeys.Control || SelectedEntry == null) return;

            switch (e.Key) {
                case Key.A:
                    SelectedEntry.IsSelected = true;
                    break;
                case Key.D:
                    SelectedEntry.IsSelected = false;
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }*/

        private void OnCarsListBoxItemClick(object sender, MouseButtonEventArgs e) {
            if (((FrameworkElement)sender).DataContext is CarObjectEntry item) {
                SelectedEntry = item;
                if (Keyboard.Modifiers == ModifierKeys.Control) {
                    item.IsSelected = item.IsSelected != true;
                }

                e.Handled = true;
            }
        }
        #endregion

        #region Presets, actual shooting
        private bool _showMessage = false;

        private async Task Run(string preset = null, UpdatePreviewMode? mode = null) {
            if (TotalSelected == 0) return;

            if (_showMessage && ModernDialog.ShowMessage(
                    "To save time and memory, new previews will be applied immediately. Current ones, if exist, will be moved to the Recycle Bin. Continue?",
                    ToolsStrings.Common_Warning, MessageBoxButton.YesNo) != MessageBoxResult.Yes) {
                return;
            }

            var errors = await Entries.Where(x => x.SelectedSkins.Count > 0)
                   .Select(x => new ToUpdatePreview(x.Car, x.SelectedSkins.ToList()))
                   .Run(mode, preset);

            if (errors != null) {
                foreach (var entry in Entries) {
                    var error = errors.Any(x => x.ToUpdate.Car == entry.Car);
                    if (!error) entry.IsSelected = false;
                }
            }
        }

        private AsyncCommand _updatePreviewsCommand;

        public AsyncCommand UpdatePreviewsCommand => _updatePreviewsCommand ??
                (_updatePreviewsCommand = new AsyncCommand(() => Run(), () => TotalSelected > 0));

        private AsyncCommand _updatePreviewsOptionsCommand;

        public AsyncCommand UpdatePreviewsOptionsCommand => _updatePreviewsOptionsCommand ??
                (_updatePreviewsOptionsCommand = new AsyncCommand(() => Run(mode: UpdatePreviewMode.Options), () => TotalSelected > 0));

        private HierarchicalItemsView _updatePreviewsPresets;
        private readonly PresetsMenuHelper _helper = new PresetsMenuHelper();

        public HierarchicalItemsView UpdatePreviewsPresets {
            get => _updatePreviewsPresets;
            set => this.Apply(value, ref _updatePreviewsPresets);
        }

        private AsyncCommand _selectDifferentCommand;

        public AsyncCommand SelectDifferentCommand => _selectDifferentCommand ?? (_selectDifferentCommand = new AsyncCommand(() => SelectDifferent()));

        private static string GetChecksum(string previewFilename) {
            var comment = ExifComment.Read(previewFilename);
            if (comment == null) return null;

            var match = Regex.Match(comment, @"checksum\w*:\s*([\w/+]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private async Task SelectDifferent(string presetFilename = null) {
            if (!SettingsHolder.CustomShowroom.CustomShowroomPreviews) return;
            using (var waiting = WaitingDialog.Create("Scanning…")) {
                var list = Entries.ToList();
                var cancellation = waiting.CancellationToken;

                await Task.Run(() => {
                    var checksum = CmPreviewsTools.GetChecksum(SettingsHolder.CustomShowroom.CspPreviewsReady, presetFilename);
                    Logging.Debug($"Checksum: {checksum}");

                    for (var i = 0; i < list.Count; i++) {
                        if (cancellation.IsCancellationRequested) return;

                        var entry = list[i];
                        waiting.Report(new AsyncProgressEntry(entry.Car.DisplayName, i, list.Count));

                        var selected = entry.Car.EnabledOnlySkins.Where(x => GetChecksum(x.PreviewImage) != checksum).ToList();
                        ActionExtension.InvokeInMainThread(() => entry.SelectedSkins = selected);
                    }
                });
            }
        }

        private bool _runButtonClicked;

        private void InitializePreviews() {
            if (UpdatePreviewsPresets == null) {
                UpdatePreviewsPresets = _helper.Create(
                        new PresetsCategory(SettingsHolder.CustomShowroom.CustomShowroomPreviews
                                ? CmPreviewsSettingsValues.DefaultPresetableKeyValue : CarUpdatePreviewsDialog.PresetableKeyValue),
                        p => {
                            if (_runButtonClicked) {
                                Run(p.VirtualFilename).Ignore();
                            } else {
                                SelectDifferent(p.VirtualFilename).Ignore();
                            }
                        });
            }
        }

        private void OnPreviewsButtonMouseDown(object sender, MouseButtonEventArgs e) {
            _runButtonClicked = true;
            InitializePreviews();
        }

        private void OnDifferentButtonMouseDown(object sender, MouseButtonEventArgs e) {
            _runButtonClicked = false;
            InitializePreviews();
        }

        protected override void DisposeOverride() {
            base.DisposeOverride();
            _helper.Dispose();
        }
        #endregion
    }
}

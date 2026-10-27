using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Typify
{
    public partial class PreferencesWindow : Window
    {
        public bool IsDarkThemeSelected { get; private set; }
        public bool IsLivePreviewSelected { get; private set; }
        public bool EnableWordWrapSelected { get; private set; }
        public bool AutoSaveSelected { get; private set; }
        public bool ShowStatusBarSelected { get; private set; }
        public bool ConfirmBeforeClosingSelected { get; private set; }
        public int AutoSaveIntervalMinutesSelected { get; private set; }
        public double UiScaleSelected { get; private set; }
        public int TabSizeSelected { get; private set; }
        public string FontFamilySelected { get; private set; }

        private bool _isInitializing;

        public event EventHandler PreferencesChanged;

        public PreferencesWindow(
            bool isDarkTheme,
            bool isLivePreview,
            bool enableWordWrap,
            bool autoSave,
            bool showStatusBar,
            bool confirmBeforeClosing,
            int autoSaveIntervalMinutes,
            double uiScale,
            int tabSize,
            string fontFamily)
        {
            _isInitializing = true;
            InitializeComponent();

            IsDarkThemeSelected = isDarkTheme;
            IsLivePreviewSelected = isLivePreview;
            EnableWordWrapSelected = enableWordWrap;
            AutoSaveSelected = autoSave;
            ShowStatusBarSelected = showStatusBar;
            ConfirmBeforeClosingSelected = confirmBeforeClosing;
            AutoSaveIntervalMinutesSelected = autoSaveIntervalMinutes;
            UiScaleSelected = uiScale;
            TabSizeSelected = tabSize;
            FontFamilySelected = fontFamily;

            ThemeComboBox.SelectedIndex = isDarkTheme ? 1 : 0;
            LivePreviewToggle.IsChecked = isLivePreview;
            WordWrapToggle.IsChecked = enableWordWrap;
            AutoSaveToggle.IsChecked = autoSave;
            StatusBarToggle.IsChecked = showStatusBar;
            ConfirmCloseToggle.IsChecked = confirmBeforeClosing;

            AutoSaveSlider.Value = Math.Max(1, Math.Min(30, autoSaveIntervalMinutes));
            UiScaleSlider.Value = Math.Max(0.8, Math.Min(1.5, uiScale));

            SelectComboByValue(TabSizeComboBox, tabSize.ToString());
            SelectComboByValue(FontFamilyComboBox, string.IsNullOrWhiteSpace(fontFamily) ? "Consolas" : fontFamily);

            UpdateAutoSaveLabel((int)AutoSaveSlider.Value);
            UpdateUiScaleLabel(UiScaleSlider.Value);

            SearchTextBox.Text = string.Empty;
            HookEvents();
            _isInitializing = false;

            this.SourceInitialized += (s, e) =>
            {
                NativeWindowStyle.ApplyWindowFrame(this, canMaximize: false, canMinimize: false);
            };
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SelectComboByValue(System.Windows.Controls.ComboBox comboBox, string value)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                var item = comboBox.Items[i] as System.Windows.Controls.ComboBoxItem;
                var content = item?.Content as string;
                if (string.Equals(content, value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private static int ParseIntFromCombo(System.Windows.Controls.ComboBox comboBox, int fallback)
        {
            var selectedItem = comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var text = selectedItem?.Content as string;
            return int.TryParse(text, out int value) ? value : fallback;
        }

        private static string ParseStringFromCombo(System.Windows.Controls.ComboBox comboBox, string fallback)
        {
            var selectedItem = comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var text = selectedItem?.Content as string;
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private void HookEvents()
        {
            ThemeComboBox.SelectionChanged += AnyPreferenceChanged;
            LivePreviewToggle.Checked += AnyPreferenceChanged;
            LivePreviewToggle.Unchecked += AnyPreferenceChanged;
            WordWrapToggle.Checked += AnyPreferenceChanged;
            WordWrapToggle.Unchecked += AnyPreferenceChanged;
            AutoSaveToggle.Checked += AnyPreferenceChanged;
            AutoSaveToggle.Unchecked += AnyPreferenceChanged;
            StatusBarToggle.Checked += AnyPreferenceChanged;
            StatusBarToggle.Unchecked += AnyPreferenceChanged;
            ConfirmCloseToggle.Checked += AnyPreferenceChanged;
            ConfirmCloseToggle.Unchecked += AnyPreferenceChanged;
            AutoSaveSlider.ValueChanged += AnyPreferenceChanged;
            UiScaleSlider.ValueChanged += AnyPreferenceChanged;
            TabSizeComboBox.SelectionChanged += AnyPreferenceChanged;
            FontFamilyComboBox.SelectionChanged += AnyPreferenceChanged;
        }

        private void AutoSaveSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateAutoSaveLabel((int)e.NewValue);
        }

        private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateUiScaleLabel(e.NewValue);
        }

        private void AnyPreferenceChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            ApplyCurrentSelections();
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplySearchFilter(SearchTextBox.Text);
        }

        private void ApplySearchFilter(string rawQuery)
        {
            string query = (rawQuery ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                AppearanceCard.Visibility = Visibility.Visible;
                EditorCard.Visibility = Visibility.Visible;
                WorkflowCard.Visibility = Visibility.Visible;
                SetChildrenVisibility(AppearanceCard, Visibility.Visible);
                SetChildrenVisibility(EditorCard, Visibility.Visible);
                SetChildrenVisibility(WorkflowCard, Visibility.Visible);
                return;
            }

            FilterCard(AppearanceCard, query);
            FilterCard(EditorCard, query);
            FilterCard(WorkflowCard, query);
        }

        private static void FilterCard(FrameworkElement card, string query)
        {
            bool cardMatches = Matches(card.Tag as string, query);
            bool anyChildVisible = false;

            foreach (FrameworkElement child in FindTaggedChildren(card))
            {
                bool childMatches = Matches(child.Tag as string, query);
                child.Visibility = childMatches ? Visibility.Visible : Visibility.Collapsed;
                anyChildVisible |= childMatches;
            }

            card.Visibility = cardMatches || anyChildVisible ? Visibility.Visible : Visibility.Collapsed;

            if (cardMatches)
            {
                SetChildrenVisibility(card, Visibility.Visible);
            }
        }

        private static bool Matches(string source, string query)
        {
            return !string.IsNullOrWhiteSpace(source)
                && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SetChildrenVisibility(DependencyObject parent, Visibility visibility)
        {
            if (parent == null)
            {
                return;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(parent, i) is FrameworkElement element && element.Tag is string)
                {
                    element.Visibility = visibility;
                }

                SetChildrenVisibility(VisualTreeHelper.GetChild(parent, i), visibility);
            }
        }

        private static System.Collections.Generic.IEnumerable<FrameworkElement> FindTaggedChildren(DependencyObject parent)
        {
            if (parent == null)
            {
                yield break;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && element.Tag is string)
                {
                    yield return element;
                }

                foreach (FrameworkElement descendant in FindTaggedChildren(child))
                {
                    yield return descendant;
                }
            }
        }

        private void UpdateAutoSaveLabel(int minutes)
        {
            if (AutoSaveIntervalText is null)
            {
                return;
            }

            AutoSaveIntervalText.Text = minutes + " min";
        }

        private void UpdateUiScaleLabel(double scale)
        {
            if (UiScaleText is null)
            {
                return;
            }

            UiScaleText.Text = scale.ToString("0.00") + "x";
        }

        private void ApplyCurrentSelections()
        {
            IsDarkThemeSelected = ThemeComboBox.SelectedIndex == 1;
            IsLivePreviewSelected = LivePreviewToggle.IsChecked == true;
            EnableWordWrapSelected = WordWrapToggle.IsChecked == true;
            AutoSaveSelected = AutoSaveToggle.IsChecked == true;
            ShowStatusBarSelected = StatusBarToggle.IsChecked == true;
            ConfirmBeforeClosingSelected = ConfirmCloseToggle.IsChecked == true;
            AutoSaveIntervalMinutesSelected = (int)AutoSaveSlider.Value;
            UiScaleSelected = UiScaleSlider.Value;
            TabSizeSelected = ParseIntFromCombo(TabSizeComboBox, 2);
            FontFamilySelected = ParseStringFromCombo(FontFamilyComboBox, "Consolas");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

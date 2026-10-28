using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Typify.TextBox
{
    /// <summary>An editable Typst source editor with lightweight Typst syntax highlighting.</summary>
    public sealed class TypifyTextbox : RichTextBox
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(TypifyTextbox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

        public static readonly DependencyProperty ShowBorderProperty = DependencyProperty.Register(
            nameof(ShowBorder), typeof(bool), typeof(TypifyTextbox),
            new FrameworkPropertyMetadata(true, OnShowBorderChanged));

        public static readonly DependencyProperty IsDarkThemeProperty = DependencyProperty.Register(
            nameof(IsDarkTheme), typeof(bool), typeof(TypifyTextbox),
            new FrameworkPropertyMetadata(false, OnThemeChanged));

        public static readonly DependencyProperty IsHorizontalScrollEnabledProperty = DependencyProperty.Register(
            nameof(IsHorizontalScrollEnabled), typeof(bool), typeof(TypifyTextbox),
            new FrameworkPropertyMetadata(true, OnScrollOptionsChanged));

        private readonly DispatcherTimer _highlightTimer;
        private bool _synchronizing;
        private bool _updatingTextProperty;
        private bool _applyingHighlight;
        private string _lastHighlightedText = string.Empty;
        private bool _lastHighlightedDarkTheme;
        private int _pendingHighlightStart;
        private int _pendingHighlightEnd;

        public TypifyTextbox()
        {
            AcceptsTab = true;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            FontFamily = new FontFamily("Consolas");
            FontSize = 14;
            Foreground = Brush("#333333");
            Background = Brush("#FFFFFF");
            BorderBrush = Brush("#E5E5E5");
            BorderThickness = ShowBorder ? new Thickness(1) : new Thickness(0);
            Padding = new Thickness(10);
            SpellCheck.SetIsEnabled(this, false);

            _highlightTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(35)
            };
            _highlightTimer.Tick += HighlightTimer_Tick;
            TextChanged += Editor_TextChanged;
            ApplyTextToDocument(Text ?? string.Empty);
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public bool ShowBorder
        {
            get { return (bool)GetValue(ShowBorderProperty); }
            set { SetValue(ShowBorderProperty, value); }
        }

        public bool IsDarkTheme
        {
            get { return (bool)GetValue(IsDarkThemeProperty); }
            set { SetValue(IsDarkThemeProperty, value); }
        }

        public bool IsHorizontalScrollEnabled
        {
            get { return (bool)GetValue(IsHorizontalScrollEnabledProperty); }
            set { SetValue(IsHorizontalScrollEnabledProperty, value); }
        }

        public bool IsWordWrapEnabled
        {
            get { return Document.PageWidth < 100000; }
            set { Document.PageWidth = value ? Math.Max(ActualWidth, 300) : 100000; }
        }

        private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var editor = (TypifyTextbox)dependencyObject;
            if (editor._updatingTextProperty) return;
            editor.ApplyTextToDocument((string)e.NewValue ?? string.Empty);
        }

        private static void OnShowBorderChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            ((TypifyTextbox)dependencyObject).BorderThickness = (bool)e.NewValue
                ? new Thickness(1) : new Thickness(0);
        }

        private static void OnThemeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var editor = (TypifyTextbox)dependencyObject;
            editor._lastHighlightedText = null;
            editor.QueueHighlight();
        }

        private static void OnScrollOptionsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            ((TypifyTextbox)dependencyObject).HorizontalScrollBarVisibility = (bool)e.NewValue
                ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        }

        private void ApplyTextToDocument(string value)
        {
            if (_synchronizing) return;
            value = NormalizeLineEndings(value);
            _synchronizing = true;
            try
            {
                string current = ReadEditorText();
                if (!string.Equals(current, value, StringComparison.Ordinal))
                {
                    MarkDirtyRange(current, value);
                    var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                    range.Text = value;
                }
                if (!string.Equals(Text, value, StringComparison.Ordinal))
                {
                    _updatingTextProperty = true;
                    try { SetCurrentValue(TextProperty, value); }
                    finally { _updatingTextProperty = false; }
                }
                QueueHighlight();
            }
            finally { _synchronizing = false; }
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_synchronizing || _applyingHighlight) return;
            string value = ReadEditorText();
            MarkDirtyFromChanges(e.Changes, value.Length);
            if (!string.Equals(Text, value, StringComparison.Ordinal))
            {
                _updatingTextProperty = true;
                try { SetCurrentValue(TextProperty, value); }
                finally { _updatingTextProperty = false; }
            }
            QueueHighlight();
        }

        private string ReadEditorText()
        {
            string value = new TextRange(Document.ContentStart, Document.ContentEnd).Text;
            if (value.EndsWith("\r\n", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 2);
            else if (value.EndsWith("\n", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }

        private static string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        private void QueueHighlight()
        {
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }

        private void HighlightTimer_Tick(object sender, EventArgs e)
        {
            _highlightTimer.Stop();
            string source = ReadEditorText();
            bool isDarkTheme = IsDarkTheme;
            if (string.Equals(source, _lastHighlightedText, StringComparison.Ordinal) && isDarkTheme == _lastHighlightedDarkTheme)
                return;

            bool themeChanged = isDarkTheme != _lastHighlightedDarkTheme;
            bool fullRehighlight = themeChanged || _lastHighlightedText == null || !string.Equals(source, _lastHighlightedText, StringComparison.Ordinal) && (_pendingHighlightEnd <= _pendingHighlightStart);
            if (fullRehighlight)
                ApplySyntaxHighlighting(source);
            else
                ApplySyntaxHighlightingRange(source, _pendingHighlightStart, _pendingHighlightEnd);

            _pendingHighlightStart = 0;
            _pendingHighlightEnd = 0;
            _lastHighlightedText = source;
            _lastHighlightedDarkTheme = isDarkTheme;
        }

        private void ApplySyntaxHighlighting(string source)
        {
            ApplySyntaxHighlightingRange(source, 0, source.Length);
        }

        private void ApplySyntaxHighlightingRange(string source, int dirtyStart, int dirtyEnd)
        {
            if (source.Length == 0) return;
            if (dirtyStart < 0) dirtyStart = 0;
            if (dirtyEnd > source.Length) dirtyEnd = source.Length;
            if (dirtyEnd <= dirtyStart) return;

            int rangeStart = ExpandToLineStart(source, dirtyStart);
            int rangeEnd = ExpandToLineEnd(source, dirtyEnd);
            TextPointer start = GetTextPointerAtOffset(rangeStart);
            TextPointer end = GetTextPointerAtOffset(rangeEnd);
            if (start == null || end == null) return;

            _applyingHighlight = true;
            try
            {
                var dirtyRange = new TextRange(start, end);
                dirtyRange.ApplyPropertyValue(TextElement.ForegroundProperty, Foreground);
                dirtyRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                dirtyRange.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);

                foreach (HighlightSpan span in TypstLexer.TokenizeRange(source, IsDarkTheme, rangeStart, rangeEnd))
                {
                    if (span.Start < rangeStart || span.End > rangeEnd || span.End <= span.Start) continue;
                    TextPointer tokenStart = GetTextPointerAtOffset(span.Start);
                    TextPointer tokenEnd = GetTextPointerAtOffset(span.End);
                    if (tokenStart == null || tokenEnd == null) continue;
                    var token = new TextRange(tokenStart, tokenEnd);
                    token.ApplyPropertyValue(TextElement.ForegroundProperty, span.Color);
                    if (span.IsBold)
                        token.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                    if (span.IsItalic)
                        token.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
                }
            }
            finally { _applyingHighlight = false; }
        }

        private void MarkDirtyFromChanges(ICollection<TextChange> changes, int currentLength)
        {
            if (changes == null || changes.Count == 0)
            {
                MarkDirtyRange(0, currentLength);
                return;
            }

            int start = int.MaxValue;
            int end = 0;
            foreach (TextChange change in changes)
            {
                if (change.Offset < start) start = change.Offset;
                int localEnd = change.Offset + Math.Max(change.AddedLength, change.RemovedLength);
                if (localEnd > end) end = localEnd;
            }

            MarkDirtyRange(start, Math.Min(end + 1, currentLength));
        }

        private void MarkDirtyRange(string oldText, string newText)
        {
            int oldLength = oldText?.Length ?? 0;
            int newLength = newText?.Length ?? 0;
            int prefix = 0;
            int minLength = Math.Min(oldLength, newLength);
            while (prefix < minLength && oldText[prefix] == newText[prefix]) prefix++;

            int oldSuffix = oldLength;
            int newSuffix = newLength;
            while (oldSuffix > prefix && newSuffix > prefix && oldText[oldSuffix - 1] == newText[newSuffix - 1])
            {
                oldSuffix--;
                newSuffix--;
            }

            MarkDirtyRange(prefix, Math.Max(prefix, newSuffix));
        }

        private void MarkDirtyRange(int start, int end)
        {
            if (start < 0) start = 0;
            if (end < start) end = start;
            if (_pendingHighlightEnd > _pendingHighlightStart)
            {
                if (start < _pendingHighlightStart) _pendingHighlightStart = start;
                if (end > _pendingHighlightEnd) _pendingHighlightEnd = end;
            }
            else
            {
                _pendingHighlightStart = start;
                _pendingHighlightEnd = end;
            }
        }

        private TextPointer GetTextPointerAtOffset(int offset)
        {
            if (offset <= 0) return Document.ContentStart;

            int remaining = offset;
            TextPointer pointer = Document.ContentStart;
            while (pointer != null)
            {
                TextPointerContext context = pointer.GetPointerContext(LogicalDirection.Forward);
                if (context == TextPointerContext.Text)
                {
                    string run = pointer.GetTextInRun(LogicalDirection.Forward);
                    if (run.Length >= remaining)
                        return pointer.GetPositionAtOffset(remaining, LogicalDirection.Forward);

                    remaining -= run.Length;
                    pointer = pointer.GetPositionAtOffset(run.Length, LogicalDirection.Forward);
                    continue;
                }

                if (context == TextPointerContext.ElementEnd && pointer.Parent is Paragraph)
                {
                    int paragraphChars = remaining > 1 ? 2 : 1;
                    if (remaining <= paragraphChars)
                        return pointer;

                    remaining -= paragraphChars;
                }

                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }

            return Document.ContentEnd;
        }

        private static int ExpandToLineStart(string source, int index)
        {
            if (index <= 0) return 0;
            int cursor = index;
            while (cursor > 0)
            {
                char ch = source[cursor - 1];
                if (ch == '\n' || ch == '\r') break;
                cursor--;
            }
            return cursor;
        }

        private static int ExpandToLineEnd(string source, int index)
        {
            if (index >= source.Length) return source.Length;
            int cursor = index;
            while (cursor < source.Length)
            {
                char ch = source[cursor];
                cursor++;
                if (ch == '\n') break;
            }
            return cursor;
        }

        private static Brush Brush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            return brush;
        }
    }

    internal sealed class HighlightSpan
    {
        internal int Start;
        internal int End;
        internal Brush Color;
        internal bool IsBold;
        internal bool IsItalic;

        internal HighlightSpan(int start, int end, Brush color, bool bold = false, bool italic = false)
        {
            Start = start;
            End = end;
            Color = color;
            IsBold = bold;
            IsItalic = italic;
        }
    }

    internal static class TypstLexer
    {
        private sealed class HighlightPalette
        {
            internal readonly Brush Heading;
            internal readonly Brush Marker;
            internal readonly Brush Keyword;
            internal readonly Brush Function;
            internal readonly Brush String;
            internal readonly Brush Number;
            internal readonly Brush Comment;
            internal readonly Brush Math;
            internal readonly Brush Raw;
            internal readonly Brush Reference;

            internal HighlightPalette(string heading, string marker, string keyword, string function,
                string text, string number, string comment, string math, string raw, string reference)
            {
                Heading = Color(heading);
                Marker = Color(marker);
                Keyword = Color(keyword);
                Function = Color(function);
                String = Color(text);
                Number = Color(number);
                Comment = Color(comment);
                Math = Color(math);
                Raw = Color(raw);
                Reference = Color(reference);
            }
        }

        private static readonly HighlightPalette LightPalette = new HighlightPalette(
            "#0000FF", "#AF00DB", "#0000FF", "#795E26", "#A31515",
            "#098658", "#008000", "#AF00DB", "#A31515", "#267F99");
        private static readonly HighlightPalette DarkPalette = new HighlightPalette(
            "#569CD6", "#C586C0", "#C586C0", "#DCDCAA", "#CE9178",
            "#B5CEA8", "#6A9955", "#DCDCAA", "#CE9178", "#4EC9B0");
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "let", "set", "show", "if", "else", "for", "while", "break", "continue",
            "return", "import", "include", "as", "in", "not", "and", "or", "none",
            "auto", "true", "false", "content", "context", "assert"
        };

        internal static List<HighlightSpan> Tokenize(string source, bool isDarkTheme)
        {
            return TokenizeRange(source, isDarkTheme, 0, source.Length);
        }

        internal static List<HighlightSpan> TokenizeRange(string source, bool isDarkTheme, int start, int end)
        {
            HighlightPalette palette = isDarkTheme ? DarkPalette : LightPalette;
            var spans = new List<HighlightSpan>();
            if (string.IsNullOrEmpty(source) || end <= start)
                return spans;

            if (start < 0) start = 0;
            if (end > source.Length) end = source.Length;
            bool inBlockComment = false;
            bool inRawFence = false;

            int lineStart = 0;
            while (lineStart < start)
            {
                int lineEnd = source.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = source.Length;
                int contentEnd = lineEnd > lineStart && source[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
                int first = lineStart;
                while (first < contentEnd && (source[first] == ' ' || source[first] == '\t')) first++;
                if (StartsWith(source, first, contentEnd, "```")) inRawFence = !inRawFence;

                int i = lineStart;
                while (i < contentEnd)
                {
                    if (inBlockComment)
                    {
                        int close = source.IndexOf("*/", i, contentEnd - i, StringComparison.Ordinal);
                        if (close < 0) { i = contentEnd; }
                        else { i = close + 2; inBlockComment = false; }
                        continue;
                    }
                    if (source[i] == '"')
                    {
                        i = FindStringEnd(source, i, contentEnd, '"');
                        continue;
                    }
                    if (i + 1 < contentEnd && source[i] == '/' && source[i + 1] == '*')
                    {
                        int close = source.IndexOf("*/", i + 2, contentEnd - i - 2, StringComparison.Ordinal);
                        if (close < 0) { inBlockComment = true; i = contentEnd; }
                        else { i = close + 2; }
                        continue;
                    }
                    if (source[i] == '`')
                    {
                        int delimiter = i + 2 < contentEnd && source[i + 1] == '`' && source[i + 2] == '`' ? 3 : 1;
                        int close = FindSequence(source, new string('`', delimiter), i + delimiter, contentEnd);
                        i = close < 0 ? contentEnd : close + delimiter;
                        continue;
                    }
                    i++;
                }

                lineStart = lineEnd < source.Length ? lineEnd + 1 : source.Length;
            }

            lineStart = start;
            while (lineStart > 0 && source[lineStart - 1] != '\n') lineStart--;

            while (lineStart < end)
            {
                int lineEnd = source.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = source.Length;
                int contentEnd = lineEnd > lineStart && source[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
                int first = lineStart;
                while (first < contentEnd && (source[first] == ' ' || source[first] == '\t')) first++;

                if (StartsWith(source, first, contentEnd, "```"))
                {
                    inRawFence = !inRawFence;
                    Add(spans, first, contentEnd, palette.Raw, true);
                    lineStart = lineEnd + 1;
                    continue;
                }
                if (inRawFence)
                {
                    Add(spans, lineStart, contentEnd, palette.Raw);
                    lineStart = lineEnd + 1;
                    continue;
                }

                int headingEnd = first;
                while (headingEnd < contentEnd && source[headingEnd] == '=') headingEnd++;
                if (headingEnd > first && headingEnd < contentEnd && source[headingEnd] == ' ')
                {
                    Add(spans, first, headingEnd, palette.Marker, true);
                    Add(spans, headingEnd + 1, contentEnd, palette.Heading, true);
                    lineStart = lineEnd + 1;
                    continue;
                }

                if (first + 1 < contentEnd && (source[first] == '-' || source[first] == '+' || source[first] == '/') && source[first + 1] == ' ')
                    Add(spans, first, first + 1, palette.Marker, true);

                int i = lineStart;
                while (i < contentEnd)
                {
                    if (inBlockComment)
                    {
                        int close = source.IndexOf("*/", i, contentEnd - i, StringComparison.Ordinal);
                        if (close < 0) { Add(spans, i, contentEnd, palette.Comment); i = contentEnd; }
                        else { Add(spans, i, close + 2, palette.Comment); i = close + 2; inBlockComment = false; }
                        continue;
                    }
                    if (source[i] == '$' && (i == 0 || source[i - 1] != '\\'))
                    {
                        int close = FindUnescaped(source, '$', i + 1, contentEnd);
                        if (close < 0) close = contentEnd - 1;
                        Add(spans, i, close + 1, palette.Math, false, true);
                        i = close + 1;
                        continue;
                    }
                    if (source[i] == '`')
                    {
                        int delimiter = i + 2 < contentEnd && source[i + 1] == '`' && source[i + 2] == '`' ? 3 : 1;
                        int close = FindSequence(source, new string('`', delimiter), i + delimiter, contentEnd);
                        if (close < 0) close = contentEnd - delimiter;
                        Add(spans, i, close + delimiter, palette.Raw);
                        i = close + delimiter;
                        continue;
                    }
                    if (source[i] == '"')
                    {
                        int close = FindStringEnd(source, i, contentEnd, '"');
                        Add(spans, i, close, palette.String);
                        i = close;
                        continue;
                    }
                    if (i + 1 < contentEnd && source[i] == '/' && source[i + 1] == '/')
                    {
                        Add(spans, i, contentEnd, palette.Comment);
                        i = contentEnd;
                        continue;
                    }
                    if (i + 1 < contentEnd && source[i] == '/' && source[i + 1] == '*')
                    {
                        int close = source.IndexOf("*/", i + 2, contentEnd - i - 2, StringComparison.Ordinal);
                        if (close < 0) { Add(spans, i, contentEnd, palette.Comment); inBlockComment = true; i = contentEnd; }
                        else { Add(spans, i, close + 2, palette.Comment); i = close + 2; }
                        continue;
                    }
                    if (source[i] == '#')
                    {
                        Add(spans, i, i + 1, palette.Marker, true);
                        int identifierStart = i + 1;
                        if (identifierStart < contentEnd && IsIdentifierStart(source[identifierStart]))
                        {
                            int tokenEnd = identifierStart + 1;
                            while (tokenEnd < contentEnd && IsIdentifierPart(source[tokenEnd])) tokenEnd++;
                            string word = source.Substring(identifierStart, tokenEnd - identifierStart);
                            int next = tokenEnd;
                            while (next < contentEnd && char.IsWhiteSpace(source[next])) next++;
                            Add(spans, identifierStart, tokenEnd, Keywords.Contains(word) ? palette.Keyword : (next < contentEnd && source[next] == '(' ? palette.Function : palette.Keyword));
                            i = tokenEnd;
                            continue;
                        }
                    }
                    if (char.IsDigit(source[i]))
                    {
                        int tokenEnd = i + 1;
                        while (tokenEnd < contentEnd && (char.IsDigit(source[tokenEnd]) || source[tokenEnd] == '.' || char.IsLetter(source[tokenEnd]) || source[tokenEnd] == '%')) tokenEnd++;
                        Add(spans, i, tokenEnd, palette.Number);
                        i = tokenEnd;
                        continue;
                    }
                    if (source[i] == '@' || (source[i] == '<' && i + 1 < contentEnd && IsIdentifierStart(source[i + 1])))
                    {
                        int tokenEnd = i + 1;
                        while (tokenEnd < contentEnd && (IsIdentifierPart(source[tokenEnd]) || source[tokenEnd] == '-' || source[tokenEnd] == ':')) tokenEnd++;
                        if (source[i] == '<' && tokenEnd < contentEnd && source[tokenEnd] == '>') tokenEnd++;
                        Add(spans, i, tokenEnd, palette.Reference);
                        i = tokenEnd;
                        continue;
                    }
                    if (source[i] == '*' && i + 1 < contentEnd && source[i + 1] == '*')
                    {
                        int close = source.IndexOf("**", i + 2, contentEnd - i - 2, StringComparison.Ordinal);
                        if (close >= 0) { Add(spans, i, close + 2, palette.Heading, true); i = close + 2; continue; }
                    }
                    if (source[i] == '*')
                    {
                        int close = source.IndexOf('*', i + 1, contentEnd - i - 1);
                        if (close > i + 1) { Add(spans, i, close + 1, palette.Heading, true); i = close + 1; continue; }
                    }
                    if (source[i] == '_')
                    {
                        int close = source.IndexOf('_', i + 1, contentEnd - i - 1);
                        if (close > i + 1) { Add(spans, i, close + 1, palette.Math, false, true); i = close + 1; continue; }
                    }
                    i++;
                }

                lineStart = lineEnd < source.Length ? lineEnd + 1 : source.Length;
            }

            return spans;
        }

        private static void Add(List<HighlightSpan> spans, int start, int end, Brush color, bool bold = false, bool italic = false)
        {
            if (end > start) spans.Add(new HighlightSpan(start, end, color, bold, italic));
        }

        private static bool StartsWith(string text, int start, int end, string value)
        {
            return start + value.Length <= end && string.CompareOrdinal(text, start, value, 0, value.Length) == 0;
        }

        private static int FindUnescaped(string text, char value, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (text[i] != value) continue;
                int slashes = 0;
                for (int j = i - 1; j >= start && text[j] == '\\'; j--) slashes++;
                if (slashes % 2 == 0) return i;
            }
            return -1;
        }

        private static int FindSequence(string text, string value, int start, int end)
        {
            for (int i = start; i + value.Length <= end; i++)
                if (StartsWith(text, i, end, value)) return i;
            return -1;
        }

        private static int FindStringEnd(string text, int start, int end, char quote)
        {
            for (int i = start + 1; i < end; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == quote) return i + 1;
            }
            return end;
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-';
        }

        private static Brush Color(string value)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value);
            brush.Freeze();
            return brush;
        }
    }
}

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

        public static readonly DependencyProperty IsHorizontalScrollEnabledProperty = DependencyProperty.Register(
            nameof(IsHorizontalScrollEnabled), typeof(bool), typeof(TypifyTextbox),
            new FrameworkPropertyMetadata(true, OnScrollOptionsChanged));

        private readonly DispatcherTimer _highlightTimer;
        private bool _synchronizing;
        private bool _updatingTextProperty;
        private bool _applyingHighlight;

        public TypifyTextbox()
        {
            AcceptsTab = true;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            FontFamily = new FontFamily("Consolas");
            FontSize = 14;
            Foreground = Brush("#D8DEE9");
            Background = Brush("#20242B");
            BorderBrush = Brush("#4B5563");
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
                var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                if (!string.Equals(ReadEditorText(), value, StringComparison.Ordinal))
                    range.Text = value;
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
            ApplySyntaxHighlighting(ReadEditorText());
        }

        private void ApplySyntaxHighlighting(string source)
        {
            if (source.Length == 0) return;
            TextPointer[] positions = BuildCharacterMap(source);
            if (positions == null) return;
            _applyingHighlight = true;
            try
            {
                var allText = new TextRange(positions[0], positions[source.Length]);
                allText.ApplyPropertyValue(TextElement.ForegroundProperty, Foreground);
                allText.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                allText.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);

                foreach (HighlightSpan span in TypstLexer.Tokenize(source))
                {
                    if (span.Start < 0 || span.End > source.Length || span.End <= span.Start) continue;
                    var token = new TextRange(positions[span.Start], positions[span.End]);
                    token.ApplyPropertyValue(TextElement.ForegroundProperty, span.Color);
                    if (span.IsBold)
                        token.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                    if (span.IsItalic)
                        token.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
                }
            }
            finally { _applyingHighlight = false; }
        }

        private TextPointer[] BuildCharacterMap(string source)
        {
            var positions = new TextPointer[source.Length + 1];
            int offset = 0;
            TextPointer pointer = Document.ContentStart;
            while (pointer != null && offset < source.Length)
            {
                TextPointerContext context = pointer.GetPointerContext(LogicalDirection.Forward);
                if (context == TextPointerContext.Text)
                {
                    string run = pointer.GetTextInRun(LogicalDirection.Forward);
                    for (int i = 0; i < run.Length && offset < source.Length; i++, offset++)
                        positions[offset] = pointer.GetPositionAtOffset(i, LogicalDirection.Forward);
                    pointer = pointer.GetPositionAtOffset(run.Length, LogicalDirection.Forward);
                    continue;
                }

                if (context == TextPointerContext.ElementEnd && pointer.Parent is Paragraph)
                {
                    positions[offset++] = pointer;
                    if (offset < source.Length) positions[offset++] = pointer;
                }
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
            positions[source.Length] = pointer ?? Document.ContentEnd;
            return offset == source.Length ? positions : null;
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
        private static readonly Brush Heading = Color("#88C0D0");
        private static readonly Brush Marker = Color("#81A1C1");
        private static readonly Brush Keyword = Color("#B48EAD");
        private static readonly Brush Function = Color("#8FBCBB");
        private static readonly Brush String = Color("#A3BE8C");
        private static readonly Brush Number = Color("#D08770");
        private static readonly Brush Comment = Color("#7F8C9D");
        private static readonly Brush Math = Color("#EBCB8B");
        private static readonly Brush Raw = Color("#A3BE8C");
        private static readonly Brush Reference = Color("#B48EAD");
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "let", "set", "show", "if", "else", "for", "while", "break", "continue",
            "return", "import", "include", "as", "in", "not", "and", "or", "none",
            "auto", "true", "false", "content", "context", "assert"
        };

        internal static List<HighlightSpan> Tokenize(string source)
        {
            var spans = new List<HighlightSpan>();
            bool inBlockComment = false;
            bool inRawFence = false;
            int lineStart = 0;
            while (lineStart < source.Length)
            {
                int lineEnd = source.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = source.Length;
                int contentEnd = lineEnd > lineStart && source[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
                int first = lineStart;
                while (first < contentEnd && (source[first] == ' ' || source[first] == '\t')) first++;

                if (StartsWith(source, first, contentEnd, "```"))
                {
                    inRawFence = !inRawFence;
                    Add(spans, first, contentEnd, Raw, true);
                    lineStart = lineEnd + 1;
                    continue;
                }
                if (inRawFence)
                {
                    Add(spans, lineStart, contentEnd, Raw);
                    lineStart = lineEnd + 1;
                    continue;
                }

                int headingEnd = first;
                while (headingEnd < contentEnd && source[headingEnd] == '=') headingEnd++;
                if (headingEnd > first && headingEnd < contentEnd && source[headingEnd] == ' ')
                {
                    Add(spans, first, headingEnd, Marker, true);
                    Add(spans, headingEnd + 1, contentEnd, Heading, true);
                    lineStart = lineEnd + 1;
                    continue;
                }

                if (first + 1 < contentEnd && (source[first] == '-' || source[first] == '+' || source[first] == '/') && source[first + 1] == ' ')
                    Add(spans, first, first + 1, Marker, true);

                int i = lineStart;
                while (i < contentEnd)
                {
                    if (inBlockComment)
                    {
                        int close = source.IndexOf("*/", i, contentEnd - i, StringComparison.Ordinal);
                        if (close < 0) { Add(spans, i, contentEnd, Comment); i = contentEnd; }
                        else { Add(spans, i, close + 2, Comment); i = close + 2; inBlockComment = false; }
                        continue;
                    }
                    if (source[i] == '$' && (i == 0 || source[i - 1] != '\\'))
                    {
                        int close = FindUnescaped(source, '$', i + 1, contentEnd);
                        if (close < 0) close = contentEnd - 1;
                        Add(spans, i, close + 1, Math, false, true);
                        i = close + 1;
                        continue;
                    }
                    if (source[i] == '`')
                    {
                        int delimiter = i + 2 < contentEnd && source[i + 1] == '`' && source[i + 2] == '`' ? 3 : 1;
                        int close = FindSequence(source, new string('`', delimiter), i + delimiter, contentEnd);
                        if (close < 0) close = contentEnd - delimiter;
                        Add(spans, i, close + delimiter, Raw);
                        i = close + delimiter;
                        continue;
                    }
                    if (source[i] == '"' || source[i] == '\'')
                    {
                        int close = FindStringEnd(source, i, contentEnd, source[i]);
                        Add(spans, i, close, String);
                        i = close;
                        continue;
                    }
                    if (i + 1 < contentEnd && source[i] == '/' && source[i + 1] == '/')
                    {
                        Add(spans, i, contentEnd, Comment);
                        i = contentEnd;
                        continue;
                    }
                    if (i + 1 < contentEnd && source[i] == '/' && source[i + 1] == '*')
                    {
                        int close = source.IndexOf("*/", i + 2, contentEnd - i - 2, StringComparison.Ordinal);
                        if (close < 0) { Add(spans, i, contentEnd, Comment); inBlockComment = true; i = contentEnd; }
                        else { Add(spans, i, close + 2, Comment); i = close + 2; }
                        continue;
                    }
                    if (source[i] == '#')
                    {
                        Add(spans, i, i + 1, Marker, true);
                        int identifierStart = i + 1;
                        if (identifierStart < contentEnd && IsIdentifierStart(source[identifierStart]))
                        {
                            int end = identifierStart + 1;
                            while (end < contentEnd && IsIdentifierPart(source[end])) end++;
                            string word = source.Substring(identifierStart, end - identifierStart);
                            int next = end;
                            while (next < contentEnd && char.IsWhiteSpace(source[next])) next++;
                            Add(spans, identifierStart, end, Keywords.Contains(word) ? Keyword : (next < contentEnd && source[next] == '(' ? Function : Keyword));
                            i = end;
                            continue;
                        }
                    }
                    if (char.IsDigit(source[i]))
                    {
                        int end = i + 1;
                        while (end < contentEnd && (char.IsDigit(source[end]) || source[end] == '.' || char.IsLetter(source[end]) || source[end] == '%')) end++;
                        Add(spans, i, end, Number);
                        i = end;
                        continue;
                    }
                    if (source[i] == '@' || (source[i] == '<' && i + 1 < contentEnd && IsIdentifierStart(source[i + 1])))
                    {
                        int end = i + 1;
                        while (end < contentEnd && (IsIdentifierPart(source[end]) || source[end] == '-' || source[end] == ':')) end++;
                        if (source[i] == '<' && end < contentEnd && source[end] == '>') end++;
                        Add(spans, i, end, Reference);
                        i = end;
                        continue;
                    }
                    if (source[i] == '*' && i + 1 < contentEnd && source[i + 1] == '*')
                    {
                        int close = source.IndexOf("**", i + 2, contentEnd - i - 2, StringComparison.Ordinal);
                        if (close >= 0) { Add(spans, i, close + 2, Heading, true); i = close + 2; continue; }
                    }
                    if (source[i] == '*')
                    {
                        int close = source.IndexOf('*', i + 1, contentEnd - i - 1);
                        if (close > i + 1) { Add(spans, i, close + 1, Math, false, true); i = close + 1; continue; }
                    }
                    i++;
                }
                lineStart = lineEnd + 1;
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

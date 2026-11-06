using System;
using System.ComponentModel;
using System.Globalization;

namespace AssetImport
{
    internal enum InterfaceLanguage
    {
        [Description("English")]
        English,
        [Description("简体中文")]
        ChineseSimplified
    }

    /// <summary>Presentation-only translations. Identifiers and serialized data stay unchanged.</summary>
    internal static partial class L
    {
        internal static InterfaceLanguage Language = InterfaceLanguage.English;

        internal static string Text(string english)
        { return Text(english, Language); }

        internal static string Text(string english, InterfaceLanguage language)
        {
            string translated;
            return language == InterfaceLanguage.ChineseSimplified && english != null &&
                Chinese.TryGetValue(english, out translated) ? translated : english;
        }

        internal static string Format(string english, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, Text(english), arguments);
        }

        internal static LocalizedText Message(string english, params object[] arguments)
        {
            return new LocalizedText(english, arguments);
        }
    }

    /// <summary>Keep pending/action messages translatable when the language changes.</summary>
    internal sealed class LocalizedText
    {
        private readonly string _english;
        private readonly object[] _arguments;
        private readonly LocalizedText _left, _right;

        internal LocalizedText(string english, object[] arguments)
        { _english = english; _arguments = arguments; }

        private LocalizedText(LocalizedText left, LocalizedText right)
        { _left = left; _right = right; }

        public override string ToString()
        { return Render(L.Language, CultureInfo.CurrentCulture); }

        internal string English { get { return Render(InterfaceLanguage.English, CultureInfo.InvariantCulture); } }

        private string Render(InterfaceLanguage language, CultureInfo culture)
        {
            if (_left != null || _right != null)
                return (_left == null ? "" : _left.Render(language, culture)) + (_right == null ? "" : _right.Render(language, culture));
            string format = L.Text(_english, language);
            if (_arguments == null || _arguments.Length == 0) return format;
            var values = new object[_arguments.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var message = _arguments[i] as LocalizedText;
                values[i] = message == null ? _arguments[i] : message.Render(language, culture);
            }
            return string.Format(culture, format, values);
        }

        internal int Length { get { return ToString().Length; } }
        public static implicit operator string(LocalizedText value) { return value == null ? null : value.ToString(); }
        public static implicit operator LocalizedText(string value) { return value == null ? null : new LocalizedText(value, null); }
        public static LocalizedText operator +(LocalizedText left, LocalizedText right) { return new LocalizedText(left, right); }
        public static LocalizedText operator +(LocalizedText left, string right) { return left + (LocalizedText)right; }
        public static LocalizedText operator +(string left, LocalizedText right) { return (LocalizedText)left + right; }
    }
}

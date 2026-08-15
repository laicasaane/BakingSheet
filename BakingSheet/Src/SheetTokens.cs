// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cathei.BakingSheet
{
    /// <summary>
    /// Provides the vocabulary used to compose and interpret BakingSheet headers,
    /// property paths, collection markers, comments, and sheet names.
    /// </summary>
    /// <remarks>
    /// For example, <c>Rewards:{}:Key</c> is composed from dictionary, path-separator,
    /// and dictionary-header tokens, while <c>&lt;#Rewards:{}#&gt;</c> uses collection-marker tokens.
    /// </remarks>
    public static class SheetTokens
    {
        /// <summary>Provides tokens that describe list levels in sheet headers and property paths.</summary>
        public static class List
        {
            /// <summary>
            /// Provides the delimiters and anonymous form of a list selector.
            /// </summary>
            public static class Selector
            {
                /// <summary>
                /// Opens a list selector, as the <c>[</c> in <c>[2]</c>.
                /// </summary>
                public const string Start = "[";

                /// <summary>
                /// Closes a list selector, as the <c>]</c> in <c>[2]</c>.
                /// </summary>
                public const string End = "]";

                /// <summary>
                /// Represents one unnamed list level as <c>[]</c> in split header content.
                /// </summary>
                public const string Anonymous = Start + End;
            }
        }

        /// <summary>
        /// Provides tokens that describe vertical dictionaries in sheet headers and marker paths.
        /// </summary>
        public static class Dictionary
        {
            /// <summary>
            /// Provides the delimiters and anonymous form of a vertical-dictionary selector.
            /// </summary>
            public static class Selector
            {
                /// <summary>
                /// Opens a dictionary selector, as the <c>{</c> in <c>{}</c>.
                /// </summary>
                public const string Start = "{";

                /// <summary>
                /// Closes a dictionary selector, as the <c>}</c> in <c>{}</c>.
                /// </summary>
                public const string End = "}";

                /// <summary>
                /// Represents an unnamed vertical-dictionary level as <c>{}</c>,
                /// for example in <c>Rewards:{}:Key</c>.
                /// </summary>
                public const string Anonymous = Start + End;
            }

            /// <summary>
            /// Provides the terminal header names for a vertical dictionary's key and value columns.
            /// </summary>
            public static class Header
            {
                /// <summary>
                /// Identifies the key column, as <c>Key</c> in <c>Rewards:{}:Key</c>.
                /// </summary>
                public const string Key = "Key";

                /// <summary>
                /// Identifies the value column, as <c>Value</c> in <c>Rewards:{}:Value</c>.
                /// </summary>
                public const string Value = "Value";
            }
        }

        /// <summary>Provides tokens used by vertical-collection content.</summary>
        public static class Collection
        {
            /// <summary>
            /// Provides the delimiters that surround a collection path in a data-row marker.
            /// </summary>
            public static class Marker
            {
                /// <summary>
                /// Opens a collection marker, as <c>&lt;#</c> in <c>&lt;#Rewards:{}#&gt;</c>.
                /// </summary>
                public const string Start = "<#";

                /// <summary>
                /// Closes a collection marker, as <c>#&gt;</c> in <c>&lt;#Rewards:{}#&gt;</c>.
                /// </summary>
                public const string End = "#>";
            }
        }

        /// <summary>Provides reserved names that appear in sheet headers.</summary>
        public static class Header
        {
            /// <summary>
            /// Identifies the required first column of a sheet as <c>Id</c>.
            /// </summary>
            public const string Id = nameof(ISheetRow.Id);
        }

        /// <summary>Provides tokens that separate names and path segments.</summary>
        public static class Separator
        {
            /// <summary>
            /// Separates property, selector, and header segments, as the <c>:</c>
            /// characters in <c>Rewards:{}:Key</c>.
            /// </summary>
            public const string Path = ":";

            /// <summary>
            /// Separates a logical sheet name from its optional postfix,
            /// for example <c>Items.en</c> becomes sheet <c>Items</c> with postfix <c>en</c>.
            /// </summary>
            public const string SheetName = ".";
        }

        /// <summary>Provides prefixes that make sheet content non-data or ignored.</summary>
        public static class Comment
        {
            /// <summary>
            /// Marks general comment content with <c>$</c>. Depending on the converter,
            /// this can exclude a sheet, header column, or row, such as <c>$ Notes</c>.
            /// </summary>
            public const string Primary = "$";

            /// <summary>
            /// Marks a non-identifier data cell as ignored with <c>$$</c>, such as <c>$$ TODO</c>.
            /// The same prefix introduces an optional note after a collection marker.
            /// </summary>
            public const string Cell = "$$";
        }

        private static readonly string[] s_pathSeparators = { Separator.Path };

        private static readonly char[] s_reservedPathNameCharacters =
        {
            Separator.Path[0],
            List.Selector.Start[0],
            List.Selector.End[0],
            Dictionary.Selector.Start[0],
            Dictionary.Selector.End[0],
        };

        /// <summary>
        /// Gets the cached, read-only set of tokens accepted when splitting a property path.
        /// </summary>
        /// <remarks>The current set contains only <see cref="Separator.Path"/>.</remarks>
        public static ReadOnlySpan<string> PathSeparators => s_pathSeparators;

        /// <summary>
        /// Gets the cached, read-only characters reserved for path separation and collection selectors.
        /// </summary>
        /// <remarks>
        /// A named path segment cannot contain <c>:</c>, <c>[</c>, <c>]</c>, <c>{</c>, or <c>}</c>.
        /// </remarks>
        public static ReadOnlySpan<char> ReservedPathNameCharacters => s_reservedPathNameCharacters;

        /// <summary>
        /// Determines whether sheet content starts with the supplied comment token,
        /// ignoring any leading whitespace.
        /// </summary>
        /// <param name="value">The sheet name, header, row identifier, or cell content to inspect.</param>
        /// <param name="comment">The comment token to find, normally <see cref="Comment.Primary"/> or <see cref="Comment.Cell"/>.</param>
        /// <returns><see langword="true"/> when <paramref name="comment"/> is the first non-whitespace content; otherwise, <see langword="false"/>.</returns>
        public static bool StartsWithComment(string value, string comment)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            int startIndex = 0;

            while (startIndex < value.Length && char.IsWhiteSpace(value[startIndex]))
                startIndex++;

            return value.IndexOf(comment, startIndex, StringComparison.Ordinal) == startIndex;
        }

        /// <summary>
        /// Splits a converter sheet name at the first <see cref="Separator.SheetName"/> token.
        /// </summary>
        /// <param name="name">The sheet or file name, such as <c>Items.en</c>.</param>
        /// <returns>
        /// The logical sheet name and its postfix. The postfix is <see langword="null"/>
        /// when <paramref name="name"/> contains no sheet-name separator.
        /// </returns>
        public static (string name, string subName) ParseSheetName(string name)
        {
            int idx = name.IndexOf(Separator.SheetName, StringComparison.Ordinal);

            if (idx == -1)
                return (name, null);

            return (name.Substring(0, idx), name.Substring(idx + Separator.SheetName.Length));
        }

        /// <summary>
        /// Enumerates serializable sheet properties declared by a type and each of its base types.
        /// </summary>
        /// <param name="type">The sheet container, row, or nested value type to inspect.</param>
        /// <returns>
        /// Public or non-public instance properties that have both a getter and a setter,
        /// excluding properties marked with <see cref="NonSerializedAttribute"/>.
        /// </returns>
        public static IEnumerable<PropertyInfo> GetEligibleProperties(Type type)
        {
            const BindingFlags bindingFlags = BindingFlags.Public |
                                              BindingFlags.NonPublic |
                                              BindingFlags.Instance |
                                              BindingFlags.DeclaredOnly;

            while (type != null)
            {
                var properties = type.GetProperties(bindingFlags);

                foreach (var property in properties)
                {
                    if (property.IsDefined(typeof(NonSerializedAttribute)))
                        continue;

                    if (property.GetMethod != null && property.SetMethod != null)
                        yield return property;
                }

                type = type.BaseType;
            }
        }

        /// <summary>
        /// Finds the <see cref="ISheetRowArray.Arr"/> property declared by a sheet-row array base type.
        /// </summary>
        /// <param name="type">A row type that may derive from <see cref="SheetRowArray{TKey, TElem}"/>.</param>
        /// <returns>
        /// The array property's reflection metadata, or <see langword="null"/> when the type hierarchy
        /// contains no sheet-row array base type.
        /// </returns>
        public static PropertyInfo GetRowArrayProperty(Type type)
        {
            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SheetRowArray<,>))
                    return type.GetProperty(nameof(ISheetRowArray.Arr));

                type = type.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Splits a sheet property path at every <see cref="Separator.Path"/> token.
        /// </summary>
        /// <param name="path">The path to split, such as <c>Rewards:{}:Key</c>.</param>
        /// <returns>
        /// The path segments in order. Empty segments are preserved; for example,
        /// <c>A::B</c> produces <c>A</c>, an empty segment, and <c>B</c>.
        /// </returns>
        public static string[] SplitPath(string path)
        {
            return path.Split(s_pathSeparators, StringSplitOptions.None);
        }

        /// <summary>
        /// Determines whether a proposed named path segment contains sheet-grammar punctuation.
        /// </summary>
        /// <param name="pathName">A single property or mapped member name, not a complete path.</param>
        /// <returns>
        /// <see langword="true"/> when the name contains a path separator or collection-selector
        /// delimiter; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool ContainsReservedPathNameCharacter(string pathName)
        {
            return pathName.IndexOfAny(s_reservedPathNameCharacters) >= 0;
        }
    }
}

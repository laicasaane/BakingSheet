using System;
using System.Globalization;

namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class EnemySheet : Sheet<EnemySheet.Id, EnemySheet.Row>
    {
        public sealed class CharId : IEquatable<CharId>
        {
            public CharacterKind Kind { get; private set; }
            public int SubId { get; private set; }

            public override string ToString()
                => $"{Kind}_{SubId.ToString(CultureInfo.InvariantCulture)}";

            public static bool TryParse(ReadOnlySpan<char> value, out CharId result)
            {
                int separator = value.LastIndexOf('_');

                if (separator <= 0 || separator >= value.Length - 1)
                {
                    result = null;
                    return false;
                }

                string kindText = value.Slice(0, separator).ToString();

                if (!Enum.TryParse(kindText, out CharacterKind kind) ||
                    !string.Equals(kind.ToString(), kindText, StringComparison.Ordinal) ||
                    !int.TryParse(
                        value.Slice(separator + 1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int subId))
                {
                    result = null;
                    return false;
                }

                result = new CharId
                {
                    Kind = kind,
                    SubId = subId,
                };
                return true;
            }

            public bool Equals(CharId other)
                => other != null && Kind == other.Kind && SubId == other.SubId;

            public override bool Equals(object obj)
                => obj is CharId other && Equals(other);

            public override int GetHashCode()
                => ((int)Kind * 397) ^ SubId;
        }

        [SheetValueConverter(typeof(IdConverter))]
        public sealed class Id : IEquatable<Id>
        {
            public CharId CharId { get; private set; }
            public int Rarity { get; private set; }

            public override string ToString()
                => $"{CharId}_{Rarity.ToString(CultureInfo.InvariantCulture)}";

            public static bool TryParse(ReadOnlySpan<char> value, out Id result)
            {
                int separator = value.LastIndexOf('_');

                if (separator <= 0 || separator >= value.Length - 1 ||
                    !CharId.TryParse(value.Slice(0, separator), out var charId) ||
                    !int.TryParse(
                        value.Slice(separator + 1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int rarity))
                {
                    result = null;
                    return false;
                }

                result = new Id
                {
                    CharId = charId,
                    Rarity = rarity,
                };
                return true;
            }

            public bool Equals(Id other)
                => other != null && Equals(CharId, other.CharId) && Rarity == other.Rarity;

            public override bool Equals(object obj)
                => obj is Id other && Equals(other);

            public override int GetHashCode()
                => ((CharId?.GetHashCode() ?? 0) * 397) ^ Rarity;
        }

        public sealed class IdConverter : SheetValueConverter<Id>
        {
            protected override Id StringToValue(
                Type type, string value, SheetValueConvertingContext context)
                => Id.TryParse(value.AsSpan(), out var result)
                    ? result
                    : throw new FormatException(
                        $"Invalid Enemy Id \"{value}\". Expected Kind_SubId_Rarity.");

            protected override string ValueToString(
                Type type, Id value, SheetValueConvertingContext context)
                => value.ToString();
        }

        public sealed class Row : SheetRow<Id>
        {
            public string Name { get; private set; }
        }
    }
}

# Complex Ids

A complex, or composite Id combines multiple values to identify a row. BakingSheet stores each value in its own column, so you do not need to combine the entire Id into one text value.

## Rules

- The sheet and its rows must use the same Id type.
- Id properties need getters and setters. Nested classes also need a parameterless constructor.
- Each final value needs a registered `ISheetValueConverter`.
- Class keys need value equality and must not change after insertion.
- The first Id column starts a row. A blank cell continues the previous row.
- Collections, references, Unity objects, and circular graphs are not supported inside a decomposed Id.

## Defining Id Type

The following key uses one column for `Kind` and another for `SubId`.

![Sample Complex Id](../.github/images/sample_complex_id.png)

<details>
<summary>Flat version</summary>

| Id:Kind | Id:SubId | Name   |
|---------|----------|--------|
| Enemy   | 2        | Goblin |

</details>

<details>
<summary>Split and Hybrid version</summary>

Named objects use the same layout in Split and Hybrid modes:

| Id    |       | Name   |
|-------|-------|--------|
| Kind  | SubId |        |
| Enemy | 2     | Goblin |

</details>

```csharp
public sealed class EntityId : IEquatable<EntityId>
{
    public string Kind { get; set; }
    public int SubId { get; set; }

    public bool Equals(EntityId other)
        => other != null && Kind == other.Kind && SubId == other.SubId;

    public override bool Equals(object obj)
        => obj is EntityId other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, SubId);
}

public sealed class EntityRow : SheetRow<EntityId>
{
    public string Name { get; set; }
}

public sealed class EntitySheet : Sheet<EntityId, EntityRow> { }
```

## Storing the Id as a Formatted String

Use a `SheetValueConverter` when you want one text value instead of separate Id columns. Apply the converter to the Id
type, then define how BakingSheet reads and writes that value:

```csharp
using System;
using System.Globalization;

[SheetValueConverter(typeof(EntityIdConverter))]
public sealed class EntityId : IEquatable<EntityId>
{
    ...
}

public sealed class EntityIdConverter : SheetValueConverter<EntityId>
{
    protected override EntityId StringToValue(
        Type type, string value, SheetValueConvertingContext context)
    {
        int separator = value?.LastIndexOf('_') ?? -1;

        if (separator <= 0 ||
            !int.TryParse(
                value.Substring(separator + 1),
                NumberStyles.Integer,
                context.FormatProvider,
                out int subId))
        {
            throw new FormatException(
                $"Invalid Entity Id \"{value}\". Expected Kind_SubId.");
        }

        return new EntityId
        {
            Kind = value.Substring(0, separator),
            SubId = subId,
        };
    }

    protected override string ValueToString(
        Type type, EntityId value, SheetValueConvertingContext context)
        => $"{value.Kind}_{value.SubId.ToString(context.FormatProvider)}";
}
```

`StringToValue` rebuilds the Id during import. `ValueToString` writes it during export.

The same Id now uses one column:

| Id      | Name   |
|---------|--------|
| Enemy_2 | Goblin |

You can also apply `SheetValueConverter` directly to an Id property. The converter controls raw-sheet values and
ScriptableObject row names.

Composite sheet references always use one cell and need a whole-key converter. Convert authoring keys to runtime-domain
values in the consuming project, outside BakingSheet.
